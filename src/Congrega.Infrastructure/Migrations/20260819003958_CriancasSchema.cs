using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cria o schema do check-in infantil — a classe de dado de maior severidade do
/// sistema (ADR-014).
/// </summary>
/// <remarks>
/// <para>
/// Corpo escrito à mão, e não o gerado pela ferramenta, pelo mesmo motivo de
/// <see cref="FinanceiroSchema"/> e <see cref="EventosSchema"/>: o scaffold
/// veio vazio porque as cinco tabelas ainda não estão mapeadas no modelo, e
/// mesmo quando estiverem ele não sabe expressar RLS, os <c>CHECK</c>, o índice
/// parcial de presença nem as FK <c>RESTRICT</c>.
/// </para>
/// <para>
/// <b>Aqui o descompasso seria mais caro que nas ondas anteriores.</b> Uma
/// tabela criada sem <c>ENABLE ROW LEVEL SECURITY</c> tem exatamente a mesma
/// aparência de uma tabela protegida — e a diferença é a ficha de alergia de
/// uma criança visível para outra igreja. O DDL revisável em <c>db/</c> é a
/// fonte, e os testes de Testcontainers rodam estas migrations do zero.
/// </para>
/// </remarks>
public partial class CriancasSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.110_criancas.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ordem inversa das dependências. `child_access_log` primeiro por ser
        // folha sem FK; `children` por último, porque as outras a referenciam.
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS child_access_log;
            DROP TABLE IF EXISTS parental_consents;
            DROP TABLE IF EXISTS child_checkins;
            DROP TABLE IF EXISTS child_guardians;
            DROP TABLE IF EXISTS children;
            """);
    }
}
