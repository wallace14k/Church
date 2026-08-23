using Congrega.Domain.Congregation;
using Congrega.Domain.Giving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class VaultMovementConfiguration : IEntityTypeConfiguration<VaultMovement>
{
    public void Configure(EntityTypeBuilder<VaultMovement> builder)
    {
        builder.ToTable("vault_movements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(m => m.PublicId).HasColumnName("public_id");
        builder.Property(m => m.TenantId).HasColumnName("tenant_id");
        builder.Property(m => m.SequenceNumber).HasColumnName("sequence_number");
        builder.Property(m => m.Direction).HasColumnName("direction").HasConversion<short>();
        builder.Property(m => m.AmountCents).HasColumnName("amount_cents");
        builder.Property(m => m.BalanceAfterCents).HasColumnName("balance_after_cents");
        builder.Property(m => m.AccountId).HasColumnName("account_id");
        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at");
        builder.Property(m => m.Notes).HasColumnName("notes")
            .HasMaxLength(VaultMovement.MaxNotesLength);
        builder.Property(m => m.PerformedByUserId).HasColumnName("performed_by_user_id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(m => m.PublicId).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.SequenceNumber }).IsUnique();

        builder.Ignore(m => m.DomainEvents);
    }
}

internal sealed class VaultRepository(CongregaDbContext db) : IVaultRepository
{
    /// <summary>
    /// Saldo e sequência, do <b>último movimento</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O saldo já está gravado na linha. Somar depósitos e retiradas a cada
    /// consulta daria o mesmo número hoje e ficaria mais lento a cada movimento
    /// — e é a consulta que roda antes de <i>toda</i> operação de cofre.
    /// </para>
    /// <para>
    /// A contagem é separada porque é para exibição, não para a operação: ela
    /// varre a tabela, e o saldo não pode esperar por isso.
    /// </para>
    /// </remarks>
    public async Task<VaultState> GetStateAsync(CancellationToken cancellationToken)
    {
        var ultimo = await db.VaultMovements
            .AsNoTracking()
            .OrderByDescending(m => m.SequenceNumber)
            .Select(m => new { m.BalanceAfterCents, m.SequenceNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var quantidade = await db.VaultMovements.CountAsync(cancellationToken);

        return new VaultState
        {
            // Cofre que nunca foi usado tem saldo zero e sequência zero — o
            // primeiro movimento nasce com a sequência 1.
            BalanceCents = ultimo?.BalanceAfterCents ?? 0,
            LastSequence = ultimo?.SequenceNumber ?? 0,
            MovementCount = quantidade,
        };
    }

    public async Task<PagedResult<VaultMovementListItem>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var consulta = db.VaultMovements.AsNoTracking();

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            // Do mais recente para o mais antigo, como todo extrato: quem abre a
            // tela quer ver o que acabou de acontecer.
            .OrderByDescending(m => m.SequenceNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new VaultMovementListItem
            {
                PublicId = m.PublicId,
                SequenceNumber = m.SequenceNumber,
                Direction = m.Direction,
                AmountCents = m.AmountCents,
                BalanceAfterCents = m.BalanceAfterCents,
                OccurredAt = m.OccurredAt,
                AccountName = db.FinancialAccounts
                    .Where(c => c.Id == m.AccountId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),

                // `IgnoreQueryFilters` porque `users` não tem `tenant_id` — a
                // identidade é global neste sistema, e o filtro de tenant não se
                // aplica a ela. Sem isso a consulta nem compilaria.
                PerformedByName = db.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == m.PerformedByUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault(),

                Notes = m.Notes,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<VaultMovementListItem>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public void Add(VaultMovement movement) => db.VaultMovements.Add(movement);
}
