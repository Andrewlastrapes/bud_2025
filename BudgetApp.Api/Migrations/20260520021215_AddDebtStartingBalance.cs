using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtStartingBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_user_id",
                table: "Transactions");

            migrationBuilder.AddColumn<decimal>(
                name: "debt_starting_balance",
                table: "Users",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "debt_starting_balance",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_user_id",
                table: "Transactions",
                column: "user_id");
        }
    }
}
