using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Congrega.Api.Authorization;
using Congrega.Api.Endpoints;
using Congrega.Api.Middleware;
using Congrega.Application.Abstractions;
using Congrega.Application.Identity;
using Congrega.Infrastructure;
using Congrega.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

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
builder.Services.AddSingleton<IHostEnvironmentAccessor, HostEnvironmentAccessor>();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenantContext>());

builder.Services.AddScoped<RequestOtpHandler>();
builder.Services.AddScoped<VerifyOtpHandler>();
builder.Services.AddScoped<RefreshSessionHandler>();

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

builder.Services.AddHealthChecks();

var app = builder.Build();

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
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();   // DEPOIS de autenticar: precisa das claims
app.UseAuthorization();                         // DEPOIS do contexto: as policies o consultam

app.MapAuthEndpoints();

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
