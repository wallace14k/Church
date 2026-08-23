using Congrega.Domain.Giving;

namespace Congrega.Domain.UnitTests;

public sealed class FiscalDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static FiscalDocument Documento(
        FiscalDocumentType tipo = FiscalDocumentType.NotaFiscal,
        string? numero = "000123",
        string? emissor = null,
        string? chave = null) =>
        FiscalDocument.Register(
            tenantId: 1,
            entryId: 7,
            entryKind: GivingKind.Saida,
            documentType: tipo,
            now: Now,
            number: numero,
            issuerTaxId: emissor,
            accessKey: chave);

    [Fact]
    public void Documento_fiscal_de_entrada_e_recusado()
    {
        // A igreja não emite nota ao receber dízimo. O banco também recusa, pela
        // FK composta — este teste protege a mesma regra do lado de cá.
        var erro = Assert.Throws<ArgumentException>(() =>
            FiscalDocument.Register(1, 7, GivingKind.Entrada, FiscalDocumentType.NotaFiscal, Now, "1"));

        Assert.Contains("só existe para saída", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nota_sem_numero_e_recusada()
    {
        Assert.Throws<ArgumentException>(() => Documento(numero: null));
    }

    [Fact]
    public void Recibo_pode_nao_ter_numero()
    {
        // Recibo escrito à mão é prova legítima e frequentemente não numerada.
        var documento = Documento(FiscalDocumentType.Recibo, numero: null);

        Assert.Null(documento.Number);
    }

    // ---------------------------------------------------------------- CPF/CNPJ

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Cnpj_valido_e_guardado_so_com_digitos(string informado)
    {
        // A pontuação é da tela. Guardá-la faria o mesmo emissor virar dois no
        // relatório, conforme quem digitou tenha usado pontos ou não.
        var documento = Documento(emissor: informado);

        Assert.Equal("11222333000181", documento.IssuerTaxId);
    }

    [Fact]
    public void Cnpj_com_digito_verificador_errado_e_recusado()
    {
        // 82 no lugar de 81: um dígito trocado ao copiar do papel. Sem esta
        // verificação, o erro só apareceria na conferência anual — quando
        // ninguém mais lembra de que fornecedor era a despesa.
        var erro = Assert.Throws<ArgumentException>(() => Documento(emissor: "11222333000182"));

        Assert.Contains("inválido", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cpf_valido_e_aceito()
    {
        var documento = Documento(emissor: "529.982.247-25");

        Assert.Equal("52998224725", documento.IssuerTaxId);
    }

    [Fact]
    public void Cpf_com_digito_errado_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => Documento(emissor: "52998224726"));
    }

    [Theory]
    [InlineData("11111111111")]
    [InlineData("00000000000000")]
    public void Numero_com_todos_os_digitos_iguais_e_recusado(string repetido)
    {
        // **Estes passam na fórmula do dígito verificador.** É o buraco clássico
        // de quem implementa só o cálculo, e por isso a rejeição é explícita.
        Assert.Throws<ArgumentException>(() => Documento(emissor: repetido));
    }

    [Fact]
    public void Emissor_em_branco_vira_nulo_sem_erro()
    {
        // O campo é opcional; string vazia vinda de um formulário não é uma
        // tentativa de informar um CNPJ inválido.
        Assert.Null(Documento(emissor: "   ").IssuerTaxId);
    }

    // ------------------------------------------------------------ chave da NFe

    [Fact]
    public void Chave_de_acesso_guarda_44_digitos_sem_pontuacao()
    {
        var chave = new string('1', 22) + new string('2', 22);

        Assert.Equal(chave, Documento(chave: $"{chave[..4]} {chave[4..]}").AccessKey);
    }

    [Fact]
    public void Chave_com_43_digitos_e_recusada()
    {
        var erro = Assert.Throws<ArgumentException>(() => Documento(chave: new string('1', 43)));

        // A mensagem diz quantos vieram: quem digitou 43 precisa saber que
        // faltou um, não que "a chave é inválida".
        Assert.Contains("43", erro.Message, StringComparison.Ordinal);
    }
}

public sealed class FiscalDocumentFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tamanho_vem_do_conteudo_e_nao_do_que_foi_declarado()
    {
        var arquivo = FiscalDocumentFile.Register(1, 2, "nota.pdf", "application/pdf", [1, 2, 3], Now);

        Assert.Equal(3, arquivo.SizeBytes);
    }

    [Fact]
    public void Executavel_disfarcado_de_pdf_e_recusado_pelo_tipo()
    {
        Assert.Throws<ArgumentException>(() =>
            FiscalDocumentFile.Register(1, 2, "nota.exe", "application/x-msdownload", [1], Now));
    }

    [Fact]
    public void Arquivo_acima_de_10_MB_e_recusado_com_o_tamanho_na_mensagem()
    {
        var grande = new byte[FiscalDocumentFile.MaxSizeBytes + 1];

        var erro = Assert.Throws<ArgumentException>(() =>
            FiscalDocumentFile.Register(1, 2, "nota.pdf", "application/pdf", grande, Now));

        Assert.Contains("10 MB", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_vazio_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            FiscalDocumentFile.Register(1, 2, "nota.pdf", "application/pdf", [], Now));
    }

    [Fact]
    public void Nome_com_caminho_e_reduzido_ao_arquivo()
    {
        // O arquivo mora no banco e o nome nunca vira caminho no servidor — mas
        // ele volta no download, e um nome com `..` confunde quem o salvar.
        var arquivo = FiscalDocumentFile.Register(
            1, 2, "../../etc/passwd", "image/png", [9], Now);

        Assert.Equal("passwd", arquivo.FileName);
    }
}
