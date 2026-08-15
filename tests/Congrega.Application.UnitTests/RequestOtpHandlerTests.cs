using Congrega.Application.Identity;
using Congrega.Application.UnitTests.Fakes;
using Congrega.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

public sealed class RequestOtpHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeOtpCodeRepository _codes = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeSecretHasher _hasher = new();

    private RequestOtpHandler CreateHandler(string code = "123456") => new(
        _users,
        _codes,
        new FakeOtpGenerator(code),
        _hasher,
        _outbox,
        _unitOfWork,
        new FakeTimeProvider(Now),
        NullLogger<RequestOtpHandler>.Instance);

    [Fact]
    public async Task Email_desconhecido_cria_a_conta_e_emite_o_codigo()
    {
        // Cadastro e login são o mesmo fluxo: não existe tela separada de criar conta.
        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "novo@igreja.com" }, CancellationToken.None);

        Assert.True(result.UserCreated);
        Assert.True(result.CodeIssued);
        Assert.Single(_users.Added);
        Assert.Equal("novo@igreja.com", _users.Added[0].Email);
        Assert.False(_users.Added[0].EmailVerified);
    }

    [Fact]
    public async Task Email_e_normalizado_antes_da_busca()
    {
        _users.Seed(User.Register("joao@igreja.com", "João", Now).WithId(10));

        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "  JOAO@Igreja.COM  " }, CancellationToken.None);

        // Sem normalização, criaria uma segunda conta para a mesma pessoa.
        Assert.False(result.UserCreated);
        Assert.Empty(_users.Added);
    }

    [Fact]
    public async Task Codigo_e_persistido_apenas_como_hash()
    {
        await CreateHandler("987654").HandleAsync(
            new RequestOtpCommand { Email = "novo@igreja.com" }, CancellationToken.None);

        var stored = Assert.Single(_codes.Added);

        // O agregado guarda o hash. O texto plano existe só no payload do Outbox,
        // que é lido pelo dispatcher de e-mail e nunca aparece em log.
        Assert.Equal(_hasher.HashOtp("987654"), stored.CodeHash);
        Assert.True(_outbox.Contains("SendOtpEmail"));
    }

    [Fact]
    public async Task Codigos_anteriores_sao_invalidados_antes_de_emitir_o_novo()
    {
        _users.Seed(User.Register("joao@igreja.com", "João", Now).WithId(10));

        await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "joao@igreja.com" }, CancellationToken.None);

        // Um código válido por vez. Sem isso, cinco reenvios dariam ao atacante cinco
        // chances simultâneas em vez de apenas renovar a chance existente.
        Assert.Equal(1, _codes.InvalidateCallCount);
    }

    [Fact]
    public async Task Conta_bloqueada_nao_emite_codigo_mas_devolve_o_mesmo_formato()
    {
        var blocked = User.Register("bloqueado@igreja.com", "Bloqueado", Now).WithId(10);
        blocked.Block(Now);
        _users.Seed(blocked);

        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "bloqueado@igreja.com" }, CancellationToken.None);

        Assert.False(result.CodeIssued);
        Assert.Empty(_codes.Added);
        Assert.False(_outbox.Contains("SendOtpEmail"));

        // A API converte qualquer resultado em 202. O chamador não distingue conta
        // bloqueada de conta inexistente de emissão bem-sucedida.
    }

    [Fact]
    public async Task Rate_limit_por_email_bloqueia_alem_do_limite_da_janela()
    {
        _users.Seed(User.Register("alvo@igreja.com", "Alvo", Now).WithId(10));
        _codes.IssuedInWindow = 5;   // limite da janela de 15 minutos já atingido

        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "alvo@igreja.com" }, CancellationToken.None);

        // Fecha o vetor que o limitador por IP não cobre: pedir códigos para a caixa
        // da vítima a partir de muitos IPs diferentes.
        Assert.False(result.CodeIssued);
        Assert.Empty(_codes.Added);
    }

    [Fact]
    public async Task Rate_limit_permite_ate_o_limite()
    {
        _users.Seed(User.Register("alvo@igreja.com", "Alvo", Now).WithId(10));
        _codes.IssuedInWindow = 4;   // ainda dentro do limite

        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "alvo@igreja.com" }, CancellationToken.None);

        Assert.True(result.CodeIssued);
    }

    [Fact]
    public async Task Conta_recem_criada_nao_e_barrada_pelo_rate_limit()
    {
        // Usuário novo tem Id 0 até o SaveChanges. Sem a guarda, a contagem por
        // usuário rodaria com Id 0 e poderia barrar todo primeiro acesso da
        // plataforma — o pior bug possível no funil de cadastro.
        _codes.IssuedInWindow = 99;

        var result = await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "primeiro@igreja.com" }, CancellationToken.None);

        Assert.True(result.CodeIssued);
    }

    [Fact]
    public async Task Tudo_e_persistido_em_uma_unica_transacao()
    {
        await CreateHandler().HandleAsync(
            new RequestOtpCommand { Email = "novo@igreja.com" }, CancellationToken.None);

        // Código e mensagem de Outbox no mesmo commit: não existe estado em que o
        // código foi gravado e o e-mail nunca sairá, nem o inverso.
        Assert.Equal(1, _unitOfWork.SaveCallCount);
    }
}
