using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

/// <summary>Forma de pagamento do lançamento.</summary>
/// <remarks>
/// <c>Cartao</c> <b>fica</b>, e não vira crédito nem débito: os lançamentos já
/// gravados com ele não dizem qual era, e escolher um seria inventar informação
/// financeira. O formulário deixa de oferecê-lo — ver
/// <see cref="Oferecidas"/> — e a listagem continua sabendo desenhá-lo.
/// </remarks>
public enum GivingMethod : short
{
    Dinheiro = 1,
    Pix = 2,

    /// <summary>Legado: cartão sem distinção de crédito ou débito.</summary>
    Cartao = 3,

    Transferencia = 4,
    Cheque = 5,
    Outro = 6,
    CartaoCredito = 7,
    CartaoDebito = 8,
    Boleto = 9,
}

/// <summary>
/// O dinheiro já se moveu, ou ainda vai se mover?
/// </summary>
/// <remarks>
/// <para>
/// A distinção existe porque a série periódica cria doze linhas com data
/// futura, e sem ela o caixa de setembro afirmaria que o aluguel de setembro já
/// foi pago — dinheiro que não se moveu aparecendo como movimentado.
/// </para>
/// <para>
/// <b>Só <see cref="Realizado"/> entra no fechamento.</b> É a separação que a
/// contabilidade usa entre previsto e realizado, e é o que permite as doze
/// linhas existirem, serem editadas e planejadas sem mentir sobre o saldo.
/// </para>
/// </remarks>
public enum GivingEntryStatus : short
{
    /// <summary>O dinheiro se moveu. Soma no fechamento.</summary>
    Realizado = 1,

    /// <summary>Previsto para uma data futura. Não soma até ser confirmado.</summary>
    Previsto = 2,
}

/// <summary>Com que frequência um lançamento recorrente se repete.</summary>
public enum GivingRecurrence : short
{
    Semanal = 1,
    Mensal = 2,
    Anual = 3,
}

/// <summary>
/// Um lançamento de caixa — dinheiro que entrou ou saiu, na data em que isso
/// aconteceu.
/// </summary>
public sealed class GivingEntry : AggregateRoot
{
    /// <summary>Teto das observações. Casa com o CHECK da coluna.</summary>
    public const int MaxNotesLength = 500;

    /// <summary>Teto do título. Casa com o <c>VARCHAR(200)</c> da coluna.</summary>
    public const int MaxDescriptionLength = 200;

    private GivingEntry() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public long CategoryId { get; private set; }

    /// <summary>
    /// Entrada ou saída — e é <b>este</b> campo que o fechamento soma.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O sinal morava em <c>GivingCategory.Kind</c>, com a justificativa de que
    /// duas representações de saída acabariam somadas no mesmo relatório. O que
    /// invalidou aquilo foi a categoria poder ser <see cref="GivingKind.Ambos"/>:
    /// uma categoria que serve a entrada E saída não tem sinal para emprestar.
    /// </para>
    /// <para>
    /// Continuam sendo <b>duas colunas com papéis diferentes</b>, não duas
    /// verdades: aqui está o que o lançamento é; em <c>GivingCategory.Kind</c>
    /// está o que a categoria aceita. A compatibilidade entre os dois é
    /// verificada em <see cref="GivingCategory.Aceita"/>.
    /// </para>
    /// </remarks>
    public GivingKind Kind { get; private set; }

    /// <summary>
    /// Título do lançamento — "Aluguel", "Energia", "Oferta do culto".
    /// </summary>
    /// <remarks>
    /// Nulo é permitido, e não é descuido: os lançamentos gravados antes deste
    /// campo não têm título, e preenchê-lo com o nome da categoria produziria
    /// exatamente a repetição ("Dízimo / Categoria: Dízimo") que ele veio
    /// resolver. Quem exibe cai no nome da categoria quando falta.
    /// </remarks>
    public string? Description { get; private set; }

    /// <summary>
    /// Conta ou caixa de onde o dinheiro saiu, ou para onde entrou.
    /// </summary>
    /// <remarks>
    /// Opcional: uma igreja com caixa único não deve ser obrigada a cadastrar
    /// uma conta para conseguir lançar.
    /// </remarks>
    public long? AccountId { get; private set; }

