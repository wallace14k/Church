using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cria a tabela <c>events</c> do calendário.
/// </summary>
/// <remarks>
/// Executa <c>db/007_eventos.sql</c> em vez do corpo gerado, pelo mesmo motivo
/// da <see cref="BaselineSchema"/> e da <see cref="FinanceiroSchema"/>: o que
/// sustenta a correção da tabela é inexprimível no modelo — a policy de Row
/// Level Security, o <c>CHECK (ends_at &gt; starts_at)</c> que impede um evento
/// de sumir de toda consulta por intervalo, e o índice
/// <c>ix_events_tenant_inicio</c> que a agenda usa a cada abertura.
/// </remarks>
public partial class EventosSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.070_eventos.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS events;");
    }
}
