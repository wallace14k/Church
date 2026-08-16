using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Middleware;

/// <summary>
/// Traduz erro de leitura do corpo da requisição em <c>400</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sem isto, corpo malformado — JSON quebrado, encoding errado, campo com tipo
/// incompatível — sobe como exceção não tratada e vira <c>500</c>. O efeito é
/// dizer ao cliente "o servidor falhou" quando quem enviou dado inválido foi ele,
/// e o time perde tempo investigando um incidente que não existe.
/// </para>
/// <para>
/// O caso que motivou este handler: um cliente enviando <c>"João"</c> em Latin-1
/// com <c>Content-Type: application/json</c>. O <c>System.Text.Json</c> exige
/// UTF-8 e lança <c>DecoderFallbackException</c>; o retorno era 500.
/// </para>
/// <para>
/// A resposta **não** repete o conteúdo recebido. Ecoar entrada malformada em
/// mensagem de erro é vetor de log injection e, dependendo de onde a mensagem é
/// exibida, de XSS refletido.
/// </para>
/// </remarks>
internal sealed class BadRequestExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            // Deixa o pipeline padrão tratar. Devolver false aqui é o que mantém
            // erro genuinamente inesperado como 500, que é o correto para ele.
            return false;
        }

        logger.LogInformation(
            "Requisição rejeitada em {Path}: corpo inválido ({Reason}).",
            httpContext.Request.Path, badRequest.Message);

        httpContext.Response.StatusCode = badRequest.StatusCode is >= 400 and < 500
            ? badRequest.StatusCode
            : StatusCodes.Status400BadRequest;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Title = "Requisição inválida",
                Detail = "O corpo da requisição não pôde ser lido. "
                       + "Verifique se o JSON está bem formado e codificado em UTF-8.",
                Status = httpContext.Response.StatusCode,
            },
        });
    }
}
