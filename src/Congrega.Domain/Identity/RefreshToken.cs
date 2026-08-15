using Congrega.Domain.Common;

namespace Congrega.Domain.Identity;

public enum RefreshRevokeReason
{
    Logout = 1,
    ReuseDetected = 2,
    AdminRevoked = 3,
    CredentialChanged = 4,
    SupersededByRotation = 5
}

/// <summary>Veredito da apresentação de um refresh token.</summary>
public enum RefreshTokenOutcome
{
    /// <summary>Token válido e inédito: pode ser rotacionado.</summary>
    Rotate = 0,
    Expired = 1,
    Revoked = 2,

    /// <summary>
    /// Token já rotacionado sendo apresentado de novo. Sinal de roubo — ou de um
    /// cliente com retry malfeito. Não há como distinguir, e a resposta é a mesma.
    /// </summary>
    ReuseDetected = 3
}

public sealed record RefreshTokenReused(
    long UserId,
    Guid FamilyId,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Refresh token opaco, com rotação e rastreamento de family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opaco, não JWT.</b> Um JWT de refresh seria autocontido e, portanto,
/// impossível de revogar sem manter uma lista negra — exatamente o oposto do que se
/// quer de um token de vida longa. Aqui o valor é aleatório e o banco é a autoridade.
/// </para>
/// <para>
/// <b>Family.</b> Todos os tokens descendentes de um mesmo login compartilham
/// <see cref="FamilyId"/>. Quando um token já rotacionado reaparece, a family inteira
/// é revogada: se o atacante roubou e usou, o legítimo é expulso junto; se o legítimo
/// repetiu por falha de rede, ele refaz o login. O custo de errar para o lado
/// permissivo é a conta comprometida — por isso a escolha conservadora.
/// </para>
/// </remarks>
public sealed class RefreshToken : AggregateRoot
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    private RefreshToken()
    {
        TokenHash = [];
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public byte[] TokenHash { get; private set; }
    public Guid FamilyId { get; private set; }
    public long? ParentId { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshRevokeReason? RevokedReason { get; private set; }
    public string? DeviceLabel { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Tenant selecionado nesta sessão. <c>null</c> para assinante Congrega+ sem igreja.
    /// </summary>
    /// <remarks>
    /// Guardado no refresh token para que a rotação reemita o access token com o
    /// mesmo tenant. Sem isso, o refresh teria de adivinhar o tenant — e um usuário
    /// com duas igrejas cairia silenciosamente na errada a cada 15 minutos.
    /// A troca explícita de igreja atualiza este campo via
    /// <see cref="SelectTenant"/>, preservando a family e, com ela, a detecção de reuso.
    /// </remarks>
    public long? SelectedTenantId { get; private set; }

    /// <summary>Cria o primeiro token de uma nova family — ou seja, um novo login.</summary>
    public static RefreshToken StartFamily(
        long userId,
        byte[] tokenHash,
        DateTimeOffset now,
        long? selectedTenantId = null,
        string? deviceLabel = null,
        string? ipAddress = null,
        TimeSpan? lifetime = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tokenHash.Length);

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            ParentId = null,
            SelectedTenantId = selectedTenantId,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime ?? DefaultLifetime),
            DeviceLabel = deviceLabel,
            IpAddress = ipAddress
        };
    }

    /// <summary>Troca o tenant da sessão sem quebrar a family.</summary>
    public void SelectTenant(long? tenantId) => SelectedTenantId = tenantId;

    /// <summary>Avalia a apresentação deste token, sem alterar estado.</summary>
    /// <remarks>
    /// A ordem importa: revogação e reuso vêm antes de expiração. Um token roubado e
    /// expirado ainda é sinal de comprometimento, e classificá-lo como "expirado"
    /// perderia o alerta de segurança.
    /// </remarks>
    public RefreshTokenOutcome Evaluate(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return RefreshTokenOutcome.Revoked;
        }

        if (UsedAt is not null)
        {
            return RefreshTokenOutcome.ReuseDetected;
        }

        return now >= ExpiresAt ? RefreshTokenOutcome.Expired : RefreshTokenOutcome.Rotate;
    }

    /// <summary>
    /// Consome este token e emite o sucessor na mesma family.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Se o token não estiver apto à rotação. Chamar <see cref="Rotate"/> sem
    /// consultar <see cref="Evaluate"/> antes é erro de programação, não condição de
    /// runtime — daí a exceção em vez de um retorno de erro.
    /// </exception>
    public RefreshToken Rotate(
        byte[] newTokenHash,
        DateTimeOffset now,
        string? deviceLabel = null,
        string? ipAddress = null,
        TimeSpan? lifetime = null)
    {
        if (Evaluate(now) != RefreshTokenOutcome.Rotate)
        {
            throw new InvalidOperationException(
                $"Refresh token {Id} não está apto à rotação (situação: {Evaluate(now)}).");
        }

        UsedAt = now;

        return new RefreshToken
        {
            UserId = UserId,
            TokenHash = newTokenHash,
            FamilyId = FamilyId,          // mesma family: a cadeia é rastreável
            ParentId = Id,
            SelectedTenantId = SelectedTenantId,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime ?? DefaultLifetime),
            DeviceLabel = deviceLabel ?? DeviceLabel,
            IpAddress = ipAddress
        };
    }

    public void Revoke(RefreshRevokeReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;   // idempotente: revogar em massa não pode falhar por repetição
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    /// <summary>
    /// Registra o evento de reuso detectado. Chamado uma vez pelo caso de uso, após
    /// revogar a family, para que o Outbox dispare o alerta ao usuário.
    /// </summary>
    public void RaiseReuseDetected(DateTimeOffset now) =>
        Raise(new RefreshTokenReused(UserId, FamilyId, now));

    public bool IsActive(DateTimeOffset now) => Evaluate(now) == RefreshTokenOutcome.Rotate;
}
