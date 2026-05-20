using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrenceFrequencyAndOriginalDueDayToFixedCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Add recurrence_frequency (default 'Monthly') ───────────────────
            // defaultValue ensures existing rows immediately get 'Monthly' rather than
            // an empty string.  EF scaffolded "" which would be wrong.
            migrationBuilder.AddColumn<string>(
                name: "recurrence_frequency",
                table: "FixedCosts",
                type: "text",
                nullable: false,
                defaultValue: "Monthly");

            // ── 2. Add original_due_day_of_month (nullable) ───────────────────────
            migrationBuilder.AddColumn<int>(
                name: "original_due_day_of_month",
                table: "FixedCosts",
                type: "integer",
                nullable: true);

            // ── 3. Backfill original_due_day_of_month for existing rows ───────────
            // For every row that already has a next_due_date we capture the day-of-month
            // now, while it still reflects the user's intended calendar day.
            // Rows without a next_due_date remain NULL — FixedCostAdvancer falls back
            // to NextDueDate.Day at advancement time, which is the correct safe default.
            //
            // Example: Phone due 2026-05-18 → original_due_day_of_month = 18
            //          Jan 31 bill        → original_due_day_of_month = 31
            //          No due date        → remains NULL
            migrationBuilder.Sql(
                @"UPDATE ""FixedCosts""
                  SET original_due_day_of_month = EXTRACT(DAY FROM next_due_date)::integer
                  WHERE next_due_date IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_due_day_of_month",
                table: "FixedCosts");

            migrationBuilder.DropColumn(
                name: "recurrence_frequency",
                table: "FixedCosts");
        }
    }
}
