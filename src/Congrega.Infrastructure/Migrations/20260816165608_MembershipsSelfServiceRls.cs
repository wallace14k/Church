using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Deixa a policy de RLS de <c>memberships</c> aceitar leitura pelo próprio
/// <c>user_id</c>, não só por <c>tenant_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>O bug que isto corrige.</b> Descoberto ao ligar <c>congrega_app</c> de
/// verdade (migration <c>AppRoles</c>): <c>ListActiveTenantsAsync</c> — usada no
/// login para resolver o tenant e no endpoint <c>/auth/tenants</c> para alimentar
/// a troca de igreja — precisa listar as memberships do usuário <b>antes</b> de
/// qualquer tenant estar selecionado. Nesse momento
/// <c>current_setting('app.tenant_id')</c> é vazio, a policy original
/// (<c>tenant_id = ...</c>) nunca casava, e a consulta voltava sempre vazia —
/// login silenciosamente parava de resolver tenant para usuário nenhum.
/// </para>
/// <para>
/// <b>Por que <c>OR user_id</c> não é um vazamento.</b> "Quais igrejas eu tenho
/// vínculo" é dado do próprio usuário, não de outro tenant — o mesmo raciocínio
/// que já vale para <c>subscriptions</c>, <c>payments</c> e
/// <c>notification_queue</c>, cujas policies já têm exatamente este <c>OR</c>.
/// <c>memberships</c> ficou de fora na primeira redação do schema; esta migration
/// alinha ela ao mesmo padrão.
/// </para>
/// </remarks>
public partial class MembershipsSelfServiceRls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY tenant_isolation_memberships ON memberships;

            CREATE POLICY tenant_isolation_memberships ON memberships
                FOR ALL
                USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT
                    OR user_id = NULLIF(current_setting('app.user_id', TRUE), '')::BIGINT
                );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY tenant_isolation_memberships ON memberships;

            CREATE POLICY tenant_isolation_memberships ON memberships
                FOR ALL
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
            """);
    }
}
