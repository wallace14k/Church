using System.Net.Http.Json;
using System.Text.Json;
using Congrega.Domain.Connectors;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Testa o bot do Telegram <b>mandando uma mensagem no destino configurado</b>.
/// </summary>
/// <remarks>
/// <para>
/// <c>getMe</c> seria mais barato e provaria menos: ele valida o token e não diz
/// nada sobre o chat. E o erro que quase todo mundo comete não é o token — é o
/// <c>chatId</c>, ou esquecer de adicionar o bot ao grupo. Um teste que passasse
/// nesses dois casos e falhasse depois, em produção, seria pior do que nenhum.
/// </para>
/// <para>
/// A mensagem vai para o próprio grupo da igreja, que é onde os avisos reais
/// vão cair — o teste é, literalmente, o primeiro aviso.
/// </para>
/// </remarks>
internal sealed class TelegramConnectorTester(IHttpClientFactory fabrica) : IConnectorTester
{
    /// <summary>Nome do cliente HTTP. Ver o registro em <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "telegram";

    public ConnectorKind Kind => ConnectorKind.Telegram;

    public async Task<ConnectorTestResult> TestAsync(
        IReadOnlyDictionary<string, string> settings,
        string secret,
        CancellationToken cancellationToken)
    {
        if (!settings.TryGetValue("chatId", out var chatId) || string.IsNullOrWhiteSpace(chatId))
        {
            return Falha("O conector está sem o campo \"chatId\".");
        }

        var cliente = fabrica.CreateClient(HttpClientName);

        try
        {
            // O token vai no CAMINHO da URL — é assim que a API do Telegram
            // funciona, e é o motivo de o cliente HTTP registrado para ela ter o
            // log de URL desligado: sem isso, o token do bot apareceria em texto
            // claro em toda linha de log de requisição.
            using var resposta = await cliente.PostAsJsonAsync(
                $"bot{secret}/sendMessage",
                new
                {
                    chat_id = chatId,
                    text =
                        "✅ Congrega conectado.\n\n"
                        + "Este bot está configurado para enviar avisos da sua igreja neste chat.",
                },
                cancellationToken);

            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);

            if (resposta.IsSuccessStatusCode)
            {
                return new ConnectorTestResult
                {
                    Succeeded = true,
                    Message = "Mensagem entregue no chat configurado.",
                };
            }

            return Falha(Explicar((int)resposta.StatusCode, corpo));
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            return Falha($"Não foi possível falar com o Telegram: {erro.Message}");
        }
    }

    /// <summary>
    /// Traduz a resposta de erro do Telegram.
    /// </summary>
    /// <remarks>
    /// A API devolve <c>{"ok":false,"description":"Forbidden: bot was kicked..."}</c>.
    /// A descrição é útil, mas está em inglês e não diz o que fazer — os três
    /// casos abaixo cobrem quase todo erro real de configuração, e cada um traz
    /// a correção junto.
    /// </remarks>
    private static string Explicar(int status, string corpo)
    {
        var descricao = LerDescricao(corpo);

        return status switch
        {
            401 => "O token do bot foi recusado. Confira o que o @BotFather enviou — "
                + "ele inclui os números antes dos dois-pontos.",

            400 when descricao.Contains("chat not found", StringComparison.OrdinalIgnoreCase) =>
                "O chat não foi encontrado. Em grupo o id começa com \"-100\"; "
                + "para descobri-lo, mande uma mensagem no grupo e abra "
                + "api.telegram.org/bot<token>/getUpdates.",

            403 => "O bot não tem permissão nesse chat. Adicione-o ao grupo — "
                + "e, se o grupo tiver tópicos, dê a ele permissão de enviar mensagens.",

            _ => descricao.Length > 0
                ? $"O Telegram recusou ({status}): {descricao}"
                : $"O Telegram respondeu {status}.",
        };
    }

    private static string LerDescricao(string corpo)
    {
        try
        {
            using var json = JsonDocument.Parse(corpo);

            return json.RootElement.TryGetProperty("description", out var d)
                ? d.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            // Resposta que não é JSON — proxy corporativo, página de erro. O
            // corpo cru não ajuda quem lê e pode ser enorme.
            return string.Empty;
        }
    }

    private static ConnectorTestResult Falha(string mensagem) =>
        new() { Succeeded = false, Message = mensagem };
}
