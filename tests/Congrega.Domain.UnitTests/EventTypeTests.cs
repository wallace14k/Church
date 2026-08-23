using Congrega.Domain.Calendar;

namespace Congrega.Domain.UnitTests;

public sealed class EventTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Depois = Now.AddHours(1);

    private static EventType Culto() => EventType.Register(1, "Culto", "heart", Now);

    [Fact]
    public void Register_nasce_ativo()
    {
        Assert.True(Culto().IsActive);
    }

    [Fact]
    public void Register_recusa_nome_vazio()
    {
        Assert.Throws<ArgumentException>(() => EventType.Register(1, "   ", "heart", Now));
    }

    [Fact]
    public void Register_recusa_tenant_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventType.Register(0, "Culto", "heart", Now));
    }

    /// <summary>
    /// Sem isto, "Culto  de  Oração" e "Culto de Oração" escapariam do índice
    /// único — que compara o texto literal — e o resumo do mês partiria em duas
    /// linhas que deveriam ser uma.
    /// </summary>
    [Fact]
    public void Register_colapsa_espacos_internos_do_nome()
    {
        var tipo = EventType.Register(1, "  Culto   de   Oração ", null, Now);
        Assert.Equal("Culto de Oração", tipo.Name);
    }

    [Fact]
    public void Register_recusa_nome_longo_demais()
    {
        var longo = new string('a', EventType.MaxNameLength + 1);
        Assert.Throws<ArgumentException>(() => EventType.Register(1, longo, null, Now));
    }

    /// <summary>
    /// O limite do domínio casa com o <c>VARCHAR</c> da coluna. Divergir faria o
    /// banco recusar com erro de driver o que o domínio tinha aceitado — um 500
    /// no lugar de um 400 legível.
    /// </summary>
    [Fact]
    public void Register_aceita_nome_exatamente_no_limite()
    {
        var noLimite = new string('a', EventType.MaxNameLength);
        Assert.Equal(noLimite, EventType.Register(1, noLimite, null, Now).Name);
    }

    /// <summary>
    /// Ícone em branco quebraria a renderização da lista, então o campo cai no
    /// padrão em vez de aceitar vazio.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_sem_icone_usa_o_padrao(string? icone)
    {
        Assert.Equal(EventType.DefaultIcon, EventType.Register(1, "Culto", icone, Now).Icon);
    }

    [Fact]
    public void Update_troca_nome_icone_e_estado()
    {
        var tipo = Culto();
        tipo.Update("Culto de Domingo", "sunrise", isActive: false, Depois);

        Assert.Equal("Culto de Domingo", tipo.Name);
        Assert.Equal("sunrise", tipo.Icon);
        Assert.False(tipo.IsActive);
        Assert.Equal(Depois, tipo.UpdatedAt);
    }

    [Fact]
    public void Update_permite_reativar()
    {
        var tipo = Culto();
        tipo.Update("Culto", "heart", isActive: false, Depois);
        tipo.Update("Culto", "heart", isActive: true, Depois);

        Assert.True(tipo.IsActive);
    }

    [Fact]
    public void Update_recusa_nome_vazio()
    {
        var tipo = Culto();
        Assert.Throws<ArgumentException>(() => tipo.Update("  ", "heart", isActive: true, Depois));
    }

    /// <summary>
    /// A recusa não pode deixar a entidade meio alterada: quem tratar o
    /// <c>ArgumentException</c> e continuar precisa encontrar o estado anterior
    /// intacto.
    /// </summary>
    [Fact]
    public void Update_recusado_nao_altera_nada()
    {
        var tipo = Culto();

        Assert.Throws<ArgumentException>(() => tipo.Update("  ", "sunrise", isActive: false, Depois));

        Assert.Equal("Culto", tipo.Name);
        Assert.Equal("heart", tipo.Icon);
        Assert.True(tipo.IsActive);
        Assert.Equal(Now, tipo.UpdatedAt);
    }
}
