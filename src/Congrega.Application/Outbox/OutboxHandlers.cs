using System.Text.Json;
using System.Text.Json.Serialization;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Outbox;

/// <summary>
/// Serialização compartilhada do Outbox.
/// </summary>
/// <remarks>
/// Ponto único de propósito: quem grava e quem lê precisam concordar na convenção
/// de nomes. Gravar em PascalCase e ler em camelCase produz um objeto com todos
/// os campos nulos — e o handler falha com "e-mail vazio" em vez de "JSON
/// incompatível", o que manda a investigação para o lado errado.
/// </remarks>
public static class OutboxSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T Deserialize<T>(string payload)
        where T : class =>
        JsonSerializer.Deserialize<T>(payload, Options)
        ?? throw new PermanentDeliveryException(
            $"Payload do Outbox não desserializou para {typeof(T).Name}.");
}

// -----------------------------------------------------------------------------
// Envio do código OTP
// -----------------------------------------------------------------------------

internal sealed record SendOtpEmailPayload
{
    public long UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Entrega o código OTP por e-mail.
/// </summary>
/// <remarks>
/// <para>
/// O handler mais crítico da plataforma: sem ele o login não existe. Toda a
/// autenticação passwordless depende de a mensagem sair daqui.
/// </para>
/// <para>
/// <b>O código em texto plano existe só neste caminho</b> — do payload até o
/// provedor. Nunca é logado, nunca aparece em exceção, nunca volta ao banco. O
/// que está persistido em <c>email_verification_codes</c> é apenas o HMAC.
/// </para>
/// <para>
/// <b>Idempotência:</b> reenviar o mesmo código é inofensivo — ele continua
/// válido até expirar ou ser consumido. O usuário eventualmente recebe dois
/// e-mails com o mesmo número, o que é bem menos grave que não receber nenhum.
/// </para>
/// </remarks>
public sealed class SendOtpEmailHandler(
    IEmailSender emailSender,
    ILogger<SendOtpEmailHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => "SendOtpEmail";

    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = OutboxSerialization.Deserialize<SendOtpEmailPayload>(payloadJson);

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            // Sem endereço, nenhuma tentativa futura resolve.
            throw new PermanentDeliveryException(
                $"Mensagem de OTP sem destinatário (usuário {payload.UserId}).");
        }

        await emailSender.SendAsync(
            new EmailMessage
            {
                ToAddress = payload.Email,
                ToName = payload.FullName,
                TemplateCode = "auth.otp",
                Variables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["code"] = payload.Code,
                    ["name"] = payload.FullName,
                    ["expiresInMinutes"] = "10",
                },
            },
            cancellationToken);

        // Sem o código e sem o e-mail no log — só o id do usuário, que já é
        // correlacionável pelo trace.
        logger.LogInformation("Código OTP entregue ao provedor para o usuário {UserId}.", payload.UserId);
    }
}

// -----------------------------------------------------------------------------
// Alerta de segurança ao titular
// -----------------------------------------------------------------------------

internal sealed record SecurityAlertPayload
{
    public long UserId { get; init; }
    public string Template { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// Avisa o titular sobre um evento de segurança na conta.
/// </summary>
/// <remarks>
/// Parte do controle, não cortesia: quando uma sessão é encerrada por suspeita de
/// roubo de refresh token, o titular é a única pessoa capaz de reconhecer que não
/// foi ele e reagir.
/// </remarks>
public sealed class SendSecurityAlertEmailHandler(
    IUserContactResolver contacts,
    IEmailSender emailSender,
    ILogger<SendSecurityAlertEmailHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => "SendSecurityAlertEmail";

    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = OutboxSerialization.Deserialize<SecurityAlertPayload>(payloadJson);
        var contato = await contacts.FindAsync(payload.UserId, cancellationToken);

        if (contato is null)
        {
            // Conta anonimizada ou bloqueada entre o evento e o envio. Não há para
            // quem avisar, e insistir não muda isso.
            logger.LogInformation(
                "Alerta de segurança descartado: usuário {UserId} não tem mais contato.", payload.UserId);
            return;
        }

        await emailSender.SendAsync(
            new EmailMessage
            {
                ToAddress = contato.Email,
                ToName = contato.FullName,
                TemplateCode = string.IsNullOrWhiteSpace(payload.Template)
                    ? "security.session_terminated"
                    : payload.Template,
                Variables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = contato.FullName,
                    ["occurredAt"] = payload.OccurredAt.ToString("dd/MM/yyyy HH:mm", null),
                },
            },
            cancellationToken);
    }
}

