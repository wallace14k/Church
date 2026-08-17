using Congrega.Domain.Giving;

namespace Congrega.Domain.UnitTests;

public sealed class GivingCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_normaliza_espacos_do_nome()
    {
        var categoria = GivingCategory.Register(1, "  Dízimo   Mensal ", GivingKind.Entrada, Now);
        Assert.Equal("Dízimo Mensal", categoria.Name);
    }

    [Fact]
    public void Register_nasce_ativa()
    {
        var categoria = GivingCategory.Register(1, "Oferta", GivingKind.Entrada, Now);
        Assert.True(categoria.IsActive);
    }

    [Fact]
    public void Register_recusa_nome_vazio()
    {
        Assert.Throws<ArgumentException>(
            () => GivingCategory.Register(1, "  ", GivingKind.Entrada, Now));
    }

    [Fact]
    public void Register_recusa_tipo_fora_do_enum()
    {
        Assert.Throws<ArgumentException>(
            () => GivingCategory.Register(1, "Oferta", (GivingKind)99, Now));
    }

    [Fact]
    public void SetActive_desliga_e_religa()
    {
        var categoria = GivingCategory.Register(1, "Aluguel", GivingKind.Saida, Now);

        categoria.SetActive(false, Now);
        Assert.False(categoria.IsActive);

        categoria.SetActive(true, Now);
        Assert.True(categoria.IsActive);
    }

    [Fact]
    public void Rename_nao_altera_o_tipo()
    {
        // O tipo carrega o sinal de todo lançamento histórico da categoria.
        // Renomear "Aluguel" não pode transformar doze meses de saída em entrada.
        var categoria = GivingCategory.Register(1, "Aluguel", GivingKind.Saida, Now);

        categoria.Rename("Aluguel do templo", Now);

        Assert.Equal(GivingKind.Saida, categoria.Kind);
        Assert.Equal("Aluguel do templo", categoria.Name);
    }
}

public sealed class GivingEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 8, 15);

    [Fact]
    public void Register_guarda_centavos_e_data()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 15_000, occurredOn: Hoje,
            method: GivingMethod.Pix, now: Now);

        Assert.Equal(15_000, lancamento.AmountCents);
        Assert.Equal(Hoje, lancamento.OccurredOn);
        Assert.Equal(GivingMethod.Pix, lancamento.Method);
    }

    [Fact]
    public void Register_recusa_valor_zero()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 0, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_recusa_valor_negativo()
    {
        // Saída é definida pela categoria. Aceitar valor negativo criaria duas
        // representações para a mesma coisa, e um dia as duas somariam juntas.
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: -5_000, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_recusa_data_futura()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 100, occurredOn: Hoje.AddDays(1),
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_aceita_hoje()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 100, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now);

        Assert.Equal(Hoje, lancamento.OccurredOn);
    }

    [Fact]
    public void Register_recusa_forma_de_pagamento_fora_do_enum()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 100, occurredOn: Hoje,
            method: (GivingMethod)42, now: Now));
    }

    [Fact]
    public void Register_aceita_membro_nulo()
    {
        // Oferta de gazofilácio não tem doador identificado — é o caso comum.
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 100, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now, memberId: null);

        Assert.Null(lancamento.MemberId);
    }

    [Fact]
    public void Register_limpa_observacao_em_branco()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, amountCents: 100, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now, notes: "   ");

        Assert.Null(lancamento.Notes);
    }
}

public sealed class MonthlyClosingTests
{
    private static ClosingLine Linha(GivingKind tipo, long total) => new()
    {
        CategoryPublicId = Guid.NewGuid(),
        CategoryName = tipo.ToString(),
        Kind = tipo,
        TotalCents = total,
        EntryCount = 1,
    };

    [Fact]
    public void Saldo_e_entradas_menos_saidas()
    {
        var fechamento = new MonthlyClosing
        {
            Year = 2026,
            Month = 8,
            Lines =
            [
                Linha(GivingKind.Entrada, 500_00),
                Linha(GivingKind.Entrada, 300_00),
                Linha(GivingKind.Saida, 200_00),
            ],
        };

        Assert.Equal(800_00, fechamento.TotalIncomeCents);
        Assert.Equal(200_00, fechamento.TotalExpenseCents);
        Assert.Equal(600_00, fechamento.BalanceCents);
    }

    [Fact]
    public void Saldo_negativo_e_informacao_valida()
    {
        var fechamento = new MonthlyClosing
        {
            Year = 2026,
            Month = 8,
            Lines = [Linha(GivingKind.Entrada, 100_00), Linha(GivingKind.Saida, 450_00)],
        };

        Assert.Equal(-350_00, fechamento.BalanceCents);
    }

    [Fact]
    public void Mes_sem_lancamento_fecha_em_zero()
    {
        var fechamento = new MonthlyClosing { Year = 2026, Month = 8, Lines = [] };

        Assert.Equal(0, fechamento.TotalIncomeCents);
        Assert.Equal(0, fechamento.TotalExpenseCents);
        Assert.Equal(0, fechamento.BalanceCents);
    }
}
