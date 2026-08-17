using System.Text.Json;
using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Congrega.Domain.Calendar;
using Congrega.Domain.Common;
using Congrega.Domain.Congregation;
using Congrega.Domain.Giving;
using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do Congrega.
/// </summary>
/// <remarks>
/// <para>
/// Implementa <see cref="IUnitOfWork"/>: <see cref="SaveChangesAsync"/> drena os
/// eventos de domínio para o Outbox <b>dentro da mesma transação</b> da mudança de
/// estado. É isso que fecha a janela entre "commit no banco" e "publicar mensagem" —
/// não existe estado persistido cujo evento tenha se perdido, nem evento publicado
/// referente a um commit que foi revertido.
/// </para>
/// <para>
/// O contexto é <c>internal</c> por design: a camada de aplicação conversa com
/// repositórios de intenção de domínio, nunca com <c>DbSet</c>.
/// </para>
/// </remarks>
public sealed class CongregaDbContext(
    DbContextOptions<CongregaDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : DbContext(options), IUnitOfWork
{
    internal DbSet<User> Users => Set<User>();
    internal DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    internal DbSet<Tenant> Tenants => Set<Tenant>();
    internal DbSet<Membership> Memberships => Set<Membership>();
    internal DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    internal DbSet<Role> Roles => Set<Role>();
    internal DbSet<Permission> Permissions => Set<Permission>();
    internal DbSet<Subscription> Subscriptions => Set<Subscription>();
    internal DbSet<Payment> Payments => Set<Payment>();
    internal DbSet<Entitlement> Entitlements => Set<Entitlement>();
    internal DbSet<Member> Members => Set<Member>();
    internal DbSet<Family> Families => Set<Family>();
    internal DbSet<GivingCategory> GivingCategories => Set<GivingCategory>();
    internal DbSet<GivingEntry> GivingEntries => Set<GivingEntry>();
    internal DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Normalização de texto para busca, mapeada para a função <c>congrega_unaccent</c>
    /// do PostgreSQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe para que a consulta use <b>exatamente</b> a mesma expressão do índice
    /// de trigramas <c>ix_members_busca</c>. Chamar <c>unaccent()</c> direto geraria
    /// uma expressão diferente e o índice — que custa escrita em toda inserção —
    /// nunca seria usado.
    /// </para>
    /// <para>
    /// O corpo lança porque o método nunca executa em .NET: o EF Core o traduz para
    /// SQL. Chamá-lo fora de uma consulta é erro de programação, e falhar alto é
    /// melhor que devolver um resultado que não corresponde ao do banco.
    /// </para>
    /// </remarks>
    public static string Unaccent(string texto) =>
        throw new NotSupportedException(
            "Congrega.Unaccent só pode ser usada dentro de uma consulta LINQ traduzida para SQL.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CongregaDbContext).Assembly);

        modelBuilder
            .HasDbFunction(typeof(CongregaDbContext).GetMethod(nameof(Unaccent), [typeof(string)])!)
            .HasName("congrega_unaccent");

        ConfigureGlobalQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Filtros globais de tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aplicados nas entidades que carregam <c>TenantId</c>. A expressão é avaliada a
    /// cada query com o valor corrente de <see cref="ITenantContext"/> — por isso o
    /// contexto precisa ser <c>scoped</c> junto com o <c>DbContext</c>.
    /// </para>
    /// <para>
    /// <b>Este é o controle que falha em silêncio.</b> Uma entidade nova cadastrada
    /// sem filtro, um <c>IgnoreQueryFilters()</c> deixado em um relatório, um
    /// <c>FromSql</c> — qualquer um deles vaza sem emitir aviso. É exatamente por
    /// isso que existe RLS por trás: com ele, o mesmo descuido devolve conjunto
    /// vazio em vez de dados de outra igreja. Ver ADR-006.
    /// </para>
    /// </remarks>
    private void ConfigureGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Membership>()
            .HasQueryFilter(m =>
                tenantContext.IsCrossTenantOperation ||
                tenantContext.TenantId == null ||
                m.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Member>()
            .HasQueryFilter(m =>
                tenantContext.IsCrossTenantOperation ||
                m.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Family>()
            .HasQueryFilter(f =>
                tenantContext.IsCrossTenantOperation ||
                f.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<GivingCategory>()
            .HasQueryFilter(c =>
                tenantContext.IsCrossTenantOperation ||
                c.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<GivingEntry>()
            .HasQueryFilter(e =>
                tenantContext.IsCrossTenantOperation ||
                e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<CalendarEvent>()
            .HasQueryFilter(e =>
                tenantContext.IsCrossTenantOperation ||
                e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Subscription>()
            .HasQueryFilter(s =>
                tenantContext.IsCrossTenantOperation ||
                tenantContext.TenantId == null ||
                s.TenantId == tenantContext.TenantId ||
                s.UserId == tenantContext.UserId);

        // Mesma forma da assinatura, e pela mesma razão: o pagamento pode ser da
        // igreja (B2B) ou da pessoa (B2C, sem tenant nenhum). Filtrar só por
        // tenant esconderia do assinante Congrega+ o próprio pagamento — e ele
        // é cidadão de primeira classe do produto, não caso degenerado.
        modelBuilder.Entity<Payment>()
            .HasQueryFilter(p =>
                tenantContext.IsCrossTenantOperation ||
                tenantContext.TenantId == null ||
                p.TenantId == tenantContext.TenantId ||
                p.UserId == tenantContext.UserId);

        // Entitlement é do USUÁRIO, nunca da igreja: o direito de acesso viaja
        // com a pessoa mesmo que ela troque de igreja ou saia de todas.
        modelBuilder.Entity<Entitlement>()
            .HasQueryFilter(e =>
                tenantContext.IsCrossTenantOperation ||
                tenantContext.UserId == null ||
                e.UserId == tenantContext.UserId);
    }

    /// <summary>
    /// Persiste e publica, atomicamente.
    /// </summary>
    /// <remarks>
    /// Traduz violação de unicidade do provedor para
    /// <see cref="UniqueConstraintViolationException"/>. Sem isso, a única forma
    /// de a borda responder "esse nome já existe" seria referenciar EF Core e
    /// Npgsql no projeto de API — detalhe de persistência atravessando duas
    /// camadas para chegar a um <c>catch</c>.
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEventsToOutbox();

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            // 23505 = unique_violation. O código é estável; a mensagem muda com
            // o idioma configurado no servidor.
            SqlState: "23505"
        } pg)
        {
            throw new UniqueConstraintViolationException(pg.ConstraintName ?? "desconhecida", ex);
        }
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Converte eventos de domínio acumulados nos agregados em linhas de Outbox.
    /// </summary>
    /// <remarks>
    /// Roda antes do <c>SaveChanges</c> da base, então as linhas de Outbox entram na
    /// mesma transação. O agregado não conhece o Outbox — ele apenas registra o que
    /// aconteceu, e a infraestrutura decide o que fazer com isso.
    /// </remarks>
    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        string? correlationId = System.Diagnostics.Activity.Current?.TraceId.ToString();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    MessageType = domainEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    OccurredAt = now,
                    NextAttemptAt = now,
                    CorrelationId = correlationId
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}

/// <summary>Linha da tabela <c>outbox_messages</c>.</summary>
public sealed class OutboxMessage
{
    public long Id { get; init; }
    public required string MessageType { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public short Attempts { get; set; }
    public required DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; init; }
}
