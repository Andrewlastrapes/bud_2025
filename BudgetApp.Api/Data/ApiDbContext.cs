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



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // This stops EF Core from trying to re-create the tables
        // with lowercase names by default, which is an Npgsql behavior.
        // It tells EF to respect the names we defined in the [Table] attributes.
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<PlaidItem>().ToTable("PlaidItems");
        modelBuilder.Entity<Balance>().ToTable("Balances");
        modelBuilder.Entity<Transaction>().ToTable("Transactions");
        modelBuilder.Entity<FixedCost>().ToTable("FixedCosts");

    }
}
