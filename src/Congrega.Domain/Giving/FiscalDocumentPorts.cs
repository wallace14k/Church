namespace Congrega.Domain.Giving;

/// <summary>
/// O anexo, sem os bytes.
/// </summary>
/// <remarks>
/// Existe para a tela de detalhe poder dizer "nota-fiscal.pdf · 240 KB" sem
/// transferir o arquivo. Quem quer ver o comprovante pede o arquivo em outra
/// chamada — e é a única que paga o custo dele.
/// </remarks>
public sealed record FiscalDocumentFileInfo
{
    public required Guid PublicId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required int SizeBytes { get; init; }
}

/// <summary>O documento fiscal como a tela de detalhe o lê.</summary>
public sealed record FiscalDocumentView
{
    public required Guid PublicId { get; init; }
    public required FiscalDocumentType DocumentType { get; init; }
    public string? Number { get; init; }
    public string? Series { get; init; }

    /// <summary>Só dígitos. Quem exibe formata.</summary>
    public string? IssuerTaxId { get; init; }

    public string? AccessKey { get; init; }

    /// <summary>Nulo quando o documento foi registrado sem anexo.</summary>
    public FiscalDocumentFileInfo? File { get; init; }
}

/// <summary>
/// O lançamento inteiro, para a tela de detalhe.
/// </summary>
/// <remarks>
/// Projeção separada de <see cref="GivingEntryListItem"/> de propósito: a
/// listagem desenha trinta linhas e não pode carregar documento nem autor, e o
/// detalhe mostra um lançamento e precisa dos dois. Uma projeção só serviria mal
/// aos dois casos.
/// </remarks>
public sealed record GivingEntryDetail
{
    public required Guid PublicId { get; init; }
    public string? Description { get; init; }

    public required Guid CategoryPublicId { get; init; }
    public required string CategoryName { get; init; }
    public string? CategoryColorHex { get; init; }

    public required GivingKind Kind { get; init; }
    public required long AmountCents { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required GivingMethod Method { get; init; }

    public string? MemberName { get; init; }
    public string? AccountName { get; init; }

    public required bool IsRecurring { get; init; }
    public GivingRecurrence? Recurrence { get; init; }
    public required GivingEntryStatus Status { get; init; }
    public Guid? SeriesId { get; init; }
    public string? Notes { get; init; }

    /// <summary>Quem digitou. Prestação de contas precisa da autoria.</summary>
    public string? RecordedByName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// O comprovante, quando existe.
    /// </summary>
    /// <remarks>
    /// Sempre nulo em entrada — a igreja não emite nota ao receber oferta, e o
    /// banco recusa o vínculo pela FK composta.
    /// </remarks>
    public FiscalDocumentView? Document { get; init; }
}

/// <summary>O arquivo com os bytes. Só quem vai exibi-lo pede isto.</summary>
public sealed record FiscalDocumentFileContent
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Content { get; init; }
}

public interface IFiscalDocumentRepository
{
    /// <summary>Documento de um lançamento, ou nulo.</summary>
    Task<FiscalDocument?> FindByEntryIdAsync(long entryId, CancellationToken cancellationToken);

    /// <summary>
    /// O arquivo de um documento, com os bytes.
    /// </summary>
    /// <remarks>
    /// Recebe o id público do <b>lançamento</b>, e não o do arquivo: quem chega
    /// aqui veio da tela de detalhe do lançamento, e resolver a partir dele
    /// mantém a autorização no mesmo objeto que o usuário já podia ver.
    /// </remarks>
    Task<FiscalDocumentFileContent?> FindFileByEntryPublicIdAsync(
        Guid entryPublicId,
        CancellationToken cancellationToken);

    void Add(FiscalDocument document);

    void AddFile(FiscalDocumentFile file);
}
