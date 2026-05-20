using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCashOnboardingFieldsToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cash_applied_to_debt_at_onboarding",
                table: "Users",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_balance_at_onboarding",
                table: "Users",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_cushion_at_onboarding",
                table: "Users",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "net_debt_starting_balance",
                table: "Users",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_applied_to_debt_at_onboarding",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "cash_balance_at_onboarding",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "cash_cushion_at_onboarding",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "net_debt_starting_balance",
                table: "Users");
        }
    }
}
