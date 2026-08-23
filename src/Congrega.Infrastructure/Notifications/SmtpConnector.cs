using System.Globalization;
using Congrega.Domain.Connectors;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Os campos que um conector SMTP guarda, lidos do dicionário de configuração.
/// </summary>
/// <remarks>
/// Existe para que o resto do código não escreva <c>settings["fromAddress"]</c>
/// espalhado: uma chave digitada errada num literal falha em tempo de execução,
/// e aqui falha num lugar só, com mensagem que nomeia o campo.
/// </remarks>
internal sealed record ConfiguracaoSmtp
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required SmtpSecurity Security { get; init; }
    public required string Username { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }

    public static ConfiguracaoSmtp Ler(IReadOnlyDictionary<string, string> settings)
    {
        string Obrigatorio(string chave) =>
            settings.TryGetValue(chave, out var valor) && !string.IsNullOrWhiteSpace(valor)
                ? valor
                : throw new InvalidOperationException(
                    $"O conector de e-mail está sem o campo \"{chave}\".");

        return new ConfiguracaoSmtp
        {
            Host = Obrigatorio("host"),
            Port = int.Parse(Obrigatorio("port"), CultureInfo.InvariantCulture),
            Security = Enum.Parse<SmtpSecurity>(Obrigatorio("security"), ignoreCase: true),
            Username = Obrigatorio("username"),
            FromAddress = Obrigatorio("fromAddress"),
            FromName = Obrigatorio("fromName"),
        };
    }

    public SecureSocketOptions ComoSocket => Security == SmtpSecurity.SslOnConnect
        ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;
}

/// <summary>
/// Abre uma sessão SMTP autenticada.
/// </summary>
/// <remarks>
/// <para>
/// Compartilhado entre o teste de conexão e o envio de verdade <b>de propósito</b>:
/// se fossem dois caminhos, o botão "Testar" poderia passar com uma configuração
/// que o envio real rejeita, e o usuário confiaria num verde que não significa
/// nada.
/// </para>
/// <para>
/// <b>Sem opção de aceitar certificado inválido.</b> A tentação é oferecer um
/// "ignorar erros de certificado" para destravar quem tem servidor mal
/// configurado — e é exatamente essa opção que transforma o TLS em teatro,
/// porque um interceptador no caminho passa a ser aceito sem reclamação.
/// </para>
/// </remarks>
internal static class SessaoSmtp
{
    /// <summary>
    /// Um provedor lento não pode segurar a requisição indefinidamente.
    /// </summary>
    /// <remarks>
    /// Vinte segundos: o suficiente para um handshake TLS mais autenticação numa
    /// rede ruim, e pouco o bastante para o usuário receber uma resposta em vez
    /// de olhar para um botão girando.
    /// </remarks>
    public const int TimeoutMs = 20_000;

