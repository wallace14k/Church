using System.Globalization;
using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Que papel comprova a despesa.</summary>
public enum FiscalDocumentType : short
{
    /// <summary>Nota fiscal eletrônica de mercadoria.</summary>
    NotaFiscal = 1,

    /// <summary>Nota fiscal de serviço, emitida pelo município.</summary>
    NotaDeServico = 2,

    /// <summary>Cupom fiscal — CF-e/SAT ou NFC-e.</summary>
    CupomFiscal = 3,

    /// <summary>Recibo. O único que pode não ter número.</summary>
    Recibo = 4,
}

/// <summary>
/// O documento fiscal que comprova uma despesa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só existe para saída</b>, e a regra não mora aqui: mora na FK composta
/// <c>(entry_id, entry_kind) → giving_entries (id, kind)</c> com
/// <c>CHECK (entry_kind = 2)</c>. Nota fiscal de entrada não existe — a igreja
/// não emite nota ao receber dízimo — e uma regra dessas escrita só em C# valeria
/// apenas para quem passasse pela aplicação.
/// </para>
/// <para>
/// O anexo mora em <see cref="FiscalDocumentFile"/>, em tabela separada. Os
/// bytes chegam a 10 MB, e mantê-los fora desta linha é o que permite listar
/// documentos sem arrastar megabytes por uma coluna que ninguém pediu.
/// </para>
/// </remarks>
public sealed class FiscalDocument : AggregateRoot
{
    public const int MaxNumberLength = 60;
    public const int MaxSeriesLength = 20;

    /// <summary>A chave da NF-e tem 44 dígitos. Não 43, não 45.</summary>
    public const int AccessKeyLength = 44;

