using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// O cofre da igreja: extrato de guarda, fora do fechamento.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime o
/// <c>CHECK (balance_after_cents &gt;= 0)</c> — que é o que impede o cofre de
/// ficar negativo sem depender de um <c>if</c> com janela de concorrência — nem
/// a concessão da permissão <c>giving.vault</c> ao papel de tesoureiro.
/// </para>
/// <para>
/// <b>Movimento de cofre não é lançamento</b>, e por isso a tabela é nova em vez
/// de uma coluna em <c>giving_entries</c>: guardar R$ 5.000 no cofre não é
/// despesa, e se fosse gravado como uma, o fechamento do mês passaria a mostrar
/// um prejuízo que não aconteceu.
/// </para>
/// </remarks>
public partial class CofreDaIgreja : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.190_cofre.sql"));
    }

    /// <summary>
    /// Remove o cofre.
    /// </summary>
    /// <remarks>
    /// Perde o extrato de guarda — o registro de quanto dinheiro em espécie
    /// entrou e saiu do cofre, e de quem o movimentou. A permissão também é
    /// removida, senão ficaria concedida apontando para uma tela que não existe
    /// mais.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS vault_movements;

            DELETE FROM role_permissions
            WHERE permission_id IN (SELECT id FROM permissions WHERE code = 'giving.vault');

            DELETE FROM permissions WHERE code = 'giving.vault';
            """);
    }
}