    public static async Task<SmtpClient> AbrirAsync(
        ConfiguracaoSmtp config,
        string senha,
        CancellationToken cancellationToken)
    {
        var cliente = new SmtpClient { Timeout = TimeoutMs };

        try
        {
            await cliente.ConnectAsync(config.Host, config.Port, config.ComoSocket, cancellationToken);
            await cliente.AuthenticateAsync(config.Username, senha, cancellationToken);

            return cliente;
        }
        catch
        {
            // Sem isto, uma falha na autenticação deixa o socket aberto até o
            // coletor rodar — e um provedor que limita conexões simultâneas
            // passa a recusar as próximas por causa das tentativas anteriores.
            cliente.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Traduz a falha para quem configurou.
    /// </summary>
    /// <remarks>
    /// <c>535 5.7.8 Username and Password not accepted</c> manda a pessoa
    /// pesquisar o código na internet; "a senha foi recusada — o Gmail exige
    /// senha de aplicativo" resolve o problema. A causa mais comum de cada erro
    /// está escrita junto porque é o que quem lê precisa saber a seguir.
    /// </remarks>
    public static string Explicar(Exception erro) => erro switch
    {
        AuthenticationException =>
            "O servidor recusou usuário ou senha. Gmail e Outlook não aceitam a senha da conta: "
            + "é preciso gerar uma senha de aplicativo, com a verificação em duas etapas ligada.",

        // MailKit lança NotSupportedException quando o servidor não anuncia
        // STARTTLS e a configuração o exige. É o erro que aparece com a porta
        // errada — e sem esta linha ele saía em inglês, pelo caso genérico.
        NotSupportedException =>
            "O servidor não oferece STARTTLS nesta porta. Tente 587 com StartTls, "
            + "ou 465 com SslOnConnect. Se o servidor não aceitar nenhum dos dois, "
            + "ele não protege a senha e não pode ser usado.",

        SslHandshakeException =>
            "Falha no TLS. Confira a porta e a segurança: 587 usa StartTls, 465 usa SslOnConnect. "
            + "Trocar as duas é o erro mais comum.",

        SmtpCommandException smtp =>
            $"O servidor recusou o comando ({smtp.StatusCode}): {smtp.Message}",

        SmtpProtocolException =>
            "O servidor respondeu fora do protocolo SMTP. Confira se a porta é mesmo de e-mail.",

        OperationCanceledException or TimeoutException =>
            $"Sem resposta em {TimeoutMs / 1000} segundos. Confira o endereço e a porta, "
            + "e se a rede permite sair por ela.",

        _ => $"Não foi possível conectar: {erro.Message}",
    };
}

/// <summary>
/// Testa o conector de e-mail <b>enviando um e-mail de verdade</b>.
/// </summary>
/// <remarks>
/// Conectar e autenticar provaria menos do que parece: vários provedores aceitam
/// a autenticação e só recusam o envio depois, quando o remetente não confere
/// com a conta ou a conta não tem permissão de relay. O único teste que responde
/// "isto vai funcionar?" é o que passa pelo caminho inteiro.
///
/// A mensagem vai para o próprio endereço de remetente — não há para onde mais
/// mandar sem incomodar terceiros, e quem configurou tem acesso a essa caixa.
/// </remarks>
internal sealed class SmtpConnectorTester(TimeProvider clock) : IConnectorTester
{
    public ConnectorKind Kind => ConnectorKind.Smtp;

    public async Task<ConnectorTestResult> TestAsync(
        IReadOnlyDictionary<string, string> settings,
        string secret,
        CancellationToken cancellationToken)
    {
        ConfiguracaoSmtp config;

        try
        {
            config = ConfiguracaoSmtp.Ler(settings);
        }
        catch (Exception erro) when (erro is InvalidOperationException or FormatException)
        {
            return new ConnectorTestResult { Succeeded = false, Message = erro.Message };
        }

        try
        {
            using var cliente = await SessaoSmtp.AbrirAsync(config, secret, cancellationToken);

            var mensagem = new MimeMessage
            {
                Subject = "Congrega — teste de configuração de e-mail",
                Body = new TextPart("plain")
                {
                    Text =
                        "Este e-mail confirma que o Congrega consegue enviar mensagens por esta conta.\r\n\r\n"
                        + $"Servidor: {config.Host}:{config.Port} ({config.Security})\r\n"
                        + $"Enviado em: {clock.GetUtcNow():dd/MM/yyyy HH:mm} UTC\r\n\r\n"
                        + "Se você não pediu este teste, alguém com acesso de administração "
                        + "à sua igreja configurou esta conta no sistema.",
                },
            };

            mensagem.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
            mensagem.To.Add(new MailboxAddress(config.FromName, config.FromAddress));

            await cliente.SendAsync(mensagem, cancellationToken);
            await cliente.DisconnectAsync(quit: true, cancellationToken);

            return new ConnectorTestResult
            {
                Succeeded = true,
                Message = $"Enviado para {config.FromAddress}. Confira a caixa de entrada.",
            };
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Falha do provedor é o RESULTADO deste método, não exceção: quem
            // chama vai gravar o diagnóstico no conector, e propagar aqui
            // transformaria "o teste reprovou" em "o teste quebrou".
            return new ConnectorTestResult { Succeeded = false, Message = SessaoSmtp.Explicar(erro) };
        }
    }
}
