using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Entrada ou saída de caixa.</summary>
/// <remarks>
/// <para>
/// <b>O sinal saiu daqui.</b> Este enum descrevia o que a categoria <i>era</i>,
/// e o fechamento somava por ele. Hoje, num lançamento, ele diz o que o
/// lançamento <b>é</b>; numa categoria, diz o que ela <b>aceita</b>.
/// </para>
/// <para>
/// A mudança foi forçada por <see cref="Ambos"/>: uma categoria que serve a
/// entrada e a saída — "Eventos", "Outros" — não tem sinal para emprestar, e o
/// fechamento não teria como somá-la.
/// </para>
/// <para>
/// O valor do lançamento continua sempre positivo, e o motivo original continua
/// valendo: duas <i>representações</i> de saída acabariam somadas no mesmo
/// relatório. O que mudou foi qual coluna carrega o sinal, não quantas.
/// </para>
/// </remarks>
public enum GivingKind : short
{
    Entrada = 1,
    Saida = 2,

    /// <summary>
    /// Só de categoria: aceita lançamento de entrada e de saída.
    /// </summary>
    /// <remarks>
    /// Um lançamento nunca é <c>Ambos</c> — não teria sinal, e
    /// <c>GivingEntry.Register</c> recusa.
    /// </remarks>
    Ambos = 3,
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
    /// <summary>O que esta categoria aceita: entrada, saída, ou os dois.</summary>
    public GivingKind Kind { get; private set; }

    /// <summary>
    /// Cor da categoria no resumo por categoria, em hexadecimal.
    /// </summary>
    /// <remarks>
    /// Escolhida pela igreja, não derivada do nome: um hash daria cores estáveis
    /// mas arbitrárias, e "Dízimo" poderia sair vermelho. Nula significa "use a
    /// cor padrão do sistema" — quem desenha não deve inventar uma.
    ///
    /// <para>
    /// <b>Nunca carrega significado sozinha.</b> A barra do resumo sempre vem
    /// com o nome e o valor escritos ao lado; verde e âmbar do sistema têm
    /// luminância quase idêntica, e cor sozinha some para quem não distingue
    /// matiz.
    /// </para>
    /// </remarks>
    public string? ColorHex { get; private set; }

    /// <summary>
    /// Esta categoria aceita um lançamento deste tipo?
    /// </summary>
    /// <remarks>
    /// É a verificação que substitui o antigo "a categoria define o sinal".
    /// Lançar uma saída em "Dízimo" continua errado — só que agora o erro é
    /// recusado explicitamente, em vez de silenciosamente virar entrada.
    /// </remarks>
    public bool Aceita(GivingKind kindDoLancamento) =>
        Kind == GivingKind.Ambos || Kind == kindDoLancamento;

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
        DateTimeOffset now,
        string? colorHex = null)
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
            ColorHex = NormalizeColor(colorHex),
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

    /// <summary>Troca a cor. Nula volta ao padrão do sistema.</summary>
    public void SetColor(string? colorHex, DateTimeOffset now)
    {
        ColorHex = NormalizeColor(colorHex);
        UpdatedAt = now;
    }

    /// <summary>
    /// Aceita <c>#RRGGBB</c>, em qualquer caixa, e normaliza para maiúscula.
    /// </summary>
    /// <remarks>
    /// Normalizar a caixa importa porque a cor é comparada como texto em teste e
    /// em seed: <c>#44831a</c> e <c>#44831A</c> são a mesma cor e precisam ser a
    /// mesma string.
    /// </remarks>
    private static string? NormalizeColor(string? value)
    {
        var limpo = value?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return null;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(limpo, "^#[0-9A-Fa-f]{6}$"))
        {
            throw new ArgumentException("A cor precisa estar no formato #RRGGBB.", nameof(value));
        }

        return limpo.ToUpperInvariant();
    }

    private static string NormalizeName(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
