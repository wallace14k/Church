using Congrega.Domain.Common;

namespace Congrega.Domain.Identity;

public enum UserStatus
{
    Active = 1,
    Blocked = 2,
    Anonymized = 3
}

public sealed record UserRegistered(long UserId, string Email, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserEmailVerified(long UserId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Identidade global da plataforma.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não tem <c>TenantId</c>, e isso é a decisão estrutural do sistema inteiro.</b>
/// Identidade é global; pertencimento é contextual, expresso em <c>Membership</c>.
/// </para>
/// <para>
/// Se esta classe tivesse <c>TenantId</c>: a mesma pessoa em duas igrejas viraria
/// duas contas com dois logins; o assinante Congrega+ sem igreja não teria onde
/// existir; e mudar de igreja destruiria o vínculo com dízimos e presenças
/// anteriores. Ver <c>docs/04-modelagem-dados.md</c> §2.1.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot
{
    private User()
    {
        Email = string.Empty;
        FullName = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public string Email { get; private set; }
    public string FullName { get; private set; }
    public string? Phone { get; private set; }
    public bool EmailVerified { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? AnonymizedAt { get; private set; }

    /// <summary>
    /// Normaliza um e-mail para comparação e armazenamento.
    /// </summary>
    /// <remarks>
    /// Um único ponto de normalização. Sem isso, "Joao@Igreja.com" e
    /// "joao@igreja.com " criariam duas contas — e o usuário juraria que já tinha
    /// cadastro. O banco reforça com <c>CITEXT</c> e índice único.
    /// </remarks>
    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    public static User Register(string email, string fullName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Email = NormalizeEmail(email),
            FullName = fullName.Trim(),
            EmailVerified = false,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        user.Raise(new UserRegistered(user.Id, user.Email, now));
        return user;
    }

    public void MarkEmailVerified(DateTimeOffset now)
    {
        if (EmailVerified)
        {
            return;
        }

        EmailVerified = true;
        UpdatedAt = now;
        Raise(new UserEmailVerified(Id, now));
    }

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void Block(DateTimeOffset now)
    {
        Status = UserStatus.Blocked;
        UpdatedAt = now;
    }

    /// <summary>
    /// Exercício do direito ao esquecimento (LGPD, Art. 18, VI).
    /// </summary>
    /// <remarks>
    /// Destrói a PII e mantém a linha. A linha precisa sobreviver porque os
    /// lançamentos financeiros a referenciam por FK <c>RESTRICT</c>: o relatório do
    /// exercício continua fechando, a auditoria continua possível, e não resta
    /// nenhum dado pessoal associado. <c>DELETE</c> quebraria a contabilidade que a
    /// igreja tem obrigação legal de manter. Ver ADR-015.
    /// </remarks>
    public void Anonymize(DateTimeOffset now)
    {
        if (AnonymizedAt is not null)
        {
            return;
        }

        // O e-mail precisa continuar único e não pode colidir com outro titular
        // anonimizado — daí o identificador opaco embutido.
        Email = $"anon-{PublicId:N}@removido.congrega.app";
        FullName = "Titular removido";
        Phone = null;
        EmailVerified = false;
        Status = UserStatus.Anonymized;
        AnonymizedAt = now;
        UpdatedAt = now;
    }

    public bool CanAuthenticate() => Status == UserStatus.Active;
}
