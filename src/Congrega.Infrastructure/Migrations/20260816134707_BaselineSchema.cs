using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Migration de baseline: cria o schema inteiro do Congrega.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que executar DDL em vez de usar o corpo gerado.</b> O modelo do EF Core
/// mapeia 11 entidades; o schema tem 26 tabelas. As demais — <c>payments</c>,
/// <c>entitlements</c>, <c>notification_queue</c>, <c>security_events</c>,
/// <c>audit_log</c>, o catálogo de conteúdo — são acessadas por SQL direto. O
/// <c>Up</c> gerado pela ferramenta criava menos da metade do banco, e com a
/// aparência de estar completo.
/// </para>
/// <para>
/// Além disso, quase tudo que sustenta a segurança deste schema é inexprimível no
/// modelo: as policies de Row Level Security, os índices parciais com predicado,
/// os índices de trigrama, <c>UNIQUE NULLS NOT DISTINCT</c>, a função imutável
/// <c>congrega_unaccent</c> e os <c>CHECK</c> que impedem estados impossíveis.
/// Gerar do modelo perderia o RLS — ou seja, perderia o isolamento entre igrejas.
/// O ADR-008 já previa <c>migrationBuilder.Sql()</c> para esses objetos.
/// </para>
/// <para>
/// O snapshot ao lado continua descrevendo as 11 entidades mapeadas, e é contra
/// ele que as próximas migrations serão diferenciadas. Mudança em tabela não
/// mapeada continua entrando como <c>Sql()</c> escrito à mão.
/// </para>
/// <para>
/// <b>Banco que já existe.</b> As bases de desenvolvimento foram criadas pelo
/// <c>docker-entrypoint-initdb.d</c> e já têm o schema. Registre o baseline como
/// aplicado sem executá-lo — <c>db/005_baseline_aplicado.sql</c> faz isso.
/// </para>
/// </remarks>
public partial class BaselineSchema : Migration
{
    /// <summary>
    /// Ordem obrigatória: os scripts são incrementais, não alternativos.
    /// </summary>
    /// <remarks>
    /// <c>020_members</c> tem chave estrangeira para <c>tenants</c>, criada em
    /// <c>010_schema</c>; <c>030_busca</c> indexa colunas de <c>members</c>. Fora
    /// de ordem, cada um falha na dependência do anterior.
    /// </remarks>
    private static readonly string[] Scripts =
    [
        "Congrega.Db.010_schema.sql",
        "Congrega.Db.020_members.sql",
        "Congrega.Db.030_busca.sql",
        "Congrega.Db.040_outbox.sql",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var script in Scripts)
        {
            // Sem `suppressTransaction`: o PostgreSQL faz DDL transacional, e um
            // script que falhe no meio não deve deixar meio schema de pé.
            migrationBuilder.Sql(SqlEmbutido.Ler(script));
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverter o baseline é derrubar o banco inteiro. Não há cenário em que
        // isso seja a ação certa em produção, e oferecer o caminho só cria a
        // chance de alguém percorrê-lo por engano.
        //
        // Para recomeçar em desenvolvimento: `docker compose down -v`.
        throw new NotSupportedException(
            "O baseline não pode ser revertido: derrubaria o schema inteiro. "
            + "Para recriar o banco de desenvolvimento, use 'docker compose down -v'.");
    }
}