    private FiscalDocument() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }

    public long EntryId { get; private set; }

    /// <summary>
    /// Sempre <see cref="GivingKind.Saida"/>.
    /// </summary>
    /// <remarks>
    /// Redundante com o lançamento de propósito: é a coluna que a FK composta
    /// compara. Sem ela, o banco não teria como exprimir "este documento
    /// pertence a um lançamento que é uma saída" de forma declarativa.
    /// </remarks>
    public GivingKind EntryKind { get; private set; }

    public FiscalDocumentType DocumentType { get; private set; }

    /// <summary>Número do documento. Nulo só é aceito em <see cref="FiscalDocumentType.Recibo"/>.</summary>
    public string? Number { get; private set; }

    public string? Series { get; private set; }

    /// <summary>
    /// CPF ou CNPJ do emissor, <b>só dígitos</b>.
    /// </summary>
    /// <remarks>
    /// A pontuação é da tela. Guardá-la faria "12.345.678/0001-90" e
    /// "12345678000190" serem dois emissores diferentes no mesmo relatório.
    /// </remarks>
    public string? IssuerTaxId { get; private set; }

    /// <summary>Chave de acesso da NF-e/NFC-e: 44 dígitos.</summary>
    public string? AccessKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static FiscalDocument Register(
        long tenantId,
        long entryId,
        GivingKind entryKind,
        FiscalDocumentType documentType,
        DateTimeOffset now,
        string? number = null,
        string? series = null,
        string? issuerTaxId = null,
        string? accessKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryId);

        if (entryKind != GivingKind.Saida)
        {
            throw new ArgumentException(
                "Documento fiscal só existe para saída. A igreja não emite nota ao receber uma oferta.",
                nameof(entryKind));
        }

        if (!Enum.IsDefined(documentType))
        {
            throw new ArgumentException("Tipo de documento inválido.", nameof(documentType));
        }

        var numeroLimpo = Aparar(number, MaxNumberLength, nameof(number));

        // Nota, nota de serviço e cupom sempre têm número — é o que os
        // identifica perante o fisco. Recibo é o único que pode não ter:
        // recibo escrito à mão é prova legítima e frequentemente não numerada.
        if (numeroLimpo is null && documentType != FiscalDocumentType.Recibo)
        {
            throw new ArgumentException(
                "Informe o número do documento. Só recibo pode não ter número.",
                nameof(number));
        }

        return new FiscalDocument
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            EntryId = entryId,
            EntryKind = entryKind,
            DocumentType = documentType,
            Number = numeroLimpo,
            Series = Aparar(series, MaxSeriesLength, nameof(series)),
            IssuerTaxId = NormalizarCpfCnpj(issuerTaxId),
            AccessKey = NormalizarChave(accessKey),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Tira a pontuação e confere os dígitos verificadores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Conferir o dígito não é preciosismo.</b> O CNPJ é digitado à mão a
    /// partir de um papel, e um dígito trocado produz um emissor que não existe
    /// — descoberto meses depois, na conferência anual, quando ninguém mais
    /// lembra de qual fornecedor era aquela despesa. O dígito verificador existe
    /// exatamente para pegar esse erro no momento em que ele acontece.
    /// </para>
    /// <para>
    /// O CHECK da coluna garante o <i>formato</i> (11 ou 14 dígitos); só o
    /// domínio consegue garantir que o número é <i>válido</i>.
    /// </para>
    /// </remarks>
    private static string? NormalizarCpfCnpj(string? valor)
    {
        var digitos = SomenteDigitos(valor);

        if (digitos is null)
        {
            return null;
        }

        var valido = digitos.Length switch
        {
            11 => CpfValido(digitos),
            14 => CnpjValido(digitos),
            _ => false,
        };

        if (!valido)
        {
            throw new ArgumentException(
                "CPF ou CNPJ do emissor inválido. Confira os números digitados.",
                nameof(valor));
        }

        return digitos;
    }

    private static string? NormalizarChave(string? valor)
    {
        var digitos = SomenteDigitos(valor);

        if (digitos is null)
        {
            return null;
        }

        if (digitos.Length != AccessKeyLength)
        {
            throw new ArgumentException(
                $"A chave de acesso tem {AccessKeyLength} dígitos. Foram informados {digitos.Length}.",
                nameof(valor));
        }

        return digitos;
    }

    private static string? SomenteDigitos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var digitos = new string([.. valor.Where(char.IsAsciiDigit)]);

        return digitos.Length == 0 ? null : digitos;
    }

    /// <summary>
    /// Dígitos verificadores do CPF.
    /// </summary>
    /// <remarks>
    /// A rejeição de todos-iguais ("111.111.111-11") é explícita porque esses
    /// números <b>passam</b> no cálculo do dígito — são o buraco clássico de
    /// quem implementa só a fórmula.
    /// </remarks>
    private static bool CpfValido(string d)
    {
        if (d.All(c => c == d[0]))
        {
            return false;
        }

        return DigitoModulo11(d, 10) == d[9] && DigitoModulo11(d, 11) == d[10];
    }

    private static bool CnpjValido(string d)
    {
        if (d.All(c => c == d[0]))
        {
            return false;
        }

        // Os pesos do CNPJ não são uma contagem regressiva simples: reiniciam em
        // 9 depois de chegar a 2. Escrevê-los explicitamente é mais claro do que
        // a aritmética modular que os reproduz.
        int[] pesosPrimeiro = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] pesosSegundo = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        return DigitoComPesos(d, pesosPrimeiro) == d[12]
            && DigitoComPesos(d, pesosSegundo) == d[13];
    }

    private static char DigitoModulo11(string digitos, int pesoInicial)
    {
        var soma = 0;

        for (var i = 0; i < pesoInicial - 1; i++)
        {
            soma += (digitos[i] - '0') * (pesoInicial - i);
        }

        return DoResto(soma);
    }

    private static char DigitoComPesos(string digitos, int[] pesos)
    {
        var soma = 0;

        for (var i = 0; i < pesos.Length; i++)
        {
            soma += (digitos[i] - '0') * pesos[i];
        }

        return DoResto(soma);
    }

    private static char DoResto(int soma)
    {
        var resto = soma % 11;
        var digito = resto < 2 ? 0 : 11 - resto;

        // `InvariantCulture` porque a conversão de número para texto já causou
        // bug real neste projeto — ver a tabela de convenções do CLAUDE.md.
        return digito.ToString(CultureInfo.InvariantCulture)[0];
    }

    private static string? Aparar(string? valor, int limite, string nomeDoParametro)
    {
        var limpo = valor?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return null;
        }

        if (limpo.Length > limite)
        {
            throw new ArgumentException($"Máximo de {limite} caracteres.", nomeDoParametro);
        }

        return limpo;
    }
}
