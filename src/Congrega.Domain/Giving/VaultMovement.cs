using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Para onde o dinheiro foi.</summary>
public enum VaultDirection : short
{
    /// <summary>Do caixa para o cofre — guardar.</summary>
    Deposito = 1,

    /// <summary>Do cofre para o caixa — buscar de volta.</summary>
    Retirada = 2,
}

/// <summary>
/// Um movimento do cofre da igreja.
/// </summary>
/// <remarks>
/// <para>
/// <b>Isto não é um lançamento, e a distinção é a razão de a entidade existir.</b>
/// Guardar R$ 5.000 do caixa no cofre é a mesma nota mudando de gaveta: nada
/// entrou, nada saiu, o patrimônio da igreja é idêntico antes e depois. Se o
/// movimento virasse <see cref="GivingEntry"/>, o fechamento do mês passaria a
/// afirmar uma despesa de R$ 5.000 — e guardar dinheiro apareceria no relatório
/// como gastá-lo.
/// </para>
/// <para>
/// Por isso o cofre tem tabela própria e <b>fica fora do fechamento</b>. O saldo
/// dele é informação de guarda, não de resultado.
/// </para>
/// </remarks>
public sealed class VaultMovement : AggregateRoot
{
    public const int MaxNotesLength = 500;

    private VaultMovement() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }

    /// <summary>
    /// Posição no extrato do cofre desta igreja.
    /// </summary>
    /// <remarks>
    /// <b>É o que serializa as gravações concorrentes.</b> O índice
    /// <c>UNIQUE (tenant_id, sequence_number)</c> recusa a segunda de duas
    /// requisições que leram o mesmo saldo — sem ele, duas retiradas simultâneas
    /// de R$ 100 sobre um cofre de R$ 100 passariam as duas.
    /// </remarks>
    public long SequenceNumber { get; private set; }

    public VaultDirection Direction { get; private set; }

    /// <summary>Centavos, sempre positivo. O sentido vem de <see cref="Direction"/>.</summary>
    public long AmountCents { get; private set; }

    /// <summary>
    /// Saldo do cofre depois deste movimento.
    /// </summary>
    /// <remarks>
    /// Gravado, e não recalculado a cada leitura: é o que torna o extrato
    /// conferível linha a linha, como o de um banco, e é sobre esta coluna que
    /// vive o <c>CHECK (balance_after_cents &gt;= 0)</c> que impede o cofre de
    /// ficar negativo.
    /// </remarks>
    public long BalanceAfterCents { get; private set; }

    /// <summary>De onde saiu, ou para onde voltou. Opcional.</summary>
    public long? AccountId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Quem mexeu no cofre. Auditoria de dinheiro em espécie começa aqui.</summary>
    public long? PerformedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Registra um movimento a partir do estado atual do cofre.
    /// </summary>
    /// <param name="saldoAtualCents">Saldo antes deste movimento.</param>
    /// <param name="ultimaSequencia">
    /// Número do último movimento, ou <c>0</c> se o cofre nunca foi usado.
    /// </param>
    /// <remarks>
    /// O saldo e a sequência entram como <b>parâmetros</b>, e não são buscados
    /// aqui: o domínio não fala com o banco. Quem chama lê o último movimento e
    /// passa o que leu — e se dois chamadores lerem o mesmo, o índice único
    /// derruba um dos dois na gravação.
    /// </remarks>
    public static VaultMovement Register(
        long tenantId,
        VaultDirection direction,
        long amountCents,
        long saldoAtualCents,
        long ultimaSequencia,
        DateTimeOffset now,
        long? accountId = null,
        string? notes = null,
        long? performedByUserId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(saldoAtualCents);
        ArgumentOutOfRangeException.ThrowIfNegative(ultimaSequencia);

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentException("Direção do movimento inválida.", nameof(direction));
        }

        if (amountCents <= 0)
        {
            throw new ArgumentException(
                "O valor precisa ser maior que zero. O sentido do movimento vem da direção, não do sinal.",
                nameof(amountCents));
        }

        var saldoDepois = direction == VaultDirection.Deposito
            ? saldoAtualCents + amountCents
            : saldoAtualCents - amountCents;

        // A mensagem diz **quanto** existe, porque "saldo insuficiente" sozinho
        // manda o tesoureiro sair procurando o extrato para descobrir o quanto
        // ele pode retirar.
        if (saldoDepois < 0)
        {
            throw new InvalidOperationException(
                $"O cofre tem {Reais(saldoAtualCents)} e a retirada é de {Reais(amountCents)}.");
        }

        return new VaultMovement
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            SequenceNumber = ultimaSequencia + 1,
            Direction = direction,
            AmountCents = amountCents,
            BalanceAfterCents = saldoDepois,
            AccountId = accountId,
            OccurredAt = now,
            Notes = Aparar(notes),
            PerformedByUserId = performedByUserId,
            CreatedAt = now,
        };
    }

    /// <summary>Formata centavos como "R$ 1.234,56".</summary>
    /// <remarks>
    /// Montado à mão, e não por <c>ToString("C", pt-BR)</c>: um processo rodando
    /// com globalização invariante — o modo enxuto de container — devolveria
    /// "¤100.00" para a mesma chamada, e a mensagem que deveria orientar o
    /// tesoureiro passaria a confundi-lo. Com separador fixo, o texto é o mesmo
    /// em qualquer ambiente.
    /// </remarks>
    private static string Reais(long centavos)
    {
        var inteiros = (centavos / 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var centavosDoValor = (centavos % 100).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

        var comPontos = new System.Text.StringBuilder();
        for (var i = 0; i < inteiros.Length; i++)
        {
            if (i > 0 && (inteiros.Length - i) % 3 == 0)
            {
                comPontos.Append('.');
            }

            comPontos.Append(inteiros[i]);
        }

        return $"R$ {comPontos},{centavosDoValor}";
    }

    private static string? Aparar(string? valor)
    {
        var limpo = valor?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return null;
        }

        if (limpo.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Máximo de {MaxNotesLength} caracteres.", nameof(valor));
        }

        return limpo;
    }
}
