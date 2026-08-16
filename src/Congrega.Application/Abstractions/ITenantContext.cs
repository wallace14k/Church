namespace Congrega.Application.Abstractions;

/// <summary>
/// Contexto de tenant e usuário da requisição corrente.
/// </summary>
/// <remarks>
/// <para>
/// Preenchido por middleware <b>depois</b> de validar que existe membership ativa
/// entre o usuário e o tenant da claim. A claim diz qual tenant o usuário
/// selecionou; este contexto só é populado com o que o banco confirmou.
/// </para>
/// <para>
/// É consumido em dois lugares, e a redundância é intencional:
/// </para>
/// <list type="number">
///   <item><description>
///     Global Query Filters do EF Core — a <b>autoridade</b> de isolamento.
///   </description></item>
///   <item><description>
///     Interceptor de conexão, que emite <c>SET LOCAL app.tenant_id</c> para as
///     policies de RLS — a <b>rede de segurança</b> para quando um filtro for
///     esquecido.
///   </description></item>
/// </list>
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// Tenant da requisição. <c>null</c> em requisição sem igreja — assinante
    /// Congrega+, endpoints públicos e jobs que cruzam tenants legitimamente.
    /// </summary>
    long? TenantId { get; }

    long? UserId { get; }

    /// <summary>
    /// Quando <c>true</c>, os Global Query Filters não se aplicam.
    /// </summary>
    /// <remarks>
    /// Reservado a workers que precisam cruzar tenants por natureza (faturamento,
    /// retenção). <b>Nunca</b> deve ser ligado a partir de uma requisição HTTP: em
    /// produção esses processos rodam com a role <c>congrega_worker</c>, e o RLS
    /// continuaria bloqueando mesmo se alguém tentasse.
    /// </remarks>
    bool IsCrossTenantOperation { get; }
}

/// <summary>
/// Informa ao contexto da requisição qual usuário acabou de se autenticar.
/// </summary>
/// <remarks>
/// <para>
/// Existe para um caso específico: <c>VerifyOtpHandler</c> e
/// <c>RefreshSessionHandler</c> rodam em endpoints anônimos — não há JWT ainda, é
/// isso que eles emitem — então o middleware nunca populou
/// <see cref="ITenantContext.UserId"/> a partir de claim nenhuma. No meio da
/// própria requisição, depois de validar o código OTP ou o hash do refresh token,
/// esses handlers descobrem a identidade e precisam consultar
/// <c>memberships</c> por <c>user_id</c> — e sem isto, o interceptor de conexão
/// manda <c>app.user_id</c> vazio para o Postgres, a policy de RLS nunca casa, e
/// a consulta volta silenciosamente vazia mesmo para um usuário com vínculo real.
/// </para>
/// <para>
/// <b>Por que não expor isto em <see cref="ITenantContext"/> direto.</b>
/// Qualquer código com uma referência a <c>ITenantContext</c> poderia então
/// reatribuir o usuário da requisição — a interface de leitura precisa continuar
/// somente-leitura para todo o resto do sistema. Só os dois handlers que resolvem
/// identidade a partir de credencial crua (não de claim) recebem esta segunda
/// interface, injetada ao lado da primeira.
/// </para>
/// </remarks>
public interface IAuthenticationContextWriter
{
    /// <summary>
    /// Chamar <b>somente</b> depois de confirmar a identidade — código OTP válido
    /// ou hash de refresh token encontrado. Nunca antes: contexto errado é pior
    /// que contexto ausente, porque parece isolamento funcionando.
    /// </summary>
    void SetAuthenticatedUser(long userId);
}
