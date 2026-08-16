using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Popula <c>roles</c>, <c>permissions</c> e <c>role_permissions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Estas linhas não são dado de exemplo: sem elas <b>toda policy de autorização
/// reprova</b>, porque a checagem resolve permissão pelo papel da membership e
/// não encontra papel nenhum. O login completa e nada mais funciona — falha
/// silenciosa e cara de diagnosticar. Por isso o seed é uma migration, e não um
/// script que alguém lembra de rodar.
/// </para>
/// <para>
/// Separada do baseline de propósito: schema e dado têm ciclos de vida
/// diferentes. Um papel novo no futuro é uma migration nova, revisável sozinha,
/// sem reabrir o arquivo que cria o banco inteiro.
/// </para>
/// <para>
/// O script é idempotente: papéis e permissões entram como upsert pelo código
/// (<c>ON CONFLICT (code) DO UPDATE</c>, o que também corrige um nome renomeado)
/// e o vínculo entre eles usa <c>DO NOTHING</c>. Reaplicá-lo sobre uma base já
/// semeada — o caso das bases de desenvolvimento — não duplica nem falha.
/// </para>
/// </remarks>
public partial class SeedRolesAndPermissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.900_seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Apagar papéis deixaria memberships apontando para papel inexistente e
        // travaria o acesso de todo mundo. Se um papel precisar sumir, isso é uma
        // migration própria, que também decide para onde vão as memberships dele.
        throw new NotSupportedException(
            "Remover papéis e permissões travaria o acesso de todas as memberships. "
            + "Para retirar um papel, escreva uma migration que também realoque quem o usa.");
    }
}
