using Congrega.Domain.Common;

namespace Congrega.Domain.Childcare;

/// <summary>
/// Ficha de criança.
/// </summary>
/// <remarks>
/// <para>
/// <b>O agregado nunca vê o texto claro dos campos sensíveis.</b> Alergia,
/// condição de saúde e referência de foto entram e saem como <c>byte[]</c> já
/// cifrado — cifrar é responsabilidade da camada que tem a chave, e o domínio
/// não tem nem deve ter dependência criptográfica (a regra do <c>CLAUDE.md</c>:
/// <c>Congrega.Domain</c> sem nenhuma <c>PackageReference</c>).
/// </para>
/// <para>
/// A consequência prática é que este tipo não consegue vazar alergia num log de
/// <c>ToString()</c>, num dump de exceção ou num serializador que alguém
/// apontar para ele sem pensar. Não é elegância: é a superfície de vazamento
/// sendo removida por construção.
/// </para>
/// </remarks>
public sealed class Child : AggregateRoot
{
    private readonly List<ChildGuardian> _guardians = [];

    private Child()
    {
        FullName = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }

    public string FullName { get; private set; }
    public DateOnly BirthDate { get; private set; }

    /// <summary>Ficha de membro, quando existir. A maioria das crianças não tem.</summary>
    public long? MemberId { get; private set; }

    public byte[]? AllergiesEncrypted { get; private set; }
    public byte[]? HealthNotesEncrypted { get; private set; }
    public byte[]? PhotoReferenceEncrypted { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ChildGuardian> Guardians => _guardians.AsReadOnly();

    public static Child Register(
        long tenantId,
        string fullName,
        DateOnly birthDate,
        DateTimeOffset now,
        long? memberId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        if (birthDate > DateOnly.FromDateTime(now.UtcDateTime))
        {
            // Espelha o CHECK do banco. Aqui falha cedo com mensagem útil; lá
            // fecha o caminho para qualquer script ou importação.
            throw new ArgumentException(
                "A data de nascimento não pode ser futura.", nameof(birthDate));
        }

        return new Child
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName.Trim(),
            BirthDate = birthDate,
            MemberId = memberId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Idade em anos completos na data dada.</summary>
    public int AgeOn(DateOnly moment)
    {
        int idade = moment.Year - BirthDate.Year;

        // Ainda não fez aniversário este ano.
        if (moment < BirthDate.AddYears(idade))
        {
            idade--;
        }

        return idade;
    }

    /// <summary>
    /// Substitui os campos sensíveis, já cifrados pela camada que tem a chave.
    /// </summary>
    /// <remarks>
    /// Um método só para os três, e não três propriedades públicas: eles são
    /// atualizados juntos pelo mesmo formulário, e expor cada um separado
    /// convidaria a gravar um campo cifrado com uma chave e outro com a
    /// seguinte, no meio de uma rotação.
    /// </remarks>
    public void UpdateSensitiveData(
        byte[]? allergiesEncrypted,
        byte[]? healthNotesEncrypted,
        byte[]? photoReferenceEncrypted,
        DateTimeOffset now)
    {
        AllergiesEncrypted = allergiesEncrypted;
        HealthNotesEncrypted = healthNotesEncrypted;
        PhotoReferenceEncrypted = photoReferenceEncrypted;
        UpdatedAt = now;
    }

    public void UpdateProfile(string fullName, DateOnly birthDate, long? memberId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        if (birthDate > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new ArgumentException(
                "A data de nascimento não pode ser futura.", nameof(birthDate));
        }

        FullName = fullName.Trim();
        BirthDate = birthDate;
        MemberId = memberId;
        UpdatedAt = now;
    }

    /// <summary>Inativa sem apagar — o histórico de check-in continua fazendo sentido.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}

/// <summary>
/// Vínculo de responsável, com a autorização de retirada.
/// </summary>
/// <remarks>
/// <b>Ser responsável e poder retirar são coisas diferentes.</b> Um acordo de
/// guarda pode registrar o pai como responsável e ainda assim não autorizá-lo a
/// buscar a criança — e o sistema precisa conseguir representar isso, porque é
/// justamente o caso em que errar tem consequência grave.
/// </remarks>
public sealed class ChildGuardian
{
    private ChildGuardian()
    {
        Relationship = string.Empty;
    }

    public long Id { get; private set; }
    public long TenantId { get; private set; }
    public long ChildId { get; private set; }
    public long MemberId { get; private set; }
    public string Relationship { get; private set; }
    public bool CanPickup { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ChildGuardian Link(
        long tenantId,
        long childId,
        long memberId,
        string relationship,
        bool canPickup,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationship);

        return new ChildGuardian
        {
            TenantId = tenantId,
            ChildId = childId,
            MemberId = memberId,
            Relationship = relationship.Trim(),
            CanPickup = canPickup,
            CreatedAt = now,
        };
    }

    public void SetPickupAuthorization(bool canPickup) => CanPickup = canPickup;
}
