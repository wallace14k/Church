using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Common;
using Congrega.Domain.Connectors;
using Congrega.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

/// <summary>
/// Um conector configurado.
/// </summary>
/// <remarks>
/// <b>Não existe campo de segredo neste contrato, e a ausência é a decisão.</b>
/// A senha nunca volta — nem cifrada, nem mascarada, nem "só os últimos quatro
/// caracteres". Um token de bot tem entropia suficiente para que qualquer
/// pedaço ajude quem o esteja adivinhando, e a tela não precisa dele: precisa
/// saber apenas <i>se existe</i>, que é o que <see cref="HasSecret"/> diz.
/// </remarks>
public sealed record ConnectorResponse
{
    public required Guid Id { get; init; }

    /// <summary><c>Smtp</c>, <c>Telegram</c> ou <c>GoogleDrive</c>.</summary>
    public required string Kind { get; init; }

    public required bool IsEnabled { get; init; }

    /// <summary>Configuração que não é segredo. Volta inteira.</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>Há um segredo guardado. O segredo em si nunca sai daqui.</summary>
    public required bool HasSecret { get; init; }

    public DateTimeOffset? LastTestedAt { get; init; }

    /// <summary>
    /// <c>null</c> = nunca testado, e isso é diferente de testado e falho.
    /// </summary>
    public bool? LastTestSucceeded { get; init; }

