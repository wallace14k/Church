using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Registra <c>Payment</c> e <c>Entitlement</c> como entidades mapeadas pelo
/// EF Core.
/// </summary>
/// <remarks>
/// <para>
/// Corpo vazio de propósito, mesmo caso da <see cref="FamilyEntityMapping"/>:
/// <c>payments</c> e <c>entitlements</c> já existem no banco desde o
/// <c>db/schema.sql</c> executado pela <see cref="BaselineSchema"/> — com RLS,
/// <c>CHECK</c>, FKs <c>RESTRICT</c> e os índices parciais que o modelo não
/// expressa. O <c>Up</c> gerado pela ferramenta chamava <c>CreateTable</c> nas
/// duas e falharia com tabela duplicada.
/// </para>
/// <para>
/// Esta migration só avança o histórico do EF para casar com o snapshot; nada
/// muda no banco. Sem ela, o <c>MigrateAsync</c> recusa subir com
/// <c>PendingModelChangesWarning</c> — foi assim que os testes de integração
/// pegaram o descompasso, antes de qualquer deploy.
/// </para>
/// </remarks>
public partial class BillingEntityMapping : Migration
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
