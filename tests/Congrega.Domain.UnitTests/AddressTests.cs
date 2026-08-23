using Congrega.Domain.Addressing;

namespace Congrega.Domain.UnitTests;

public sealed class PostalCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// É o que faz "01001-000" e "01001000" encontrarem a mesma linha do cache.
    /// Sem normalizar na escrita, o mesmo lugar entraria duas vezes e a busca
    /// acertaria metade das vezes.
    /// </summary>
    [Theory]
    [InlineData("01001000")]
    [InlineData("01001-000")]
    [InlineData(" 01001 000 ")]
    [InlineData("01.001-000")]
    public void Normalize_reduz_a_oito_digitos(string entrada)
    {
        Assert.Equal("01001000", PostalCode.Normalize(entrada));
    }

    /// <summary>
    /// CEP incompleto é o estado normal de quem está digitando, não erro a
    /// reportar — por isso devolve nulo em vez de lançar.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0100100")]
    [InlineData("010010000")]
    [InlineData("abcdefgh")]
    public void Normalize_devolve_nulo_para_o_que_nao_e_cep(string? entrada)
    {
        Assert.Null(PostalCode.Normalize(entrada));
    }

    [Fact]
    public void Format_insere_o_hifen()
    {
        Assert.Equal("01001-000", PostalCode.Format("01001000"));
    }

    /// <summary>
    /// A ViaCEP devolve string vazia — não nulo — para CEP de cidade inteira,
    /// que não tem logradouro nem bairro próprios. Guardar "" é fiel: o campo
    /// foi consultado e a resposta é "não há".
    /// </summary>
    [Fact]
    public void FromLookup_aceita_logradouro_e_bairro_vazios()
    {
        var postal = PostalCode.FromLookup("01001000", "", "", "São Paulo", "São Paulo", Now);

        Assert.Equal(string.Empty, postal.Logradouro);
        Assert.Equal("São Paulo", postal.Localidade);
    }

    [Fact]
    public void FromLookup_recusa_cep_invalido()
    {
        Assert.Throws<ArgumentException>(() =>
            PostalCode.FromLookup("123", "Rua", "Centro", "Cidade", "Estado", Now));
    }
}

