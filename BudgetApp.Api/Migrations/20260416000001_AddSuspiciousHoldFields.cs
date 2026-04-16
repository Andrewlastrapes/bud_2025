using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspiciousHoldFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_suspicious_hold",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "hold_reviewed",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "hold_override_amount",
                table: "Transactions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "budget_applied_amount",
                table: "Transactions",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_applied_amount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "hold_override_amount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "hold_reviewed",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "is_suspicious_hold",
                table: "Transactions");
        }
    }
}