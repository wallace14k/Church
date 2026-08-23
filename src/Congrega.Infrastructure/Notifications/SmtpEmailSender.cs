using Congrega.Application.Abstractions;
using Congrega.Domain.Connectors;
using Congrega.Infrastructure.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Envia pelo servidor de e-mail que a igreja configurou.
/// </summary>
/// <remarks>
/// <para>
/// <b>É um decorador, não um substituto.</b> Quando a igreja corrente tem um
/// conector SMTP ligado, a mensagem sai por ele; em qualquer outro caso, cai no
/// remetente que já estava registrado. Sem esse desvio, um único conector mal
/// configurado derrubaria o envio de <i>toda</i> a plataforma.
/// </para>
/// <para>
/// <b>O código de login não passa por aqui, e isso é estrutural.</b> O OTP é
/// enviado antes de existir igreja selecionada — <c>users</c> não tem
/// <c>tenant_id</c>, e a mesma pessoa pode estar em duas igrejas ou em nenhuma.
/// Não há conector de tenant a consultar nesse instante. O e-mail de login
/// continua sendo responsabilidade de um remetente da plataforma, configurado
/// fora do banco. Ver o registro em <c>DependencyInjection</c>.
/// </para>
/// <para>
/// <b>O corpo é montado aqui</b>, ao contrário do que <see cref="EmailMessage"/>
/// pressupõe ao falar em "template no provedor". É o preço de usar uma conta de
/// e-mail comum em vez de um serviço transacional: não existe template do outro
/// lado. Corrigir um texto volta a exigir publicar versão — a alternativa seria
/// guardar os corpos no banco, e isso é uma tela de edição de template, não uma
/// linha de código.
/// </para>
/// </remarks>
internal sealed class SmtpEmailSender(
    ITenantConnectorRepository conectores,
    IConnectorSecretProtector protetor,
    IEmailSender proximo,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var conector = await conectores.FindByKindAsync(ConnectorKind.Smtp, cancellationToken);

        if (conector is null || !conector.IsEnabled || conector.SecretCiphertext is null)
        {
            await proximo.SendAsync(message, cancellationToken);
            return;
        }

        var config = ConfiguracaoSmtp.Ler(conector.Settings);
        var senha = protetor.Reveal(conector.SecretCiphertext);

        var mime = new MimeMessage { Subject = Assunto(message) };
        mime.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Body = new TextPart("plain") { Text = Corpo(message) };

        try
        {
            using var cliente = await SessaoSmtp.AbrirAsync(config, senha, cancellationToken);
            await cliente.SendAsync(mime, cancellationToken);
            await cliente.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (AuthenticationException erro)
        {
            // Credencial recusada não melhora com retentativa: repetir só gasta
            // as tentativas restantes e, em vários provedores, aproxima o
            // bloqueio da conta por tentativas malsucedidas.
            throw new PermanentDeliveryException(SessaoSmtp.Explicar(erro), erro);
        }
        catch (SmtpCommandException erro) when (erro.StatusCode is
            SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed)
        {
            throw new PermanentDeliveryException(
                $"O servidor recusou o destinatário {message.ToAddress}: {erro.Message}", erro);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException and not OperationCanceledException)
        {
            // Todo o resto é transitório até prova em contrário: servidor fora do
            // ar, limite de taxa, rede. O dispatcher tenta de novo.
            throw new TransientDeliveryException(SessaoSmtp.Explicar(erro), erro);
        }

        // Sem o destinatário no log: o endereço é dado pessoal, e o template já
        // diz o suficiente para correlacionar com o trace.
        logger.LogInformation(
            "E-mail {Template} enviado pelo servidor da igreja.", message.TemplateCode);
    }

    /// <summary>
    /// Assunto por template.
    /// </summary>
    /// <remarks>
    /// Um template desconhecido não impede o envio: cai num assunto genérico e a
    /// mensagem sai. Lançar aqui transformaria "alguém acrescentou um template e
    /// esqueceu do assunto" em e-mail nenhum — e o e-mail perdido pode ser o
    /// aviso de que a assinatura vence amanhã.
    /// </remarks>
    private static string Assunto(EmailMessage m) => m.TemplateCode switch
    {
        "auth.otp" => "Seu código de acesso",
        "security.session_terminated" => "Atividade suspeita na sua conta",
        "retention.d15" or "retention.d7" or "retention.d3" or "retention.d1" =>
            "Sua assinatura Congrega+ está para vencer",
        "retention.grace.d3" => "Sua assinatura Congrega+ venceu",
        _ => "Aviso da sua igreja",
    };

    private static string Corpo(EmailMessage m)
    {
        string V(string chave) => m.Variables.TryGetValue(chave, out var valor) ? valor : string.Empty;

        var saudacao = V("name").Length > 0 ? $"Olá, {V("name")}." : "Olá.";

        var miolo = m.TemplateCode switch
        {
            "auth.otp" =>
                $"Seu código de acesso é {V("code")}.\r\n\r\n"
                + $"Ele vale por {V("expiresInMinutes")} minutos e só pode ser usado uma vez.\r\n"
                + "Se não foi você quem pediu, ignore este e-mail — ninguém entra sem o código.",

            "security.session_terminated" =>
                $"Encerramos suas sessões em {V("occurredAt")} por suspeita de acesso indevido.\r\n\r\n"
                + "Se foi você usando o aplicativo normalmente, basta entrar de novo. "
                + "Se não foi, entre em contato com a administração da sua igreja.",

            "retention.grace.d3" =>
                "Sua assinatura Congrega+ venceu e o acesso ao conteúdo exclusivo foi suspenso.\r\n\r\n"
                + "Renovando, ele volta imediatamente.",

            _ when m.TemplateCode.StartsWith("retention.", StringComparison.Ordinal) =>
                "Sua assinatura Congrega+ está próxima do vencimento.\r\n\r\n"
                + "Renove para não perder o acesso ao conteúdo exclusivo.",

            _ => "Você recebeu um aviso da sua igreja pelo Congrega.",
        };

        return $"{saudacao}\r\n\r\n{miolo}\r\n\r\n—\r\nCongrega";
    }
}