public sealed class AddressTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Depois = Now.AddHours(1);

    private static Address Casa() =>
        Address.Register(1, "01001-000", "Praça da Sé", "Sé", "São Paulo", "São Paulo",
            ResidenceType.Casa, "123", andar: null, Now);

    [Fact]
    public void Register_normaliza_o_cep()
    {
        Assert.Equal("01001000", Casa().Cep);
    }

    /// <summary>
    /// O preenchimento manual é caminho legítimo do requisito: recusar o
    /// cadastro inteiro por um CEP que a pessoa não sabe impediria de registrar
    /// onde ela mora.
    /// </summary>
    [Fact]
    public void Register_com_cep_invalido_guarda_nulo_em_vez_de_lancar()
    {
        var endereco = Address.Register(1, "abc", "Rua sem CEP", null, "Interior", null,
            ResidenceType.Casa, "10", null, Now);

        Assert.Null(endereco.Cep);
        Assert.Equal("Rua sem CEP", endereco.Logradouro);
    }

    [Fact]
    public void Register_recusa_tenant_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Address.Register(0, null, null, null, null, null, ResidenceType.Casa, null, null, Now));
    }

    // -------------------------------------------------------------------
    // A regra do andar
    // -------------------------------------------------------------------

    /// <summary>
    /// Casa com "3º andar" gravado sairia errado na etiqueta de correspondência.
    /// O valor é DESCARTADO, não recusado: quem marcou apartamento, digitou o
    /// andar e depois corrigiu para casa mandaria os dois campos, e lançar ali
    /// transformaria uma correção comum em erro de validação.
    /// </summary>
    [Fact]
    public void Casa_descarta_o_andar_em_vez_de_recusar()
    {
        var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null,
            ResidenceType.Casa, "10", andar: "3", Now);

        Assert.Null(endereco.Andar);
        Assert.Equal("10", endereco.Numero);
    }

    [Fact]
    public void Apartamento_guarda_o_andar()
    {
        var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null,
            ResidenceType.Apartamento, "32", andar: "3", Now);

        Assert.Equal("3", endereco.Andar);
        Assert.Equal("32", endereco.Numero);
    }

    /// <summary>
    /// <b>Opcional mesmo em apartamento.</b> Exigi-lo obrigaria quem digita uma
    /// lista de papel que só traz "Apto 32" a inventar um andar para salvar — e
    /// inventar dado é pior do que deixar em branco.
    /// </summary>
    [Fact]
    public void Apartamento_aceita_ficar_sem_andar()
    {
        var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null,
            ResidenceType.Apartamento, "32", andar: null, Now);

        Assert.Null(endereco.Andar);
    }

    /// <summary>
    /// Trocar de apartamento para casa precisa apagar o andar que já estava
    /// gravado — senão a linha ficaria fora do CHECK do banco na próxima escrita.
    /// </summary>
    [Fact]
    public void Update_para_casa_limpa_o_andar_ja_gravado()
    {
        var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null,
            ResidenceType.Apartamento, "32", "3", Now);

        endereco.Update(null, "Rua A", null, "Cidade", null, ResidenceType.Casa, "32", null, Depois);

        Assert.Null(endereco.Andar);
        Assert.Equal(ResidenceType.Casa, endereco.ResidenceType);
    }

    // -------------------------------------------------------------------
    // Vazio
    // -------------------------------------------------------------------

    /// <summary>
    /// Serve para a borda não gravar linha em branco quando o formulário volta
    /// sem endereço nenhum.
    /// </summary>
    [Fact]
    public void IsEmpty_reconhece_endereco_sem_nada()
    {
        var vazio = Address.Register(1, null, null, null, null, null, ResidenceType.Casa, null, null, Now);

        Assert.True(vazio.IsEmpty);
    }

    /// <summary>
    /// O tipo de residência NÃO conta como conteúdo: ele tem valor padrão e
    /// estaria preenchido mesmo num formulário em que ninguém tocou.
    /// </summary>
    [Fact]
    public void IsEmpty_ignora_o_tipo_de_residencia()
    {
        var soTipo = Address.Register(1, null, null, null, null, null,
            ResidenceType.Apartamento, null, null, Now);

        Assert.True(soTipo.IsEmpty);
    }

    [Theory]
    [InlineData("01001000", null, null, null)]
    [InlineData(null, "Rua A", null, null)]
    [InlineData(null, null, "Cidade", null)]
    [InlineData(null, null, null, "123")]
    public void IsEmpty_é_falso_com_qualquer_campo_util(
        string? cep, string? logradouro, string? localidade, string? numero)
    {
        var endereco = Address.Register(1, cep, logradouro, null, localidade, null,
            ResidenceType.Casa, numero, null, Now);

        Assert.False(endereco.IsEmpty);
    }

    [Fact]
    public void Register_apara_espacos_e_transforma_vazio_em_nulo()
    {
        var endereco = Address.Register(1, null, "  Rua A  ", "   ", "Cidade", null,
            ResidenceType.Casa, "  10 ", null, Now);

        Assert.Equal("Rua A", endereco.Logradouro);
        Assert.Null(endereco.Bairro);
        Assert.Equal("10", endereco.Numero);
    }

    [Fact]
    public void Register_recusa_numero_longo_demais()
    {
        var longo = new string('9', Address.MaxNumeroLength + 1);

        Assert.Throws<ArgumentException>(() =>
            Address.Register(1, null, "Rua A", null, "Cidade", null, ResidenceType.Casa, longo, null, Now));
    }

    /// <summary>
    /// A regra do andar era NEGATIVA — "casa não tem andar" — o que cobria tudo
    /// enquanto existiam dois valores. Com sítio e "outro" no conjunto, aquela
    /// expressão passaria a PERMITIR andar nos dois valores novos, e um sítio com
    /// "3º andar" entraria sem ninguém ver.
    ///
    /// Invertida para positiva, ela vale para qualquer valor que venha depois —
    /// e este teste falha se alguém acrescentar um quinto tipo e esquecer disso.
    /// </summary>
    [Theory]
    [InlineData(ResidenceType.Casa)]
    [InlineData(ResidenceType.Sitio)]
    [InlineData(ResidenceType.Outro)]
    public void So_apartamento_guarda_andar(ResidenceType tipo)
    {
        var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null, tipo, "10", andar: "3", Now);

        Assert.Null(endereco.Andar);
    }

    [Fact]
    public void Todo_tipo_declarado_e_aceito()
    {
        foreach (var tipo in Enum.GetValues<ResidenceType>())
        {
            var endereco = Address.Register(1, null, "Rua A", null, "Cidade", null, tipo, "10", null, Now);
            Assert.Equal(tipo, endereco.ResidenceType);
        }
    }

    [Fact]
    public void Register_recusa_tipo_fora_do_enum()
    {
        Assert.Throws<ArgumentException>(() =>
            Address.Register(1, null, "Rua A", null, "Cidade", null, (ResidenceType)99, "10", null, Now));
    }
}
