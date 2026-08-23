using Congrega.Domain.Connectors;

namespace Congrega.Domain.UnitTests;

public sealed class TenantConnectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Segredo = [1, 2, 3, 4];

    private static Dictionary<string, string> Smtp(params (string Chave, string Valor)[] trocas)
    {
        var s = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = "smtp.gmail.com",
            ["port"] = "587",
            ["security"] = "StartTls",
            ["username"] = "igreja@gmail.com",
            ["fromAddress"] = "igreja@gmail.com",
            ["fromName"] = "Igreja Betel",
        };

        foreach (var (chave, valor) in trocas)
        {
            s[chave] = valor;
        }

        return s;
    }

    private static TenantConnector Registrar(
        ConnectorKind kind = ConnectorKind.Smtp,
        Dictionary<string, string>? settings = null,
        byte[]? segredo = null) =>
        TenantConnector.Register(1, kind, settings ?? Smtp(), segredo ?? Segredo, true, Now);

    // --------------------------------------------------------------- criação

    [Fact]
    public void Conector_novo_sem_segredo_e_recusado()
    {
        // Sem segredo, a configuração nunca vai funcionar — e falhar agora é bem
        // melhor do que falhar no primeiro envio de verdade.
        var erro = Assert.Throws<ArgumentException>(() => Registrar(segredo: []));

        Assert.Contains("senha da conta de e-mail", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mensagem_de_segredo_faltando_nomeia_o_campo_do_tipo()
    {
        // "O segredo é obrigatório" não diz o que digitar. Cada tipo tem nome
        // próprio para o seu.
        var erro = Assert.Throws<ArgumentException>(() => TenantConnector.Register(
            1,
            ConnectorKind.Telegram,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["chatId"] = "-100123" },
            null,
            true,
            Now));

        Assert.Contains("token do bot", erro.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("fromAddress")]
    public void Campo_obrigatorio_ausente_e_nomeado_no_erro(string ausente)
    {
        var settings = Smtp();
        settings.Remove(ausente);

        var erro = Assert.Throws<ArgumentException>(() => Registrar(settings: settings));

        Assert.Contains(ausente, erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Campo_obrigatorio_em_branco_conta_como_ausente()
    {
        // Espaço em branco num campo obrigatório é a mesma falha que não
        // preencher; tratá-los como casos distintos daria dois erros diferentes
        // para o mesmo descuido.
        var erro = Assert.Throws<ArgumentException>(() => Registrar(settings: Smtp(("host", "   "))));

        Assert.Contains("host", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Espacos_em_volta_do_valor_sao_removidos()
    {
        // "smtp.gmail.com " colado de um tutorial não pode virar um host que não
        // resolve.
        var conector = Registrar(settings: Smtp(("host", "  smtp.gmail.com  ")));

        Assert.Equal("smtp.gmail.com", conector.Settings["host"]);
    }

    // ----------------------------------------------------------------- SMTP

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("587a")]
    public void Porta_invalida_e_recusada(string porta)
    {
        Assert.Throws<ArgumentException>(() => Registrar(settings: Smtp(("port", porta))));
    }

    [Fact]
    public void Seguranca_desconhecida_e_recusada()
    {
        // Não existe "None": SMTP em claro entrega a senha da conta de e-mail
        // para quem estiver no caminho.
        var erro = Assert.Throws<ArgumentException>(() => Registrar(settings: Smtp(("security", "None"))));

        Assert.Contains("StartTls", erro.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("igreja.gmail.com")]
    [InlineData("@gmail.com")]
    [InlineData("igreja@")]
    [InlineData("igreja betel@gmail.com")]
    public void Remetente_que_nao_parece_email_e_recusado(string endereco)
    {
        // Um remetente errado não falha aqui: falha no envio, horas depois, num
        // log que ninguém está olhando.
        Assert.Throws<ArgumentException>(() => Registrar(settings: Smtp(("fromAddress", endereco))));
    }

    // ---------------------------------------------------------------- edição

    [Fact]
    public void Editar_sem_segredo_MANTEM_o_que_ja_estava()
    {
        // **A regra que impede o bug clássico:** a tela nunca recebe a senha de
        // volta, logo não tem como reenviá-la. Se ausência apagasse, corrigir a
        // porta apagaria a senha e o envio pararia sem ninguém tocar nela.
        var conector = Registrar();

        conector.Update(Smtp(("port", "465"), ("security", "SslOnConnect")), null, true, Now);

        Assert.Equal(Segredo, conector.SecretCiphertext);
        Assert.Equal("465", conector.Settings["port"]);
    }

    [Fact]
    public void Trocar_o_segredo_INVALIDA_o_resultado_do_teste_anterior()
    {
        // Manter o "funcionando" verde depois de trocar a senha faria a tela
        // afirmar algo sobre uma credencial que nunca foi experimentada.
        var conector = Registrar();
        conector.RegistrarTeste(true, "Conectado.", Now);

        conector.Update(Smtp(), [9, 9, 9], true, Now);

        Assert.Null(conector.LastTestSucceeded);
        Assert.Null(conector.LastTestedAt);
        Assert.Null(conector.LastTestMessage);
    }

    [Fact]
    public void Editar_so_a_configuracao_PRESERVA_o_resultado_do_teste()
    {
        // O oposto do caso acima: mudar o nome do remetente não muda se a
        // credencial funciona, e zerar o diagnóstico faria a pessoa retestar
        // sem motivo.
        var conector = Registrar();
        conector.RegistrarTeste(true, "Conectado.", Now);

        conector.Update(Smtp(("fromName", "Igreja Betel Central")), null, true, Now);

        Assert.True(conector.LastTestSucceeded);
    }

    [Fact]
    public void Desligar_preserva_a_configuracao_e_o_segredo()
    {
        // Desligar existe para parar os envios sem perder o que foi digitado —
        // inclusive a senha, que ninguém consegue redigitar de memória.
        var conector = Registrar();

        conector.Update(Smtp(), null, isEnabled: false, Now);

        Assert.False(conector.IsEnabled);
        Assert.Equal(Segredo, conector.SecretCiphertext);
        Assert.Equal("smtp.gmail.com", conector.Settings["host"]);
    }

    // ----------------------------------------------------------------- teste

    [Fact]
    public void Mensagem_de_teste_longa_e_truncada_e_nao_recusada()
    {
        // A mensagem vem do servidor externo e pode ser enorme. Recusá-la
        // perderia o diagnóstico inteiro por causa do tamanho.
        var conector = Registrar();

        conector.RegistrarTeste(false, new string('x', 5000), Now);

        Assert.Equal(TenantConnector.MaxTestMessageLength, conector.LastTestMessage!.Length);
    }

    [Fact]
    public void Nunca_testado_e_distinguivel_de_testado_e_falho()
    {
        // Três estados, não dois: "ninguém sabe" não é a mesma coisa que
        // "sabemos que está quebrado".
        var conector = Registrar();
        Assert.Null(conector.LastTestSucceeded);

        conector.RegistrarTeste(false, "Autenticação recusada.", Now);
        Assert.False(conector.LastTestSucceeded);
    }

    // ---------------------------------------------------------- outros tipos

    [Fact]
    public void Telegram_exige_o_chat()
    {
        var erro = Assert.Throws<ArgumentException>(() => TenantConnector.Register(
            1, ConnectorKind.Telegram,
            new Dictionary<string, string>(StringComparer.Ordinal), Segredo, true, Now));

        Assert.Contains("chatId", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Google_drive_exige_pasta_e_conta_de_servico()
    {
        var erro = Assert.Throws<ArgumentException>(() => TenantConnector.Register(
            1, ConnectorKind.GoogleDrive,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["folderId"] = "abc" },
            Segredo, true, Now));

        Assert.Contains("clientEmail", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void As_regras_do_smtp_nao_valem_para_os_outros_tipos()
    {
        // A validação de porta e remetente é do SMTP. Aplicá-la a todos faria o
        // Telegram exigir um campo que ele não tem.
        var telegram = TenantConnector.Register(
            1, ConnectorKind.Telegram,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["chatId"] = "-1001234567890" },
            Segredo, true, Now);

        Assert.Equal("-1001234567890", telegram.Settings["chatId"]);
    }
}
