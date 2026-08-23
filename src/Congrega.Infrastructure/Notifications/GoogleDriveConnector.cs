using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Congrega.Domain.Connectors;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Testa a conta de serviço do Google e o acesso à pasta.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conta de serviço, e não OAuth com consentimento.</b> O pedido é por
/// conector manual: OAuth exige redirecionar o navegador do usuário ao Google,
/// receber o retorno numa URL registrada e guardar um refresh token que expira
/// quando a senha muda. Conta de serviço é um arquivo JSON que a pessoa cola uma
/// vez e que não expira — é o que "manual" significa aqui.
/// </para>
/// <para>
/// <b>O teste confere as duas coisas que quebram, na ordem em que quebram:</b>
/// primeiro se a chave assina (credencial válida), depois se a pasta responde.
/// A segunda é a que quase sempre falha: criar a conta de serviço não dá a ela
/// acesso a nada, e é preciso compartilhar a pasta do Drive com o e-mail dela,
/// exatamente como se compartilha com uma pessoa. Sem verificar isso, o teste
/// passaria e o primeiro backup falharia.
/// </para>
/// <para>
/// Sem SDK do Google: são duas chamadas HTTP e um JWT assinado com RSA, e o
/// pacote traria uma árvore de dependências desproporcional a isso.
/// </para>
/// </remarks>
internal sealed class GoogleDriveConnectorTester(
    IHttpClientFactory fabrica,
    TimeProvider clock) : IConnectorTester
{
    public const string HttpClientName = "google";

    /// <summary>
    /// Só leitura e escrita nos arquivos que a própria aplicação criar.
    /// </summary>
    /// <remarks>
    /// <c>drive.file</c> e não <c>drive</c>: o escopo amplo daria à chave acesso
    /// ao Drive inteiro da conta. Se esta credencial vazar, o estrago fica
    /// limitado ao que o Congrega gravou.
    /// </remarks>
    private const string Escopo = "https://www.googleapis.com/auth/drive.file";

    public ConnectorKind Kind => ConnectorKind.GoogleDrive;

    public async Task<ConnectorTestResult> TestAsync(
        IReadOnlyDictionary<string, string> settings,
        string secret,
        CancellationToken cancellationToken)
    {
        if (!settings.TryGetValue("folderId", out var folderId) || string.IsNullOrWhiteSpace(folderId))
        {
            return Falha("O conector está sem o campo \"folderId\".");
        }

        string clientEmail;
        string privateKeyPem;

        try
        {
            using var json = JsonDocument.Parse(secret);
            var raiz = json.RootElement;

            clientEmail = raiz.GetProperty("client_email").GetString() ?? string.Empty;
            privateKeyPem = raiz.GetProperty("private_key").GetString() ?? string.Empty;
        }
        catch (Exception erro) when (erro is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Falha(
                "O conteúdo colado não parece o JSON da conta de serviço. "
                + "Baixe a chave em IAM e Admin › Contas de serviço › Chaves › Adicionar chave (JSON).");
        }

        if (clientEmail.Length == 0 || privateKeyPem.Length == 0)
        {
            return Falha("O JSON da conta de serviço está sem \"client_email\" ou \"private_key\".");
        }

        var cliente = fabrica.CreateClient(HttpClientName);

        string token;

        try
        {
            token = await ObterTokenAsync(cliente, clientEmail, privateKeyPem, cancellationToken);
        }
        catch (CryptographicException)
        {
            return Falha(
                "A chave privada do JSON não pôde ser lida. O arquivo pode estar truncado — "
                + "cole o conteúdo inteiro, das chaves de abertura às de fechamento.");
        }
        catch (InvalidOperationException erro)
        {
            return Falha(erro.Message);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            return Falha($"Não foi possível autenticar no Google: {erro.Message}");
        }

        // Agora a parte que realmente falha na prática: a pasta existe e está
        // compartilhada com esta conta de serviço?
        try
        {
            using var pedido = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://www.googleapis.com/drive/v3/files/{folderId}?fields=id,name,mimeType&supportsAllDrives=true");

            pedido.Headers.Authorization = new("Bearer", token);

            using var resposta = await cliente.SendAsync(pedido, cancellationToken);
            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);

            if (!resposta.IsSuccessStatusCode)
            {
                return Falha((int)resposta.StatusCode switch
                {
                    404 => $"A pasta não foi encontrada. Compartilhe-a com {clientEmail} "
                        + "(como Editor) — criar a conta de serviço não dá acesso a nada sozinho.",
                    403 => $"Acesso negado à pasta. Confira se {clientEmail} tem permissão de Editor "
                        + "e se a API do Drive está ativada no projeto.",
                    _ => $"O Drive respondeu {(int)resposta.StatusCode}.",
                });
            }

            using var json = JsonDocument.Parse(corpo);
            var raiz = json.RootElement;
            var nome = raiz.TryGetProperty("name", out var n) ? n.GetString() : null;
            var tipo = raiz.TryGetProperty("mimeType", out var m) ? m.GetString() : null;

            // Apontar para um arquivo em vez de uma pasta passaria em tudo acima
            // e falharia no primeiro upload.
            if (tipo != "application/vnd.google-apps.folder")
            {
                return Falha(
                    $"\"{nome}\" existe, mas não é uma pasta. O id deve ser o da PASTA — "
                    + "ele aparece no fim da URL quando você a abre no Drive.");
            }

            return new ConnectorTestResult
            {
                Succeeded = true,
                Message = $"Conectado à pasta \"{nome}\" como {clientEmail}.",
            };
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            return Falha($"Não foi possível ler a pasta: {erro.Message}");
        }
    }

    /// <summary>
    /// Troca um JWT assinado por um access token — o fluxo de conta de serviço.
    /// </summary>
    /// <remarks>
    /// O JWT é montado à mão em vez de por biblioteca: são dois objetos JSON em
    /// Base64URL e uma assinatura RSA-SHA256. Uma dependência a mais para isso
    /// custaria mais do que economiza.
    /// </remarks>
    private async Task<string> ObterTokenAsync(
        HttpClient cliente,
        string clientEmail,
        string privateKeyPem,
        CancellationToken cancellationToken)
    {
        var agora = clock.GetUtcNow().ToUnixTimeSeconds();

        var cabecalho = Base64Url("""{"alg":"RS256","typ":"JWT"}"""u8.ToArray());

        var corpo = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = clientEmail,
            scope = Escopo,
            aud = "https://oauth2.googleapis.com/token",
            iat = agora,

            // Uma hora é o máximo que o Google aceita. Mais do que isso e a
            // troca é recusada com "invalid_grant", sem dizer o porquê.
            exp = agora + 3600,
        })));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var assinatura = Base64Url(rsa.SignData(
            Encoding.ASCII.GetBytes($"{cabecalho}.{corpo}"),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        using var resposta = await cliente.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = $"{cabecalho}.{corpo}.{assinatura}",
            }),
            cancellationToken);

        var texto = await resposta.Content.ReadAsStringAsync(cancellationToken);

        if (!resposta.IsSuccessStatusCode)
        {
            using var erroJson = JsonDocument.Parse(texto);

            var descricao = erroJson.RootElement.TryGetProperty("error_description", out var d)
                ? d.GetString()
                : null;

            throw new InvalidOperationException(
                descricao?.Contains("Invalid JWT Signature", StringComparison.OrdinalIgnoreCase) == true
                    ? "A assinatura foi recusada. A chave desta conta de serviço pode ter sido apagada "
                        + "no Google Cloud — gere uma nova e cole o JSON de novo."
                    : $"O Google recusou a credencial: {descricao ?? texto}");
        }

        using var json = JsonDocument.Parse(texto);

        return json.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("O Google não devolveu um token de acesso.");
    }

    /// <summary>Base64 no alfabeto de URL, sem preenchimento — o que o JWT exige.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ConnectorTestResult Falha(string mensagem) =>
        new() { Succeeded = false, Message = mensagem };
}
