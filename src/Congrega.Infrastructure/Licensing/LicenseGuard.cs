using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.Licensing;

/// <summary>Situação da licença desta instalação, num instante.</summary>
public sealed record LicenseStatus
{
    public required bool Valid { get; init; }

    /// <summary>Motivo em português, para a tela e para o log.</summary>
    public required string Reason { get; init; }

    /// <summary>Até quando o veredito assinado continua válido por si só.</summary>
    public required DateTimeOffset ValidUntil { get; init; }

    /// <summary>
    /// O servidor central está inacessível e este veredito é o último conhecido.
    /// </summary>
    /// <remarks>
    /// Distinto de <c>Valid</c> de propósito: a tela pode avisar "não
    /// conseguimos confirmar sua assinatura desde terça" sem bloquear ninguém,
    /// e o operador descobre o problema antes de a tolerância acabar.
    /// </remarks>
    public bool InGrace { get; init; }

    public static LicenseStatus Recusada(string motivo) =>
        new() { Valid = false, Reason = motivo, ValidUntil = DateTimeOffset.MinValue };
}

public interface ILicenseGuard
{
    /// <summary>
    /// A licença vale agora?
    /// </summary>
    /// <remarks>
    /// Barata de chamar: responde do cache na esmagadora maioria das vezes, e
    /// colapsa chamadas concorrentes numa só quando precisa ir à rede.
    /// </remarks>
    Task<LicenseStatus> AvaliarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Valida a licença contra o servidor central, com cache, chamada única e
/// tolerância a indisponibilidade.
/// </summary>
/// <remarks>
/// <para>
/// <b>Isto não é a barreira de segurança.</b> Roda no servidor do cliente, com
/// o código-fonte na mão dele: quem quiser burlar troca este arquivo por um
/// <c>return Valid = true</c> e recompila. A barreira de verdade é o servidor
/// central <b>não emitir a URL assinada</b> sem licença ativa — e o conteúdo não
/// existir na infraestrutura do cliente. Ver a seção 3 de
/// <c>docs/08-self-hosted-e-licenciamento.md</c>.
/// </para>
/// <para>
/// O que este componente resolve é outra coisa, e é real: <b>não derrubar o
/// servidor central</b> e <b>falhar rápido e com mensagem clara</b> quando a
/// assinatura não está em dia.
/// </para>
/// <para>
/// <b>Singleton, e não <c>IMemoryCache</c>.</b> Um cache genérico resolve o
/// "guardar" e não resolve os dois problemas que realmente aparecem aqui:
/// </para>
/// <list type="number">
/// <item>
/// <b>Debandada no cache frio.</b> Com <c>IMemoryCache</c>, cinquenta membros
/// abrindo o app às 19h de domingo produzem cinquenta requisições simultâneas ao
/// servidor central — o padrão "verifica, não achou, busca" não tem exclusão
/// entre as verificações. O <see cref="_portao"/> colapsa todas numa só.
/// </item>
/// <item>
/// <b>Último veredito bom.</b> Uma entrada de cache expira e some. Aqui a
/// resposta antiga precisa <i>sobreviver</i> à expiração, porque é ela que
/// sustenta a janela de tolerância quando o servidor central cai.
/// </item>
/// </list>
/// </remarks>
internal sealed class LicenseGuard : ILicenseGuard, IDisposable
{
    /// <summary>Uma instância só: montar as opções a cada veredito é alocação pura.</summary>
    private static readonly JsonSerializerOptions JsonPadrao = new(JsonSerializerDefaults.Web);

    /// <summary>Nome do cliente HTTP. Ver o registro em <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "licenca-central";

    private readonly IHttpClientFactory _fabrica;
    private readonly LicenseOptions _opcoes;
    private readonly TimeProvider _relogio;
    private readonly ILogger<LicenseGuard> _log;

    /// <summary>Colapsa as chamadas concorrentes numa só ida à rede.</summary>
    private readonly SemaphoreSlim _portao = new(1, 1);

    private LicenseStatus? _ultimo;
    private DateTimeOffset _consultadoEm = DateTimeOffset.MinValue;

