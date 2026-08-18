using System.Globalization;
using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Application.Retention;
using Congrega.Domain.Retention;
using Congrega.Infrastructure;
using Congrega.Infrastructure.Locking;
using Congrega.Infrastructure.Notifications;
using Congrega.Infrastructure.Persistence;
using Congrega.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Serilog;

// Bootstrap logger: captura falhas que acontecem ANTES do host existir — erro de
// configuração, secret ausente, ValidateOnStart reprovando. Sem ele, o pod morre
// na subida sem deixar nenhuma linha de log, que é o pior modo de falhar.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

var builder = Host.CreateApplicationBuilder(args);

// -----------------------------------------------------------------------------
// Logging estruturado
// -----------------------------------------------------------------------------
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "congrega-workers"));

// -----------------------------------------------------------------------------
// Configuração com validação no startup (fail fast)
// -----------------------------------------------------------------------------
// ValidateOnStart faz o pod morrer na subida se a configuração estiver errada, em
// vez de subir "saudável" e falhar no primeiro ciclo — quando o erro já estaria
// misturado ao ruído de operação.
builder.Services
    .AddOptions<RetentionOptions>()
    .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<WebhookProcessorOptions>()
    .Bind(builder.Configuration.GetSection(WebhookProcessorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// DatabaseOptions é vinculada por AddCongregaPersistence, mais abaixo.

// -----------------------------------------------------------------------------
// TimeProvider: relógio injetável
// -----------------------------------------------------------------------------
// Torna possível testar janelas de retenção e o agendamento do PeriodicTimer sem
// Thread.Sleep e sem esperar 15 dias de calendário.
builder.Services.AddSingleton(TimeProvider.System);

// -----------------------------------------------------------------------------
// Resiliência (Polly v8)
// -----------------------------------------------------------------------------
builder.Services.AddSingleton(_ => new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        // Só erros transitórios. Repetir violação de constraint ou erro de sintaxe
        // é desperdiçar tentativas em algo que nunca vai passar.
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            .Handle<TimeoutRejectedException>(),
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),

        // Jitter é obrigatório, não enfeite: sem ele, três réplicas que falham no
        // mesmo instante voltam a bater no banco exatamente juntas, repetidas vezes.
        UseJitter = true
    })
    .AddTimeout(new TimeoutStrategyOptions
    {
        // Teto por operação de banco. Sem timeout, uma query travada segura o lock
        // distribuído indefinidamente e nenhuma réplica consegue rodar o ciclo.
        Timeout = TimeSpan.FromMinutes(2)
    })
    .Build());

// Circuit breaker NÃO é usado aqui, deliberadamente. Ele protege um chamador de um
// dependente instável abrindo o circuito e falhando rápido. Para um job periódico
// que já tolera falha de ciclo e tenta de novo em uma hora, o breaker só adicionaria
// estado a gerenciar sem reduzir carga real. Ele é indicado no adaptador do
// Abacate.pay, onde o volume de chamadas é alto e a degradação precisa ser rápida.

// -----------------------------------------------------------------------------
// Portas e adaptadores
// -----------------------------------------------------------------------------
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<RetentionScanner>();

// -----------------------------------------------------------------------------
// Persistência (EF Core) — necessária para o processador de webhook de
// pagamento, que grava Payment/Entitlement através do modelo de domínio.
// -----------------------------------------------------------------------------
// ITenantContext e IHostEnvironmentAccessor precisam existir ANTES de
// AddCongregaPersistence: o DbContext e o interceptor de conexão dependem dos
// dois. O Workers roda com congrega_worker (BYPASSRLS) e não representa
// nenhum usuário/tenant específico — WorkerTenantContext é fixo e
// cross-tenant, nunca resolvido por requisição.
builder.Services.AddSingleton<ITenantContext, WorkerTenantContext>();
builder.Services.AddSingleton<IHostEnvironmentAccessor, WorkersHostEnvironmentAccessor>();
builder.Services.AddCongregaPersistence(builder.Configuration);

// Gateway de pagamento — mesma extensão que a API usa, com o ambiente
// explícito na composição (ver AddCongregaPayments). Em produção não há
// adaptador registrado ainda, e a resolução falha ao processar o primeiro
// webhook — comportamento correto, ver premissa P8.
builder.Services.AddCongregaPayments(builder.Environment.IsDevelopment());
builder.Services.AddScoped<ProcessPaymentWebhookHandler>();

// -----------------------------------------------------------------------------
// Outbox
// -----------------------------------------------------------------------------
builder.Services.AddCongregaOutbox(builder.Configuration, builder.Environment.IsDevelopment());

// -----------------------------------------------------------------------------
// Workers
// -----------------------------------------------------------------------------
builder.Services.AddHostedService<RetentionBackgroundService>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<WebhookDispatcherService>();

// -----------------------------------------------------------------------------
// Health checks
// -----------------------------------------------------------------------------
// Só readiness depende do banco. Liveness NÃO checa Postgres de propósito: se o
// banco cair, o Kubernetes deve tirar os pods do balanceamento — e não entrar em
// loop de reinício, que atrasa a recuperação quando o banco voltar.
builder.Services
    .AddHealthChecks()
    .AddNpgSql(
        builder.Configuration["Database:PooledConnectionString"]
            ?? throw new InvalidOperationException("Database:PooledConnectionString não configurada."),
        name: "postgres",
        tags: ["ready"]);

var host = builder.Build();

try
{
    Log.Information("Congrega.Workers iniciando.");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Congrega.Workers encerrou de forma inesperada.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
