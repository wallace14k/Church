using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Entrada ou saída de caixa.</summary>
/// <remarks>
/// O sinal do dinheiro mora <b>aqui</b>, e não no valor do lançamento. Um
/// lançamento sempre guarda centavos positivos; é a categoria que decide se
/// somam ou subtraem no fechamento. Permitir valor negativo criaria duas
/// representações para "saída" e, algum dia, as duas apareceriam somadas no
/// mesmo relatório.
/// </remarks>
public enum GivingKind : short
{
    Entrada = 1,
    Saida = 2,
}

/// <summary>
/// Categoria de lançamento — "Dízimo", "Oferta", "Aluguel", "Energia".
/// </summary>
public sealed class GivingCategory : AggregateRoot
{
    private GivingCategory()
    {
        Name = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public string Name { get; private set; }
    public GivingKind Kind { get; private set; }

    /// <summary>
    /// Categoria desativada some do formulário de lançamento mas continua no
    /// relatório histórico.
    /// </summary>
    /// <remarks>
    /// É por isso que não existe exclusão: apagar "Aluguel" faria os doze meses
    /// de aluguel do ano passado deixarem de somar em qualquer lugar. A FK
    /// <c>RESTRICT</c> no banco recusa o <c>DELETE</c> mesmo que alguém tente
    /// por fora.
    /// </remarks>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GivingCategory Register(
        long tenantId,
        string name,
        GivingKind kind,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Tipo de categoria inválido.", nameof(kind));
        }

        return new GivingCategory
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

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = NormalizeName(name);
        UpdatedAt = now;
    }

    /// <summary>
    /// Liga ou desliga a categoria. O tipo (entrada/saída) <b>não</b> muda:
    /// trocá-lo inverteria o sinal de todo lançamento histórico já feito nela,
    /// e o fechamento de meses já prestados mudaria sozinho.
    /// </summary>
    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAt = now;
    }

    private static string NormalizeName(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
