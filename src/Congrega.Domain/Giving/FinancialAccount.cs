using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Onde o dinheiro fica.</summary>
public enum FinancialAccountKind : short
{
    /// <summary>Dinheiro em espécie sob guarda da tesouraria.</summary>
    Caixa = 1,

    ContaBancaria = 2,
}

/// <summary>
/// Uma conta da igreja — o caixa físico, a conta do banco, a poupança do
/// terreno.
/// </summary>
/// <remarks>
/// <para>
/// Existe para igrejas com mais de um lugar guardando dinheiro: sem ela, o
/// fechamento diz quanto entrou e saiu, mas não de onde — e conciliar o extrato
/// do banco vira conferência linha a linha no papel.
/// </para>
/// <para>
/// <b>O vínculo do lançamento é opcional</b>, e isso é decisão, não descuido:
/// uma igreja com caixa único não deve ser obrigada a cadastrar uma conta para
/// conseguir registrar a oferta do domingo.
/// </para>
/// </remarks>
public sealed class FinancialAccount : AggregateRoot
{
    public const int MaxNameLength = 100;

    private FinancialAccount()
    {
        Name = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public string Name { get; private set; }
    public FinancialAccountKind Kind { get; private set; }

    /// <summary>
    /// Conta desativada some do formulário e continua nos lançamentos passados.
    /// </summary>
    /// <remarks>
    /// É a saída para "encerrei essa conta" sem destruir a conciliação do que
    /// passou por ela. A FK <c>RESTRICT</c> recusa o <c>DELETE</c> mesmo que
    /// alguém tente por fora — mesmo raciocínio de
    /// <see cref="GivingCategory.IsActive"/>.
    /// </remarks>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static FinancialAccount Register(
        long tenantId,
        string name,
        FinancialAccountKind kind,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Tipo de conta inválido.", nameof(kind));
        }

        return new FinancialAccount
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = NormalizeName(name),
            Kind = kind,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, FinancialAccountKind kind, bool isActive, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Tipo de conta inválido.", nameof(kind));
        }

        Name = NormalizeName(name);
        Kind = kind;
        IsActive = isActive;
        UpdatedAt = now;
    }

    /// <summary>
    /// Colapsa espaços internos e apara as pontas.
    /// </summary>
    /// <remarks>
    /// Sem isto, "Conta  Itaú" e "Conta Itaú" escapariam do índice único — que
    /// compara o texto literal — e o resumo por conta partiria em duas linhas
    /// que deveriam ser uma.
    /// </remarks>
    private static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var limpo = string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (limpo.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"O nome da conta pode ter no máximo {MaxNameLength} caracteres.",
                nameof(value));
        }

        return limpo;
    }
}
