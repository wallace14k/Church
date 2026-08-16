namespace Congrega.Application.Abstractions;

/// <summary>Mensagem de e-mail transacional pronta para envio.</summary>
public sealed record EmailMessage
{
    public required string ToAddress { get; init; }
    public required string ToName { get; init; }

    /// <summary>
    /// Código do template no provedor. O corpo do e-mail **não** é montado aqui:
    /// conteúdo fora do código permite corrigir um texto sem publicar versão.
    /// </summary>
    public required string TemplateCode { get; init; }

    public required IReadOnlyDictionary<string, string> Variables { get; init; }
}

/// <summary>
/// Envio de e-mail transacional.
/// </summary>
/// <remarks>
/// Implementações <b>devem lançar</b> em falha, distinguindo transitória de
/// permanente. O dispatcher decide entre nova tentativa e dead letter pelo tipo
/// da exceção — engolir o erro e retornar normalmente faria o dispatcher marcar
/// como enviada uma mensagem que nunca saiu.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Falha transitória — vale tentar de novo.
/// </summary>
/// <remarks>Timeout, 5xx do provedor, limite de taxa.</remarks>
public sealed class TransientDeliveryException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Falha permanente — repetir não adianta.
/// </summary>
/// <remarks>
/// Endereço inválido, conta suspensa no provedor, template inexistente. O
/// dispatcher manda direto para dead letter em vez de gastar as tentativas
/// restantes em algo que nunca vai passar.
/// </remarks>
public sealed class PermanentDeliveryException(string message, Exception? inner = null)
    : Exception(message, inner);
