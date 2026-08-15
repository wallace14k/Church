using Congrega.Domain.Identity;

namespace Congrega.Domain.UnitTests;

/// <summary>
/// Invariantes do código OTP. Cada teste corresponde a um controle descrito em
/// <c>docs/02-autenticacao.md</c> §8 — se um deles quebrar, um controle de segurança
/// documentado deixou de existir.
/// </summary>
public sealed class EmailVerificationCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CorrectHash = [1, 2, 3, 4];
    private static readonly byte[] WrongHash = [9, 9, 9, 9];

    // Comparação trivial: o teste verifica a lógica do agregado, não a criptografia.
    private static bool Compare(byte[] a, byte[] b) => a.SequenceEqual(b);

    private static EmailVerificationCode Issue(short maxAttempts = 5) =>
        EmailVerificationCode.Issue(
            userId: 1337,
            codeHash: CorrectHash,
            purpose: OtpPurpose.Login,
            now: Now,
            maxAttempts: maxAttempts);

    [Fact]
    public void Codigo_correto_e_aceito_e_consumido()
    {
        var code = Issue();

        Assert.Equal(OtpValidationResult.Valid, code.Validate(CorrectHash, Compare, Now));
        Assert.NotNull(code.ConsumedAt);
    }

    [Fact]
    public void Codigo_e_de_uso_unico()
    {
        var code = Issue();
        code.Validate(CorrectHash, Compare, Now);

        // Segunda apresentação do MESMO código correto: recusada. Sem isso, um código
        // interceptado continuaria valendo durante toda a janela de 10 minutos.
        Assert.Equal(OtpValidationResult.AlreadyConsumed, code.Validate(CorrectHash, Compare, Now));
    }

    [Fact]
    public void Codigo_expirado_e_recusado()
    {
        var code = Issue();
        var afterExpiry = Now.Add(EmailVerificationCode.DefaultLifetime).AddSeconds(1);

        Assert.Equal(OtpValidationResult.Expired, code.Validate(CorrectHash, Compare, afterExpiry));
    }

    [Fact]
    public void Codigo_expira_exatamente_no_limite_e_nao_um_segundo_depois()
    {
        var code = Issue();
        var exactlyAtExpiry = Now.Add(EmailVerificationCode.DefaultLifetime);

        // Fronteira fechada no fim: no instante da expiração já não vale.
        Assert.Equal(OtpValidationResult.Expired, code.Validate(CorrectHash, Compare, exactlyAtExpiry));
        Assert.Equal(
            OtpValidationResult.Valid,
            Issue().Validate(CorrectHash, Compare, exactlyAtExpiry.AddTicks(-1)));
    }

    [Fact]
    public void Tentativa_errada_incrementa_o_contador()
    {
        var code = Issue();

        Assert.Equal(OtpValidationResult.Mismatch, code.Validate(WrongHash, Compare, Now));
        Assert.Equal((short)1, code.AttemptCount);
    }

    [Fact]
    public void Contador_e_incrementado_antes_da_comparacao()
    {
        var code = Issue();

        // O comparador lança. Se o incremento viesse depois da comparação, esta
        // tentativa sairia de graça — e um atacante que forçasse a exceção teria
        // tentativas ilimitadas.
        Assert.ThrowsAny<Exception>(() =>
            code.Validate(WrongHash, (_, _) => throw new InvalidOperationException(), Now));

        Assert.Equal((short)1, code.AttemptCount);
    }

    [Fact]
    public void Codigo_e_travado_apos_o_limite_de_tentativas()
    {
        var code = Issue(maxAttempts: 3);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(OtpValidationResult.Mismatch, code.Validate(WrongHash, Compare, Now));
        }

        // A quarta tentativa nem chega a comparar — e o código correto também é
        // recusado. Travar apenas as erradas deixaria a força bruta continuar.
        Assert.Equal(OtpValidationResult.TooManyAttempts, code.Validate(WrongHash, Compare, Now));
        Assert.Equal(OtpValidationResult.TooManyAttempts, code.Validate(CorrectHash, Compare, Now));
        Assert.Equal((short)3, code.AttemptCount);
    }

    [Fact]
    public void Estado_terminal_nao_consome_tentativa()
    {
        var code = Issue();
        code.Validate(CorrectHash, Compare, Now);
        short attemptsAfterConsumption = code.AttemptCount;

        code.Validate(WrongHash, Compare, Now);

        // Já consumido: não há o que proteger, então não faz sentido gastar tentativa.
        Assert.Equal(attemptsAfterConsumption, code.AttemptCount);
    }

    [Fact]
    public void Invalidacao_encerra_o_codigo_sem_consumi_lo()
    {
        var code = Issue();
        code.Invalidate(Now);

        Assert.False(code.IsActive(Now));
        Assert.Null(code.ConsumedAt);
        Assert.Equal(OtpValidationResult.Expired, code.Validate(CorrectHash, Compare, Now));
    }
}

