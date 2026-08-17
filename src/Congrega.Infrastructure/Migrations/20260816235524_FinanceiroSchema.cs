using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cria o schema do módulo financeiro: <c>giving_categories</c> e
/// <c>giving_entries</c>.
/// </summary>
/// <remarks>
/// <para>
/// Executa <c>db/006_financeiro.sql</c> em vez do corpo gerado, pela mesma razão
/// da <see cref="BaselineSchema"/>: quase tudo que sustenta a correção destas
/// duas tabelas é inexprimível no modelo do EF Core — as policies de Row Level
/// Security, o <c>CHECK (amount_cents &gt; 0)</c> que impede uma saída
/// representada como valor negativo, as chaves estrangeiras <c>RESTRICT</c> que
/// o ADR-015 exige para dado financeiro, o índice único funcional sobre
/// <c>lower(name)</c> e o índice parcial de <c>member_id</c>.
/// </para>
/// <para>
/// O corpo gerado criava as duas tabelas <b>sem</b> nada disso, e com aparência
/// de estar completo: sem RLS, um <c>IgnoreQueryFilters()</c> em qualquer
/// relatório passaria a devolver o caixa de outra igreja.
/// </para>
/// <para>
/// O script fica fora da lista da <c>BaselineSchema</c> de propósito: os bancos
/// existentes já registraram aquela migration como aplicada e nunca a
/// executariam de novo. Banco novo recebe o baseline e, em seguida, esta.
/// </para>
/// </remarks>
public partial class FinanceiroSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.060_financeiro.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ordem inversa da criação: giving_entries referencia giving_categories
        // com RESTRICT, e derrubar a categoria primeiro falharia.
        migrationBuilder.Sql("DROP TABLE IF EXISTS giving_entries;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS giving_categories;");
    }
}
