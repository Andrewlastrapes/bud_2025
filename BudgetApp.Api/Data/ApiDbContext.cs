using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Api.Data;

public class ApiDbContext : DbContext
{
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<PlaidItem> PlaidItems { get; set; }
    public DbSet<Balance> Balances { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<FixedCost> FixedCosts { get; set; }
    public DbSet<UserDevice> UserDevices { get; set; } = null!;
    public DbSet<PaycheckSummary> PaycheckSummaries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Respect the [Table] attribute names and stop EF from trying to
        // auto-derive lowercase Npgsql table names.
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<PlaidItem>().ToTable("PlaidItems");
        modelBuilder.Entity<Balance>().ToTable("Balances");
        modelBuilder.Entity<Transaction>().ToTable("Transactions");
        modelBuilder.Entity<FixedCost>().ToTable("FixedCosts");
        modelBuilder.Entity<PaycheckSummary>().ToTable("PaycheckSummaries");

        // Unique index prevents duplicate summaries if multiple Plaid webhooks
        // arrive on the same paycheck day for the same user.
        modelBuilder.Entity<PaycheckSummary>()
            .HasIndex(s => new { s.UserId, s.PaycheckDate })
            .IsUnique();

        // Unique index prevents duplicate transaction rows for the same user + Plaid transaction.
        // Root cause: the application-level "check then insert" guard had a race condition when
        // two Plaid webhooks arrived simultaneously. The DB constraint is the authoritative guard.
        // Scoped to (UserId, PlaidTransactionId) rather than PlaidTransactionId alone for
        // architectural clarity, even though Plaid IDs are globally unique in practice.
        modelBuilder.Entity<Transaction>()
            .HasIndex(t => new { t.UserId, t.PlaidTransactionId })
            .IsUnique()
            .HasDatabaseName("IX_Transactions_UserId_PlaidTransactionId");

        // SQL DEFAULT values for the historical-backfill sentinel columns.
        // These must match the defaultValue parameters used in migration
        // 20260520200000_AddHistoricalBackfillFieldsToTransactions and the
        // HasDefaultValue entries in ApiDbContextModelSnapshot.cs.
        // Without these, EF's compiled model differs from the snapshot and
        // throws PendingModelChangesWarning at production startup.
        modelBuilder.Entity<Transaction>()
            .Property(t => t.IsHistoricalBackfill)
            .HasDefaultValue(false);

        modelBuilder.Entity<Transaction>()
            .Property(t => t.BudgetImpactEligible)
            .HasDefaultValue(true);
    }
}