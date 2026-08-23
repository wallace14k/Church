using Congrega.Domain.Common;

namespace Congrega.Domain.Calendar;

/// <summary>
/// Tipo de evento da igreja — "Culto", "Célula", "Vigília", "Ensaio do coral".
/// </summary>
/// <remarks>
/// <para>
/// Era um <c>enum</c> de cinco valores fixos. Virou entidade porque o
/// vocabulário da agenda pertence à igreja, não a quem escreveu o código: uma
/// congregação com "Escola Bíblica" e "Célula" não tinha onde encaixá-las, e
/// "Outro" apagava a diferença entre as duas.
/// </para>
/// <para>
/// Evento sem tipo é estado legítimo — <c>CalendarEvent.TypeId</c> é anulável.
/// Uma igreja que ainda não montou seu vocabulário agenda normalmente; só não
/// classifica. É o que o antigo valor "Outro" realmente significava, agora dito
/// pelo próprio <c>NULL</c> em vez de por uma linha que competia com ele.
/// </para>
/// </remarks>
public sealed class EventType : AggregateRoot
{
    /// <summary>
    /// Limite do nome. Casa com o <c>VARCHAR(60)</c> da coluna — divergir faria
    /// o banco recusar com erro de driver o que o domínio tinha aceitado.
    /// </summary>
    public const int MaxNameLength = 60;

    /// <summary>Limite do nome do ícone. Casa com o <c>VARCHAR(40)</c>.</summary>
    public const int MaxIconLength = 40;

    /// <summary>
    /// Ícone de quem não escolheu nenhum.
    /// </summary>
    /// <remarks>
    /// O domínio não conhece a lista de ícones válidos: quais existem depende da
    /// fonte que o aplicativo embarca, e isso muda de versão para versão. A
    /// borda HTTP valida contra o conjunto que o cliente sabe desenhar; aqui só
    /// existe a garantia de que o campo nunca fica vazio, porque um ícone em
    /// branco quebraria a renderização da lista.
    /// </remarks>
    public const string DefaultIcon = "calendar";

    private EventType()
    {
        Name = string.Empty;
        Icon = DefaultIcon;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public string Name { get; private set; }
    public string Icon { get; private set; }

    /// <summary>
    /// Tipo desativado some do formulário e continua na agenda histórica.
    /// </summary>
    /// <remarks>
    /// É a saída para "não uso mais isto" sem destruir a classificação dos
    /// eventos passados. A exclusão de verdade continua existindo e a FK
    /// <c>RESTRICT</c> a recusa exatamente quando ela apagaria história.
    /// </remarks>
    /// <summary>
    /// Cor do tipo, em <c>#RRGGBB</c>, ou <c>null</c> para a cor padrão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Escolhida pela igreja, e não derivada do nome: "Vigília" é azul aqui e
    /// pode ser roxo na igreja vizinha, e é a igreja que sabe qual convenção os
    /// membros dela já reconhecem. É a mesma decisão de
    /// <c>GivingCategory.ColorHex</c>.
    /// </para>
    /// <para>
    /// <b>Nunca carrega significado sozinha.</b> Na agenda a cor aparece sempre
    /// ao lado do nome do tipo escrito — na etiqueta da linha e no filtro. Uma
    /// barra colorida sem rótulo diria nada a quem não distingue matiz, e é
    /// justamente o tipo do evento que a barra existe para comunicar.
    /// </para>
    /// </remarks>
    public string? ColorHex { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EventType Register(
        long tenantId,
        string name,
        string? icon,
        DateTimeOffset now,
        string? colorHex = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        return new EventType
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = NormalizeName(name),
            Icon = NormalizeIcon(icon),
            ColorHex = NormalizeColor(colorHex),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string? icon,
        bool isActive,
        DateTimeOffset now,
        string? colorHex = null)
    {
        Name = NormalizeName(name);
        Icon = NormalizeIcon(icon);
        ColorHex = NormalizeColor(colorHex);
        IsActive = isActive;
        UpdatedAt = now;
    }

    /// <summary>
    /// Colapsa espaços internos e apara as pontas.
    /// </summary>
    /// <remarks>
    /// Sem isto, <c>"Culto  de  Domingo"</c> e <c>"Culto de Domingo"</c>
    /// escapariam do índice único — que compara o texto literal — e o resumo do
    /// mês partiria em duas linhas que deveriam ser uma.
    /// </remarks>
    /// <summary>
    /// Valida <c>#RRGGBB</c> e normaliza a caixa.
    /// </summary>
    /// <remarks>
    /// A caixa importa porque a cor é comparada como texto em teste e em seed:
    /// <c>#4A5FBF</c> e <c>#4a5fbf</c> são a mesma cor e precisam ser a mesma
    /// string. Mesmo tratamento de <c>GivingCategory</c>.
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

    private static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var limpo = string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (limpo.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"O nome do tipo pode ter no máximo {MaxNameLength} caracteres.",
                nameof(value));
        }

        return limpo;
    }

    private static string NormalizeIcon(string? value)
    {
        var limpo = value?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return DefaultIcon;
        }

        if (limpo.Length > MaxIconLength)
        {
            throw new ArgumentException(
                $"O nome do ícone pode ter no máximo {MaxIconLength} caracteres.",
                nameof(value));
        }

        return limpo;
    }
}
