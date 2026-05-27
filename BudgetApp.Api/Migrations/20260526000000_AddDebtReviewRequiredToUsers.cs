using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtReviewRequiredToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "debt_review_required",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "debt_review_required",
                table: "Users");
        }
    }
}
