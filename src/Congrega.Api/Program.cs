using System.Globalization;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Congrega.Api.Authorization;
using Congrega.Api.Endpoints;
using Congrega.Api.Middleware;
using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Application.Identity;
using Congrega.Infrastructure;
using Congrega.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "congrega-api"));

// -----------------------------------------------------------------------------
// Infraestrutura e casos de uso
// -----------------------------------------------------------------------------
builder.Services.AddCongregaInfrastructure(builder.Configuration);

// Gateway de pagamento. Em desenvolvimento entra o adaptador falso; em produção
// nada é registrado e a resolução falha no startup, de propósito — ver a nota em
// AddCongregaPayments.
builder.Services.AddCongregaPayments(builder.Environment.IsDevelopment());

builder.Services.AddSingleton<IHostEnvironmentAccessor, HostEnvironmentAccessor>();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenantContext>());
builder.Services.AddScoped<IAuthenticationContextWriter>(sp => sp.GetRequiredService<RequestTenantContext>());

builder.Services.AddScoped<RequestOtpHandler>();
builder.Services.AddScoped<VerifyOtpHandler>();
builder.Services.AddScoped<RefreshSessionHandler>();
builder.Services.AddScoped<StartCheckoutHandler>();
builder.Services.AddScoped<ReceivePaymentWebhookHandler>();

// -----------------------------------------------------------------------------
// Autenticação — validação rigorosa do JWT
// -----------------------------------------------------------------------------
var authOptions = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>()
    ?? throw new InvalidOperationException("Seção Authentication ausente na configuração.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(authOptions.SigningKeyPem);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Cada item desligado aqui seria um vetor conhecido:
            ValidateIssuer = true,               // token de outro emissor
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,             // token de outro público
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,             // token expirado
            ValidateIssuerSigningKey = true,     // token forjado
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),

            // Fixar o algoritmo é o que impede o ataque "alg: none" e a confusão
            // RS256→HS256, em que a chave pública é usada como segredo HMAC.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

            // Padrão é 5 minutos de tolerância — tempo demais para um token de 15.
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.MapInboundClaims = false;   // preserva "sub" em vez de renomear
    });

builder.Services.AddSingleton<IAuthorizationHandler, EmailVerifiedHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TenantScopedHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var authorizationBuilder = builder.Services.AddAuthorizationBuilder();
Policies.Register(authorizationBuilder);

// -----------------------------------------------------------------------------
// Rate limiting de borda
// -----------------------------------------------------------------------------
// Particiona por IP. O limite por E-MAIL é feito no RequestOtpHandler, contando
// no banco — sem isso, um atacante distribuído passaria por aqui e inundaria a
// caixa da vítima, e um contador em memória seria por réplica.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0        // rejeita na hora; enfileirar só adia o erro
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "900";
        await context.HttpContext.Response.WriteAsync(
            "Muitas solicitações. Tente novamente em alguns minutos.", cancellationToken);
    };
});

// -----------------------------------------------------------------------------
// Erros padronizados (RFC 7807)
// -----------------------------------------------------------------------------
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // Correlation ID em toda resposta de erro: é o que permite pedir ao usuário
        // "me passe esse código" e achar o rastro exato no log.
        context.ProblemDetails.Extensions["correlationId"] =
            System.Diagnostics.Activity.Current?.TraceId.ToString();
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        // Nunca instance com a URL completa: query string pode carregar dado sensível.
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    };
});

// -----------------------------------------------------------------------------
// CORS
// -----------------------------------------------------------------------------
// O app web roda em outra origem (porta do Metro em desenvolvimento, domínio
// próprio em produção). Sem esta política o navegador barra a chamada no
// preflight, e o usuário vê "sem conexão com o servidor" mesmo com a API no ar —
// o POST nem chega a ser enviado.
//
// ATENÇÃO: CORS não é mecanismo de autenticação. Ele diz ao NAVEGADOR quais
// origens podem ler a resposta; não impede ninguém de chamar a API por curl ou
// por um cliente nativo. A autorização continua sendo do JWT e das policies.
const string WebCorsPolicy = "congrega-web";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCorsPolicy, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders(
                "Content-Type", "Authorization", "X-Congrega-Client", "X-Correlation-Id", "Idempotency-Key")
            // Obrigatório porque o cliente web usa `credentials: 'include'` para
            // que o cookie HttpOnly do refresh viaje. E é justamente por causa
            // disso que as origens precisam ser explícitas: a especificação
            // proíbe combinar credenciais com `Access-Control-Allow-Origin: *`,
            // e o navegador rejeita a resposta se alguém tentar.
            .AllowCredentials()
            // Devolve o correlation ID para o cliente poder exibi-lo numa tela de
            // erro. Sem expor explicitamente, o JavaScript não enxerga o header.
            .WithExposedHeaders("X-Correlation-Id")
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

if (allowedOrigins.Length == 0)
{
    // Falha visível em vez de silenciosa: sem origens, toda chamada do navegador
    // seria bloqueada e o sintoma apareceria como problema de rede no cliente.
    Log.Warning(
        "Cors:AllowedOrigins está vazio. Nenhuma origem de navegador poderá consumir a API.");
}

// -----------------------------------------------------------------------------
// Pipeline — a ordem é significativa
// -----------------------------------------------------------------------------
app.UseExceptionHandler();      // captura tudo abaixo e devolve Problem Details
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging();

// CORS ANTES do rate limiter e da autenticação. O preflight OPTIONS é anônimo e
// não carrega token: se passar pelo limitador ou pelo pipeline de autenticação,
// volta 405 ou 401 e o navegador aborta a requisição real.
app.UseCors(WebCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();   // DEPOIS de autenticar: precisa das claims
app.UseAuthorization();                         // DEPOIS do contexto: as policies o consultam

app.MapAuthEndpoints();
app.MapMemberEndpoints();
app.MapFamilyEndpoints();
app.MapGivingEndpoints();
app.MapEventEndpoints();
app.MapBillingEndpoints();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

try
{
    Log.Information("Congrega.Api iniciando.");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Congrega.Api encerrou de forma inesperada.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
