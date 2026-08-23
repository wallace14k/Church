using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>
/// O arquivo anexado a um documento fiscal — a foto da nota, o PDF do cupom.
/// </summary>
/// <remarks>
/// <para>
/// <b>Guarda bytes, não Base64.</b> O contrato da API recebe e devolve Base64,
/// porque JSON não carrega binário — mas persistir o texto Base64 custaria 33% a
/// mais de disco em cada comprovante e ainda exigiria decodificar a cada
/// leitura. A tradução acontece na borda; aqui só existem os bytes.
/// </para>
/// <para>
/// <b>Entidade separada de <see cref="FiscalDocument"/></b> porque um anexo de
/// 10 MB numa linha que a listagem lê é uma listagem lenta que ninguém consegue
/// explicar: a tela continua certa, só demora, e a causa está numa coluna que
/// ninguém pediu para ver.
/// </para>
/// </remarks>
public sealed class FiscalDocumentFile : AggregateRoot
{
    /// <summary>10 MB. É o teto que a tela anuncia, e o mesmo que o CHECK aplica.</summary>
    public const int MaxSizeBytes = 10 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    /// <summary>
    /// O que o navegador sabe exibir sem baixar.
    /// </summary>
    /// <remarks>
    /// Lista fechada, e não "qualquer coisa": um campo de upload que aceita
    /// qualquer tipo é um canal para subir executável, e o comprovante que
    /// ninguém consegue abrir também não comprova nada.
    /// </remarks>
    public static readonly IReadOnlySet<string> TiposAceitos =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "application/pdf",
            "image/png",
            "image/jpeg",
        };

    private FiscalDocumentFile()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Content = [];
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public long DocumentId { get; private set; }

    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public int SizeBytes { get; private set; }
    public byte[] Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static FiscalDocumentFile Register(
        long tenantId,
        long documentId,
        string fileName,
        string contentType,
        byte[] content,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentId);
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0)
        {
            throw new ArgumentException("O arquivo está vazio.", nameof(content));
        }

        if (content.Length > MaxSizeBytes)
        {
            var mb = content.Length / (1024.0 * 1024.0);

            throw new ArgumentException(
                $"O arquivo tem {mb:F1} MB e o limite é 10 MB.",
                nameof(content));
        }

        if (!TiposAceitos.Contains(contentType))
        {
            throw new ArgumentException(
                "Anexe um PDF, PNG ou JPG.",
                nameof(contentType));
        }

        return new FiscalDocumentFile
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentId = documentId,
            FileName = NormalizarNome(fileName),
            ContentType = contentType,

            // Gravado a partir do que CHEGOU, nunca do que o cliente declarou.
            // O CHECK `octet_length(content) = size_bytes` recusaria a
            // divergência de qualquer jeito — derivar aqui é o que garante que
            // ela nunca chega ao banco para ser recusada.
            SizeBytes = content.Length,
            Content = content,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Reduz o nome ao arquivo, sem caminho.
    /// </summary>
    /// <remarks>
    /// O navegador manda só o nome, mas um cliente qualquer pode mandar
    /// <c>../../etc/passwd</c>. O nome nunca vira caminho no servidor — o
    /// arquivo mora no banco — mas ele <b>é devolvido no download</b>, e um
    /// nome com barra ou <c>..</c> pode confundir o cliente que o salvar.
    /// </remarks>
    private static string NormalizarNome(string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);

        var semCaminho = valor.Trim().Split(['/', '\\']).Last().Trim();

        if (semCaminho.Length == 0 || semCaminho.All(c => c == '.'))
        {
            throw new ArgumentException("Nome de arquivo inválido.", nameof(valor));
        }

        return semCaminho.Length > MaxFileNameLength
            ? semCaminho[^MaxFileNameLength..]
            : semCaminho;
    }
}
