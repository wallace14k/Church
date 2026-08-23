using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Lançamento ganha tipo próprio, título, conta, recorrência e cor de categoria.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime nenhuma das coisas que
/// importam aqui: a policy de RLS de <c>financial_accounts</c>, os <c>CHECK</c>
/// de formato de cor e de coerência da recorrência, o índice único por
/// <c>lower(name)</c>, a semeadura das categorias — e sobretudo a
/// <b>transferência do sinal</b>.
/// </para>
/// <para>
/// <b>A ordem da transferência é o ponto frágil.</b> <c>giving_entries.kind</c>
/// entra anulável, é preenchido a partir de <c>giving_categories.kind</c> — que
/// hoje é a autoridade — e só então vira <c>NOT NULL</c>. Na ordem inversa, a
/// constraint recusaria toda linha existente e a migration falharia no meio,
/// deixando o banco com a coluna criada e vazia.
/// </para>
/// </remarks>
public partial class LancamentoDetalhado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.160_lancamento_detalhado.sql"));
    }

    /// <summary>
    /// Devolve o sinal para a categoria.
    /// </summary>
    /// <remarks>
    /// <b>Lossy</b>, e quem executar precisa saber de três perdas:
    /// <list type="bullet">
    /// <item>Títulos, contas, recorrência e cores somem — não há para onde voltar.</item>
    /// <item>
    /// Um lançamento feito numa categoria <c>Ambos</c> perde o sinal: a
    /// categoria vira <c>Entrada</c> pelo <c>CHECK</c> antigo, e a saída lançada
    /// nela passa a somar como entrada no fechamento.
    /// </item>
    /// <item>
    /// Formas de pagamento novas (crédito, débito, boleto) viram <c>Outro</c>,
    /// porque o <c>CHECK</c> antigo para em 6.
    /// </item>
    /// </list>
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Métodos novos viram "Outro" (6) antes de o CHECK antigo voltar.
            UPDATE giving_entries SET method = 6 WHERE method > 6;

            ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_method;
            ALTER TABLE giving_entries
                ADD CONSTRAINT ck_giving_entries_method CHECK (method BETWEEN 1 AND 6);

            ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_notes;
            ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_recorrencia;
            ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_kind;
            ALTER TABLE giving_entries DROP CONSTRAINT fk_giving_entries_account;

            DROP INDEX IF EXISTS ix_giving_entries_tenant_kind;
            DROP INDEX IF EXISTS ix_giving_entries_account;

            ALTER TABLE giving_entries DROP COLUMN kind;
            ALTER TABLE giving_entries DROP COLUMN description;
            ALTER TABLE giving_entries DROP COLUMN account_id;
            ALTER TABLE giving_entries DROP COLUMN is_recurring;
            ALTER TABLE giving_entries DROP COLUMN recurrence;

            DROP TABLE financial_accounts;

            -- Categorias "Ambos" (3) precisam caber no CHECK antigo. Viram
            -- Entrada, e os lançamentos de saída feitos nelas passam a somar
            -- como entrada — a perda está documentada acima.
            UPDATE giving_categories SET kind = 1 WHERE kind = 3;

            ALTER TABLE giving_categories DROP CONSTRAINT ck_giving_categories_kind;
            ALTER TABLE giving_categories
                ADD CONSTRAINT ck_giving_categories_kind CHECK (kind BETWEEN 1 AND 2);

            ALTER TABLE giving_categories DROP CONSTRAINT ck_giving_categories_cor;
            ALTER TABLE giving_categories DROP COLUMN color_hex;
            """);
    }
}
