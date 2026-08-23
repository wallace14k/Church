using Congrega.Domain.Giving;

namespace Congrega.Domain.UnitTests;

public sealed class VaultMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Deposito_soma_ao_saldo()
    {
        var movimento = VaultMovement.Register(
            tenantId: 1,
            direction: VaultDirection.Deposito,
            amountCents: 50_000,
            saldoAtualCents: 20_000,
            ultimaSequencia: 3,
            now: Now);

        Assert.Equal(70_000, movimento.BalanceAfterCents);
        Assert.Equal(4, movimento.SequenceNumber);
    }

    [Fact]
    public void Retirada_subtrai_do_saldo()
    {
        var movimento = VaultMovement.Register(1, VaultDirection.Retirada, 15_000, 20_000, 1, Now);

        Assert.Equal(5_000, movimento.BalanceAfterCents);
    }

    [Fact]
    public void Retirada_pode_zerar_o_cofre()
    {
        // Zero é um saldo legítimo — o cofre esvaziado. Só negativo é impossível.
        Assert.Equal(0, VaultMovement.Register(1, VaultDirection.Retirada, 20_000, 20_000, 1, Now)
            .BalanceAfterCents);
    }

    [Fact]
    public void Retirada_maior_que_o_saldo_diz_quanto_existe()
    {
        var erro = Assert.Throws<InvalidOperationException>(() =>
            VaultMovement.Register(1, VaultDirection.Retirada, 30_000, 20_000, 1, Now));

        // "Saldo insuficiente" sozinho manda o tesoureiro procurar o extrato
        // para descobrir quanto ele pode retirar.
        Assert.Contains("R$ 200,00", erro.Message, StringComparison.Ordinal);
        Assert.Contains("R$ 300,00", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valor_formatado_usa_separador_de_milhar_brasileiro()
    {
        // Montado à mão de propósito: `ToString("C", pt-BR)` num processo com
        // globalização invariante devolveria "¤12345.67".
        var erro = Assert.Throws<InvalidOperationException>(() =>
            VaultMovement.Register(1, VaultDirection.Retirada, 1_234_567, 0, 0, Now));

        Assert.Contains("R$ 12.345,67", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Primeiro_movimento_do_cofre_comeca_na_sequencia_1()
    {
        Assert.Equal(1, VaultMovement.Register(1, VaultDirection.Deposito, 100, 0, 0, Now)
            .SequenceNumber);
    }

    [Fact]
    public void Valor_zero_ou_negativo_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            VaultMovement.Register(1, VaultDirection.Deposito, 0, 0, 0, Now));

        Assert.Throws<ArgumentException>(() =>
            VaultMovement.Register(1, VaultDirection.Deposito, -500, 0, 0, Now));
    }

    [Fact]
    public void Duas_leituras_do_mesmo_saldo_produzem_a_mesma_sequencia()
    {
        // **É este empate que o índice único do banco resolve.** As duas
        // requisições leram o cofre com R$ 100 e a sequência 5; as duas
        // calculam a sequência 6, e o `UNIQUE (tenant_id, sequence_number)`
        // derruba a segunda. Se o domínio inventasse sequências distintas aqui,
        // as duas retiradas passariam e o cofre ficaria negativo.
        var primeira = VaultMovement.Register(1, VaultDirection.Retirada, 10_000, 10_000, 5, Now);
        var segunda = VaultMovement.Register(1, VaultDirection.Retirada, 10_000, 10_000, 5, Now);

        Assert.Equal(primeira.SequenceNumber, segunda.SequenceNumber);
    }

    [Fact]
    public void Observacao_acima_do_limite_e_recusada()
    {
        Assert.Throws<ArgumentException>(() => VaultMovement.Register(
            1, VaultDirection.Deposito, 100, 0, 0, Now,
            notes: new string('x', VaultMovement.MaxNotesLength + 1)));
    }
}
