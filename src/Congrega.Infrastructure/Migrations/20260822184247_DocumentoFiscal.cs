using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Documento fiscal da despesa e o comprovante anexado.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL, como nas anteriores — e aqui o motivo é mais forte do
/// que de costume. O scaffold não exprime <b>a FK composta</b>
/// <c>(entry_id, entry_kind) → giving_entries (id, kind)</c> que, junto com
/// <c>CHECK (entry_kind = 2)</c>, é o que declara "documento fiscal só existe
/// para saída" no banco em vez de num <c>if</c> da aplicação.
/// </para>
/// <para>
/// Também não exprime <c>CHECK (octet_length(content) = size_bytes)</c>, que é o
/// que impede um cliente de declarar 1 byte e gravar 500 MB — sem ele, o teto de
/// 10 MB estaria protegendo um número em vez do arquivo.
/// </para>
/// </remarks>
public partial class DocumentoFiscal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.180_documento_fiscal.sql"));
    }

    /// <summary>
    /// Remove documentos e anexos.
    /// </summary>
    /// <remarks>
    /// <b>Perde os comprovantes, e não há como não perder:</b> os bytes só
    /// existem aqui. Quem descer esta migration fica sem a prova das despesas
    /// que já haviam sido registradas — a ordem das quedas respeita as FKs para
    /// que a queda em si não falhe pela metade.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS giving_document_files;
            DROP TABLE IF EXISTS giving_documents;

            ALTER TABLE giving_entries DROP CONSTRAINT IF EXISTS uq_giving_entries_id_kind;
            """);
    }
}
