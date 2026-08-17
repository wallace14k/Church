using Congrega.Domain.Common;

namespace Congrega.Domain.Congregation;

/// <summary>
/// Família — o agrupamento de membros que a secretaria já usa no papel: "família
/// Silva", "família Oliveira".
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente simples: um nome e um tenant. Não modela parentesco,
/// responsável, nem hierarquia — a tabela existe para responder "quem mais da
/// família Silva está cadastrado", não para reconstruir uma árvore genealógica.
/// Se um dia isso for necessário, é uma entidade nova, não uma extensão desta.
/// </para>
/// <para>
/// O vínculo membro↔família mora em <see cref="Member.FamilyId"/> — uma família
/// não conhece seus membros, os membros é que apontam para ela. Isso evita que
/// criar uma família e adicionar membros vire duas operações que podem divergir;
/// só existe um lugar onde o vínculo é escrito.
/// </para>
/// </remarks>
public sealed class Family : AggregateRoot
{
    private Family()
    {
        Name = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Family Register(long tenantId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        return new Family
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = NormalizeName(name),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = NormalizeName(name);
        UpdatedAt = now;
    }

    private static string NormalizeName(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
