using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixPaycheckSummarySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaycheckSummaries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    paycheck_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    next_paycheck_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paycheck_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    prior_period_starting_budget = table.Column<decimal>(type: "numeric", nullable: true),
                    prior_period_spend = table.Column<decimal>(type: "numeric", nullable: false),
                    prior_period_remaining = table.Column<decimal>(type: "numeric", nullable: false),
                    was_under_budget = table.Column<bool>(type: "boolean", nullable: false),
                    leftover_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    over_budget_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    fixed_costs_until_next_paycheck = table.Column<decimal>(type: "numeric", nullable: false),
                    savings_contribution = table.Column<decimal>(type: "numeric", nullable: false),
                    debt_payment_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    recommended_debt_payment_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    new_dynamic_budget_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    user_decision = table.Column<string>(type: "text", nullable: true),
                    is_dismissed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaycheckSummaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_PaycheckSummaries_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaycheckSummaries_user_id_paycheck_date",
                table: "PaycheckSummaries",
                columns: new[] { "user_id", "paycheck_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaycheckSummaries");
        }
    }
}
