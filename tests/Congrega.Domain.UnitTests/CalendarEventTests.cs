using Congrega.Domain.Calendar;

namespace Congrega.Domain.UnitTests;

public sealed class CalendarEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Inicio = new(2026, 8, 16, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Fim = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Culto() =>
        CalendarEvent.Schedule(tenantId: 1, title: "Culto de domingo", startsAt: Inicio, endsAt: Fim, now: Now);

    [Fact]
    public void Schedule_normaliza_espacos_do_titulo()
    {
        var evento = CalendarEvent.Schedule(1, "  Culto   de   domingo ", Inicio, Fim, Now);
        Assert.Equal("Culto de domingo", evento.Title);
    }

    [Fact]
    public void Schedule_nasce_agendado()
    {
        Assert.Equal(EventStatus.Agendado, Culto().Status);
    }

    [Fact]
    public void Schedule_recusa_titulo_vazio()
    {
        Assert.Throws<ArgumentException>(() => CalendarEvent.Schedule(1, "   ", Inicio, Fim, Now));
    }

    [Fact]
    public void Schedule_recusa_fim_antes_do_comeco()
    {
        Assert.Throws<ArgumentException>(() => CalendarEvent.Schedule(1, "Culto", Fim, Inicio, Now));
    }

    [Fact]
    public void Schedule_recusa_fim_igual_ao_comeco()
    {
        // Duração zero some de qualquer consulta por sobreposição de intervalo:
        // o evento existiria no banco e nunca apareceria na agenda.
        Assert.Throws<ArgumentException>(() => CalendarEvent.Schedule(1, "Culto", Inicio, Inicio, Now));
    }

    [Fact]
    public void Schedule_aceita_evento_no_passado()
    {
        // Registrar um culto que já aconteceu é legítimo — a agenda também é
        // histórico. Diferente de lançamento financeiro, aqui não há data
        // "impossível" para trás.
        var passado = CalendarEvent.Schedule(
            1, "Culto retroativo", Now.AddDays(-30), Now.AddDays(-30).AddHours(2), Now);

        Assert.Equal(EventStatus.Agendado, passado.Status);
    }

    [Fact]
    public void Schedule_limpa_descricao_e_local_em_branco()
    {
        var evento = CalendarEvent.Schedule(1, "Culto", Inicio, Fim, Now, description: "  ", location: "");

        Assert.Null(evento.Description);
        Assert.Null(evento.Location);
    }

    [Fact]
    public void Update_troca_titulo_e_horario_juntos()
    {
        var evento = Culto();
        var novoInicio = Inicio.AddHours(1);
        var novoFim = Fim.AddHours(1);

        evento.Update("Culto especial", novoInicio, novoFim, Now, location: "Templo");

        Assert.Equal("Culto especial", evento.Title);
        Assert.Equal(novoInicio, evento.StartsAt);
        Assert.Equal(novoFim, evento.EndsAt);
        Assert.Equal("Templo", evento.Location);
    }

    [Fact]
    public void Update_recusa_periodo_invertido()
    {
        var evento = Culto();
        Assert.Throws<ArgumentException>(() => evento.Update("Culto", Fim, Inicio, Now));
    }

    [Fact]
    public void Update_recusa_titulo_vazio()
    {
        var evento = Culto();
        Assert.Throws<ArgumentException>(() => evento.Update(" ", Inicio, Fim, Now));
    }

    [Fact]
    public void Cancel_marca_sem_apagar()
    {
        var evento = Culto();
        evento.Cancel(Now);

        Assert.Equal(EventStatus.Cancelado, evento.Status);
        // O evento continua completo: quem já sabia do culto precisa encontrar
        // o registro e ver que foi cancelado, não um vazio.
        Assert.Equal("Culto de domingo", evento.Title);
        Assert.Equal(Inicio, evento.StartsAt);
    }

    [Fact]
    public void Reactivate_desfaz_o_cancelamento()
    {
        var evento = Culto();
        evento.Cancel(Now);
        evento.Reactivate(Now);

        Assert.Equal(EventStatus.Agendado, evento.Status);
    }

    [Fact]
    public void Schedule_normaliza_o_instante_para_offset_zero()
    {
        // Não basta o instante estar certo: o **offset** precisa ser zero. O
        // Npgsql recusa gravar DateTimeOffset com offset diferente de zero em
        // `timestamptz`, e o cliente manda -03:00 — a primeira versão deste
        // teste comparava `.UtcDateTime.Hour`, que converte, e passava verde
        // enquanto a API devolvia 500 ao agendar qualquer culto.
        var comOffset = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.FromHours(-3));
        var evento = CalendarEvent.Schedule(1, "Culto", comOffset, comOffset.AddHours(2), Now);

        Assert.Equal(TimeSpan.Zero, evento.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, evento.EndsAt.Offset);

        // E o instante continua o mesmo: 19h em São Paulo é 22h UTC.
        Assert.Equal(22, evento.StartsAt.Hour);
        Assert.Equal(comOffset, evento.StartsAt);
    }

    [Fact]
    public void Update_tambem_normaliza_o_offset()
    {
        var evento = Culto();
        var comOffset = new DateTimeOffset(2026, 8, 18, 20, 0, 0, TimeSpan.FromHours(-3));

        evento.Update("Culto", comOffset, comOffset.AddHours(2), Now);

        Assert.Equal(TimeSpan.Zero, evento.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, evento.EndsAt.Offset);
    }

    [Fact]
    public void EventQuery_normaliza_a_janela_para_offset_zero()
    {
        // O caminho que a normalização da entidade NÃO cobria: o filtro. O
        // Npgsql recusa offset != 0 também em parâmetro de consulta, e a agenda
        // respondia 500 a qualquer busca vinda do app.
        var inicio = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(-3));
        var fim = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(-3));

        var janela = new EventQuery { From = inicio, To = fim };

        Assert.Equal(TimeSpan.Zero, janela.From.Offset);
        Assert.Equal(TimeSpan.Zero, janela.To.Offset);

        // Instante preservado: meia-noite em São Paulo é 03:00 UTC.
        Assert.Equal(inicio, janela.From);
        Assert.Equal(3, janela.From.Hour);
    }
}
