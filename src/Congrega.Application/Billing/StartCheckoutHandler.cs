using System.Globalization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Congrega.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Billing;

/// <summary>Pedido de checkout, já com o titular resolvido pela borda.</summary>
public sealed record StartCheckoutCommand
{
    /// <summary>Quem paga. Vem da claim <c>sub</c>, nunca do corpo da requisição.</summary>
    public required long UserId { get; init; }

    /// <summary>Código do plano. É o <b>único</b> dado ligado a preço que o cliente informa.</summary>
    public required string PlanCode { get; init; }

    /// <summary>
    /// Chave de idempotência enviada pelo cliente, crua.
    /// </summary>
    /// <remarks>
    /// Ainda <b>não</b> é a chave que vai para o banco — ver a nota de
    /// <see cref="StartCheckoutHandler"/> sobre por que ela é prefixada pelo
    /// titular antes de ser persistida.
    /// </remarks>
    public required string IdempotencyKey { get; init; }

    public string? CustomerEmail { get; init; }
    public string? CustomerName { get; init; }
}

public enum CheckoutOutcome
{
    /// <summary>Cobrança criada agora.</summary>
    Created,

    /// <summary>Chave já usada: devolve a MESMA cobrança, sem criar outra.</summary>
    Reused,

    /// <summary>Plano inexistente, inativo ou de audiência incompatível.</summary>
    PlanUnavailable,

    /// <summary>
    /// O titular já tem uma assinatura em andamento (pendente, ativa, atrasada
    /// ou em carência) — de <b>outro</b> plano.
    /// </summary>
    /// <remarks>
    /// <c>uq_sub_active_user</c> permite só uma por pessoa nesses estados.
    /// Retomar a mesma assinatura do MESMO plano é o caminho feliz
    /// (<see cref="StartCheckoutHandler.ResolverAssinaturaAsync"/>); pedir um
    /// plano diferente enquanto a primeira ainda não resolveu é a troca de
    /// plano, que este handler não faz — precisaria decidir o que acontece com
    /// o período já pago da assinatura anterior, e essa decisão não existe
    /// ainda.
    /// </remarks>
    SubscriptionConflict,
}

