using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Registra <c>Family</c> como entidade mapeada pelo EF Core, substituindo a
/// projeção somente-leitura <c>FamilyRow</c> usada até aqui.
/// </summary>
/// <remarks>
/// Corpo vazio de propósito: <c>created_at</c>, <c>updated_at</c> e a
/// constraint <c>uq_families_public_id</c> já existem na tabela física —
/// foram criados por <c>db/002_members.sql</c>, antes de existir timeline de
/// migrations. O <c>Up</c> gerado pela ferramenta tentava recriá-los
/// (<c>AddColumn</c> falharia com coluna duplicada), o mesmo descompasso já
/// documentado na <see cref="BaselineSchema"/>. Esta migration só avança o
/// histórico do EF para casar com o snapshot; nada muda no banco.
/// </remarks>
public partial class FamilyEntityMapping : Migration
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
