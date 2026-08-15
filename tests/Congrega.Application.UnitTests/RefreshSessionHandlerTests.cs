using Congrega.Application.Identity;
using Congrega.Application.UnitTests.Fakes;
using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

public sealed class RefreshSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantPublicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TokenValue = "refresh-abc";

    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _refreshTokens = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakeSecretHasher _hasher = new();
    private readonly FakeTokenIssuer _tokenIssuer = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RefreshSessionHandler CreateHandler() => new(
        _refreshTokens, _users, _memberships,
        _hasher, _tokenIssuer, new FakeTierProvider(), _outbox, _unitOfWork,
        new FakeTimeProvider(Now),
        NullLogger<RefreshSessionHandler>.Instance);

    private User SeedUser(long id = 10)
    {
        var user = User.Register("joao@igreja.com", "João", Now).WithId(id);
        user.MarkEmailVerified(Now);
        _users.Seed(user);
        return user;
    }

    private RefreshToken SeedToken(long userId = 10, long? tenantId = null, long id = 1)
    {
        var token = RefreshToken
            .StartFamily(userId, _hasher.HashToken(TokenValue), Now.AddDays(-1), tenantId)
            .WithId(id);
        _refreshTokens.Seed(token);
        return token;
    }

    private static RefreshSessionCommand Command(Guid? switchTo = null) => new()
    {
        RefreshToken = TokenValue,
        SwitchToTenantPublicId = switchTo
    };

    // -------------------------------------------------------------------------
    // Rotação
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Token_valido_e_rotacionado()
    {
        SeedUser();
        var original = SeedToken();

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(original.UsedAt);
        var rotated = Assert.Single(_refreshTokens.Added);
        Assert.Equal(original.FamilyId, rotated.FamilyId);
    }

    [Fact]
    public async Task Rotacao_reemite_o_access_token_com_o_tenant_da_sessao()
    {
        var user = SeedUser();
        SeedToken(user.Id, tenantId: 42);
        _memberships.Seed(user.Id, 42, TenantPublicId, roles: [SystemRoles.Treasurer]);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(TenantPublicId, result.Session!.TenantPublicId);
        Assert.Contains(SystemRoles.Treasurer, result.Session.Roles);
    }

    [Fact]
    public async Task Papeis_sao_relidos_do_banco_a_cada_rotacao()
    {
        var user = SeedUser();
        SeedToken(user.Id, tenantId: 42);
        _memberships.Seed(user.Id, 42, TenantPublicId, roles: [SystemRoles.ChurchAdmin]);

        await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // A revalidação a cada rotação é o que faz uma revogação de papel valer em no
        // máximo 15 minutos, sem lista negra de tokens.
        var issued = Assert.Single(_tokenIssuer.Issued);
        Assert.Contains(SystemRoles.ChurchAdmin, issued.Roles);
    }

    // -------------------------------------------------------------------------
    // Detecção de reuso — o controle central
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Reuso_revoga_a_family_inteira()
    {
        SeedUser();
        var original = SeedToken();
        original.Rotate(_hasher.HashToken("refresh-def"), Now.AddMinutes(-10));

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.SessionTerminated);

        // Não dá para distinguir atacante de cliente com retry malfeito. Diante do
        // empate, a escolha conservadora: derruba a sessão inteira. Custo para o
        // legítimo é um login; custo de errar para o outro lado é a conta
        // comprometida por até 30 dias.
        Assert.Contains(original.FamilyId, _refreshTokens.RevokedFamilies);
    }

    [Fact]
    public async Task Reuso_registra_evento_critico_e_alerta_o_titular()
    {
        SeedUser();
        var original = SeedToken();
        original.Rotate(_hasher.HashToken("refresh-def"), Now.AddMinutes(-10));

        await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(_outbox.ContainsSecurityEvent("RefreshTokenReuseDetected"));

        // Avisar o titular é parte do controle, não cortesia: se foi roubo, ele é a
        // única pessoa capaz de reconhecer que não foi ele.
        Assert.True(_outbox.Contains("SendSecurityAlertEmail"));
    }

    [Fact]
    public async Task Reuso_nao_emite_sessao_nova()
    {
        SeedUser();
        var original = SeedToken();
        original.Rotate(_hasher.HashToken("refresh-def"), Now.AddMinutes(-10));

        await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Empty(_tokenIssuer.Issued);
    }

    // -------------------------------------------------------------------------
    // Recusas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Token_desconhecido_e_recusado()
    {
        SeedUser();

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.SessionTerminated);
    }

    [Fact]
    public async Task Token_revogado_e_recusado()
    {
        SeedUser();
        var token = SeedToken();
        token.Revoke(RefreshRevokeReason.Logout, Now.AddMinutes(-5));

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(_tokenIssuer.Issued);
    }

    [Fact]
    public async Task Conta_bloqueada_durante_a_sessao_derruba_todos_os_tokens()
    {
        var user = SeedUser();
        SeedToken(user.Id);
        user.Block(Now);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.SessionTerminated);

        // Um token válido não pode sobreviver ao bloqueio da conta.
        Assert.Contains(user.Id, _refreshTokens.RevokedUsers);
    }

    // -------------------------------------------------------------------------
    // Troca de igreja
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Troca_de_igreja_mantem_a_family()
    {
        var user = SeedUser();
        var original = SeedToken(user.Id, tenantId: 42);
        _memberships.Seed(user.Id, 43, TenantPublicId);

        var result = await CreateHandler().HandleAsync(
            Command(switchTo: TenantPublicId), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(TenantPublicId, result.Session!.TenantPublicId);

        // Emitir family nova a cada troca de contexto fragmentaria o rastreamento e
        // criaria sessões órfãs que a detecção de reuso não cobriria.
        Assert.Equal(original.FamilyId, _refreshTokens.Added[0].FamilyId);
    }

    [Fact]
    public async Task Troca_para_igreja_sem_vinculo_e_recusada()
    {
        var user = SeedUser();
        SeedToken(user.Id, tenantId: 42);

        var result = await CreateHandler().HandleAsync(
            Command(switchTo: Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(_refreshTokens.Added);
    }

    [Fact]
    public async Task Vinculo_revogado_durante_a_sessao_mantem_o_login_sem_tenant()
    {
        var user = SeedUser();
        SeedToken(user.Id, tenantId: 42);
        _memberships.RevokeAll();

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // Derrubar a sessão inteira seria hostil com quem apenas saiu de uma igreja e
        // continua assinante Congrega+.
        Assert.True(result.Succeeded);
        Assert.Null(result.Session!.TenantPublicId);
        Assert.Empty(result.Session.Roles);
    }
}
