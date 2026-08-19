using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cria <c>ix_pay_user</c>, que sustenta o histórico de pagamentos do assinante
/// Congrega+.
/// </summary>
/// <remarks>
/// <para>
/// O corpo gerado pela ferramenta veio <b>vazio</b>, e desta vez sem nada de
/// errado nele: o índice é <b>parcial</b> (<c>WHERE user_id IS NOT NULL</c>), e
/// índice parcial não é exprimível pelo modelo do EF Core — ele simplesmente
/// não existe no snapshot para ser comparado. Vazio aqui significa "a
/// ferramenta não tem como saber", não "não há o que fazer".
/// </para>
/// <para>
/// Sem este índice, <c>WHERE user_id = @eu ORDER BY created_at DESC LIMIT n</c>
/// varre a tabela inteira de pagamentos da plataforma a cada abertura da aba de
/// assinatura. O custo cresce com o número de clientes, não com o tamanho do
/// histórico de quem está olhando — que é o pior formato de degradação, porque
/// não aparece em desenvolvimento e piora justamente quando o produto dá certo.
/// </para>
/// </remarks>
public partial class IndicePagamentosPorTitular : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.090_indice_pagamentos.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reversível de verdade, diferente do seed de planos: um índice não
        // guarda dado, então derrubá-lo devolve o banco ao estado anterior sem
        // perder nada — só volta a consulta lenta.
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_pay_user;");
    }
}
