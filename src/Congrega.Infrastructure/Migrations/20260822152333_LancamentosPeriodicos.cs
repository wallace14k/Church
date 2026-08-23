using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Lançamentos periódicos: estado (realizado/previsto) e identidade de série.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL: o scaffold não exprime o <c>CHECK</c> do estado nem
/// o <b>índice único parcial</b> <c>(series_id, occurred_on) WHERE series_id IS
/// NOT NULL</c> — que é o que impede a geração duplicar uma parcela quando ela
/// roda duas vezes.
/// </para>
/// <para>
/// <b>A coluna <c>status</c> nasce com <c>DEFAULT 1</c> (Realizado)</b>, e não
/// precisa de <c>UPDATE</c>: todo lançamento que existia até aqui é dinheiro que
/// já se moveu.
/// </para>
/// </remarks>
public partial class LancamentosPeriodicos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.170_lancamentos_periodicos.sql"));
    }

    /// <summary>
    /// Remove estado e série.
    /// </summary>
    /// <remarks>
    /// <b>Lossy, e de um jeito perigoso:</b> as parcelas <c>Previsto</c> deixam
    /// de ser distinguíveis das realizadas e passam a somar no fechamento — o
    /// caixa de meses futuros começa a contar dinheiro que ninguém pagou.
    /// Por isso elas são <b>apagadas</b> aqui, e não convertidas: um relatório
    /// errado é pior do que um lançamento a menos, e a série pode ser recriada.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM giving_entries WHERE status = 2;

            DROP INDEX IF EXISTS uq_giving_entries_serie_data;
            DROP INDEX IF EXISTS ix_giving_entries_tenant_status;
            DROP INDEX IF EXISTS ix_giving_entries_serie;

            ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_status;
            ALTER TABLE giving_entries DROP COLUMN status;
            ALTER TABLE giving_entries DROP COLUMN series_id;
            """);
    }
}
