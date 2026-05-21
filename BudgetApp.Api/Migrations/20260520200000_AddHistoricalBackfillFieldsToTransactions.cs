using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalBackfillFieldsToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Marks rows that were imported specifically for recurring-pattern analysis.
            // These rows do NOT affect live budget calculations, balance, deposit review,
            // large-expense review, or push notifications.
            // Default false so all existing rows are treated as normal synced transactions.
            migrationBuilder.AddColumn<bool>(
                name: "is_historical_backfill",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Marks rows that are eligible for live budget impact (balance deltas,
            // deposit review, large-expense review, notifications).
            // Default true so all existing rows continue to behave as before.
            // Historical backfill rows are inserted with this set to false.
            migrationBuilder.AddColumn<bool>(
                name: "budget_impact_eligible",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_historical_backfill",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "budget_impact_eligible",
                table: "Transactions");
        }
    }
}