    public string? LastTestMessage { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record SaveConnectorRequest
{
    /// <summary>Configuração não-secreta. As chaves variam por tipo.</summary>
    [Required]
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>
    /// A credencial. <b>Ausente mantém a que já está guardada.</b>
    /// </summary>
    /// <remarks>
    /// A tela nunca recebe o segredo de volta, logo não tem como reenviá-lo. Se
    /// ausência apagasse, corrigir a porta de 465 para 587 apagaria a senha
    /// junto — e o envio pararia por causa de um campo que ninguém tocou.
    /// </remarks>
    [MaxLength(8000)]
    public string? Secret { get; init; }

    public bool IsEnabled { get; init; } = true;
}

/// <summary>O que a tentativa de conexão descobriu.</summary>
public sealed record ConnectorTestResponse
{
    public required bool Succeeded { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset TestedAt { get; init; }
}

public static class ConnectorEndpoints
{
    public static void MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/connectors").WithTags("Configurações");

        // **Ler também exige `connectors.manage`, e não uma permissão de
        // leitura.** A configuração revela para onde a igreja manda dados, com
        // qual conta e para qual pasta — é mapa de superfície de ataque, não
        // informação operacional. Não há caso de uso para "ver sem poder mexer".
        group.MapGet("/", ListAsync)
            .RequireAuthorization(Policies.ConnectorsManage)
            .WithSummary("Lista as integrações configuradas");

        group.MapPut("/{kind}", SaveAsync)
            .RequireAuthorization(Policies.ConnectorsManage)
            .WithSummary("Cria ou atualiza uma integração");

        group.MapDelete("/{kind}", DeleteAsync)
            .RequireAuthorization(Policies.ConnectorsManage)
            .WithSummary("Remove uma integração e a credencial dela");

        group.MapPost("/{kind}/test", TestAsync)
            .RequireAuthorization(Policies.ConnectorsManage)
            .WithSummary("Tenta falar com o serviço externo de verdade");
    }

    private static async Task<IResult> ListAsync(
        ITenantConnectorRepository connectors,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await connectors.ListAsync(cancellationToken);

        return TypedResults.Ok(itens.Select(ToResponse).ToList());
    }

    private static async Task<IResult> SaveAsync(
        string kind,
        [FromBody] SaveConnectorRequest request,
        ITenantConnectorRepository connectors,
        IConnectorSecretProtector protector,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        if (!TryLerTipo(kind, out var tipo))
        {
            return TipoInvalido();
        }

        var existente = await connectors.FindByKindAsync(tipo, cancellationToken);

        byte[]? cifrado = null;
        if (!string.IsNullOrWhiteSpace(request.Secret))
        {
            cifrado = protector.Protect(request.Secret);
        }

        try
        {
            if (existente is null)
            {
                connectors.Add(TenantConnector.Register(
                    tenantId, tipo, request.Settings, cifrado, request.IsEnabled,
                    timeProvider.GetUtcNow()));
            }
            else
            {
                existente.Update(
                    request.Settings, cifrado, request.IsEnabled, timeProvider.GetUtcNow());
            }
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Configuração inválida",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Duas abas salvando o mesmo tipo ao mesmo tempo: as duas leram
            // "não existe" e as duas tentaram criar. A constraint recusa a
            // segunda em vez de a igreja ficar com dois SMTP e nenhum critério
            // para escolher entre eles.
            return TypedResults.Problem(
                title: "Integração já configurada",
                detail: "Esta integração foi criada em outra aba. Recarregue a página.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var salvo = await connectors.FindByKindAsync(tipo, cancellationToken);

        return TypedResults.Ok(ToResponse(salvo!));
    }

    private static async Task<IResult> DeleteAsync(
        string kind,
        ITenantConnectorRepository connectors,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (!TryLerTipo(kind, out var tipo))
        {
            return TipoInvalido();
        }

        var conector = await connectors.FindByKindAsync(tipo, cancellationToken);

        // 204 mesmo quando não existe: o resultado que quem chamou queria — não
        // haver conector deste tipo — já é o estado atual. Devolver 404 faria a
        // tela mostrar erro depois de uma exclusão que deu certo em outra aba.
        if (conector is not null)
        {
            connectors.Remove(conector);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Testa a conexão de verdade e grava o resultado no conector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O resultado é persistido</b>, e não só devolvido. Uma tela que só
    /// mostrasse o verde do momento perderia a informação assim que alguém
    /// mudasse de página — e a pergunta que importa dias depois é justamente
    /// "isto chegou a funcionar alguma vez?".
    /// </para>
    /// <para>
    /// <b>Devolve 200 mesmo quando o teste reprova.</b> A requisição foi bem
    /// sucedida: ela perguntou e obteve resposta. Um 4xx aqui faria o cliente
    /// tratar "a senha está errada" como "a chamada falhou", e o diagnóstico —
    /// que é o produto deste endpoint — seria descartado pelo caminho de erro.
    /// </para>
    /// </remarks>
    private static async Task<IResult> TestAsync(
        string kind,
        ITenantConnectorRepository connectors,
        IEnumerable<IConnectorTester> testers,
        IConnectorSecretProtector protector,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (!TryLerTipo(kind, out var tipo))
        {
            return TipoInvalido();
        }

        var conector = await connectors.FindByKindAsync(tipo, cancellationToken);

        if (conector?.SecretCiphertext is null)
        {
            return TypedResults.Problem(
                title: "Integração não configurada",
                detail: "Configure e salve a integração antes de testá-la.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var tester = testers.FirstOrDefault(t => t.Kind == tipo);

        if (tester is null)
        {
            return TypedResults.Problem(
                title: "Sem teste disponível",
                detail: "Esta integração ainda não sabe testar a própria conexão.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var segredo = protector.Reveal(conector.SecretCiphertext);
        var resultado = await tester.TestAsync(conector.Settings, segredo, cancellationToken);

        var agora = timeProvider.GetUtcNow();
        conector.RegistrarTeste(resultado.Succeeded, resultado.Message, agora);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ConnectorTestResponse
        {
            Succeeded = resultado.Succeeded,
            Message = resultado.Message,
            TestedAt = agora,
        });
    }

    private static bool TryLerTipo(string valor, out ConnectorKind kind) =>
        Enum.TryParse(valor, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    private static ConnectorResponse ToResponse(TenantConnector c) => new()
    {
        Id = c.PublicId,
        Kind = c.Kind.ToString(),
        IsEnabled = c.IsEnabled,
        Settings = c.Settings,
        HasSecret = c.SecretCiphertext is { Length: > 0 },
        LastTestedAt = c.LastTestedAt,
        LastTestSucceeded = c.LastTestSucceeded,
        LastTestMessage = c.LastTestMessage,
        UpdatedAt = c.UpdatedAt,
    };

    private static ProblemHttpResult TipoInvalido() =>
        TypedResults.Problem(
            title: "Integração desconhecida",
            detail: "Use Smtp, Telegram ou GoogleDrive.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Igreja não informada",
            detail: "Selecione a igreja antes de configurar as integrações.",
            statusCode: StatusCodes.Status400BadRequest);
}
