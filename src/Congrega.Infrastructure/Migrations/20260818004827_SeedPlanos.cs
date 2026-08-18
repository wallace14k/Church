using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Popula o catálogo de <c>plans</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mesma classe de necessidade do <see cref="SeedRolesAndPermissions"/>: sem
/// estas linhas o endpoint de checkout sobe, autentica, valida a chave de
/// idempotência e responde <b>"plano indisponível"</b> para qualquer código —
/// falha silenciosa, nada quebrado, nada funcionando.
/// </para>
/// <para>
/// O corpo gerado pela ferramenta veio vazio porque <c>plans</c> não está
/// mapeada no modelo do EF: ela é lida por SQL direto no
/// <c>PlanRepository</c>. O script embutido é a fonte.
/// </para>
/// <para>
/// Idempotente por <c>ON CONFLICT (code) DO UPDATE</c> — as bases de
/// desenvolvimento já existem e reaplicar não pode duplicar nem falhar. O
/// <c>UPDATE</c> também é como uma correção de preço chega ao ambiente.
/// </para>
/// </remarks>
public partial class SeedPlanos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.080_planos.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Apagar planos deixaria assinaturas e pagamentos apontando para um
        // plano inexistente — e as FKs para dado financeiro são RESTRICT, então
        // o DELETE falharia no meio da reversão, com o banco pela metade.
        // Retirar um plano de circulação é `is_active = FALSE`, não DELETE.
        throw new NotSupportedException(
            "Remover planos quebraria assinaturas e pagamentos existentes. "
            + "Para tirar um plano de venda, use is_active = FALSE numa migration própria.");
    }
}
