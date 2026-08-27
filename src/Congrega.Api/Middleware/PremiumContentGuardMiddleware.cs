using Congrega.Infrastructure.Licensing;
using Microsoft.AspNetCore.Http.Extensions;

namespace Congrega.Api.Middleware;

/// <summary>
/// Barra o conteúdo premium quando a licença não está ativa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Guarda SÓ o conteúdo premium, e essa é a decisão mais importante deste
/// arquivo.</b> Se a assinatura do Congrega+ vencer, a igreja continua entrando
/// no sistema, vendo os membros dela, lançando o dízimo e imprimindo a agenda —
/// tudo isso roda no servidor dela, com dados dela, e travar o acesso seria
/// tomar como refém um dado do qual ela é a controladora. Só o catálogo de
/// conteúdo, que é <b>nosso</b> e mora na <b>nossa</b> infraestrutura, depende
/// da assinatura estar em dia.
/// </para>
/// <para>
/// <b>Isto não é a barreira de segurança.</b> Roda no servidor do cliente, e
/// quem quiser burlar remove esta linha do <c>Program.cs</c>. A barreira real é
/// o servidor central recusar-se a emitir a URL assinada — e o arquivo não
/// existir na infraestrutura do cliente para ser servido de outro jeito.
/// </para>
/// <para>
/// O que este middleware entrega é <b>falhar cedo e explicar</b>: sem ele, a
/// requisição iria até o servidor central para voltar com um 402 sem contexto, e
/// a igreja veria "erro" em vez de "sua assinatura venceu em 12/08".
/// </para>
/// </remarks>
public sealed class PremiumContentGuardMiddleware(RequestDelegate proximo)
{
    /// <summary>
    /// Prefixos que exigem licença.
    /// </summary>
    /// <remarks>
    /// Lista de <b>inclusão</b>, e não de exclusão. Uma lista de exclusão faria
    /// toda rota nova nascer protegida — e a primeira tela de membros que
    /// alguém acrescentasse ficaria inacessível para uma igreja sem Congrega+,
    /// sem ninguém perceber até o suporte tocar.
    /// </remarks>
    private static readonly string[] RotasProtegidas =
    [
        "/api/v1/premium",
    ];

    public async Task InvokeAsync(HttpContext contexto, ILicenseGuard licenca)
    {
        var caminho = contexto.Request.Path.Value ?? string.Empty;

        var protegida = RotasProtegidas.Any(
            prefixo => caminho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase));

        if (!protegida)
        {
            await proximo(contexto);
            return;
        }

        var status = await licenca.AvaliarAsync(contexto.RequestAborted);

        if (!status.Valid)
        {
            // 402 e não 403: "pagamento necessário" descreve o estado melhor do
            // que "proibido", e o cliente consegue distinguir uma assinatura
            // vencida de uma falta de permissão do usuário — que também devolve
            // 403 nesta API e pede uma ação completamente diferente.
            await Microsoft.AspNetCore.Http.Results
                .Problem(
                    title: "Congrega+ indisponível",
                    detail: status.Reason,
                    statusCode: StatusCodes.Status402PaymentRequired,
                    instance: contexto.Request.GetEncodedPathAndQuery())
                .ExecuteAsync(contexto);

            return;
        }

        if (status.InGrace)
        {
            // Passa, e avisa. A igreja continua assistindo enquanto o problema é
            // nosso — e o cabeçalho dá ao aplicativo o que precisa para mostrar
            // "não conseguimos confirmar sua assinatura desde terça" antes de a
            // tolerância acabar de vez.
            contexto.Response.Headers["X-Congrega-License-Grace"] =
                status.ValidUntil.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        await proximo(contexto);
    }
}