/// <summary>Rotação, family e detecção de reuso do refresh token.</summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken StartFamily(long? tenantId = null) =>
        RefreshToken.StartFamily(
            userId: 1337,
            tokenHash: [1, 2, 3, 4],
            now: Now,
            selectedTenantId: tenantId,
            deviceLabel: "iPhone da Ana");

    [Fact]
    public void Token_novo_pode_ser_rotacionado()
    {
        Assert.Equal(RefreshTokenOutcome.Rotate, StartFamily().Evaluate(Now));
    }

    [Fact]
    public void Rotacao_consome_o_token_e_mantem_a_family()
    {
        var original = StartFamily();

        var rotated = original.Rotate([5, 6, 7, 8], Now.AddMinutes(10));

        Assert.NotNull(original.UsedAt);
        Assert.Equal(original.FamilyId, rotated.FamilyId);
        Assert.Equal(original.UserId, rotated.UserId);
        Assert.Equal("iPhone da Ana", rotated.DeviceLabel);
    }

    [Fact]
    public void Rotacao_preserva_o_tenant_da_sessao()
    {
        // Sem isso, um usuário com duas igrejas cairia silenciosamente na errada a
        // cada renovação de access token.
        var original = StartFamily(tenantId: 42);

        var rotated = original.Rotate([5, 6, 7, 8], Now.AddMinutes(10));

        Assert.Equal(42L, rotated.SelectedTenantId);
    }

    [Fact]
    public void Reapresentar_token_ja_rotacionado_e_detectado_como_reuso()
    {
        var original = StartFamily();
        original.Rotate([5, 6, 7, 8], Now.AddMinutes(10));

        Assert.Equal(RefreshTokenOutcome.ReuseDetected, original.Evaluate(Now.AddMinutes(11)));
    }

    [Fact]
    public void Reuso_tem_precedencia_sobre_expiracao()
    {
        // Um token roubado E expirado ainda é sinal de comprometimento. Classificá-lo
        // como "expirado" perderia o alerta de segurança e a revogação da family.
        var original = StartFamily();
        original.Rotate([5, 6, 7, 8], Now.AddMinutes(10));

        var longAfterExpiry = Now.Add(RefreshToken.DefaultLifetime).AddDays(1);

        Assert.Equal(RefreshTokenOutcome.ReuseDetected, original.Evaluate(longAfterExpiry));
    }

    [Fact]
    public void Revogacao_tem_precedencia_sobre_tudo()
    {
        var original = StartFamily();
        original.Revoke(RefreshRevokeReason.Logout, Now.AddMinutes(5));

        Assert.Equal(RefreshTokenOutcome.Revoked, original.Evaluate(Now.AddMinutes(6)));
    }

    [Fact]
    public void Token_expirado_e_recusado()
    {
        var token = StartFamily();
        Assert.Equal(
            RefreshTokenOutcome.Expired,
            token.Evaluate(Now.Add(RefreshToken.DefaultLifetime)));
    }

    [Fact]
    public void Rotacionar_token_inapto_e_erro_de_programacao()
    {
        var token = StartFamily();
        token.Revoke(RefreshRevokeReason.Logout, Now);

        // Exceção e não retorno de erro: chamar Rotate sem consultar Evaluate antes
        // é bug do chamador, e falhar alto é melhor que emitir sessão indevida.
        Assert.Throws<InvalidOperationException>(() => token.Rotate([9, 9], Now.AddMinutes(1)));
    }

    [Fact]
    public void Revogacao_e_idempotente()
    {
        var token = StartFamily();
        token.Revoke(RefreshRevokeReason.Logout, Now);
        var firstRevocation = token.RevokedAt;

        // Revogação em massa da family não pode falhar por já ter revogado um item.
        token.Revoke(RefreshRevokeReason.ReuseDetected, Now.AddHours(1));

        Assert.Equal(firstRevocation, token.RevokedAt);
        Assert.Equal(RefreshRevokeReason.Logout, token.RevokedReason);
    }

    [Fact]
    public void Cadeia_de_rotacoes_mantem_a_mesma_family()
    {
        // Uma sessão de 30 dias rotaciona ~2.900 vezes. A family precisa sobreviver
        // à cadeia inteira, senão a detecção de reuso só cobre o último elo.
        var current = StartFamily();
        var familyId = current.FamilyId;

        for (int i = 1; i <= 50; i++)
        {
            current = current.Rotate([(byte)i, 0, 0, 0], Now.AddMinutes(i * 15));
            Assert.Equal(familyId, current.FamilyId);
        }

        Assert.Equal(RefreshTokenOutcome.Rotate, current.Evaluate(Now.AddMinutes(760)));
    }
}