// -----------------------------------------------------------------------------
// Registro de evento de segurança
// -----------------------------------------------------------------------------

internal sealed record SecurityEventPayload
{
    public string EventType { get; init; } = string.Empty;
    public long? UserId { get; init; }
    public short Severity { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? FamilyId { get; init; }
    public int? RevokedTokens { get; init; }
}

/// <summary>
/// Persiste eventos de segurança em <c>security_events</c>.
/// </summary>
/// <remarks>
/// Vai pelo Outbox, e não por escrita direta no fluxo de autenticação, por um
/// motivo específico: o registro não pode fazer o login falhar. Se a gravação do
/// evento falhasse inline, um problema de auditoria viraria uma indisponibilidade
/// de autenticação — trocando um incômodo por um incidente.
/// </remarks>
public sealed class SecurityEventRecorder(ISecurityEventStore store) : IOutboxMessageHandler
{
    public string MessageType => "SecurityEvent";

    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = OutboxSerialization.Deserialize<SecurityEventPayload>(payloadJson);

        await store.RecordAsync(
            new SecurityEventRecord
            {
                EventType = payload.EventType,
                UserId = payload.UserId,
                Severity = payload.Severity == 0 ? (short)1 : payload.Severity,
                OccurredAt = payload.OccurredAt == default ? DateTimeOffset.UtcNow : payload.OccurredAt,
                Metadata = payload.FamilyId is null && payload.RevokedTokens is null
                    ? null
                    : JsonSerializer.Serialize(
                        new { familyId = payload.FamilyId, revokedTokens = payload.RevokedTokens },
                        OutboxSerialization.Options),
            },
            cancellationToken);
    }
}

// -----------------------------------------------------------------------------
// Eventos de domínio ainda sem efeito
// -----------------------------------------------------------------------------

/// <summary>
/// Reconhece um evento de domínio que ainda não dispara ação.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que o processador não mande esses eventos para dead letter. A
/// alternativa — tratar tipo desconhecido como ignorável — esconderia erro de
/// configuração de verdade: um handler que alguém esqueceu de registrar passaria
/// despercebido.
/// </para>
/// <para>
/// Registrar explicitamente o que é conhecido-e-sem-ação transforma "não sei o
/// que fazer com isso" numa decisão declarada no código. Quando o efeito existir,
/// troca-se o registro por um handler real.
/// </para>
/// </remarks>
public sealed class AcknowledgedMessageHandler(
    string messageType,
    ILogger<AcknowledgedMessageHandler> logger) : IOutboxMessageHandler
{
    /// <summary>
    /// Eventos de domínio que hoje são apenas registro histórico.
    /// </summary>
    /// <remarks>
    /// Manter a lista aqui, e não espalhada pelo DI, deixa visível numa olhada o
    /// que ainda está esperando efeito.
    /// </remarks>
    public static readonly IReadOnlyList<string> KnownWithoutEffect =
    [
        "UserRegistered",
        "UserEmailVerified",
        "RefreshTokenReused",
        "MemberRegistered",
        "SubscriptionActivated",
        "SubscriptionEnteredGrace",
        "SubscriptionExpired",
        "RetentionAlertEnqueued",
    ];

    public string MessageType { get; } = messageType;

    public Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        logger.LogDebug("Evento {MessageType} reconhecido, sem efeito associado.", MessageType);
        return Task.CompletedTask;
    }
}
