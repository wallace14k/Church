using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Registra o mapeamento de <c>Subscription</c> para <c>subscriptions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Corpo vazio de propósito — terceira ocorrência do mesmo descompasso já
/// documentado em <see cref="FamilyEntityMapping"/> e
/// <see cref="BillingEntityMapping"/>, e a de consequência mais grave até aqui.
/// </para>
/// <para>
/// <b>O que a ferramenta gerou, e por que não serve.</b> Até esta migration,
/// <c>Subscription</c> não tinha <c>IEntityTypeConfiguration</c> nenhuma: o EF
/// caía na convenção padrão e acreditava numa tabela <c>"Subscriptions"</c>, com
/// colunas <c>"PlanId"</c>, <c>"CurrentPeriodEnd"</c> e afins. Ao ganhar o
/// mapeamento correto, o scaffold leu a diferença como uma renomeação e emitiu
/// <c>RenameTable</c> + treze <c>RenameColumn</c> — sobre uma tabela que
/// <b>nunca existiu</b>. Aplicá-la falharia no primeiro comando.
/// </para>
/// <para>
/// A tabela real, <c>subscriptions</c>, vem do <c>db/schema.sql</c> desde o
/// baseline, já em snake_case, com RLS, o <c>CHECK ck_sub_owner</c> e as FKs
/// <c>RESTRICT</c> para <c>plans</c>. O mapeamento novo apenas passa a
/// descrevê-la corretamente; nada muda no banco.
/// </para>
/// <para>
/// <b>Como o descompasso apareceu:</b> não pelos testes, que usavam dublê, mas
/// pela primeira chamada real ao checkout — <c>42P01: relation
/// "public.Subscriptions" does not exist</c>. O <c>ISubscriptionStore</c>
/// compilava e tinha teste verde desde a Onda 3 sem nunca ter executado uma
/// consulta de verdade.
/// </para>
/// </remarks>
public partial class SubscriptionEntityMapping : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
