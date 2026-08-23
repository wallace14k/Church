using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Troca o enum <c>events.event_type</c> pela tabela <c>event_types</c>.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL, como nas anteriores, e aqui por três motivos que o
/// scaffold não cobre nenhum: <b>RLS</b> (a policy de isolamento não é
/// exprimível no modelo), o <b>índice único por <c>lower(name)</c></b> (índice
/// funcional idem), e sobretudo a <b>migração dos dados</b> — os eventos já
/// classificados precisam apontar para as linhas novas antes de a coluna antiga
/// cair, e a ferramenta emitiria um <c>DROP COLUMN</c> que apagaria a
/// classificação existente sem transferi-la.
/// </para>
/// <para>
/// O aviso "may result in the loss of data" do scaffold é exatamente sobre
/// isso, e é procedente: o script embutido faz o <c>UPDATE ... FROM</c> de
/// transferência antes do <c>DROP</c>.
/// </para>
/// </remarks>
public partial class TiposDeEventoPorIgreja : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.130_tipos_por_igreja.sql"));
    }

    /// <summary>
    /// Volta ao enum, preservando o que der.
    /// </summary>
    /// <remarks>
    /// A volta é <b>lossy</b> por natureza, e o comentário existe para que quem
    /// a executar saiba: um tipo criado pela igreja — "Vigília", "Célula" — não
    /// tem para onde voltar no enum de cinco valores, e vira <c>5</c> (Outro).
    /// Só os quatro nomes originais recuperam o valor que tinham.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE events ADD COLUMN event_type SMALLINT NOT NULL DEFAULT 5;

            UPDATE events e
            SET event_type = CASE et.name
                    WHEN 'Culto'   THEN 1
                    WHEN 'Reunião' THEN 2
                    WHEN 'Estudo'  THEN 3
                    WHEN 'Ensaio'  THEN 4
                    ELSE 5
                END
            FROM event_types et
            WHERE et.id = e.event_type_id;

            ALTER TABLE events ADD CONSTRAINT ck_events_tipo
                CHECK (event_type BETWEEN 1 AND 5);
            CREATE INDEX ix_events_tenant_tipo ON events (tenant_id, event_type);

            ALTER TABLE events DROP CONSTRAINT IF EXISTS fk_events_event_type;
            DROP INDEX IF EXISTS ix_events_tenant_tipo_id;
            ALTER TABLE events DROP COLUMN event_type_id;

            DROP TABLE event_types;
            """);
    }
}
