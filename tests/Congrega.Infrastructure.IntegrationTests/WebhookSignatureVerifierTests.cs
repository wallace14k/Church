using Congrega.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.IntegrationTests;

/// <summary>
/// Verificação de assinatura de webhook.
/// </summary>
/// <remarks>
/// Mora neste projeto porque <c>WebhookSignatureVerifier</c> é <c>internal</c> e
/// é para cá que o <c>InternalsVisibleTo</c> aponta — mas <b>não usa
/// container</b>: são testes puros, sem banco, e rodam em milissegundos.
/// </remarks>
public sealed class WebhookSignatureVerifierTests
{
    private const string Segredo = "segredo-de-teste-nao-usar-em-producao";
    private static readonly DateTimeOffset Agora = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static WebhookSignatureVerifier Criar(TimeSpan? tolerancia = null) =>
        new(Options.Create(new PaymentOptions
        {
            WebhookSecret = Segredo,
            WebhookTolerance = tolerancia ?? TimeSpan.FromMinutes(5),
        }));

    [Fact]
    public void Aceita_assinatura_valida()
    {
        const string corpo = """{"event_id":"evt_1","type":"charge.paid"}""";
        string cabecalho = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, Agora);

        Assert.True(Criar().IsValid(corpo, cabecalho, Agora));
    }

    [Fact]
    public void Recusa_corpo_adulterado()
    {
        // O ponto do HMAC: a assinatura foi gerada para outro corpo.
        const string original = """{"event_id":"evt_1","amount":100}""";
        const string adulterado = """{"event_id":"evt_1","amount":999999}""";
        string cabecalho = WebhookSignatureVerifier.BuildHeader(original, Segredo, Agora);

        Assert.False(Criar().IsValid(adulterado, cabecalho, Agora));
    }

    [Fact]
    public void Recusa_assinatura_de_outro_segredo()
    {
        const string corpo = """{"event_id":"evt_1"}""";
        string cabecalho = WebhookSignatureVerifier.BuildHeader(corpo, "outro-segredo", Agora);

        Assert.False(Criar().IsValid(corpo, cabecalho, Agora));
    }

    [Fact]
    public void Recusa_evento_antigo_replay()
    {
        // Um evento legítimo capturado ontem continua com assinatura VÁLIDA
        // para sempre. Só o timestamp o impede de ser reapresentado — é este
        // teste que prova que a proteção de replay existe.
        const string corpo = """{"event_id":"evt_1"}""";
        var ontem = Agora.AddDays(-1);
        string cabecalho = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, ontem);

        Assert.False(Criar().IsValid(corpo, cabecalho, Agora));
    }

    [Fact]
    public void Recusa_evento_do_futuro_alem_da_tolerancia()
    {
        // Relógio adiantado do atacante não deve comprar janela extra.
        const string corpo = """{"event_id":"evt_1"}""";
        string cabecalho = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, Agora.AddHours(1));

        Assert.False(Criar().IsValid(corpo, cabecalho, Agora));
    }

    [Fact]
    public void Aceita_dentro_da_tolerancia_nos_dois_sentidos()
    {
        // Diferença de relógio entre servidores é normal; a janela existe para
        // isso, não para permitir replay.
        const string corpo = """{"event_id":"evt_1"}""";

        string doPassado = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, Agora.AddMinutes(-2));
        string doFuturo = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, Agora.AddMinutes(2));

        Assert.True(Criar().IsValid(corpo, doPassado, Agora));
        Assert.True(Criar().IsValid(corpo, doFuturo, Agora));
    }

    [Fact]
    public void Recusa_quando_nao_ha_segredo_configurado()
    {
        // Falhar FECHADO. Um endpoint de webhook sem segredo configurado
        // aceitaria "pagamento confirmado" de qualquer um na internet.
        const string corpo = """{"event_id":"evt_1"}""";
        string cabecalho = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, Agora);

        var semSegredo = new WebhookSignatureVerifier(
            Options.Create(new PaymentOptions { WebhookSecret = string.Empty }));

        Assert.False(semSegredo.IsValid(corpo, cabecalho, Agora));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("v1=abc")]                       // sem timestamp
    [InlineData("t=1700000000")]                 // sem assinatura
    [InlineData("t=abc,v1=deadbeef")]            // timestamp não numérico
    [InlineData("t=1700000000,v1=nao-e-hex")]    // assinatura não hexadecimal
    public void Recusa_cabecalho_malformado(string? cabecalho)
    {
        // Cabeçalho malformado é recusa, nunca exceção que sobe: o remetente é
        // entrada não confiável por definição.
        const string corpo = """{"event_id":"evt_1"}""";

        Assert.False(Criar().IsValid(corpo, cabecalho, Agora));
    }

    [Fact]
    public void Assinatura_cobre_o_timestamp_e_nao_so_o_corpo()
    {
        // Se o timestamp ficasse FORA do que é assinado, bastaria trocá-lo num
        // evento capturado para reapresentá-lo para sempre. Este teste monta
        // exatamente esse ataque: assinatura válida de ontem, timestamp de hoje.
        const string corpo = """{"event_id":"evt_1"}""";
        var ontem = Agora.AddDays(-1);

        string cabecalhoAntigo = WebhookSignatureVerifier.BuildHeader(corpo, Segredo, ontem);
        string assinaturaAntiga = cabecalhoAntigo.Split(",v1=")[1];

        string forjado = $"t={Agora.ToUnixTimeSeconds()},v1={assinaturaAntiga}";

        Assert.False(Criar().IsValid(corpo, forjado, Agora));
    }
}
