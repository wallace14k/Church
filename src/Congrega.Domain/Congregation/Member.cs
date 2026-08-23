using Congrega.Domain.Common;

namespace Congrega.Domain.Congregation;

public enum MemberStatus
{
    Ativo = 1,
    Inativo = 2,
    Transferido = 3,
    Falecido = 4
}

public enum Gender
{
    Feminino = 1,
    Masculino = 2,
    NaoInformado = 3
}

public enum MaritalStatus
{
    Solteiro = 1,
    Casado = 2,
    Divorciado = 3,
    Viuvo = 4,
    NaoInformado = 5
}

public sealed record MemberRegistered(long MemberId, long TenantId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Membro da igreja.
/// </summary>
/// <remarks>
/// <para>
/// <b>O vínculo com <c>User</c> é opcional, e essa é a decisão central desta
/// entidade.</b> A maioria dos membros de uma igreja nunca vai abrir o app: eles
/// são digitados pela secretaria a partir da ficha de papel que já existe.
/// Exigir uma conta de login transformaria cadastro em convite, e a igreja não
/// conseguiria migrar a lista que tem hoje.
/// </para>
/// <para>
/// Quando a pessoa entra no app e é reconhecida, o vínculo é criado — e aí o
/// mesmo registro passa a servir para gestão e para autoatendimento.
/// </para>
/// <para>
/// <c>Member</c> é <b>tenant-scoped</b>, ao contrário de <c>User</c>. A mesma
/// pessoa em duas igrejas tem dois registros de membro e uma única conta.
/// </para>
/// </remarks>
public sealed class Member : AggregateRoot
{
    private Member()
    {
        FullName = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public long? UserId { get; private set; }
    public long? FamilyId { get; private set; }

    public string FullName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public Gender? Gender { get; private set; }
    public MaritalStatus? MaritalStatus { get; private set; }
    /// <summary>
    /// Endereço do membro, ou <c>null</c> quando não informado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Era um objeto de valor em colunas inline (<c>address_street</c> e
    /// companhia), com a justificativa registrada em <c>db/002_members.sql</c>:
    /// uma pessoa tem um endereço, e normalizar custaria um JOIN.
    /// </para>
    /// <para>
    /// <b>O que invalidou aquela decisão foi o endereço passar a ser preciso no
    /// evento também.</b> Inline agora significaria repetir seis colunas mais as
    /// regras de tipo de residência em duas tabelas, sem nada garantindo que as
    /// cópias continuassem iguais. O JOIN que se evitava continua evitável: a
    /// listagem de membros não seleciona endereço, só o detalhe seleciona.
    /// </para>
    /// <para>
    /// Guarda a chave e não a entidade, pelo mesmo motivo de
    /// <c>CalendarEvent.TypeId</c>: quem lista mil membros não deveria
    /// materializar mil endereços que a tela não mostra.
    /// </para>
    /// </remarks>
    public long? AddressId { get; private set; }

    public MemberStatus Status { get; private set; }
    public DateOnly? MembershipDate { get; private set; }
    public DateOnly? BaptismDate { get; private set; }
    public string? Notes { get; private set; }
    public string? PhotoKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public static Member Register(
        long tenantId,
        string fullName,
        DateTimeOffset now,
        string? email = null,
        string? phone = null,
        DateOnly? birthDate = null,
        Gender? gender = null,
        MaritalStatus? maritalStatus = null,
        long? addressId = null,
        DateOnly? membershipDate = null,
        DateOnly? baptismDate = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (birthDate is { } nascimento && nascimento > today)
        {
            // Data futura é erro de digitação, quase sempre ano trocado. Barrar
            // aqui evita relatório de aniversariantes com gente que nasceu em 2087.
            throw new ArgumentException("Data de nascimento não pode ser futura.", nameof(birthDate));
        }

        var member = new Member
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = NormalizeName(fullName),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Phone = NormalizePhone(phone),
            BirthDate = birthDate,
            Gender = gender,
            MaritalStatus = maritalStatus,
            AddressId = addressId,
            MembershipDate = membershipDate,
            BaptismDate = baptismDate,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = MemberStatus.Ativo,
            CreatedAt = now,
            UpdatedAt = now
        };

        member.Raise(new MemberRegistered(member.Id, tenantId, now));
        return member;
    }

    public void UpdateContact(string? email, string? phone, long? addressId, DateTimeOffset now)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Phone = NormalizePhone(phone);
        AddressId = addressId;
        UpdatedAt = now;
    }

    public void Rename(string fullName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = NormalizeName(fullName);
        UpdatedAt = now;
    }

    /// <summary>
    /// Atualiza os campos que a ficha permite editar, num só limite transacional.
    /// </summary>
    /// <remarks>
    /// Cobre exatamente os campos que a tela de cadastro já coleta — nome,
    /// e-mail, telefone, nascimento, endereço. Reúne a validação de
    /// <see cref="Rename"/> e <see cref="UpdateContact"/> mais a checagem de
    /// data futura que <see cref="Register"/> já fazia, para editar não abrir
    /// uma porta que cadastrar fecha.
    /// </remarks>
    public void UpdateProfile(
        string fullName,
        string? email,
        string? phone,
        DateOnly? birthDate,
        long? addressId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (birthDate is { } nascimento && nascimento > today)
        {
            throw new ArgumentException("Data de nascimento não pode ser futura.", nameof(birthDate));
        }

        FullName = NormalizeName(fullName);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Phone = NormalizePhone(phone);
        BirthDate = birthDate;
        AddressId = addressId;
        UpdatedAt = now;
    }

    /// <summary>Liga o registro a uma conta de login.</summary>
    /// <remarks>
    /// Não sobrescreve um vínculo existente: se o membro já tem conta, associar
    /// outra silenciosamente daria a uma pessoa o histórico de outra.
    /// </remarks>
    public void LinkToUser(long userId, DateTimeOffset now)
    {
        if (UserId is { } existente && existente != userId)
        {
            throw new InvalidOperationException(
                $"Membro {Id} já está vinculado ao usuário {existente}.");
        }

        UserId = userId;
        UpdatedAt = now;
    }

    public void AssignToFamily(long? familyId, DateTimeOffset now)
    {
        FamilyId = familyId;
        UpdatedAt = now;
    }

    public void ChangeStatus(MemberStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    /// <summary>Idade em anos completos, na data informada.</summary>
    public int? AgeOn(DateOnly reference)
    {
        if (BirthDate is not { } nascimento)
        {
            return null;
        }

        int idade = reference.Year - nascimento.Year;

        // Ainda não fez aniversário este ano.
        if (reference < nascimento.AddYears(idade))
        {
            idade--;
        }

        return idade;
    }

    /// <summary>
    /// Indica se o membro é menor de idade.
    /// </summary>
    /// <remarks>
    /// Usado para exigir os controles do Art. 14 da LGPD. Sem data de
    /// nascimento devolve <c>true</c>: na dúvida, trata como criança e aplica a
    /// proteção mais forte.
    /// </remarks>
    public bool IsMinorOn(DateOnly reference) => AgeOn(reference) is null or < 18;

    /// <summary>LGPD Art. 18, VI. Preserva a linha para o ledger financeiro.</summary>
    public void Anonymize(DateTimeOffset now)
    {
        if (AnonymizedAt is not null)
        {
            return;
        }

        FullName = "Membro removido";
        Email = null;
        Phone = null;
        BirthDate = null;
        AddressId = null;
        Notes = null;
        PhotoKey = null;
        AnonymizedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Normaliza o nome preservando a grafia informada.
    /// </summary>
    /// <remarks>
    /// Só colapsa espaços. Nada de Title Case automático: "d'Ávila", "MEIRELLES"
    /// e "de Souza" são grafias legítimas, e "corrigi-las" desrespeita o nome da
    /// pessoa — que é o dado mais pessoal do cadastro.
    /// </remarks>
    private static string NormalizeName(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Guarda apenas dígitos; a formatação é responsabilidade da interface.</summary>
    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string digits = new(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
