using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.Licensing;

/// <summary>
/// Anexa a chave de licença às chamadas ao servidor central.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um <c>DelegatingHandler</c> aqui e um middleware lá, e a divisão não é
/// estilo.</b> Middleware intercepta o que <i>entra</i> no servidor do cliente —
/// é onde a requisição do membro é barrada. <c>DelegatingHandler</c> intercepta
/// o que <i>sai</i> — é onde a credencial é anexada. Trocar os dois de lugar
/// significaria carimbar a chave em requisições de membros ou tentar barrar uma
/// chamada que já é nossa.
/// </para>
/// <para>
/// <b>A chave vai em cabeçalho, nunca no caminho da URL.</b> Caminho aparece em
/// log de acesso, em referer e em qualquer proxy do meio — foi por isso que o
/// cliente do Telegram, cuja API exige o token na URL, teve o log desligado.
/// Aqui não há essa imposição, e cabeçalho é a escolha barata e correta.
/// </para>
/// </remarks>
internal sealed class LicenseKeyHandler(IOptions<LicenseOptions> opcoes) : DelegatingHandler
{
    private const string Cabecalho = "X-Congrega-License";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var chave = opcoes.Value.Key;

        if (!string.IsNullOrWhiteSpace(chave))
        {
            request.Headers.Remove(Cabecalho);
            request.Headers.Add(Cabecalho, chave);
        }

        // O identificador da instalação viaja junto e não é segredo: ele é o que
        // permite ao servidor central perceber a mesma licença rodando em nove
        // servidores diferentes. Ver a seção 3 do documento de licenciamento.
        request.Headers.Remove("X-Congrega-Instance");
        request.Headers.Add("X-Congrega-Instance", InstanceIdentity.Atual);

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Identidade desta instalação.
/// </summary>
/// <remarks>
/// <para>
/// Serve para <b>medir</b>, não para autorizar: o servidor central usa o
/// identificador para notar que uma licença de igreja única está sendo usada por
/// nove instalações ao mesmo tempo. Um cliente mal-intencionado pode forjá-lo, e
/// isso é esperado — o valor está em detectar o compartilhamento casual, que é o
/// caso comum, não o adversário dedicado.
/// </para>
/// <para>
/// <b>Precisa ser estável entre reinícios</b>, e a implementação abaixo não é —
/// ela vive em memória. O lugar certo é uma linha na tabela de configuração do
/// banco local do cliente, gravada no primeiro boot. Registrado como pendência
/// na seção 3 do documento; enquanto isso, cada reinício parece uma instalação
/// nova para a nossa medição.
/// </para>
/// </remarks>
internal static class InstanceIdentity
{
    public static string Atual { get; } = Guid.NewGuid().ToString("N");
}
