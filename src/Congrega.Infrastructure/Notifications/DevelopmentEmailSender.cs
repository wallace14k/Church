using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Adaptador de e-mail para desenvolvimento local.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não é um provedor.</b> Escreve a mensagem no log para que o fluxo de OTP
/// possa ser exercido localmente sem contratar serviço nenhum — o mesmo papel que
/// o <c>letter_opener</c> cumpre em outros ecossistemas.
/// </para>
/// <para>
/// <b>Registra o código em texto plano</b>, que é exatamente o que um adaptador
/// de produção jamais pode fazer. Por isso ele só é registrado em Development, e
/// o startup falha em Production se nenhum <c>IEmailSender</c> real estiver
/// configurado — falhar ao subir é muito melhor que subir mandando OTP para o
/// console.
/// </para>
/// <para>
/// A premissa P8 deixou o provedor em aberto. Quando ele for escolhido, o
/// adaptador real implementa esta mesma interface e mapeia as falhas do provedor
/// para <c>TransientDeliveryException</c> e <c>PermanentDeliveryException</c> — é
/// essa distinção que o dispatcher usa para decidir entre nova tentativa e dead
/// letter.
/// </para>
/// </remarks>
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "[DEV] E-mail NÃO enviado de verdade. Template {Template} para {ToAddress}. {Variables}",
            message.TemplateCode,
            message.ToAddress,
            string.Join(" · ", message.Variables.Select(v => $"{v.Key}={v.Value}")));

        return Task.CompletedTask;
    }
}
