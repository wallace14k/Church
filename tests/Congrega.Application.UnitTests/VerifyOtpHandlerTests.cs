using Congrega.Application.Identity;
using Congrega.Application.UnitTests.Fakes;
using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

public sealed class VerifyOtpHandlerTests
{
    private const string ValidCode = "123456";
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantPublicId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeUserRepository _users = new();
    private readonly FakeOtpCodeRepository _codes = new();
    private readonly FakeRefreshTokenRepository _refreshTokens = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakeSecretHasher _hasher = new();
    private readonly FakeTokenIssuer _tokenIssuer = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuthenticationContextWriter _authContext = new();

    private VerifyOtpHandler CreateHandler() => new(
        _users, _codes, _refreshTokens, _memberships,
        _hasher, _tokenIssuer, new FakeTierProvider(), _outbox, _unitOfWork,
        new FakeTimeProvider(Now),
        _authContext,
        NullLogger<VerifyOtpHandler>.Instance);

    private User SeedUser(long id = 10)
    {
        var user = User.Register("joao@igreja.com", "João", Now).WithId(id);
        _users.Seed(user);
        return user;
    }

    private void SeedCode(long userId, string code = ValidCode)
    {
        _codes.Seed(EmailVerificationCode.Issue(
            userId, _hasher.HashOtp(code), OtpPurpose.Login, Now).WithId(1));
    }

    private static VerifyOtpCommand Command(string code = ValidCode, Guid? tenantId = null) => new()
    {
        Email = "joao@igreja.com",
        Code = code,
        TenantPublicId = tenantId
    };

    // -------------------------------------------------------------------------
    // Caminho feliz
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Codigo_correto_emite_a_sessao_e_verifica_o_email()
    {
        var user = SeedUser();
        SeedCode(user.Id);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Session!.AccessToken);
        Assert.NotEmpty(result.Session.RefreshToken);
        Assert.True(user.EmailVerified);
        Assert.NotNull(user.LastLoginAt);
        Assert.Single(_refreshTokens.Added);
    }

    [Fact]
    public async Task Login_bem_sucedido_registra_evento_de_seguranca()
    {
        var user = SeedUser();
        SeedCode(user.Id);

        await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(_outbox.ContainsSecurityEvent("LoginSucceeded"));
    }

    // -------------------------------------------------------------------------
    // Falhas — todas indistinguíveis para o chamador
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Codigo_errado_falha_e_persiste_a_tentativa()
    {
        var user = SeedUser();
        SeedCode(user.Id);

        var result = await CreateHandler().HandleAsync(Command("000000"), CancellationToken.None);

        Assert.False(result.Succeeded);

        // ESTA é a asserção que importa. Validate() incrementou o contador de
        // tentativas; sem o SaveChanges no caminho de erro, o contador voltaria a
        // zero a cada tentativa, o limite de 5 nunca seria atingido e a força bruta
        // sobre 10^6 combinações ficaria viável.
        Assert.Equal(1, _unitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task Email_inexistente_tambem_calcula_um_hash()
    {
        // Sem o hash descartado, o caminho "usuário inexistente" retornaria
        // consistentemente mais rápido, e a diferença de latência viraria o oráculo
        // de enumeração que a resposta uniforme tenta evitar.
        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, _hasher.HashOtpCallCount);
    }

    [Fact]
    public async Task Sem_codigo_ativo_tambem_calcula_um_hash()
    {
        SeedUser();   // usuário existe, mas nunca pediu código

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, _hasher.HashOtpCallCount);
    }

    [Fact]
    public async Task Conta_bloqueada_nao_autentica_mesmo_com_codigo_valido()
    {
        var user = SeedUser();
        SeedCode(user.Id);
        user.Block(Now);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(_refreshTokens.Added);
    }

    [Fact]
    public async Task Codigo_expirado_falha()
    {
        var user = SeedUser();
        _codes.Seed(EmailVerificationCode.Issue(
            user.Id,
            _hasher.HashOtp(ValidCode),
            OtpPurpose.Login,
            Now.Subtract(EmailVerificationCode.DefaultLifetime).AddMinutes(-1)).WithId(1));

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Codigo_e_de_uso_unico_entre_chamadas()
    {
        var user = SeedUser();
        SeedCode(user.Id);

        var first = await CreateHandler().HandleAsync(Command(), CancellationToken.None);
        var second = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
    }

    // -------------------------------------------------------------------------
    // Seleção de tenant
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Vinculo_unico_e_selecionado_automaticamente()
    {
        var user = SeedUser();
        SeedCode(user.Id);
        _memberships.Seed(user.Id, tenantId: 42, TenantPublicId, roles: [SystemRoles.ChurchAdmin]);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // Poupa uma tela de seleção com uma opção só — o caso da maioria dos usuários.
        Assert.Equal(TenantPublicId, result.Session!.TenantPublicId);
        Assert.Contains(SystemRoles.ChurchAdmin, result.Session.Roles);
        Assert.Equal(42L, _refreshTokens.Added[0].SelectedTenantId);
    }

    [Fact]
    public async Task Usuario_sem_igreja_recebe_sessao_sem_tenant()
    {
        var user = SeedUser();
        SeedCode(user.Id);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // Assinante Congrega+ sem igreja é cidadão de primeira classe: autentica
        // normalmente, apenas sem contexto de tenant.
        Assert.True(result.Succeeded);
        Assert.Null(result.Session!.TenantPublicId);
        Assert.Empty(result.Session.Roles);
    }

    [Fact]
    public async Task Tenant_pedido_sem_vinculo_nao_e_concedido()
    {
        var user = SeedUser();
        SeedCode(user.Id);
        // Nenhuma membership cadastrada, mas o cliente pede um tenant.

        var result = await CreateHandler().HandleAsync(
            Command(tenantId: Guid.NewGuid()), CancellationToken.None);

        // A sessão sai SEM tenant — não com o tenant pedido. A claim descreve
        // escolha; o banco decide permissão.
        Assert.True(result.Succeeded);
        Assert.Null(result.Session!.TenantPublicId);
    }

    [Fact]
    public async Task Tenant_suspenso_nao_entra_na_sessao()
    {
        var user = SeedUser();
        SeedCode(user.Id);
        _memberships.Seed(user.Id, 42, TenantPublicId, TenantStatus.Suspended, [SystemRoles.ChurchAdmin]);

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // Corte por inadimplência precisa valer no login, não só na tela de cobrança.
        Assert.True(result.Succeeded);
        Assert.Null(result.Session!.TenantPublicId);
    }

    [Fact]
    public async Task Com_varios_vinculos_nenhum_e_escolhido_sem_pedido_explicito()
    {
        var user = SeedUser();
        SeedCode(user.Id);
        _memberships.Seed(user.Id, 42, TenantPublicId);
        _memberships.Seed(user.Id, 43, Guid.NewGuid());

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        // Escolher por conta própria colocaria o usuário na igreja errada em
        // silêncio. Melhor exigir a seleção explícita.
        Assert.Null(result.Session!.TenantPublicId);
    }
}
