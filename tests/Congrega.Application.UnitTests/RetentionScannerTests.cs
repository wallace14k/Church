using Congrega.Application.Retention;
using Congrega.Domain.Billing;
using Congrega.Domain.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly;

namespace Congrega.Application.UnitTests;

/// <summary>
/// Testes do caso de uso. Sem banco e sem host — o scanner só conhece portas.
/// </summary>
public sealed class RetentionScannerTests
{
    // 15/08/2026, 15:00 UTC = 12:00 em America/Sao_Paulo. Mesmo dia nos dois fusos,
    // então o teste não depende de qual deles o código usa por engano.
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 15, 15, 0, 0, TimeSpan.Zero);

    private static RetentionCandidate Candidate(long subscriptionId, long userId, int daysUntilEnd) => new()
    {
        SubscriptionId = subscriptionId,
        UserId = userId,
        TenantId = null,
        UserEmail = $"user{userId}@exemplo.com",
        UserFullName = $"Usuário {userId}",
        PlanCode = "premium_monthly",
        PeriodEnd = new DateOnly(2026, 8, 15).AddDays(daysUntilEnd),
        Status = SubscriptionStatus.Active
    };

    private static (RetentionScanner Scanner, FakeDispatcher Dispatcher, FakeRepository Repository)
        Build(IReadOnlyList<RetentionCandidate> candidates, int batchSize = 500)
    {
        var repository = new FakeRepository(candidates);
        var dispatcher = new FakeDispatcher();
        var options = Options.Create(new RetentionOptions { BatchSize = batchSize });

        var scanner = new RetentionScanner(
            repository,
            dispatcher,
            new FakeTimeProvider(UtcNow),
            ResiliencePipeline.Empty,
            options,
            NullLogger<RetentionScanner>.Instance);

        return (scanner, dispatcher, repository);
    }

    [Fact]
    public async Task Faz_fan_out_por_canal_conforme_a_janela()
    {
        // D-15 → 1 canal; D-7 → 2; D-3 → 3. Total esperado: 6 alertas.
        var (scanner, dispatcher, _) = Build([
            Candidate(1, 100, daysUntilEnd: 15),
            Candidate(2, 200, daysUntilEnd: 7),
            Candidate(3, 300, daysUntilEnd: 3)
        ]);

        var result = await scanner.ExecuteAsync(CancellationToken.None);

        Assert.Equal(3, result.CandidatesScanned);
        Assert.Equal(6, result.AlertsBuilt);
        Assert.Equal(6, result.AlertsEnqueued);

        Assert.Single(dispatcher.Received.Where(a => a.SubscriptionId == 1));
        Assert.Equal(2, dispatcher.Received.Count(a => a.SubscriptionId == 2));
        Assert.Equal(3, dispatcher.Received.Count(a => a.SubscriptionId == 3));
    }

    [Fact]
    public async Task Ignora_assinaturas_fora_de_qualquer_janela()
    {
        var (scanner, dispatcher, _) = Build([
            Candidate(1, 100, daysUntilEnd: 40),  // longe demais
            Candidate(2, 200, daysUntilEnd: -1),  // silêncio pós-vencimento
            Candidate(3, 300, daysUntilEnd: -2)   // idem
        ]);

        var result = await scanner.ExecuteAsync(CancellationToken.None);

        Assert.Equal(3, result.CandidatesScanned);
        Assert.Equal(0, result.AlertsBuilt);
        Assert.Empty(dispatcher.Received);
    }

    [Fact]
    public async Task Percorre_todas_as_paginas_com_keyset_pagination()
    {
        // 12 assinaturas, lotes de 5 → 3 páginas (5, 5, 2). Nenhuma pode ficar para trás.
        var candidates = Enumerable.Range(1, 12)
            .Select(i => Candidate(i, 100 + i, daysUntilEnd: 7))
            .ToList();

        var (scanner, dispatcher, repository) = Build(candidates, batchSize: 5);

        var result = await scanner.ExecuteAsync(CancellationToken.None);

        Assert.Equal(12, result.CandidatesScanned);
        Assert.Equal(3, result.Batches);
        Assert.Equal(24, result.AlertsBuilt);  // D-7 → 2 canais por assinatura

        // A verificação que importa: nenhuma assinatura foi pulada na virada de página.
        Assert.Equal(
            Enumerable.Range(1, 12).Select(i => (long)i),
            dispatcher.Received.Select(a => a.SubscriptionId).Distinct().Order());

        // 3 páginas de dados + nenhuma extra: o lote final incompleto encerra o laço.
        Assert.Equal(3, repository.CallCount);
    }

    [Fact]
    public async Task Nao_encerra_cedo_quando_uma_assinatura_tem_varios_destinatarios()
    {
        // Regressão de um defeito real do desenho: com assinatura de igreja, uma
        // assinatura devolve várias linhas (uma por administrador). Comparar a
        // contagem de LINHAS com o batchSize encerraria a varredura antes da hora e
        // deixaria assinaturas sem alerta. A comparação correta é de ASSINATURAS.
        var candidates = new List<RetentionCandidate>();
        for (long subscriptionId = 1; subscriptionId <= 4; subscriptionId++)
        {
            // 3 administradores por igreja → 12 linhas para 4 assinaturas.
            for (long adminId = 1; adminId <= 3; adminId++)
            {
                candidates.Add(Candidate(subscriptionId, subscriptionId * 10 + adminId, daysUntilEnd: 7) with
                {
                    TenantId = subscriptionId
                });
            }
        }

        var (scanner, dispatcher, _) = Build(candidates, batchSize: 2);

        var result = await scanner.ExecuteAsync(CancellationToken.None);

        // 4 assinaturas × 3 admins × 2 canais (D-7) = 24 alertas.
        Assert.Equal(24, result.AlertsBuilt);
        Assert.Equal(4, dispatcher.Received.Select(a => a.SubscriptionId).Distinct().Count());
        Assert.Equal(12, dispatcher.Received.Select(a => a.UserId).Distinct().Count());
    }

    [Fact]
    public async Task Contabiliza_alertas_descartados_pela_deduplicacao()
    {
        var (scanner, dispatcher, _) = Build([Candidate(1, 100, daysUntilEnd: 3)]);
        dispatcher.SimulateAllDuplicates = true;

        var result = await scanner.ExecuteAsync(CancellationToken.None);

        Assert.Equal(3, result.AlertsBuilt);
        Assert.Equal(0, result.AlertsEnqueued);
        Assert.Equal(3, result.AlertsDeduplicated);
    }

    [Fact]
    public async Task Honra_o_cancelamento_entre_lotes()
    {
        var candidates = Enumerable.Range(1, 50)
            .Select(i => Candidate(i, 100 + i, daysUntilEnd: 7))
            .ToList();

        var (scanner, _, _) = Build(candidates, batchSize: 5);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Cancelamento antes do primeiro lote: o laço nem começa e o método retorna
        // um resultado vazio, sem lançar. Shutdown de pod não vira stack trace no log.
        var result = await scanner.ExecuteAsync(cts.Token);

        Assert.Equal(0, result.CandidatesScanned);
    }

    // -------------------------------------------------------------------------
    // Dublês
    // -------------------------------------------------------------------------

    private sealed class FakeRepository(IReadOnlyList<RetentionCandidate> all) : ISubscriptionRepository
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<RetentionCandidate>> GetRetentionCandidatesAsync(
            DateOnly periodEndFrom,
            DateOnly periodEndTo,
            long afterSubscriptionId,
            int batchSize,
            CancellationToken cancellationToken)
        {
            CallCount++;

            // Reproduz o contrato da query real: o LIMIT se aplica a ASSINATURAS,
            // e todos os destinatários de cada assinatura vêm juntos.
            IReadOnlyList<RetentionCandidate> page = all
                .Where(c => c.SubscriptionId > afterSubscriptionId)
                .GroupBy(c => c.SubscriptionId)
                .OrderBy(g => g.Key)
                .Take(batchSize)
                .SelectMany(g => g)
                .ToList();

            return Task.FromResult(page);
        }
    }

    private sealed class FakeDispatcher : INotificationDispatcher
    {
        public List<RetentionAlert> Received { get; } = [];
        public bool SimulateAllDuplicates { get; set; }

        public Task<int> DispatchAsync(
            IReadOnlyCollection<RetentionAlert> alerts,
            CancellationToken cancellationToken)
        {
            Received.AddRange(alerts);
            return Task.FromResult(SimulateAllDuplicates ? 0 : alerts.Count);
        }
    }
}
