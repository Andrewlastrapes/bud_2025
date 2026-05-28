using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingTransactionIdToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adds the nullable pending_transaction_id column to Transactions.
            // This stores Plaid's pending_transaction_id field from posted transactions —
            // the PlaidTransactionId of the prior pending row that this posted row replaces.
            //
            // Used for delta reconciliation: when a posted transaction arrives in the Added
            // list without the corresponding pending appearing in the Removed list, the sync
            // loop can look up the prior pending by this value and apply only the balance
            // delta rather than double-subtracting the full posted amount.
            //
            // All existing rows correctly receive NULL (no migration default needed);
            // the column is optional on new rows too.
            migrationBuilder.AddColumn<string>(
                name: "pending_transaction_id",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_transaction_id",
                table: "Transactions");
        }
    }
}
