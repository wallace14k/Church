using System.ComponentModel.DataAnnotations;

namespace Congrega.Application.Retention;

/// <summary>Configuração do motor de retenção. Validada no startup (fail fast).</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>
    /// Permite desligar o motor sem redeploy — útil durante incidente no provedor
    /// de e-mail, quando continuar enfileirando só aumentaria a fila a drenar.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Intervalo entre ciclos. Uma hora é suficiente: as janelas são diárias, e
    /// varrer com mais frequência gasta banco sem antecipar nenhum alerta.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Linhas por lote. Equilibra memória e número de idas ao banco; 500 mantém o
    /// lote na casa das centenas de KB mesmo com 300 mil assinaturas na base.
    /// </summary>
    [Range(50, 5000)]
    public int BatchSize { get; init; } = 500;

    /// <summary>Chave do lock distribuído. Precisa ser idêntica em todas as réplicas.</summary>
    [Required]
    public string LockKey { get; init; } = "congrega:retention-scan";

    /// <summary>
    /// Fuso de negócio. As janelas são contadas em dias-calendário do usuário, não
    /// em UTC — vencer "hoje" precisa significar hoje no Brasil, ou o alerta D-1
    /// chega no dia errado para parte da base.
    /// </summary>
    [Required]
    public string BusinessTimeZone { get; init; } = "America/Sao_Paulo";

    /// <summary>Teto de segurança por ciclo. Evita que um bug de dados vire tempestade de notificação.</summary>
    [Range(100, 1_000_000)]
    public int MaxAlertsPerCycle { get; init; } = 50_000;
}
