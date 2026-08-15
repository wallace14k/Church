namespace Congrega.Domain.Retention;

/// <summary>
/// Resolve qual janela de alerta de retenção se aplica a uma assinatura, a partir
/// da data de fim do período pago e da data corrente.
/// </summary>
/// <remarks>
/// <para>
/// Função pura: sem I/O, sem relógio, sem estado. Toda a lógica que decide "este
/// usuário deve receber alerta hoje?" mora aqui e é testável sem banco, sem mock
/// e sem contêiner — que é o motivo de ela viver no domínio e não no worker.
/// </para>
/// <para>
/// <b>Decisão de projeto — faixas em vez de igualdade exata.</b> A implementação
/// ingênua seria <c>daysRemaining == 15</c>. Ela quebra silenciosamente no primeiro
/// dia em que o job não roda: se o worker ficar fora do ar no dia D-7, aquele alerta
/// nunca mais dispara, porque no dia seguinte a comparação já falhou.
/// </para>
/// <para>
/// Aqui cada janela cobre uma <i>faixa</i>, e o resultado é sempre a janela mais
/// urgente já alcançada. Efeitos práticos:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Job parado por quatro dias: ao voltar, envia a janela correta para o estado
///     atual — não uma enxurrada de quatro alertas atrasados.
///   </description></item>
///   <item><description>
///     Assinatura criada no meio de uma faixa (ex.: com 5 dias restantes) recebe
///     D7 imediatamente, em vez de ficar sem nenhum alerta até D3.
///   </description></item>
/// </list>
/// <para>
/// A não repetição do mesmo alerta é garantida pela constraint
/// <c>UNIQUE (dedupe_key)</c> em <c>notification_queue</c> — não por esta classe e
/// não pelo lock distribuído. Ver <c>docs/04-modelagem-dados.md</c> §2.3.
/// </para>
/// </remarks>
public static class RetentionWindowCalculator
{
    /// <summary>Quantos dias à frente a varredura precisa enxergar.</summary>
    public const int LookAheadDays = 15;

    /// <summary>
    /// Quantos dias após o vencimento a varredura ainda considera. Coberto por
    /// <see cref="RetentionAlertWindow.GraceD3"/>; alguns dias de folga absorvem
    /// uma eventual parada do worker sem perder o alerta de grace period.
    /// </summary>
    public const int LookBehindDays = 10;

    /// <summary>
    /// Devolve a janela aplicável, ou <c>null</c> quando a assinatura está fora de
    /// qualquer faixa de alerta.
    /// </summary>
    /// <param name="periodEnd">Fim do período pago da assinatura.</param>
    /// <param name="today">Data corrente no fuso de negócio (America/Sao_Paulo).</param>
    public static RetentionAlertWindow? Resolve(DateOnly periodEnd, DateOnly today)
    {
        int daysRemaining = periodEnd.DayNumber - today.DayNumber;

        return daysRemaining switch
        {
            // Longe demais: alertar agora só treina o usuário a ignorar o alerta.
            >= 16 => null,

            // Faixas de pré-vencimento. A ordem importa — o primeiro padrão que
            // casa vence, então cada braço já exclui os anteriores.
            >= 8 => RetentionAlertWindow.D15,   // 8..15 dias restantes
            >= 4 => RetentionAlertWindow.D7,    // 4..7
            >= 2 => RetentionAlertWindow.D3,    // 2..3
            >= 0 => RetentionAlertWindow.D1,    // 0..1 (0 = vence hoje)

            // Vencida há 1 ou 2 dias: silêncio deliberado. O usuário acabou de
            // receber o D1; insistir no dia seguinte irrita mais do que converte.
            > -3 => null,

            // Vencida há 3 dias ou mais, ainda dentro do grace period: última
            // chamada, com o tom mais urgente da sequência.
            _ => RetentionAlertWindow.GraceD3
        };
    }

    /// <summary>
    /// Canais de notificação da janela. O escalonamento é intencional: quanto mais
    /// perto do vencimento, mais intrusivo o canal. Começar por push no D-15
    /// gastaria o canal mais caro no momento de menor urgência.
    /// </summary>
    public static IReadOnlyList<NotificationChannel> ChannelsFor(RetentionAlertWindow window) =>
        window switch
        {
            RetentionAlertWindow.D15 => [NotificationChannel.Email],
            RetentionAlertWindow.D7 => [NotificationChannel.Email, NotificationChannel.Push],
            RetentionAlertWindow.D3 =>
                [NotificationChannel.Email, NotificationChannel.Push, NotificationChannel.InAppBanner],
            RetentionAlertWindow.D1 =>
                [NotificationChannel.Email, NotificationChannel.Push, NotificationChannel.InAppBanner],
            RetentionAlertWindow.GraceD3 =>
                [NotificationChannel.Email, NotificationChannel.Push, NotificationChannel.InAppBanner],
            _ => []
        };

    /// <summary>Código do template de conteúdo correspondente à janela.</summary>
    public static string TemplateCodeFor(RetentionAlertWindow window) =>
        window switch
        {
            RetentionAlertWindow.D15 => "retention.d15",
            RetentionAlertWindow.D7 => "retention.d7",
            RetentionAlertWindow.D3 => "retention.d3",
            RetentionAlertWindow.D1 => "retention.d1",
            RetentionAlertWindow.GraceD3 => "retention.grace.d3",
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Janela desconhecida.")
        };
}
