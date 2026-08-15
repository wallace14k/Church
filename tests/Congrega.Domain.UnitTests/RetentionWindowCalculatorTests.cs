using Congrega.Domain.Retention;

namespace Congrega.Domain.UnitTests;

/// <summary>
/// Testes da lógica de janelas de retenção.
/// </summary>
/// <remarks>
/// Sem banco, sem mock, sem relógio. Toda a regra que decide "quem recebe qual
/// alerta hoje" é uma função pura, e é por isso que ela pôde ser posta no domínio.
/// </remarks>
public sealed class RetentionWindowCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static DateOnly PeriodEndIn(int days) => Today.AddDays(days);

    // -------------------------------------------------------------------------
    // Faixas — o comportamento central
    // -------------------------------------------------------------------------

    [Theory]
    // Longe demais: nenhum alerta. Avisar com 20 dias só ensina o usuário a ignorar.
    [InlineData(60, null)]
    [InlineData(30, null)]
    [InlineData(16, null)]

    // Faixa D-15: de 15 a 8 dias restantes.
    [InlineData(15, RetentionAlertWindow.D15)]
    [InlineData(12, RetentionAlertWindow.D15)]
    [InlineData(8, RetentionAlertWindow.D15)]

    // Faixa D-7: de 7 a 4.
    [InlineData(7, RetentionAlertWindow.D7)]
    [InlineData(5, RetentionAlertWindow.D7)]
    [InlineData(4, RetentionAlertWindow.D7)]

    // Faixa D-3: 3 e 2.
    [InlineData(3, RetentionAlertWindow.D3)]
    [InlineData(2, RetentionAlertWindow.D3)]

    // Faixa D-1: 1 e 0. Zero é o próprio dia do vencimento.
    [InlineData(1, RetentionAlertWindow.D1)]
    [InlineData(0, RetentionAlertWindow.D1)]

    // Silêncio deliberado logo após vencer: o D-1 acabou de sair.
    [InlineData(-1, null)]
    [InlineData(-2, null)]

    // Grace period: última chamada.
    [InlineData(-3, RetentionAlertWindow.GraceD3)]
    [InlineData(-5, RetentionAlertWindow.GraceD3)]
    public void Resolve_devolve_a_janela_correta_para_cada_faixa(
        int daysRemaining,
        RetentionAlertWindow? expected)
    {
        var actual = RetentionWindowCalculator.Resolve(PeriodEndIn(daysRemaining), Today);

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Fronteiras — onde erro de "off by one" costuma se esconder
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(16, null, RetentionAlertWindow.D15)]                          // fora → D15
    [InlineData(8, RetentionAlertWindow.D15, RetentionAlertWindow.D7)]        // D15 → D7
    [InlineData(4, RetentionAlertWindow.D7, RetentionAlertWindow.D3)]         // D7  → D3
    [InlineData(2, RetentionAlertWindow.D3, RetentionAlertWindow.D1)]         // D3  → D1
    public void Resolve_muda_de_janela_exatamente_na_fronteira(
        int boundaryDays,
        RetentionAlertWindow? atBoundary,
        RetentionAlertWindow nextWindow)
    {
        Assert.Equal(atBoundary, RetentionWindowCalculator.Resolve(PeriodEndIn(boundaryDays), Today));
        Assert.Equal(nextWindow, RetentionWindowCalculator.Resolve(PeriodEndIn(boundaryDays - 1), Today));
    }

    // -------------------------------------------------------------------------
    // A propriedade que justifica usar faixas em vez de igualdade exata
    // -------------------------------------------------------------------------

    [Fact]
    public void Assinatura_que_atravessa_uma_janela_com_o_job_parado_ainda_recebe_alerta()
    {
        // Cenário real: o worker fica fora do ar do dia 8 ao dia 11 (deploy travado,
        // incidente no cluster). A assinatura vence no dia 20.
        var periodEnd = new DateOnly(2026, 8, 20);

        // Dia 5: 15 dias restantes → D15 sai normalmente.
        Assert.Equal(
            RetentionAlertWindow.D15,
            RetentionWindowCalculator.Resolve(periodEnd, new DateOnly(2026, 8, 5)));

        // Dia 13 seria exatamente D-7. O worker estava parado e não rodou nesse dia.
        // Com comparação por igualdade (daysRemaining == 7), o alerta D7 estaria
        // perdido para sempre.

        // Dia 16, worker de volta: 4 dias restantes. Ainda dentro da faixa D-7,
        // então o alerta sai — atrasado, mas sai.
        Assert.Equal(
            RetentionAlertWindow.D7,
            RetentionWindowCalculator.Resolve(periodEnd, new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void Assinatura_criada_no_meio_de_uma_faixa_recebe_o_alerta_daquela_faixa()
    {
        // Trial de 5 dias: a assinatura nasce já dentro da faixa D-7 e nunca passará
        // por D-15. Sem faixas, esse usuário só receberia o primeiro aviso em D-3.
        var periodEnd = Today.AddDays(5);

        Assert.Equal(RetentionAlertWindow.D7, RetentionWindowCalculator.Resolve(periodEnd, Today));
    }

    [Fact]
    public void Cada_janela_e_alcancada_exatamente_uma_vez_ao_longo_do_ciclo()
    {
        // Percorre dia a dia um ciclo completo e coleta a sequência de janelas.
        var periodEnd = new DateOnly(2026, 9, 1);
        var observed = new List<RetentionAlertWindow>();

        for (int offset = -20; offset <= 5; offset++)
        {
            var window = RetentionWindowCalculator.Resolve(periodEnd, periodEnd.AddDays(offset));
            if (window is not null && (observed.Count == 0 || observed[^1] != window.Value))
            {
                observed.Add(window.Value);
            }
        }

        // A ordem importa: o escalonamento precisa ser monotonicamente mais urgente.
        Assert.Equal(
            new[]
            {
                RetentionAlertWindow.D15,
                RetentionAlertWindow.D7,
                RetentionAlertWindow.D3,
                RetentionAlertWindow.D1,
                RetentionAlertWindow.GraceD3
            },
            observed);
    }

    // -------------------------------------------------------------------------
    // Escalonamento de canais
    // -------------------------------------------------------------------------

    [Fact]
    public void Canais_escalam_conforme_a_urgencia_da_janela()
    {
        // D-15 usa apenas e-mail: push no aviso mais distante gasta o canal mais
        // intrusivo no momento de menor urgência.
        Assert.Equal(
            new[] { NotificationChannel.Email },
            RetentionWindowCalculator.ChannelsFor(RetentionAlertWindow.D15));

        Assert.Equal(
            new[] { NotificationChannel.Email, NotificationChannel.Push },
            RetentionWindowCalculator.ChannelsFor(RetentionAlertWindow.D7));

        // Da faixa D-3 em diante, todos os canais.
        var allChannels = new[]
        {
            NotificationChannel.Email, NotificationChannel.Push, NotificationChannel.InAppBanner
        };

        foreach (var window in new[]
                 {
                     RetentionAlertWindow.D3, RetentionAlertWindow.D1, RetentionAlertWindow.GraceD3
                 })
        {
            Assert.Equal(allChannels, RetentionWindowCalculator.ChannelsFor(window));
        }
    }

    [Theory]
    [InlineData(RetentionAlertWindow.D15, "retention.d15")]
    [InlineData(RetentionAlertWindow.D7, "retention.d7")]
    [InlineData(RetentionAlertWindow.D3, "retention.d3")]
    [InlineData(RetentionAlertWindow.D1, "retention.d1")]
    [InlineData(RetentionAlertWindow.GraceD3, "retention.grace.d3")]
    public void Cada_janela_mapeia_para_seu_template(RetentionAlertWindow window, string expected)
    {
        Assert.Equal(expected, RetentionWindowCalculator.TemplateCodeFor(window));
    }
}
