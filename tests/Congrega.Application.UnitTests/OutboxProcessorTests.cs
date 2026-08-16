using Congrega.Application.Abstractions;
using Congrega.Application.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

public sealed class OutboxProcessorTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeOutboxStore _store = new();
    private readonly OutboxOptions _opcoes = new() { BatchSize = 10, MaxAttempts = 3 };

    private OutboxProcessor Criar(params IOutboxMessageHandler[] handlers) =>
        new(_store, handlers, new FakeTimeProvider(Agora), NullLogger<OutboxProcessor>.Instance);

    private static OutboxEnvelope Mensagem(string tipo = "Teste", short tentativas = 1, long id = 1) => new()
    {
        Id = id,
        MessageType = tipo,
        Payload = "{}",
        Attempts = tentativas,
    };

    // -------------------------------------------------------------------------
    // Roteamento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Roteia_cada_mensagem_para_o_handler_do_seu_tipo()
    {
        var email = new FakeHandler("SendOtpEmail");
        var seguranca = new FakeHandler("SecurityEvent");
        _store.Enfileirar(Mensagem("SendOtpEmail", id: 1), Mensagem("SecurityEvent", id: 2));

        var resultado = await Criar(email, seguranca).ProcessBatchAsync(_opcoes, CancellationToken.None);

        Assert.Equal(2, resultado.Processed);
        Assert.Equal(1, email.Chamadas);
        Assert.Equal(1, seguranca.Chamadas);
    }

    [Fact]
    public async Task Tipo_sem_handler_vai_para_dead_letter_em_vez_de_ficar_girando()
    {
        _store.Enfileirar(Mensagem("TipoDesconhecido"));

        var resultado = await Criar().ProcessBatchAsync(_opcoes, CancellationToken.None);

        // Erro de configuração, não falha transitória: nenhuma tentativa futura vai
        // encontrar um handler que ninguém registrou. Reagendar seria manter a
        // mensagem circulando para sempre.
        Assert.Equal(1, resultado.DeadLettered);
        Assert.Equal(0, resultado.Retried);
        Assert.Contains("TipoDesconhecido", _store.DeadLetter[1], StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Falhas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Falha_permanente_nao_gasta_tentativas()
    {
        _store.Enfileirar(Mensagem());
        var handler = new FakeHandler("Teste", () => throw new PermanentDeliveryException("endereço inválido"));

        var resultado = await Criar(handler).ProcessBatchAsync(_opcoes, CancellationToken.None);

        // Endereço inválido não melhora com o tempo. Consumir as seis tentativas
        // antes de desistir só atrasaria o diagnóstico.
        Assert.Equal(1, resultado.DeadLettered);
        Assert.Empty(_store.Reagendadas);
    }

    [Fact]
    public async Task Falha_transitoria_reagenda_com_backoff()
    {
        _store.Enfileirar(Mensagem(tentativas: 1));
        var handler = new FakeHandler("Teste", () => throw new TransientDeliveryException("provedor fora do ar"));

        var resultado = await Criar(handler).ProcessBatchAsync(_opcoes, CancellationToken.None);

        Assert.Equal(1, resultado.Retried);
        Assert.True(_store.Reagendadas[1] > Agora, "a próxima tentativa precisa ficar no futuro");
    }

    [Fact]
    public async Task Excecao_desconhecida_e_tratada_como_transitoria()
    {
        _store.Enfileirar(Mensagem(tentativas: 1));
        var handler = new FakeHandler("Teste", () => throw new InvalidOperationException("algo inesperado"));

        var resultado = await Criar(handler).ProcessBatchAsync(_opcoes, CancellationToken.None);

        // O palpite conservador: desistir por engano perde a mensagem, tentar de
        // novo por engano custa uma requisição.
        Assert.Equal(1, resultado.Retried);
        Assert.Equal(0, resultado.DeadLettered);
    }

    [Fact]
    public async Task Mensagem_desiste_ao_esgotar_as_tentativas()
    {
        // MaxAttempts = 3 e a mensagem já está na terceira.
        _store.Enfileirar(Mensagem(tentativas: 3));
        var handler = new FakeHandler("Teste", () => throw new TransientDeliveryException("ainda fora"));

        var resultado = await Criar(handler).ProcessBatchAsync(_opcoes, CancellationToken.None);

        Assert.Equal(1, resultado.DeadLettered);
        Assert.Empty(_store.Reagendadas);
    }

    [Fact]
    public async Task Mensagem_venenosa_nao_bloqueia_o_lote()
    {
        // É o cenário que mata uma fila mal feita: um payload malformado impede
        // que qualquer outra mensagem seja entregue. Aqui a que falha consome as
        // tentativas dela sozinha, e as demais passam.
        _store.Enfileirar(
            Mensagem("Veneno", id: 1),
            Mensagem("Bom", id: 2),
            Mensagem("Bom", id: 3));

        var veneno = new FakeHandler("Veneno", () => throw new PermanentDeliveryException("payload inválido"));
        var bom = new FakeHandler("Bom");

        var resultado = await Criar(veneno, bom).ProcessBatchAsync(_opcoes, CancellationToken.None);

        Assert.Equal(1, resultado.DeadLettered);
        Assert.Equal(2, resultado.Processed);
        Assert.Equal(2, bom.Chamadas);
    }

    // -------------------------------------------------------------------------
    // Backoff
    // -------------------------------------------------------------------------

    [Fact]
    public void Backoff_cresce_exponencialmente_e_tem_teto()
    {
        var opcoes = new OutboxOptions { BaseBackoff = TimeSpan.FromSeconds(30) };

        var primeira = OutboxProcessor.CalcularBackoff(opcoes, 1);
        var terceira = OutboxProcessor.CalcularBackoff(opcoes, 3);
        var vigesima = OutboxProcessor.CalcularBackoff(opcoes, 20);

        Assert.InRange(primeira.TotalSeconds, 30, 39);      // 30s + até 30% de jitter
        Assert.InRange(terceira.TotalSeconds, 120, 156);    // 30 * 2^2
        Assert.Equal(TimeSpan.FromHours(1), vigesima);      // teto
    }

    [Fact]
    public void Backoff_tem_jitter()
    {
        // Sem jitter, cem mensagens que falham juntas voltam a bater no provedor
        // exatamente no mesmo instante — e a rajada atrasa a recuperação que se
        // está esperando.
        var opcoes = new OutboxOptions { BaseBackoff = TimeSpan.FromSeconds(30) };

        var amostras = Enumerable.Range(0, 40)
            .Select(_ => OutboxProcessor.CalcularBackoff(opcoes, 3).Ticks)
            .Distinct()
            .Count();

        Assert.True(amostras > 1, "o backoff precisa variar entre chamadas");
    }

    // -------------------------------------------------------------------------
    // Dublês
    // -------------------------------------------------------------------------

    private sealed class FakeOutboxStore : IOutboxStore
    {
        private readonly List<OutboxEnvelope> _fila = [];

        public List<long> Processadas { get; } = [];
        public Dictionary<long, DateTimeOffset> Reagendadas { get; } = [];
        public Dictionary<long, string> DeadLetter { get; } = [];

        public void Enfileirar(params OutboxEnvelope[] mensagens) => _fila.AddRange(mensagens);

        public Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
            int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            List<OutboxEnvelope> lote = _fila.Take(batchSize).ToList();
            _fila.RemoveRange(0, lote.Count);
            return Task.FromResult<IReadOnlyList<OutboxEnvelope>>(lote);
        }

        public Task MarkProcessedAsync(long messageId, CancellationToken cancellationToken)
        {
            Processadas.Add(messageId);
            return Task.CompletedTask;
        }

        public Task MarkForRetryAsync(
            long messageId, string failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken)
        {
            Reagendadas[messageId] = nextAttemptAt;
            return Task.CompletedTask;
        }

        public Task MarkDeadLetteredAsync(long messageId, string failure, CancellationToken cancellationToken)
        {
            DeadLetter[messageId] = failure;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler(string tipo, Action? aoProcessar = null) : IOutboxMessageHandler
    {
        public string MessageType { get; } = tipo;
        public int Chamadas { get; private set; }

        public Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
        {
            Chamadas++;
            aoProcessar?.Invoke();
            return Task.CompletedTask;
        }
    }
}