public sealed record CheckoutResult
{
    public required CheckoutOutcome Outcome { get; init; }
    public Guid PaymentId { get; init; }
    public long AmountCents { get; init; }
    public string PlanName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? CheckoutUrl { get; init; }
    public string? PixCode { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Abre uma cobrança de assinatura Congrega+.
/// </summary>
/// <remarks>
/// <para>
/// <b>O preço vem do banco, nunca da requisição.</b> O cliente informa apenas o
/// código do plano; valor, período e audiência são lidos de <c>plans</c>. Aceitar
/// <c>amountCents</c> do corpo é a adulteração de preço mais banal que existe — e
/// ela não aparece em teste nenhum, porque o cliente honesto sempre manda o valor
/// certo.
/// </para>
/// <para>
/// <b>A chave de idempotência é prefixada pelo titular.</b>
/// <c>uq_pay_idempotency_key</c> é <c>UNIQUE</c> sobre a tabela inteira. Se a
/// chave fosse gravada como o cliente a envia, duas pessoas escolhendo
/// <c>"1"</c> colidiriam: a segunda receberia de volta a cobrança da primeira,
/// com o identificador público dela — vazamento de dado financeiro entre
/// titulares, causado por uma constraint que existia para proteger. Prefixar com
/// o <c>user_id</c> preserva a garantia e a confina a um titular.
/// </para>
/// <para>
/// <b>A unicidade é resolvida pelo banco.</b> A consulta prévia por chave existe
/// para o caminho feliz — retry de rede devolvendo a mesma cobrança —, mas duas
/// requisições simultâneas passam pelas duas consultas antes de qualquer
/// <c>INSERT</c>. Quem decide é a constraint: o segundo <c>INSERT</c> falha e o
/// <c>catch</c> relê e devolve a cobrança vencedora. Trocar isso por
/// <c>if (!existe)</c> seria a race condition que o <c>CLAUDE.md</c> proíbe.
/// </para>
/// <para>
/// <b>A cobrança nasce no gateway antes de o pagamento ser gravado.</b> A ordem
/// inversa gravaria pagamento sem cobrança correspondente quando o gateway
/// falhasse — linha órfã que nenhum webhook jamais resolveria, porque não existe
/// cobrança sobre a qual notificar.
/// </para>
/// </remarks>
public sealed class StartCheckoutHandler(
    IPlanRepository plans,
    IPaymentRepository payments,
    ISubscriptionStore subscriptions,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<StartCheckoutHandler> logger)
{
    /// <summary>Teto do que o cliente pode mandar como chave.</summary>
    /// <remarks>
    /// Derivado da coluna, não escolhido: <c>payments.idempotency_key</c> é
    /// <c>VARCHAR(100)</c>, e o prefixo do titular custa até 21 caracteres
    /// (<c>"u"</c> + 19 dígitos de um <c>BIGINT</c> + <c>":"</c>). Sobram 79.
    /// <para>
    /// O limite é validado na borda em vez de truncado aqui: truncar
    /// transformaria duas chaves distintas na mesma e devolveria ao cliente uma
    /// cobrança que não é dele — silenciosamente, e só para quem manda chave
    /// longa.
    /// </para>
    /// </remarks>
    public const int MaxIdempotencyKeyLength = 79;

    public async Task<CheckoutResult> HandleAsync(
        StartCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var agora = timeProvider.GetUtcNow();
        string chave = ComporChave(command.UserId, command.IdempotencyKey);

        // Caminho feliz do retry: a mesma chave devolve a mesma cobrança.
        var existente = await payments.FindByIdempotencyKeyAsync(chave, cancellationToken);
        if (existente is not null)
        {
            return Reutilizar(existente, command.UserId);
        }

        var plano = await plans.FindByCodeAsync(command.PlanCode, cancellationToken);

        // Mesma resposta para "não existe", "inativo" e "audiência errada":
        // distinguir entregaria a quem sonda a lista de códigos de plano.
        if (plano is null)
        {
            return PlanoIndisponivel();
        }

        if (plano.Audience != PlanAudience.User)
        {
            // Plano de igreja não se compra como pessoa física: o titular seria
            // o tenant, o preço é outro, e o acesso ao ChMS vem da membership e
            // não de entitlement. Sem esta checagem bastaria o código do plano.
            logger.LogWarning(
                "Usuário {UserId} tentou checkout do plano {PlanCode}, de audiência {Audience}.",
                command.UserId, plano.Code, plano.Audience);

            return PlanoIndisponivel();
        }

        // A assinatura precisa existir COM IDENTIDADE antes do pagamento: o
        // `subscription_id` é o que liga a confirmação à concessão de acesso, e
        // uma entidade recém-adicionada ainda tem Id 0 até o commit. Gravar o
        // pagamento com `subscription_id` nulo faria o GrantEntitlementHandler
        // registrar "pagamento sem assinatura" e não conceder nada — o usuário
        // pagaria e não receberia acesso.
        Subscription assinatura;
        try
        {
            assinatura = await ResolverAssinaturaAsync(command.UserId, plano, agora, cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (
            string.Equals(ex.ConstraintName, "uq_sub_active_user", StringComparison.Ordinal))
        {
            // FindReusableForCheckoutAsync filtra pelo PLANO pedido — não acha
            // nada quando a assinatura em andamento é de outro. O INSERT então
            // colide com uq_sub_active_user, que permite só uma por pessoa nos
            // estados não terminais. Correção vem da constraint, não de uma
            // consulta prévia "o usuário já tem assinatura?" — essa checagem
            // teria a mesma janela de corrida que a chave de idempotência já
            // resolve por constraint mais abaixo.
            logger.LogInformation(
                ex,
                "Usuário {UserId} já tem assinatura em andamento; checkout do plano {PlanCode} recusado.",
                command.UserId, plano.Code);

            return new CheckoutResult
            {
                Outcome = CheckoutOutcome.SubscriptionConflict,
                Detail = "Você já tem uma assinatura em andamento. Aguarde a confirmação do "
                    + "pagamento anterior antes de assinar outro plano.",
            };
        }

        // A MESMA chave vai para o gateway: idempotência só do nosso lado não
        // impede a segunda chamada de virar a segunda cobrança lá dentro.
        var cobranca = await gateway.CreateChargeAsync(
            new ChargeRequest
            {
                AmountCents = plano.PriceCents,
                IdempotencyKey = chave,
                Description = plano.Name,
                CustomerEmail = command.CustomerEmail,
                CustomerName = command.CustomerName,
            },
            cancellationToken);

        var pagamento = Payment.Start(
            amountCents: plano.PriceCents,
            idempotencyKey: chave,
            source: SubscriptionSource.AbacatePay,
            now: agora,
            subscriptionId: assinatura.Id,
            userId: command.UserId,
            method: PaymentMethod.Pix);

        pagamento.AttachGatewayCharge(cobranca.ChargeId, agora);
        payments.Add(pagamento);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
        {
            // A corrida: outra requisição com a mesma chave commitou primeiro.
            // Reler e devolver a vencedora é o comportamento correto — as duas
            // chamadas representam a MESMA intenção do usuário.
            logger.LogInformation(
                ex,
                "Checkout concorrente na chave {Chave}. Devolvendo a cobrança já criada.",
                chave);

            var vencedora = await payments.FindByIdempotencyKeyAsync(chave, cancellationToken);

            if (vencedora is null)
            {
                // A constraint acusou colisão e a releitura não achou nada: ou a
                // violação foi de outra constraint, ou há inconsistência. Deixar
                // subir é melhor do que devolver um checkout inventado.
                throw;
            }

            return Reutilizar(vencedora, command.UserId);
        }

        logger.LogInformation(
            "Checkout aberto: pagamento {PaymentId}, plano {PlanCode}, {Centavos} centavos.",
            pagamento.PublicId, plano.Code, plano.PriceCents);

        return new CheckoutResult
        {
            Outcome = CheckoutOutcome.Created,
            PaymentId = pagamento.PublicId,
            AmountCents = pagamento.AmountCents,
            PlanName = plano.Name,
            Status = pagamento.Status.ToString(),
            CheckoutUrl = cobranca.CheckoutUrl,
            PixCode = cobranca.PixCode,
        };
    }

    private static CheckoutResult PlanoIndisponivel() => new()
    {
        Outcome = CheckoutOutcome.PlanUnavailable,
        Detail = "Plano indisponível.",
    };

    /// <summary>
    /// Reaproveita a cobrança existente, conferindo o titular antes.
    /// </summary>
    /// <remarks>
    /// A conferência é redundante — a chave já carrega o <c>user_id</c> no
    /// prefixo. Fica porque é barata e porque, no dia em que alguém mexer no
    /// formato da chave, é ela que transforma um erro de composição em falha
    /// visível em vez de em vazamento silencioso.
    /// </remarks>
    private static CheckoutResult Reutilizar(Payment pagamento, long userId)
    {
        if (pagamento.UserId != userId)
        {
            throw new InvalidOperationException(
                "Chave de idempotência resolveu para pagamento de outro titular.");
        }

        return new CheckoutResult
        {
            Outcome = CheckoutOutcome.Reused,
            PaymentId = pagamento.PublicId,
            AmountCents = pagamento.AmountCents,
            Status = pagamento.Status.ToString(),
        };
    }

    /// <summary>
    /// Devolve a assinatura à qual o pagamento se pendura, já persistida.
    /// </summary>
    /// <remarks>
    /// Reaproveita inclusive a <c>Pending</c>: sem isso, cada tentativa de
    /// checkout que falhasse no gateway deixaria para trás mais uma assinatura
    /// pendente do mesmo plano. Pendente não concede nada — só o webhook de
    /// pagamento a ativa —, então reaproveitá-la não antecipa acesso.
    /// </remarks>
    private async Task<Subscription> ResolverAssinaturaAsync(
        long userId,
        PlanSnapshot plano,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var reaproveitavel = await subscriptions.FindReusableForCheckoutAsync(
            userId, plano.Id, cancellationToken);

        if (reaproveitavel is not null)
        {
            return reaproveitavel;
        }

        var nova = Subscription.Create(
            planId: plano.Id,
            tenantId: null,
            userId: userId,
            source: SubscriptionSource.AbacatePay,
            periodStart: agora,
            periodEnd: FimDoPeriodo(agora, plano.BillingPeriod));

        subscriptions.Add(nova);

        // Commit próprio, só para materializar o Id. Ver a nota na chamada.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return nova;
    }

    /// <summary>Fim do primeiro período. 2 = anual; qualquer outro, mensal.</summary>
    private static DateTimeOffset FimDoPeriodo(DateTimeOffset inicio, short billingPeriod) =>
        billingPeriod == 2 ? inicio.AddYears(1) : inicio.AddMonths(1);

    /// <summary>Compõe a chave persistida. Ver a nota sobre colisão entre titulares.</summary>
    private static string ComporChave(long userId, string chaveDoCliente) =>
        string.Create(CultureInfo.InvariantCulture, $"u{userId}:{chaveDoCliente.Trim()}");
}
