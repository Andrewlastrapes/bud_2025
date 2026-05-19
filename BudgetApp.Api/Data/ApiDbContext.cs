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
    }
}