/// <summary>Invariantes da identidade global.</summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  Joao@Igreja.COM  ", "joao@igreja.com")]
    [InlineData("ANA@congrega.app", "ana@congrega.app")]
    public void Email_e_normalizado_no_cadastro(string input, string expected)
    {
        // Sem normalização em ponto único, "Joao@Igreja.com" e "joao@igreja.com "
        // criariam duas contas — e o usuário juraria que já tinha cadastro.
        Assert.Equal(expected, User.Register(input, "João", Now).Email);
    }

    [Fact]
    public void Usuario_nasce_com_email_nao_verificado()
    {
        Assert.False(User.Register("joao@igreja.com", "João", Now).EmailVerified);
    }

    [Fact]
    public void Anonimizacao_destroi_a_pii_e_preserva_a_linha()
    {
        var user = User.Register("joao@igreja.com", "João Silva", Now);
        user.MarkEmailVerified(Now);

        user.Anonymize(Now.AddYears(1));

        Assert.Equal("Titular removido", user.FullName);
        Assert.DoesNotContain("joao", user.Email, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UserStatus.Anonymized, user.Status);
        Assert.False(user.EmailVerified);
        Assert.NotNull(user.AnonymizedAt);

        // A linha continua existindo: os lançamentos financeiros a referenciam por FK
        // RESTRICT, e o fechamento contábil da igreja precisa continuar fechando.
        Assert.NotEqual(Guid.Empty, user.PublicId);
    }

    [Fact]
    public void Email_anonimizado_permanece_unico_entre_titulares()
    {
        var first = User.Register("joao@igreja.com", "João", Now);
        var second = User.Register("maria@igreja.com", "Maria", Now);

        first.Anonymize(Now);
        second.Anonymize(Now);

        // Colisão aqui violaria o índice único de e-mail e faria a segunda
        // anonimização falhar — travando um pedido de exclusão da LGPD.
        Assert.NotEqual(first.Email, second.Email);
    }

    [Fact]
    public void Conta_anonimizada_ou_bloqueada_nao_autentica()
    {
        var blocked = User.Register("joao@igreja.com", "João", Now);
        blocked.Block(Now);
        Assert.False(blocked.CanAuthenticate());

        var anonymized = User.Register("maria@igreja.com", "Maria", Now);
        anonymized.Anonymize(Now);
        Assert.False(anonymized.CanAuthenticate());
    }
}
