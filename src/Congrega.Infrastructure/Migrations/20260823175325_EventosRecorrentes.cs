using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Séries semanais na agenda — "todo domingo tem culto às 19h".
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime o <b>índice único parcial</b>
/// <c>(series_id, starts_at) WHERE series_id IS NOT NULL</c>, que é o que impede
/// a geração criar dois cultos no mesmo domingo quando ela roda duas vezes —
/// clique duplo, retry de rede.
/// </para>
/// <para>
/// A série é gravada como <b>linhas de verdade</b>, e não como uma regra de
/// repetição expandida na leitura: a igreja precisa mexer numa ocorrência
/// isolada — o culto do dia 25 será no salão, o de 1º de novembro está
/// cancelado — e com linhas isso é editar uma linha, não manter uma tabela de
/// exceções ao lado da regra.
/// </para>
/// </remarks>
public partial class EventosRecorrentes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.220_eventos_recorrentes.sql"));
    }

    /// <summary>
    /// Remove a identidade de série.
    /// </summary>
    /// <remarks>
    /// <b>Os eventos permanecem</b>, e é a decisão certa: eles são compromissos
    /// reais da igreja, marcados na agenda de todo mundo. O que se perde é a
    /// ligação entre eles — cancelar "todos os domingos" volta a ser cinquenta e
    /// duas exclusões, uma a uma.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS uq_events_serie_inicio;
            DROP INDEX IF EXISTS ix_events_serie;

            ALTER TABLE events DROP COLUMN IF EXISTS series_id;
            """);
    }
}
