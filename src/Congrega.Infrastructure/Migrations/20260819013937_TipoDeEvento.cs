using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Acrescenta <c>events.event_type</c>.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL, e desta vez o gerado estava <b>errado</b>, não
/// apenas incompleto: a ferramenta emitiu <c>defaultValue: (short)0</c>, e zero
/// está fora do <c>CHECK (event_type BETWEEN 1 AND 5)</c>. Todo evento já
/// cadastrado nasceria com um valor que a própria constraint recusa.
/// </para>
/// <para>
/// O script usa <c>DEFAULT 5</c> (Outro) — o único valor que não afirma algo
/// falso sobre um evento que ninguém classificou — e acrescenta o CHECK e o
/// índice do resumo por tipo, nenhum dos dois exprimível pelo modelo.
/// </para>
/// </remarks>
public partial class TipoDeEvento : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.120_tipo_evento.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS ix_events_tenant_tipo;
            ALTER TABLE events DROP CONSTRAINT IF EXISTS ck_events_tipo;
            ALTER TABLE events DROP COLUMN IF EXISTS event_type;
            """);
    }
}