    /// <summary>
    /// O lançamento se repete.
    /// </summary>
    /// <remarks>
    /// <b>Marca a intenção; não gera nada.</b> Criar o lançamento do mês
    /// seguinte automaticamente exige worker com janela de execução,
    /// idempotência por período e uma regra para quando a igreja editar a série
    /// — e qualquer uma delas errada duplica dinheiro no relatório. Enquanto
    /// isso não existir, o campo serve para a tela marcar a linha e para a
    /// tesouraria saber o que esperar no mês que vem.
    /// </remarks>
    public bool IsRecurring { get; private set; }

    /// <summary>Frequência. Existe apenas quando <see cref="IsRecurring"/> é verdadeiro.</summary>
    public GivingRecurrence? Recurrence { get; private set; }

    /// <summary>
    /// Realizado ou previsto. Só o realizado entra no fechamento.
    /// </summary>
    public GivingEntryStatus Status { get; private set; }

    /// <summary>
    /// Identidade da série periódica, quando o lançamento pertence a uma.
    /// </summary>
    /// <remarks>
    /// Compartilhada pelo lançamento original e por todas as parcelas geradas a
    /// partir dele. Sem ela, editar "o aluguel" significaria editar doze linhas
    /// soltas que só a memória de quem criou liga entre si.
    /// </remarks>
    public Guid? SeriesId { get; private set; }

    /// <summary>
    /// Doador identificado, quando houver.
    /// </summary>
    /// <remarks>
    /// Nulo é o caso <b>comum</b>, não a exceção: oferta de gazofilácio não tem
    /// nome. Exigir membro impediria de lançar justamente a receita mais
    /// frequente da igreja.
    /// </remarks>
    public long? MemberId { get; private set; }

    /// <summary>Centavos, sempre positivo. Ver <see cref="GivingKind"/>.</summary>
    public long AmountCents { get; private set; }

    public DateOnly OccurredOn { get; private set; }
    public GivingMethod Method { get; private set; }

    /// <summary>Observações livres. Teto de <see cref="MaxNotesLength"/>.</summary>
    public string? Notes { get; private set; }

    /// <summary>Conta que digitou o lançamento. Prestação de contas precisa da autoria.</summary>
    public long? RecordedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GivingEntry Register(
        long tenantId,
        long categoryId,
        GivingKind kind,
        long amountCents,
        DateOnly occurredOn,
        GivingMethod method,
        DateTimeOffset now,
        long? memberId = null,
        string? notes = null,
        long? recordedByUserId = null,
        string? description = null,
        long? accountId = null,
        GivingRecurrence? recurrence = null,
        GivingEntryStatus status = GivingEntryStatus.Realizado,
        Guid? seriesId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryId);

        if (amountCents <= 0)
        {
            throw new ArgumentException(
                "O valor precisa ser maior que zero. Saída é definida pela categoria, não por valor negativo.",
                nameof(amountCents));
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentException("Forma de pagamento inválida.", nameof(method));
        }

        // `Ambos` descreve o que uma CATEGORIA aceita, não o que um lançamento
        // é. Um lançamento "ambos" não teria sinal, e o fechamento não saberia
        // se soma ou subtrai.
        if (kind is not (GivingKind.Entrada or GivingKind.Saida))
        {
            throw new ArgumentException(
                "O lançamento precisa ser entrada ou saída. \"Ambos\" só descreve o que uma categoria aceita.",
                nameof(kind));
        }

        if (recurrence is { } frequencia && !Enum.IsDefined(frequencia))
        {
            throw new ArgumentException("Frequência inválida.", nameof(recurrence));
        }

        var tituloLimpo = Aparar(description, MaxDescriptionLength, nameof(description));
        var notasLimpas = Aparar(notes, MaxNotesLength, nameof(notes));

        // Data futura em livro-caixa é erro de digitação — quase sempre o ano.
        // Sem a barreira, um lançamento de 2027 sairia silenciosamente do
        // fechamento do mês e ninguém acharia o dinheiro que "sumiu".
        //
        // A barreira vale para o REALIZADO. O previsto existe justamente para
        // ter data futura — e não soma no fechamento, então não esconde nada.
        var hoje = DateOnly.FromDateTime(now.UtcDateTime);
        if (status == GivingEntryStatus.Realizado && occurredOn > hoje)
        {
            throw new ArgumentException(
                "A data do lançamento não pode ser futura.", nameof(occurredOn));
        }

