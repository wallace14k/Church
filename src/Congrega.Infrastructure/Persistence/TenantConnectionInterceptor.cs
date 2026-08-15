using System.Data.Common;
using Congrega.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Propaga o contexto de segurança da aplicação para o PostgreSQL, alimentando as
/// policies de Row Level Security.
/// </summary>
/// <remarks>
/// <para>
/// As policies leem <c>current_setting('app.tenant_id', true)</c>. Este interceptor é
/// o que coloca esse valor lá, uma vez por abertura de conexão.
/// </para>
/// <para>
/// <b>Refinamento em relação ao ADR-006.</b> O ADR previa <c>SET LOCAL</c> por
/// transação, escolhido para ser seguro sob o pooler do Supabase em <i>transaction
/// mode</i>. Ao implementar, esbarrei no problema real: <c>SET LOCAL</c> só vale
/// dentro de uma transação, e leituras em autocommit — a maioria das queries de uma
/// API — ficariam sem o contexto, fazendo o RLS negar tudo. Forçar toda leitura a
/// abrir transação explícita seria caro e fácil de esquecer.
/// </para>
/// <para>
/// A solução adotada é conectar direto ao Postgres (porta 5432) e usar GUC de
/// <b>sessão</b>, apoiada em um detalhe do Npgsql: ao devolver a conexão ao pool ele
/// emite <c>DISCARD ALL</c> por padrão, o que limpa os GUCs. O vazamento entre
/// requisições — a razão de ser do <c>SET LOCAL</c> — não acontece.
/// </para>
/// <para>
/// <b>Se algum dia a API passar a usar o Supavisor em transaction mode, este
/// interceptor precisa voltar para <c>SET LOCAL</c> dentro de transação explícita.</b>
/// O <c>DISCARD ALL</c> do Npgsql não protege quando o multiplexador de conexões é
/// externo ao processo. Deixar isso registrado importa mais do que a escolha em si:
/// é uma armadilha silenciosa, que não quebra nenhum teste e vaza dados entre
/// tenants em produção.
/// </para>
/// </remarks>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    // set_config com is_local = false: vale para a sessão, e o DISCARD ALL do Npgsql
    // limpa ao devolver a conexão ao pool. Parâmetros posicionais, nunca concatenação
    // de string — GUC recebendo valor interpolado seria injeção de SQL.
    private const string ApplyContextSql = """
        SELECT set_config('app.tenant_id', $1, false),
               set_config('app.user_id',   $2, false);
        """;

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not NpgsqlConnection npgsqlConnection)
        {
            return;
        }

        // Operação cross-tenant (workers) roda com a role congrega_worker, que tem
        // BYPASSRLS. Definir o contexto seria inócuo, e definir um tenant errado
        // seria pior que não definir nenhum.
        if (tenantContext.IsCrossTenantOperation)
        {
            return;
        }

        await using var command = new NpgsqlCommand(ApplyContextSql, npgsqlConnection);

        // String vazia — e não NULL — porque as policies usam
        // NULLIF(current_setting(...), '')::bigint. Vazio vira NULL na comparação, e
        // "coluna = NULL" é falso: o comportamento é fail closed. Um contexto ausente
        // nega acesso em vez de liberar tudo.
        command.Parameters.Add(new NpgsqlParameter<string>
        {
            TypedValue = tenantContext.TenantId?.ToString() ?? string.Empty
        });
        command.Parameters.Add(new NpgsqlParameter<string>
        {
            TypedValue = tenantContext.UserId?.ToString() ?? string.Empty
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
