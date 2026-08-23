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
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 15_000, occurredOn: Hoje,
            method: GivingMethod.Pix, now: Now);

        Assert.Equal(15_000, lancamento.AmountCents);
        Assert.Equal(Hoje, lancamento.OccurredOn);
        Assert.Equal(GivingMethod.Pix, lancamento.Method);
    }

    [Fact]
    public void Register_recusa_valor_zero()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 0, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_recusa_valor_negativo()
    {
        // O valor continua sempre positivo. O sinal vem de `kind`, e aceitar
        // valor negativo criaria uma segunda representação para saída — um dia
        // as duas apareceriam somadas no mesmo relatório.
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: -5_000, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_recusa_data_futura()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100, occurredOn: Hoje.AddDays(1),
            method: GivingMethod.Dinheiro, now: Now));
    }

    [Fact]
    public void Register_aceita_hoje()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now);

        Assert.Equal(Hoje, lancamento.OccurredOn);
    }

    [Fact]
    public void Register_recusa_forma_de_pagamento_fora_do_enum()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100, occurredOn: Hoje,
            method: (GivingMethod)42, now: Now));
    }

    [Fact]
    public void Register_aceita_membro_nulo()
    {
        // Oferta de gazofilácio não tem doador identificado — é o caso comum.
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100, occurredOn: Hoje,
            method: GivingMethod.Dinheiro, now: Now, memberId: null);

        Assert.Null(lancamento.MemberId);
    }

    [Fact]
    public void Register_limpa_observacao_em_branco()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100, occurredOn: Hoje,
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
            PlannedIncomeCents = 0,
            PlannedExpenseCents = 0,
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
            PlannedIncomeCents = 0,
            PlannedExpenseCents = 0,
            Lines = [Linha(GivingKind.Entrada, 100_00), Linha(GivingKind.Saida, 450_00)],
        };

        Assert.Equal(-350_00, fechamento.BalanceCents);
    }

    [Fact]
    public void Mes_sem_lancamento_fecha_em_zero()
    {
        var fechamento = new MonthlyClosing
        {
            Year = 2026,
            Month = 8,
            PlannedIncomeCents = 0,
            PlannedExpenseCents = 0,
            Lines = [],
        };

        Assert.Equal(0, fechamento.TotalIncomeCents);
        Assert.Equal(0, fechamento.TotalExpenseCents);
        Assert.Equal(0, fechamento.BalanceCents);
    }

    [Fact]
    public void Previsto_NAO_entra_no_total_realizado_nem_no_saldo()
    {
        // **É o caso que gerou o relatório de bug.** Setembro tem uma parcela
        // prevista de R$ 1.200 e nenhum pagamento: o total precisa dizer R$ 0,00,
        // porque o dinheiro não se moveu. Somá-lo aqui faria o fechamento
        // afirmar um aluguel pago que ninguém pagou.
        var fechamento = new MonthlyClosing
        {
            Year = 2026,
            Month = 9,
            PlannedIncomeCents = 0,
            PlannedExpenseCents = 120_000,
            Lines = [],
        };

        Assert.Equal(0, fechamento.TotalExpenseCents);
        Assert.Equal(0, fechamento.BalanceCents);

        // E ao mesmo tempo o previsto continua VISÍVEL. Escondê-lo faria a tela
        // mostrar R$ 0,00 ao lado de uma lista com um lançamento de R$ 1.200 —
        // que foi exatamente o que pareceu defeito.
        Assert.Equal(120_000, fechamento.PlannedExpenseCents);
        Assert.True(fechamento.HasPlanned);
    }

    [Fact]
    public void Mes_sem_previsto_nao_precisa_explicar_nada()
    {
        var fechamento = new MonthlyClosing
        {
            Year = 2026,
            Month = 8,
            PlannedIncomeCents = 0,
            PlannedExpenseCents = 0,
            Lines = [],
        };

        Assert.False(fechamento.HasPlanned);
    }
}

