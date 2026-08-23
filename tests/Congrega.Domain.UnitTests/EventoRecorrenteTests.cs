using Congrega.Domain.Calendar;

namespace Congrega.Domain.UnitTests;

public sealed class EventoRecorrenteTests
{
    private static readonly TimeZoneInfo SaoPaulo =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Domingo, 2 de agosto de 2026, 19h em São Paulo (UTC−3).</summary>
    private static readonly DateTimeOffset DomingoAs19 =
        new(2026, 8, 2, 22, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DomingoAs21 =
        new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<(DateTimeOffset StartsAt, DateTimeOffset EndsAt)> Repeticoes() =>
        CalendarEvent.CalcularRepeticoesSemanais(DomingoAs19, DomingoAs21, SaoPaulo);

    [Fact]
    public void Cobre_doze_meses_de_domingos()
    {
        // Doze meses de semanas dão 52 repetições, e o original não entra na
        // conta — ele já existe.
        var repeticoes = Repeticoes();

        Assert.InRange(repeticoes.Count, 51, 53);
    }

    [Fact]
    public void O_original_NAO_entra_na_lista()
    {
        // Ele é a semente da série; incluí-lo geraria o mesmo culto duas vezes
        // no mesmo domingo — e o índice único do banco recusaria a segunda.
        Assert.DoesNotContain(Repeticoes(), r => r.StartsAt == DomingoAs19);
    }

    [Fact]
    public void A_primeira_repeticao_e_exatamente_uma_semana_depois()
    {
        Assert.Equal(DomingoAs19.AddDays(7), Repeticoes()[0].StartsAt);
    }

    [Fact]
    public void Todas_caem_no_MESMO_dia_da_semana()
    {
        // "Todo domingo" precisa continuar sendo domingo na quinquagésima
        // repetição. Verificado no fuso da igreja, que é onde o domingo é
        // domingo — em UTC, um culto de domingo às 19h já é segunda-feira.
        foreach (var (inicio, _) in Repeticoes())
        {
            var local = TimeZoneInfo.ConvertTime(inicio, SaoPaulo);
            Assert.Equal(DayOfWeek.Sunday, local.DayOfWeek);
        }
    }

    [Fact]
    public void Todas_mantem_o_MESMO_horario_no_relogio_da_igreja()
    {
        // **É a razão de a conta ser feita no relógio de parede.** Somar 168
        // horas ao instante dá o mesmo resultado hoje e daria uma hora de
        // diferença se o Brasil voltasse ao horário de verão — o culto das 19h
        // cairia às 18h no meio da série, e ninguém olharia a agenda de agosto
        // para descobrir isso em outubro.
        foreach (var (inicio, _) in Repeticoes())
        {
            var local = TimeZoneInfo.ConvertTime(inicio, SaoPaulo);
            Assert.Equal(19, local.Hour);
            Assert.Equal(0, local.Minute);
        }
    }

    [Fact]
    public void A_duracao_e_preservada_em_todas()
    {
        var duracao = DomingoAs21 - DomingoAs19;

        Assert.All(Repeticoes(), r => Assert.Equal(duracao, r.EndsAt - r.StartsAt));
    }

    [Fact]
    public void Nenhuma_ultrapassa_o_horizonte()
    {
        var limite = DomingoAs19.AddMonths(CalendarEvent.HorizonteEmMeses);

        Assert.All(Repeticoes(), r => Assert.True(r.StartsAt <= limite));
    }

    [Fact]
    public void Cada_repeticao_sai_da_ORIGINAL_e_nao_da_anterior()
    {
        // Calcular a partir da anterior acumula erro. Semanal é menos suscetível
        // que mensal — foi lá que o bug apareceu — mas a regra é a mesma, e o
        // teste fixa que ela vale aqui: a repetição N está exatamente 7N dias do
        // início, sem deriva.
        var repeticoes = Repeticoes();

        for (var i = 0; i < repeticoes.Count; i++)
        {
            var esperado = TimeZoneInfo
                .ConvertTime(DomingoAs19, SaoPaulo)
                .DateTime.AddDays(7 * (i + 1));

            var obtido = TimeZoneInfo.ConvertTime(repeticoes[i].StartsAt, SaoPaulo).DateTime;

            Assert.Equal(esperado, obtido);
        }
    }

    [Fact]
    public void Periodo_invertido_e_recusado_antes_de_gerar_cinquenta_linhas()
    {
        // Falhar aqui, e não na décima repetição: um fim antes do início produz
        // uma série inteira de eventos impossíveis.
        Assert.Throws<ArgumentException>(() =>
            CalendarEvent.CalcularRepeticoesSemanais(DomingoAs21, DomingoAs19, SaoPaulo));
    }

    [Fact]
    public void Evento_da_serie_guarda_a_identidade_dela()
    {
        var serie = Guid.NewGuid();

        var evento = CalendarEvent.Schedule(
            tenantId: 1,
            title: "Culto de domingo",
            startsAt: DomingoAs19,
            endsAt: DomingoAs21,
            now: DomingoAs19,
            seriesId: serie);

        Assert.Equal(serie, evento.SeriesId);
    }

    [Fact]
    public void Evento_avulso_nao_tem_serie()
    {
        var evento = CalendarEvent.Schedule(1, "Retiro", DomingoAs19, DomingoAs21, DomingoAs19);

        Assert.Null(evento.SeriesId);
    }
}
