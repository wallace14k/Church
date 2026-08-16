using System.Text.Json;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Congrega.Infrastructure.Notifications;
using Congrega.Application.Outbox;
using Congrega.Domain.Congregation;
using Congrega.Domain.Identity;
using Congrega.Infrastructure.Locking;
using Congrega.Infrastructure.Persistence;
using Congrega.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Congrega.Infrastructure;

/// <summary>Acumula mensagens de Outbox junto às mudanças de estado da transação corrente.</summary>
internal sealed class EfOutbox(CongregaDbContext db, TimeProvider timeProvider) : IOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue(string messageType, object payload, string? correlationId = null)
    {
        var now = timeProvider.GetUtcNow();

        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageType = messageType,
            Payload = JsonSerializer.Serialize(payload, SerializerOptions),
            OccurredAt = now,
            NextAttemptAt = now,
            CorrelationId = correlationId ?? System.Diagnostics.Activity.Current?.TraceId.ToString()
        });
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddCongregaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        // O interceptor é scoped porque depende de ITenantContext, que é da
        // requisição. Registrá-lo como singleton congelaria o contexto do primeiro
        // request e aplicaria o tenant errado a todos os seguintes — uma falha de
        // isolamento que só apareceria sob concorrência.
        services.AddScoped<TenantConnectionInterceptor>();

        services.AddDbContext<CongregaDbContext>((serviceProvider, options) =>
        {
            var dbOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

            options
                .UseNpgsql(dbOptions.PooledConnectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                })
                .AddInterceptors(serviceProvider.GetRequiredService<TenantConnectionInterceptor>());

            // Nunca em produção: expõe valores de parâmetro nos logs, incluindo hash
            // de token e e-mail. A guarda é explícita para que ligar isso em
            // desenvolvimento não vaze para outro ambiente por descuido.
            if (serviceProvider.GetRequiredService<IHostEnvironmentAccessor>().IsDevelopment)
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CongregaDbContext>());
        services.AddScoped<IOutbox, EfOutbox>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEmailVerificationCodeRepository, EmailVerificationCodeRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<ISubscriptionTierProvider, SubscriptionTierProvider>();
        services.AddScoped<IMemberRepository, MemberRepository>();

        services.AddSingleton<ISecretHasher, SecretHasher>();
        services.AddSingleton<IOtpGenerator, OtpGenerator>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();

        return services;
    }

    /// <summary>
    /// Registra a fila do Outbox e seus adaptadores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separado de <see cref="AddCongregaInfrastructure"/> porque só o processo de
    /// workers drena a fila. A API <b>grava</b> no Outbox — isso vem do
    /// <c>IOutbox</c>, que faz parte do contexto de persistência — mas não deve
    /// ter um dispatcher registrado: duas partes lendo a mesma fila dobrariam o
    /// trabalho sem dobrar a vazão.
    /// </para>
    /// <para>
    /// A extensão existe também para manter as implementações <c>internal</c>. Um
    /// worker que precisasse escrever <c>AddScoped&lt;IOutboxStore, OutboxStore&gt;</c>
    /// obrigaria a tornar <c>OutboxStore</c> público, e detalhe de persistência
    /// vazaria para a camada de hospedagem.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCongregaOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services
            .AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<ISecurityEventStore, SecurityEventStore>();
        services.AddScoped<IUserContactResolver, UserContactResolver>();
        services.AddScoped<OutboxProcessor>();

        // Provedor de e-mail.
        //
        // Em desenvolvimento, o adaptador que escreve no log — permite exercer o
        // fluxo de OTP sem contratar serviço nenhum. Em produção não há adaptador
        // registrado ainda, e a resolução falha no startup. É o comportamento
        // correto: subir mandando código de acesso para o console seria muito pior
        // do que não subir. Ver premissa P8 em docs/00-premissas.md.
        if (isDevelopment)
        {
            services.AddScoped<IEmailSender, DevelopmentEmailSender>();
        }

        services.AddScoped<IOutboxMessageHandler, SendOtpEmailHandler>();
        services.AddScoped<IOutboxMessageHandler, SendSecurityAlertEmailHandler>();
        services.AddScoped<IOutboxMessageHandler, SecurityEventRecorder>();

        // Eventos de domínio conhecidos que ainda não disparam efeito. Registrá-los
        // explicitamente evita que caiam em dead letter, sem mascarar o caso que
        // importa — handler esquecido continua falhando alto.
        foreach (var tipo in AcknowledgedMessageHandler.KnownWithoutEffect)
        {
            services.AddScoped<IOutboxMessageHandler>(sp =>
                new AcknowledgedMessageHandler(
                    tipo, sp.GetRequiredService<ILogger<AcknowledgedMessageHandler>>()));
        }

        return services;
    }
}

/// <summary>
/// Acesso ao ambiente sem arrastar <c>Microsoft.AspNetCore</c> para a infraestrutura.
/// </summary>
/// <remarks>
/// Sem esta abstração, o projeto de infraestrutura precisaria referenciar o ASP.NET
/// Core apenas para consultar <c>IsDevelopment()</c> — e passaria a não poder ser
/// usado pelo projeto de workers, que não é web.
/// </remarks>
public interface IHostEnvironmentAccessor
{
    bool IsDevelopment { get; }
}