/// <summary>
/// As regras que entraram quando o lançamento ganhou tipo próprio, título,
/// conta e recorrência.
/// </summary>
public sealed class LancamentoDetalhadoTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 8, 15);

    /// <summary>
    /// <c>Ambos</c> descreve o que uma CATEGORIA aceita, não o que um lançamento
    /// é.
    /// Um lançamento "ambos" não teria sinal, e o fechamento não saberia se
    /// soma ou subtrai.
    /// </summary>
    [Fact]
    public void Register_recusa_lancamento_ambos()
    {
        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Ambos, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now));
    }

    [Fact]
    public void Register_guarda_o_tipo_do_lancamento()
    {
        var saida = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Boleto, now: Now);

        Assert.Equal(GivingKind.Saida, saida.Kind);
        Assert.Equal(1_000, saida.AmountCents);
    }

    /// <summary>
    /// A categoria virou restrição: <c>Ambos</c> aceita os dois, e as demais só o
    /// próprio tipo. Antes, lançar saída em "Dízimo" virava entrada em
    /// silêncio, porque o sinal vinha da categoria.
    /// </summary>
    [Theory]
    [InlineData(GivingKind.Entrada, GivingKind.Entrada, true)]
    [InlineData(GivingKind.Entrada, GivingKind.Saida, false)]
    [InlineData(GivingKind.Saida, GivingKind.Saida, true)]
    [InlineData(GivingKind.Saida, GivingKind.Entrada, false)]
    [InlineData(GivingKind.Ambos, GivingKind.Entrada, true)]
    [InlineData(GivingKind.Ambos, GivingKind.Saida, true)]
    public void Categoria_aceita_apenas_o_que_declara(
        GivingKind daCategoria, GivingKind doLancamento, bool esperado)
    {
        var categoria = GivingCategory.Register(1, "Teste", daCategoria, Now);

        Assert.Equal(esperado, categoria.Aceita(doLancamento));
    }

    // -------------------------------------------------------------------
    // Recorrência
    // -------------------------------------------------------------------

    /// <summary>
    /// A frequência é o que DEFINE a recorrência. Derivar a bandeira dela evita
    /// o par inconsistente "recorrente sem frequência", que a constraint do
    /// banco recusaria com um erro que não explica nada.
    /// </summary>
    [Fact]
    public void Register_sem_frequencia_nao_e_recorrente()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Boleto, now: Now);

        Assert.False(lancamento.IsRecurring);
        Assert.Null(lancamento.Recurrence);
    }

    [Fact]
    public void Register_com_frequencia_marca_recorrente()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 120_000,
            occurredOn: Hoje, method: GivingMethod.Transferencia, now: Now,
            recurrence: GivingRecurrence.Mensal);

        Assert.True(lancamento.IsRecurring);
        Assert.Equal(GivingRecurrence.Mensal, lancamento.Recurrence);
    }

    // -------------------------------------------------------------------
    // Título e observações
    // -------------------------------------------------------------------

    [Fact]
    public void Register_apara_titulo_e_transforma_vazio_em_nulo()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now,
            description: "  Aluguel  ", notes: "   ");

        Assert.Equal("Aluguel", lancamento.Description);
        Assert.Null(lancamento.Notes);
    }

    [Fact]
    public void Register_recusa_observacoes_longas_demais()
    {
        var longo = new string('a', GivingEntry.MaxNotesLength + 1);

        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now, notes: longo));
    }

    /// <summary>
    /// O limite do domínio casa com o <c>VARCHAR</c> da coluna. Divergir faria o
    /// banco recusar com erro de driver o que o domínio tinha aceitado — um 500
    /// no lugar de um 400 legível.
    /// </summary>
    [Fact]
    public void Register_aceita_observacoes_exatamente_no_limite()
    {
        var noLimite = new string('a', GivingEntry.MaxNotesLength);

        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now, notes: noLimite);

        Assert.Equal(noLimite, lancamento.Notes);
    }

    // -------------------------------------------------------------------
    // Conta financeira
    // -------------------------------------------------------------------

    [Fact]
    public void Conta_nasce_ativa_e_normaliza_o_nome()
    {
        var conta = FinancialAccount.Register(1, "  Conta   Itaú ", FinancialAccountKind.ContaBancaria, Now);

        Assert.Equal("Conta Itaú", conta.Name);
        Assert.True(conta.IsActive);
    }

    [Fact]
    public void Conta_recusa_nome_vazio()
    {
        Assert.Throws<ArgumentException>(() =>
            FinancialAccount.Register(1, "   ", FinancialAccountKind.Caixa, Now));
    }

    // -------------------------------------------------------------------
    // Cor da categoria
    // -------------------------------------------------------------------

    [Fact]
    public void Categoria_normaliza_a_cor_para_maiuscula()
    {
        var categoria = GivingCategory.Register(1, "Dízimo", GivingKind.Entrada, Now, "#44831a");

        Assert.Equal("#44831A", categoria.ColorHex);
    }

    [Theory]
    [InlineData("44831A")]
    [InlineData("#4483")]
    [InlineData("#GGGGGG")]
    public void Categoria_recusa_cor_invalida(string cor)
    {
        Assert.Throws<ArgumentException>(() =>
            GivingCategory.Register(1, "Dízimo", GivingKind.Entrada, Now, cor));
    }

    [Fact]
    public void Categoria_sem_cor_guarda_nulo()
    {
        Assert.Null(GivingCategory.Register(1, "Dízimo", GivingKind.Entrada, Now).ColorHex);
    }
}

