using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Cria as duas roles de banco que o ADR-006 já documentava, mas que nunca
/// existiram de fato: a API rodava com a mesma credencial que criou as tabelas.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema que isso fecha.</b> No PostgreSQL, o dono de uma tabela
/// atravessa Row Level Security por padrão — <c>ENABLE ROW LEVEL SECURITY</c> não
/// vale para quem é dono. Como <c>schema.sql</c> sempre rodou com o usuário
/// <c>congrega</c>, que também é o usuário da string de conexão da API, todo o
/// RLS descrito no ADR-006 era decorativo: a única coisa impedindo vazamento
/// cross-tenant era o Global Query Filter do EF Core, o exato cenário de falha
/// único que o RLS existia para cobrir.
/// </para>
/// <para>
/// <c>congrega_app</c> não é dona de tabela nenhuma — só recebe GRANT — e por
/// isso o RLS passa a valer para ela de verdade. <c>congrega_worker</c> tem
/// <c>BYPASSRLS</c> para os processos que legitimamente cruzam tenant (retenção,
/// dispatcher do Outbox).
/// </para>
/// <para>
/// <b>Sem senha aqui de propósito.</b> Uma migration compilada entra no
/// histórico do Git; gravar segredo nela seria expô-lo para sempre, mesmo que a
/// senha depois rotacione. A senha é aplicada fora desta migration, por
/// <c>ALTER ROLE ... PASSWORD</c> a partir de uma variável de ambiente — ver
/// <c>db/010_bootstrap_roles.sql</c>. Até que isso rode, as roles existem mas não
/// autenticam, o que é o estado seguro por padrão.
/// </para>
/// <para>
/// <c>ALTER DEFAULT PRIVILEGES</c> cobre tabela e sequência <b>futuras</b> criadas
/// pelo dono — sem isso, toda migration nova precisaria lembrar de conceder
/// acesso à <c>congrega_app</c>, e esquecer travaria a API em produção com "permission
/// denied" na primeira tabela nova.
/// </para>
/// </remarks>
public partial class AppRoles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'congrega_app') THEN
                    CREATE ROLE congrega_app LOGIN;
                END IF;

                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'congrega_worker') THEN
                    CREATE ROLE congrega_worker LOGIN BYPASSRLS;
                END IF;
            END $$;

            GRANT CONNECT ON DATABASE congrega TO congrega_app, congrega_worker;
            GRANT USAGE ON SCHEMA public TO congrega_app, congrega_worker;

            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public
                TO congrega_app, congrega_worker;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public
                TO congrega_app, congrega_worker;
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public
                TO congrega_app, congrega_worker;

            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO congrega_app, congrega_worker;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO congrega_app, congrega_worker;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT EXECUTE ON FUNCTIONS TO congrega_app, congrega_worker;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // DROP ROLE falharia com objetos ainda dependentes das concessões (GRANTs
        // ficam presos ao papel). Reverter aqui não tem uso real — se as roles
        // precisarem sumir, é uma decisão operacional, não um rollback de schema.
        throw new NotSupportedException(
            "Remover congrega_app/congrega_worker é operação de infraestrutura, não de "
            + "migration. Revogue os GRANTs e rode DROP ROLE manualmente se for necessário.");
    }
}
