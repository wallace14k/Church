using System.Text.Json;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Congrega.Infrastructure.Notifications;
using Congrega.Application.Billing;
using Congrega.Application.Outbox;
using Congrega.Domain.Billing;
using Congrega.Domain.Addressing;
using Congrega.Domain.Calendar;
using Congrega.Domain.Congregation;
using Congrega.Domain.Connectors;
using Congrega.Domain.Giving;
using Congrega.Domain.Identity;
using Congrega.Infrastructure.Locking;
using Congrega.Infrastructure.Payments;
using Congrega.Infrastructure.Addressing;
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
    /// <summary>
    /// Registra persistência: <c>DatabaseOptions</c>, <see cref="CongregaDbContext"/>
    /// e todos os repositórios baseados nele, mais o lock distribuído.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separado de <see cref="AddCongregaInfrastructure"/> porque é a parte que os
    /// <b>dois</b> hosts precisam — API e Workers. <see cref="AuthenticationOptions"/>
    /// e <see cref="PaymentOptions"/> ficam de fora de propósito: são
    /// <c>[Required]</c> e <c>ValidateOnStart</c>, e vinculá-los aqui obrigaria o
    /// Workers a ter a chave privada do JWT configurada só para abrir uma conexão de
    /// banco — um processo que nunca emite nem verifica token nenhum.
    /// </para>
    /// <para>
    /// Exige que o chamador já tenha registrado <see cref="ITenantContext"/> e
    /// <see cref="IHostEnvironmentAccessor"/> antes de chamar este método — cada host
    /// tem a sua implementação (a API resolve do <c>HttpContext</c>; o Workers usa um
    /// contexto cross-tenant fixo, porque roda com <c>congrega_worker</c>, que tem
    /// BYPASSRLS).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCongregaPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
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
        services.AddScoped<IFamilyRepository, FamilyRepository>();
        services.AddScoped<IGivingCategoryRepository, GivingCategoryRepository>();
        services.AddScoped<IGivingEntryRepository, GivingEntryRepository>();
        services.AddScoped<IFinancialAccountRepository, FinancialAccountRepository>();
        services.AddScoped<IFiscalDocumentRepository, FiscalDocumentRepository>();
        services.AddScoped<IVaultRepository, VaultRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IEventTypeRepository, EventTypeRepository>();
        services.AddScoped<IAddressRepository, AddressRepository>();
        services.AddScoped<IPostalCodeRepository, PostalCodeRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IEntitlementRepository, EntitlementRepository>();
        services.AddScoped<ISubscriptionStore, SubscriptionStore>();
        services.AddScoped<IPaymentWebhookRepository, PaymentWebhookRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();

        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();

        return services;
    }

    /// <summary>Registra persistência (ver <see cref="AddCongregaPersistence"/>) mais autenticação — só a API precisa disto.</summary>
    public static IServiceCollection AddCongregaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCongregaPersistence(configuration);

        // Consulta de CEP na ViaCEP.
        //
        // `AddHttpClient` e não um `HttpClient` novo por chamada: instanciar
        // um por requisição esgota portas do sistema sob carga (o socket fica
        // em TIME_WAIT depois do dispose), e é o erro clássico de cliente HTTP
        // em .NET. A fábrica recicla o handler.
        //
        // Timeout curto de propósito. A ViaCEP é conveniência com caminho
        // alternativo pronto — o preenchimento manual — então esperar 100
        // segundos (o padrão do HttpClient) travaria a tela de cadastro por
        // um serviço de terceiro. Cinco segundos é mais do que a ViaCEP leva
        // quando está de pé, e curto o bastante para a pessoa não desistir.
        services
            .AddHttpClient<IPostalCodeLookup, ViaCepPostalCodeLookup>(http =>
            {
                http.BaseAddress = new Uri("https://viacep.com.br/");
                http.Timeout = TimeSpan.FromSeconds(5);
            });

        services
            .AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Segredos do check-in infantil. ValidateOnStart não é cerimônia aqui:
        // sem a chave, o processo subiria gravando alergia de criança em texto
        // claro sem nada acusar. Ver ChildSafetyOptions e a premissa P8.
        services
            .AddOptions<ChildSafetyOptions>()
            .Bind(configuration.GetSection(ChildSafetyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Credenciais de integração têm chave PRÓPRIA — ver ConnectorOptions.
        // Rotacionar a chave dos conectores não pode tornar ilegível a ficha de
        // alergia de nenhuma criança.
        services.AddSingleton<ISecretHasher, SecretHasher>();

        // A fábrica nomeia a origem da chave. Sem isso, "DataKey não é Base64
        // válido" — com mais de um segredo configurado no processo — não diz qual
        // dos dois corrigir. A chave dos conectores é outra, e vive em
        // AddCongregaConnectors.
        services.AddSingleton<IFieldEncryptor>(sp => new AesGcmFieldEncryptor(
            sp.GetRequiredService<IOptions<ChildSafetyOptions>>().Value.DataKey,
            $"{ChildSafetyOptions.SectionName}:DataKey"));
        services.AddSingleton<IOtpGenerator, OtpGenerator>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        // Verificação de assinatura de webhook. Scoped porque lê PaymentOptions
        // e TimeProvider; sem estado próprio entre requisições.
        services.AddScoped<IWebhookSignatureVerifier, WebhookSignatureVerifier>();

        return services;
    }

    /// <summary>
    /// Integrações que cada igreja configura: e-mail, Telegram, Google Drive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Método próprio porque os dois processos precisam dele, e por caminhos
    /// diferentes.</b> A API o usa para a tela de configurações; o worker o usa
    /// porque o remetente de e-mail lê o conector da igreja antes de enviar.
    /// </para>
    /// <para>
    /// Isto já quebrou de duas formas em uma tarde, e as duas em silêncio. Os
    /// testadores ficaram num método que a API não chamava, e o endpoint
    /// respondia "sem teste disponível" para uma integração que sabe se testar.
    /// Depois o protetor de segredo ficou num método que o <b>worker</b> não
    /// chamava — e ali o estrago seria maior: o envio de e-mail não resolveria,
    /// e todo código de login iria para dead letter. Reunir num método com nome
    /// próprio, chamado explicitamente pelos dois, é o que impede a terceira vez.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCongregaConnectors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Chave própria — ver ConnectorOptions. Rotacionar a chave dos conectores
        // não pode tornar ilegível a ficha de alergia de nenhuma criança.
        services
            .AddOptions<ConnectorOptions>()
            .Bind(configuration.GetSection(ConnectorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConnectorSecretProtector>(sp => new ConnectorSecretProtector(
            new AesGcmFieldEncryptor(
                sp.GetRequiredService<IOptions<ConnectorOptions>>().Value.DataKey,
                $"{ConnectorOptions.SectionName}:DataKey")));

        services.AddScoped<ITenantConnectorRepository, TenantConnectorRepository>();

        // Cada testador fala com o serviço de verdade — o valor do botão
        // "Testar" está justamente em não simular nada.
        services.AddScoped<IConnectorTester, SmtpConnectorTester>();
        services.AddScoped<IConnectorTester, TelegramConnectorTester>();
        services.AddScoped<IConnectorTester, GoogleDriveConnectorTester>();

        // **O log de URL fica desligado para o Telegram.**
        //
        // A API dele carrega o token do bot no CAMINHO da requisição
        // (`/bot<token>/sendMessage`). Com o log padrão do HttpClient, o token de
        // toda igreja apareceria em texto claro em cada linha de log — e logs
        // costumam ir para onde a credencial não deveria ir.
        services
            .AddHttpClient(TelegramConnectorTester.HttpClientName, cliente =>
            {
                cliente.BaseAddress = new Uri("https://api.telegram.org/");
                cliente.Timeout = TimeSpan.FromSeconds(15);
            })
            .RemoveAllLoggers();

        services.AddHttpClient(GoogleDriveConnectorTester.HttpClientName, cliente =>
        {
            cliente.Timeout = TimeSpan.FromSeconds(20);
        });

        return services;
    }

    /// <summary>
    /// Registra o gateway de pagamento.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extensão separada, com <paramref name="isDevelopment"/> <b>explícito</b>,
    /// pelo mesmo desenho de <see cref="AddCongregaOutbox"/>: quem hospeda
    /// decide, e a decisão fica visível na composição em vez de escondida numa
    /// checagem de ambiente lá dentro.
    /// </para>
    /// <para>
    /// <b>Em produção não há adaptador registrado</b> e a resolução falha no
    /// startup — de propósito. Subir cobrando contra um gateway falso seria
    /// muito pior do que não subir; é a mesma postura do <c>IEmailSender</c>,
    /// registrada na premissa P8. Quando o adaptador Abacate.pay existir, ele
    /// entra no <c>else</c> deste método.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCongregaPayments(
        this IServiceCollection services,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            // Singleton: o estado das cobranças simuladas precisa sobreviver
            // entre a criação e a consulta do fetch-on-notify.
            services.AddSingleton<DevelopmentPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<DevelopmentPaymentGateway>());
        }

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

        // O conector SMTP da igreja, por cima do remetente da plataforma.
        //
        // **Decoração, não substituição.** `SmtpEmailSender` desvia para o
        // conector quando a igreja corrente tem um ligado, e delega ao anterior
        // em todo o resto. Substituir faria uma única igreja com credencial
        // errada derrubar o envio da plataforma inteira — inclusive o código de
        // login, que nem tem igreja para consultar.
        //
        // Registrado só quando já existe um remetente para decorar: sem isso, a
        // resolução da cadeia falharia no startup em Production, que é
        // exatamente onde nenhum remetente está registrado ainda (premissa P8).
        if (isDevelopment)
        {
            services.AddScoped<IEmailSender>(sp => new SmtpEmailSender(
                sp.GetRequiredService<ITenantConnectorRepository>(),
                sp.GetRequiredService<IConnectorSecretProtector>(),
                new DevelopmentEmailSender(sp.GetRequiredService<ILogger<DevelopmentEmailSender>>()),
                sp.GetRequiredService<ILogger<SmtpEmailSender>>()));
        }

        services.AddScoped<IOutboxMessageHandler, SendOtpEmailHandler>();
        services.AddScoped<IOutboxMessageHandler, SendSecurityAlertEmailHandler>();
        services.AddScoped<IOutboxMessageHandler, SecurityEventRecorder>();

        // Pagamento confirmado/estornado vira acesso — ou deixa de valer.
        // GrantEntitlementHandler é a regra de negócio (testável isolada); os
        // dois adaptadores só ligam a mensagem do Outbox à chamada de método.
        services.AddScoped<GrantEntitlementHandler>();
        services.AddScoped<IOutboxMessageHandler, PaymentConfirmedOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, PaymentRefundedOutboxHandler>();

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