/// <summary>
/// A série periódica: quantas parcelas, em que datas, e por que elas não podem
/// somar no fechamento.
/// </summary>
public sealed class SeriePeriodicaTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 8, 15);

    // -------------------------------------------------------------------
    // Quantas e quando
    // -------------------------------------------------------------------

    /// <summary>
    /// O horizonte é de doze MESES, e não de doze parcelas. "Doze parcelas" só
    /// faria sentido para a frequência mensal — semanal daria três meses e
    /// anual daria doze anos.
    /// </summary>
    [Fact]
    public void Mensal_gera_doze_parcelas()
    {
        var datas = GivingEntry.CalcularOcorrencias(new DateOnly(2026, 1, 10), GivingRecurrence.Mensal);

        Assert.Equal(12, datas.Count);
        Assert.Equal(new DateOnly(2026, 2, 10), datas[0]);
        Assert.Equal(new DateOnly(2027, 1, 10), datas[^1]);
    }

    [Fact]
    public void Anual_gera_uma_parcela_dentro_do_horizonte()
    {
        var datas = GivingEntry.CalcularOcorrencias(new DateOnly(2026, 3, 5), GivingRecurrence.Anual);

        Assert.Single(datas);
        Assert.Equal(new DateOnly(2027, 3, 5), datas[0]);
    }

    [Fact]
    public void Semanal_cobre_o_ano_inteiro()
    {
        var datas = GivingEntry.CalcularOcorrencias(new DateOnly(2026, 1, 1), GivingRecurrence.Semanal);

        // 365 dias / 7 = 52 semanas cheias.
        Assert.Equal(52, datas.Count);
        Assert.Equal(new DateOnly(2026, 1, 8), datas[0]);
    }

    /// <summary>
    /// A data original NÃO entra: ela já é o lançamento que originou a série, e
    /// incluí-la duplicaria a primeira parcela.
    /// </summary>
    [Fact]
    public void A_data_original_nao_entra_na_lista()
    {
        var inicio = new DateOnly(2026, 5, 20);
        var datas = GivingEntry.CalcularOcorrencias(inicio, GivingRecurrence.Mensal);

        Assert.DoesNotContain(inicio, datas);
    }

    // -------------------------------------------------------------------
    // Fim de mês — onde este cálculo erra fácil
    // -------------------------------------------------------------------

    /// <summary>
    /// Não existe 31 de fevereiro. A parcela vai para o último dia do mês, e
    /// **volta** para 31 quando o mês comporta — empurrar para 1º de março
    /// jogaria a parcela para o fechamento do mês errado.
    /// </summary>
    [Fact]
    public void Dia_31_encolhe_no_mes_curto_e_volta_depois()
    {
        var datas = GivingEntry.CalcularOcorrencias(new DateOnly(2026, 1, 31), GivingRecurrence.Mensal);

        Assert.Equal(new DateOnly(2026, 2, 28), datas[0]);
        Assert.Equal(new DateOnly(2026, 3, 31), datas[1]);
        Assert.Equal(new DateOnly(2026, 4, 30), datas[2]);
    }

    [Fact]
    public void Fevereiro_de_ano_bissexto_ganha_o_dia_29()
    {
        // 2028 é bissexto.
        var datas = GivingEntry.CalcularOcorrencias(new DateOnly(2028, 1, 31), GivingRecurrence.Mensal);

        Assert.Equal(new DateOnly(2028, 2, 29), datas[0]);
    }

    [Fact]
    public void Toda_parcela_cai_dentro_do_horizonte()
    {
        var inicio = new DateOnly(2026, 6, 15);
        var limite = inicio.AddMonths(GivingEntry.HorizonteEmMeses);

        foreach (var frequencia in Enum.GetValues<GivingRecurrence>())
        {
            foreach (var data in GivingEntry.CalcularOcorrencias(inicio, frequencia))
            {
                Assert.InRange(data, inicio.AddDays(1), limite);
            }
        }
    }

    // -------------------------------------------------------------------
    // Previsto não é dinheiro que se moveu
    // -------------------------------------------------------------------

    /// <summary>
    /// A barreira de data futura vale para o realizado — é ela que impede um
    /// lançamento de 2027 sumir do fechamento deste mês. O previsto existe
    /// justamente para ter data futura, e não soma até ser confirmado.
    /// </summary>
    [Fact]
    public void Previsto_aceita_data_futura_e_realizado_nao()
    {
        var futuro = Hoje.AddMonths(3);

        var previsto = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 120_000,
            occurredOn: futuro, method: GivingMethod.Boleto, now: Now,
            status: GivingEntryStatus.Previsto);

        Assert.Equal(GivingEntryStatus.Previsto, previsto.Status);
        Assert.Equal(futuro, previsto.OccurredOn);

        Assert.Throws<ArgumentException>(() => GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 120_000,
            occurredOn: futuro, method: GivingMethod.Boleto, now: Now));
    }

    [Fact]
    public void Lancamento_nasce_realizado_por_padrao()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now);

        Assert.Equal(GivingEntryStatus.Realizado, lancamento.Status);
        Assert.Null(lancamento.SeriesId);
    }

    // -------------------------------------------------------------------
    // Confirmação
    // -------------------------------------------------------------------

    [Fact]
    public void Confirmar_torna_previsto_em_realizado()
    {
        var previsto = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now,
            status: GivingEntryStatus.Previsto);

        previsto.Confirmar(Now);

        Assert.Equal(GivingEntryStatus.Realizado, previsto.Status);
    }

    /// <summary>
    /// Sem esta barreira, alguém confirmaria a parcela de dezembro em agosto e o
    /// fechamento de dezembro contaria dinheiro que ninguém pagou — exatamente o
    /// problema que o estado "previsto" existe para evitar.
    /// </summary>
    [Fact]
    public void Confirmar_recusa_parcela_que_ainda_nao_venceu()
    {
        var futura = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Saida, amountCents: 1_000,
            occurredOn: Hoje.AddMonths(4), method: GivingMethod.Pix, now: Now,
            status: GivingEntryStatus.Previsto);

        Assert.Throws<InvalidOperationException>(() => futura.Confirmar(Now));
    }

    /// <summary>
    /// Clique duplo, ou dois usuários confirmando a mesma parcela, chegam ao
    /// mesmo estado. Recusar o segundo mostraria erro sobre algo que deu certo.
    /// </summary>
    [Fact]
    public void Confirmar_o_que_ja_esta_realizado_e_silencioso()
    {
        var lancamento = GivingEntry.Register(
            tenantId: 1, categoryId: 2, kind: GivingKind.Entrada, amountCents: 100,
            occurredOn: Hoje, method: GivingMethod.Pix, now: Now);

        lancamento.Confirmar(Now);
        lancamento.Confirmar(Now);

        Assert.Equal(GivingEntryStatus.Realizado, lancamento.Status);
    }
}
