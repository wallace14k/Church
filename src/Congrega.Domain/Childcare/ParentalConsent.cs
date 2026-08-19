namespace Congrega.Domain.Childcare;

/// <summary>
/// Prova do consentimento parental exigido pelo Art. 14 da LGPD.
/// </summary>
/// <remarks>
/// <para>
/// <b>A versão do texto é o campo que dá valor ao registro.</b> Guardar apenas
/// "fulano consentiu em tal data" não demonstra <i>a que</i> ele consentiu — e
/// o termo muda com o tempo. Sem a versão, o registro não serve para o que
/// existe, que é sustentar o tratamento diante de questionamento.
/// </para>
/// <para>
/// <b>Revogar não apaga.</b> O consentimento é revogável por lei, mas a prova
/// de que ele existiu no passado é justamente o que protege o tratamento já
/// realizado sob ele. Apagar a linha destruiria a defesa junto com o
/// consentimento.
/// </para>
/// </remarks>
public sealed class ParentalConsent
{
    private ParentalConsent()
    {
        ConsentVersion = string.Empty;
    }

    public long Id { get; private set; }
    public long TenantId { get; private set; }
    public long ChildId { get; private set; }
    public long GrantedByMemberId { get; private set; }

    /// <summary>Identifica o texto consentido — ex.: <c>checkin-v1-2026-08</c>.</summary>
    public string ConsentVersion { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }
    public string? GrantedIp { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ParentalConsent Grant(
        long tenantId,
        long childId,
        long grantedByMemberId,
        string consentVersion,
        DateTimeOffset now,
        string? grantedIp = null,
        string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consentVersion);

        return new ParentalConsent
        {
            TenantId = tenantId,
            ChildId = childId,
            GrantedByMemberId = grantedByMemberId,
            ConsentVersion = consentVersion.Trim(),
            GrantedAt = now,
            GrantedIp = grantedIp,
            UserAgent = userAgent,
        };
    }

    /// <summary>Vale agora? Revogado nunca volta a valer.</summary>
    public bool IsActiveOn(DateTimeOffset moment) => RevokedAt is null || RevokedAt > moment;

    /// <returns><c>true</c> se esta chamada revogou; <c>false</c> se já estava revogado.</returns>
    public bool Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return false;
        }

        RevokedAt = now;
        return true;
    }
}