        return new GivingEntry
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Kind = kind,
            Description = tituloLimpo,
            MemberId = memberId,
            AccountId = accountId,
            AmountCents = amountCents,
            OccurredOn = occurredOn,
            Method = method,
            Notes = notasLimpas,
            // A frequência é o que DEFINE a recorrência: sem ela, "recorrente"
            // não diz quando. Derivar a bandeira do campo evita o par
            // inconsistente que a constraint do banco recusaria.
            IsRecurring = recurrence is not null,
            Recurrence = recurrence,
            Status = status,
            SeriesId = seriesId,
            RecordedByUserId = recordedByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Horizonte da série periódica.
    /// </summary>
    /// <remarks>
    /// Doze meses, contados a partir do lançamento original. Para a frequência
    /// mensal isso dá as doze parcelas que o pedido descreve; para semanal dá
    /// cerca de 52 e para anual dá uma. O horizonte é a regra — "doze parcelas"
    /// seria uma regra que só faz sentido para uma das três frequências.
    /// </remarks>
    public const int HorizonteEmMeses = 12;

    /// <summary>
    /// Confirma um lançamento previsto: o dinheiro se moveu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recusa confirmar o que ainda não aconteceu. Sem essa barreira, alguém
    /// confirmaria a parcela de dezembro em agosto e o fechamento de dezembro
    /// passaria a contar dinheiro que ninguém pagou — exatamente o problema que
    /// o estado <see cref="GivingEntryStatus.Previsto"/> existe para evitar.
    /// </para>
    /// <para>
    /// Confirmar o que já está realizado é <b>silencioso</b>, não erro: um
    /// clique duplo, ou dois usuários confirmando a mesma parcela, chegam ao
    /// mesmo estado, e recusar o segundo mostraria um erro sobre algo que deu
    /// certo.
    /// </para>
    /// </remarks>
    public void Confirmar(DateTimeOffset now)
    {
        if (Status == GivingEntryStatus.Realizado)
        {
            return;
        }

        var hoje = DateOnly.FromDateTime(now.UtcDateTime);
        if (OccurredOn > hoje)
        {
            throw new InvalidOperationException(
                "Este lançamento é de uma data futura e ainda não pode ser confirmado.");
        }

        Status = GivingEntryStatus.Realizado;
        UpdatedAt = now;
    }

    /// <summary>
    /// As datas das parcelas seguintes, dentro do horizonte de doze meses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O dia é fixado ao fim do mês quando não existe.</b> Uma despesa criada
    /// em 31 de janeiro repete em 28 de fevereiro (ou 29, em bissexto) e volta a
    /// 31 em março: <c>AddMonths</c> do .NET já faz isso, e é o comportamento
    /// certo — não existe 31 de fevereiro, e empurrar para 1º de março jogaria a
    /// parcela para o mês seguinte, fora do fechamento a que ela pertence.
    /// </para>
    /// <para>
    /// A data original <b>não</b> entra na lista: ela já é o lançamento que
    /// originou a série.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DateOnly> CalcularOcorrencias(
        DateOnly inicio,
        GivingRecurrence frequencia)
    {
        var limite = inicio.AddMonths(HorizonteEmMeses);
        var datas = new List<DateOnly>();

        // **Cada parcela é calculada a partir da data ORIGINAL, nunca da
        // anterior.** Caminhar de uma para a próxima perde o dia
        // permanentemente: 31/jan encolhe para 28/fev, e a partir daí "mais um
        // mês" a partir de 28 dá 28/mar — a série inteira desliza para o dia 28
        // por causa de um único mês curto. Calculando do início, fevereiro
        // encolhe e março volta a 31.
        //
        // Isto foi um bug real, pego pelo teste de fim de mês.
        for (int passo = 1; ; passo++)
        {
            var data = Deslocar(inicio, frequencia, passo);

            if (data > limite)
            {
                break;
            }

            datas.Add(data);
        }

        return datas;
    }

    private static DateOnly Deslocar(DateOnly inicio, GivingRecurrence frequencia, int passos) =>
        frequencia switch
        {
            GivingRecurrence.Semanal => inicio.AddDays(7 * passos),
            GivingRecurrence.Mensal => inicio.AddMonths(passos),
            GivingRecurrence.Anual => inicio.AddYears(passos),
            _ => throw new ArgumentException("Frequência inválida.", nameof(frequencia)),
        };

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