    /// <summary>
    /// Maior instante já observado, para detectar o relógio andando para trás.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uma licença expirada volta a valer se o servidor tiver a data recuada —
    /// e num servidor do próprio cliente isso é um comando. Guardar o maior
    /// instante visto e recusar qualquer leitura anterior a ele fecha o truque
    /// mais barato.
    /// </para>
    /// <para>
    /// <b>Em memória, e por isso zera no restart.</b> Sobreviver a reinício
    /// exige gravar o instante no banco local do cliente — uma linha só. Está
    /// registrado como pendência na seção 3 do documento; sem isso, reiniciar o
    /// contêiner apaga a proteção.
    /// </para>
    /// </remarks>
    private DateTimeOffset _maiorInstanteVisto = DateTimeOffset.MinValue;

    public LicenseGuard(
        IHttpClientFactory fabrica,
        IOptions<LicenseOptions> opcoes,
        TimeProvider relogio,
        ILogger<LicenseGuard> log)
    {
        _fabrica = fabrica;
        _opcoes = opcoes.Value;
        _relogio = relogio;
        _log = log;
    }

    public async Task<LicenseStatus> AvaliarAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.Key))
        {
            return LicenseStatus.Recusada(
                "Esta instalação não tem uma chave do Congrega+ configurada.");
        }

        var agora = Agora();

        // Caminho quente: sem rede, sem trava.
        if (_ultimo is { } cacheado && agora - _consultadoEm < _opcoes.RefreshInterval)
        {
            return cacheado;
        }

        await _portao.WaitAsync(cancellationToken);

        try
        {
            // **Segunda verificação, agora sob a trava.** É ela que faz o
            // colapso valer: as quarenta e nove requisições que esperaram na
            // fila encontram o resultado que a primeira trouxe e voltam sem
            // tocar na rede. Sem esta linha, a trava só serializa a debandada —
            // não a evita.
            var depoisDaFila = Agora();

            if (_ultimo is { } recente && depoisDaFila - _consultadoEm < _opcoes.RefreshInterval)
            {
                return recente;
            }

            return await ConsultarAsync(depoisDaFila, cancellationToken);
        }
        finally
        {
            _portao.Release();
        }
    }

    private async Task<LicenseStatus> ConsultarAsync(
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        try
        {
            var cliente = _fabrica.CreateClient(HttpClientName);

            using var origem = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            origem.CancelAfter(_opcoes.Timeout);

            using var resposta = await cliente.GetAsync("v1/license/status", origem.Token);

            if (resposta.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.PaymentRequired)
            {
                // Recusa EXPLÍCITA do servidor central: assinatura vencida ou
                // licença revogada. Não é indisponibilidade, e por isso não
                // entra na tolerância — o veredito velho é descartado na hora.
                _ultimo = LicenseStatus.Recusada(
                    "A assinatura do Congrega+ não está ativa para esta instalação.");
                _consultadoEm = agora;

                _log.LogWarning("Licença recusada pelo servidor central ({Status}).", resposta.StatusCode);

                return _ultimo;
            }

            resposta.EnsureSuccessStatusCode();

            var corpo = await resposta.Content.ReadFromJsonAsync<VereditoAssinado>(origem.Token)
                ?? throw new InvalidOperationException("Resposta vazia do servidor de licenças.");

            var status = Verificar(corpo, agora);

            _ultimo = status;
            _consultadoEm = agora;

            return status;
        }
        catch (Exception erro) when (erro is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return SobTolerancia(agora, erro);
        }
    }

    /// <summary>
    /// O servidor central não respondeu. Vale o último veredito bom?
    /// </summary>
    private LicenseStatus SobTolerancia(DateTimeOffset agora, Exception erro)
    {
        if (_ultimo is { Valid: true } bom && agora < bom.ValidUntil)
        {
            _log.LogWarning(
                erro,
                "Servidor de licenças inacessível. Usando o último veredito, válido até {ValidUntil}.",
                bom.ValidUntil);

            return bom with { InGrace = true };
        }

        _log.LogError(erro, "Servidor de licenças inacessível e sem veredito válido em cache.");

        return LicenseStatus.Recusada(
            "Não foi possível confirmar a assinatura do Congrega+ com o servidor da Congrega.");
    }

    /// <summary>
    /// Confere a assinatura do veredito e a coerência do relógio.
    /// </summary>
    /// <remarks>
    /// A assinatura é o que impede o cliente de responder a si mesmo: com um
    /// <c>hosts</c> apontando o domínio central para <c>localhost</c>, qualquer
    /// um devolve <c>{"valid":true}</c>. Sem a chave privada, ninguém devolve um
    /// <c>{"valid":true}</c> <b>assinado</b>.
    /// </remarks>
    private LicenseStatus Verificar(VereditoAssinado veredito, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.PublicKeyPem))
        {
            // Falhar fechado. Sem a chave pública não há como distinguir o
            // servidor central de qualquer coisa que responda JSON — e aceitar
            // nesse estado é o mesmo que não verificar.
            return LicenseStatus.Recusada(
                "Instalação sem a chave pública de licenciamento. Confira Licensing__PublicKeyPem.");
        }

        byte[] carga;
        byte[] assinatura;

        try
        {
            carga = Base64Url(veredito.Payload);
            assinatura = Base64Url(veredito.Signature);
        }
        catch (FormatException)
        {
            return LicenseStatus.Recusada("Resposta do servidor de licenças malformada.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_opcoes.PublicKeyPem);

        if (!rsa.VerifyData(carga, assinatura, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            _log.LogError("Veredito de licença com assinatura inválida — origem não confiável.");

            return LicenseStatus.Recusada("A resposta do servidor de licenças não pôde ser verificada.");
        }

        var conteudo = JsonSerializer.Deserialize<VereditoConteudo>(
            Encoding.UTF8.GetString(carga),
            JsonPadrao);

        if (conteudo is null)
        {
            return LicenseStatus.Recusada("Resposta do servidor de licenças ilegível.");
        }

        // O veredito é para ESTA instalação. Sem esta checagem, a resposta
        // assinada de uma igreja assinante serve para todas as outras — bastaria
        // capturá-la uma vez e reproduzi-la.
        if (!string.Equals(conteudo.LicenseKey, _opcoes.Key, StringComparison.Ordinal))
        {
            _log.LogError("Veredito assinado para outra licença — possível reprodução de resposta.");

            return LicenseStatus.Recusada("A resposta recebida não corresponde a esta instalação.");
        }

        if (!conteudo.Valid)
        {
            return LicenseStatus.Recusada(
                conteudo.Reason ?? "A assinatura do Congrega+ não está ativa.");
        }

        return new LicenseStatus
        {
            Valid = true,
            Reason = "Assinatura ativa.",
            // O veredito vale até a menor entre a validade que ele declara e a
            // janela de tolerância local. O servidor central manda no teto; a
            // tolerância só encurta, nunca estende.
            ValidUntil = Menor(conteudo.ValidUntil, agora + _opcoes.GraceWindow),
        };
    }

    /// <summary>
    /// Agora, monotônico.
    /// </summary>
    /// <remarks>
    /// Recuar o relógio do servidor é a forma mais barata de esticar uma licença
    /// vencida. Guardar o maior instante já visto e nunca voltar atrás dele
    /// custa uma comparação e fecha o truque.
    /// </remarks>
    private DateTimeOffset Agora()
    {
        var lido = _relogio.GetUtcNow();

        if (lido < _maiorInstanteVisto)
        {
            _log.LogWarning(
                "Relógio do servidor recuou de {Visto} para {Lido}. Usando o maior instante visto.",
                _maiorInstanteVisto,
                lido);

            return _maiorInstanteVisto;
        }

        _maiorInstanteVisto = lido;

        return lido;
    }

    private static DateTimeOffset Menor(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static byte[] Base64Url(string valor)
    {
        var normalizado = valor.Replace('-', '+').Replace('_', '/');
        var faltando = normalizado.Length % 4;

        if (faltando > 0)
        {
            normalizado += new string('=', 4 - faltando);
        }

        return Convert.FromBase64String(normalizado);
    }

    public void Dispose() => _portao.Dispose();

    /// <summary>O que o servidor central devolve: a carga e a assinatura dela.</summary>
    private sealed record VereditoAssinado
    {
        public string Payload { get; init; } = string.Empty;
        public string Signature { get; init; } = string.Empty;
    }

    /// <summary>O conteúdo assinado, depois de verificado.</summary>
    private sealed record VereditoConteudo
    {
        public bool Valid { get; init; }
        public string LicenseKey { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public DateTimeOffset ValidUntil { get; init; }
    }
}
