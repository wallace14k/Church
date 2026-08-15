using System.Security.Cryptography;
using System.Text;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Congrega.Infrastructure.Locking;

/// <summary>Strings de conexão. Ver a distinção crítica entre as duas em <see cref="PostgresAdvisoryLock"/>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Conexão através do pooler (Supavisor, modo transaction). Uso geral da aplicação.</summary>
    public required string PooledConnectionString { get; init; }

    /// <summary>
    /// Conexão <b>direta</b> ao Postgres, sem pooler. Usada exclusivamente por
    /// advisory locks de sessão.
    /// </summary>
    public required string DirectConnectionString { get; init; }
}

/// <summary>
/// Lock distribuído baseado em advisory lock de sessão do PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que uma conexão dedicada e direta.</b> <c>pg_advisory_lock</c> em modo de
/// sessão é vinculado à sessão Postgres, não à transação. Sob o Supavisor em
/// <i>transaction pooling</i> — o modo padrão do Supabase — a sessão volta ao pool
/// ao fim de cada transação e pode ser entregue a outro cliente, que herdaria o lock
/// ou o liberaria sem saber. O resultado é um lock que aparenta funcionar em
/// desenvolvimento e falha silenciosamente em produção, permitindo execução
/// concorrente exatamente do job que ele deveria serializar.
/// </para>
/// <para>
/// A conexão direta desta classe é aberta na aquisição e mantida aberta enquanto o
/// lock existir. É uma conexão por réplica, mantida por poucos segundos a cada ciclo
/// — custo desprezível diante da garantia.
/// </para>
/// <para>
/// Alternativa considerada e descartada: <c>pg_try_advisory_xact_lock</c>, que é
/// transacional e funcionaria sob o pooler. Ela obrigaria o ciclo inteiro a rodar
/// dentro de uma única transação longa, o que segura tuplas mortas e atrapalha o
/// autovacuum em uma tabela que recebe escrita constante. Ver ADR-021.
/// </para>
/// <para>
/// <b>Este lock não é fonte de correção.</b> Ele evita trabalho duplicado. A garantia
/// de não duplicar alerta vem da constraint <c>UNIQUE (dedupe_key)</c>.
/// </para>
/// </remarks>
public sealed class PostgresAdvisoryLock(
    IOptions<DatabaseOptions> options,
    ILogger<PostgresAdvisoryLock> logger) : IDistributedLock
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockKey, CancellationToken cancellationToken)
    {
        long key = DeriveLockKey(lockKey);
        var connection = new NpgsqlConnection(_options.DirectConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
            command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = key });

            // try_ e não pg_advisory_lock: a variante bloqueante esperaria
            // indefinidamente e faria as réplicas enfileirarem ciclos, disparando
            // uma rajada de execuções assim que o lock fosse liberado.
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            bool acquired = result is true;

            if (!acquired)
            {
                logger.LogDebug("Lock {LockKey} já detido por outra réplica. Ciclo ignorado.", lockKey);
                await connection.DisposeAsync();
                return null;
            }

            logger.LogDebug("Lock {LockKey} adquirido (chave numérica {Key}).", lockKey, key);
            return new AdvisoryLockHandle(connection, key, lockKey, logger);
        }
        catch
        {
            // Sem isso, uma falha entre abrir a conexão e devolver o handle vazaria
            // a conexão — e, se o lock já tiver sido adquirido, o vazamento impediria
            // qualquer réplica de rodar até o timeout do servidor.
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Converte a chave textual em <c>bigint</c> de forma determinística.
    /// </summary>
    /// <remarks>
    /// SHA-256 truncado em 8 bytes. Determinístico entre processos e versões — o que
    /// <c>string.GetHashCode()</c> não é: ele é randomizado por processo no .NET, e
    /// usá-lo aqui faria cada réplica travar uma chave diferente, anulando o lock.
    /// </remarks>
    private static long DeriveLockKey(string lockKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(lockKey), hash);
        return BitConverter.ToInt64(hash[..8]);
    }

    private sealed class AdvisoryLockHandle(
        NpgsqlConnection connection,
        long key,
        string lockKey,
        ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection);
                command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                await command.ExecuteScalarAsync(CancellationToken.None);

                logger.LogDebug("Lock {LockKey} liberado.", lockKey);
            }
            catch (Exception ex)
            {
                // Falhar aqui não é fatal: fechar a conexão libera o lock de sessão
                // do lado do servidor de qualquer forma. Registrar e seguir é
                // correto — lançar no Dispose mascararia a exceção original do ciclo.
                logger.LogWarning(ex, "Falha ao liberar o lock {LockKey}. Será liberado ao fechar a conexão.", lockKey);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
