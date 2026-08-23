using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cor do tipo de evento, para a agenda distinguir os tipos de relance.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime o <c>CHECK</c> do formato
/// <c>#RRGGBB</c> nem a atribuição de uma cor inicial aos tipos que já existem —
/// sem ela, toda igreja abriria a agenda nova com as faixas cinzas e concluiria
/// que a cor não funciona, em vez de que ela ainda não foi escolhida.
/// </para>
/// </remarks>
public partial class CorDoTipoDeEvento : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.210_cor_tipo_evento.sql"));
    }

    /// <summary>
    /// Remove a cor.
    /// </summary>
    /// <remarks>
    /// Perde a escolha de cada igreja. A agenda volta a distinguir os tipos
    /// apenas pelo nome e pelo ícone, que continuam sendo o que carrega o
    /// significado — a cor sempre foi reforço.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE event_types DROP CONSTRAINT IF EXISTS ck_event_types_cor;
            ALTER TABLE event_types DROP COLUMN IF EXISTS color_hex;
            """);
    }
}
