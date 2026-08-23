using Congrega.Domain.Common;

namespace Congrega.Domain.Addressing;

/// <summary>
/// Tipo de moradia. É o que decide se o andar faz sentido.
/// </summary>
/// <remarks>
/// Nasceu com dois valores porque o requisito falava de "casa ou apto". Sítio e
/// "outro" entraram depois: uma igreja no interior cadastra sítio o tempo todo, e
/// sem o valor essas pessoas caíam em <c>Casa</c> — que não é falso o bastante
/// para alguém notar, nem verdadeiro o bastante para servir a nada.
/// </remarks>
public enum ResidenceType : short
{
    Casa = 1,
    Apartamento = 2,
    Sitio = 3,
    Outro = 4,
}

/// <summary>
/// Um endereço de membro ou de evento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entidade, e não mais objeto de valor.</b> O <c>db/002_members.sql</c>
/// guardava endereço em colunas inline, com um motivo bom na época: uma pessoa
/// tem um endereço, e normalizar custaria um JOIN. O que invalidou a decisão
/// foi o endereço passar a ser preciso no <b>evento</b> também — manter inline
/// significaria repetir seis colunas mais as regras de tipo de residência em
/// duas tabelas, sem nada garantindo que as duas cópias continuassem iguais.
/// </para>
/// <para>
/// <b>Os campos postais são cópia, não referência.</b> Um endereço é registro
/// histórico: se a ViaCEP corrigir o nome de uma rua, o cadastro que alguém
/// conferiu não deve mudar sozinho por baixo. E o preenchimento manual precisa
/// destes campos de qualquer forma.
/// </para>
/// </remarks>
public sealed class Address : AggregateRoot
{
    public const int MaxNumeroLength = 20;
    public const int MaxAndarLength = 10;

    private Address()
    {
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }

    /// <summary>Oito dígitos sem hífen, ou <c>null</c> quando não informado.</summary>
    public string? Cep { get; private set; }

    public string? Logradouro { get; private set; }
    public string? Bairro { get; private set; }
    public string? Localidade { get; private set; }
    public string? Estado { get; private set; }

    public ResidenceType ResidenceType { get; private set; }

    /// <summary>
    /// Número da casa ou do apartamento.
    /// </summary>
    /// <remarks>
    /// Texto e não inteiro: "123-A", "S/N" e "12 fundos" são endereços reais, e
    /// um <c>int</c> recusaria os três.
    /// </remarks>
    public string? Numero { get; private set; }

    /// <summary>
    /// Andar. Só existe em apartamento.
    /// </summary>
    /// <remarks>
    /// <b>Opcional mesmo em apartamento</b>, de propósito. Exigi-lo pareceria
    /// mais rigoroso, mas obrigaria quem digita uma lista de papel que só traz
    /// "Apto 32" a inventar um andar para conseguir salvar — e inventar dado é
    /// pior do que deixar em branco.
    ///
    /// <para>
    /// A regra é <b>positiva</b>: só apartamento tem andar. Ela já foi negativa
    /// ("casa não tem andar"), o que cobria tudo enquanto existiam dois valores
    /// — e passaria a permitir andar em sítio no dia em que o terceiro valor
    /// entrasse. Ver <c>db/015_tipos_de_residencia.sql</c>.
    /// </para>
    /// </remarks>
    public string? Andar { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Verdadeiro quando não há nada de útil aqui.
    /// </summary>
    /// <remarks>
    /// Serve para a borda não gravar uma linha vazia quando o formulário volta
    /// sem endereço nenhum — um endereço em branco é ruído que a tela teria de
    /// aprender a esconder.
    /// </remarks>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Cep)
        && string.IsNullOrWhiteSpace(Logradouro)
        && string.IsNullOrWhiteSpace(Localidade)
        && string.IsNullOrWhiteSpace(Numero);

    public static Address Register(
        long tenantId,
        string? cep,
        string? logradouro,
        string? bairro,
        string? localidade,
        string? estado,
        ResidenceType residenceType,
        string? numero,
        string? andar,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        var endereco = new Address
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedAt = now,
        };

        endereco.Aplicar(cep, logradouro, bairro, localidade, estado, residenceType, numero, andar, now);

        return endereco;
    }

    public void Update(
        string? cep,
        string? logradouro,
        string? bairro,
        string? localidade,
        string? estado,
        ResidenceType residenceType,
        string? numero,
        string? andar,
        DateTimeOffset now) =>
        Aplicar(cep, logradouro, bairro, localidade, estado, residenceType, numero, andar, now);

    private void Aplicar(
        string? cep,
        string? logradouro,
        string? bairro,
        string? localidade,
        string? estado,
        ResidenceType residenceType,
        string? numero,
        string? andar,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(residenceType))
        {
            throw new ArgumentException("Tipo de residência inválido.", nameof(residenceType));
        }

        // CEP inválido vira nulo em vez de exceção: o endereço manual é um
        // caminho legítimo do requisito, e recusar o cadastro inteiro por um
        // CEP que a pessoa não sabe impediria de registrar onde ela mora.
        Cep = PostalCode.Normalize(cep);

        Logradouro = Limpar(logradouro);
        Bairro = Limpar(bairro);
        Localidade = Limpar(localidade);
        Estado = Limpar(estado);
        ResidenceType = residenceType;
        Numero = LimparComLimite(numero, MaxNumeroLength, nameof(numero));

        // O andar é DESCARTADO em casa, não recusado.
        //
        // Alguém que marcou "Apartamento", digitou o andar e depois corrigiu
        // para "Casa" mandaria os dois campos. Lançar ali transformaria uma
        // correção comum em erro de validação; zerar o campo faz a entidade
        // refletir o que a pessoa quis dizer. A constraint do banco continua
        // sendo a rede de segurança para quem escrever por fora.
        Andar = residenceType == ResidenceType.Apartamento
            ? LimparComLimite(andar, MaxAndarLength, nameof(andar))
            : null;

        UpdatedAt = now;
    }

    private static string? Limpar(string? valor)
    {
        var limpo = valor?.Trim();
        return string.IsNullOrEmpty(limpo) ? null : limpo;
    }

    private static string? LimparComLimite(string? valor, int limite, string nomeDoParametro)
    {
        var limpo = Limpar(valor);

        if (limpo is not null && limpo.Length > limite)
        {
            throw new ArgumentException($"Máximo de {limite} caracteres.", nomeDoParametro);
        }

        return limpo;
    }
}
