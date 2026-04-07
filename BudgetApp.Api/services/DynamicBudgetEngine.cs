using System;
using System.Collections.Generic;
using BudgetApp.Api.Data;

namespace BudgetApp.Api.Services;


public record DepositContext
{
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
    public string? MerchantName { get; init; }
    public int? PayDay1 { get; init; }
    public int? PayDay2 { get; init; }
    public decimal? ExpectedPaycheckAmount { get; init; }
}

/// <summary>
/// Input to the pure budget calculation engine.
/// All DB-dependent values (bill sums, savings sums) are resolved before calling.
/// </summary>
public record BudgetCalculationRequest
{
    public decimal PaycheckAmount { get; init; }
    public DateTime Today { get; init; }
    public DateTime NextPaycheckDate { get; init; }

    /// <summary>Sum of fixed-cost bills due within [Today, NextPaycheckDate].</summary>
    public decimal TotalFixedBills { get; init; }

    /// <summary>Savings contribution per paycheck (always included, no date filter).</summary>
    public decimal SavingsContribution { get; init; }

    /// <summary>Per-paycheck debt payoff allocation.</summary>
    public decimal DebtPerPaycheck { get; init; }
}

/// <summary>
/// Result from the no-proration budget engine.
/// Formula: remainingToSpend = paycheckAmount - fixedBills - savings - debt
/// All monetary values are rounded to 2 decimal places.
/// </summary>
public record BudgetCalculationResult
{
    public decimal PaycheckAmount { get; init; }
    public decimal FixedCostsRemaining { get; init; }
    public decimal DebtPerPaycheck { get; init; }
    public decimal SavingsContribution { get; init; }
    public decimal RemainingToSpend { get; init; }

    /// <summary>Legacy alias — equals RemainingToSpend. Kept for API backwards compatibility.</summary>
    public decimal DynamicSpendableAmount => RemainingToSpend;

    public string Explanation { get; init; } = string.Empty;
}

public interface IDynamicBudgetEngine
{
    TransactionSuggestedKind ClassifyDeposit(DepositContext ctx);

    /// <summary>
    /// Returns true if an outflow is "large" relative to the user's expected paycheck.
    /// Threshold: >= 30% of expected paycheck.
    /// </summary>
    bool IsLargeExpense(decimal amount, decimal expectedPaycheckAmount);

    /// <summary>
    /// Determines the previous paycheck date given two monthly pay days and the next paycheck date.
    /// Handles month-boundary rollover and short-month clamping.
    /// </summary>
    DateTime CalculatePreviousPaycheckDate(int payDay1, int payDay2, DateTime nextPaycheckDate);

    /// <summary>
    /// Calculates how much the user can spend until the next paycheck.
    ///
    /// NO proration — the full paycheck minus obligations is available to spend,
    /// regardless of where we are in the pay cycle.
    ///
    /// Formula: remainingToSpend = paycheckAmount - fixedBills - savings - debt
    /// </summary>
    BudgetCalculationResult CalculateDynamicBudget(BudgetCalculationRequest request);
}

public class DynamicBudgetEngine : IDynamicBudgetEngine
{
    private const decimal PaycheckAmountTolerance = 0.15m;   // 15%
    private const decimal LargeExpenseThresholdRatio = 0.30m; // 30%

    // ─── Deposit Classification ───────────────────────────────────────────────

    public TransactionSuggestedKind ClassifyDeposit(DepositContext ctx)
    {
        if (ctx.Amount <= 0)
            return TransactionSuggestedKind.Unknown;

        if (ctx.ExpectedPaycheckAmount is null or <= 0)
        {
            if (!string.IsNullOrWhiteSpace(ctx.MerchantName) &&
                ctx.MerchantName.Contains("PAYROLL", StringComparison.OrdinalIgnoreCase))
            {
                return TransactionSuggestedKind.Paycheck;
            }
            return TransactionSuggestedKind.Windfall;
        }

        var expected = ctx.ExpectedPaycheckAmount.Value;
        var ratio = ctx.Amount / expected;
        var withinTolerance = Math.Abs(1 - ratio) <= PaycheckAmountTolerance;

        if (!withinTolerance)
            return TransactionSuggestedKind.Windfall;

        if (ctx.PayDay1.HasValue && ctx.PayDay2.HasValue)
        {
            var isOnOrNearPayday = IsWithinPaydayWindow(ctx.Date, ctx.PayDay1.Value, ctx.PayDay2.Value, daysWindow: 2);
            return isOnOrNearPayday
                ? TransactionSuggestedKind.Paycheck
                : TransactionSuggestedKind.Windfall;
        }

        return TransactionSuggestedKind.Paycheck;
    }

    // ─── Large Expense ────────────────────────────────────────────────────────

    public bool IsLargeExpense(decimal amount, decimal expectedPaycheckAmount)
    {
        if (amount <= 0 || expectedPaycheckAmount <= 0)
            return false;

        return (amount / expectedPaycheckAmount) >= LargeExpenseThresholdRatio;
    }

    // ─── Pay Cycle (used only for date validation, not for calculation) ───────

    /// <summary>
    /// Given two pay-day-of-month numbers and the next paycheck date, returns the
    /// immediately preceding paycheck date.
    ///
    /// Rules:
    ///   - If nextPaycheck falls on the smaller of the two days, the previous paycheck
    ///     was the larger day in the prior month.
    ///   - Otherwise, the previous paycheck was the smaller day in the same month.
    ///   - Clamps to last valid day of month to handle short months (e.g., Feb 28/29).
    /// </summary>
    public DateTime CalculatePreviousPaycheckDate(int payDay1, int payDay2, DateTime nextPaycheckDate)
    {
        var days = new[] { payDay1, payDay2 }.OrderBy(d => d).ToArray();
        int nextDay = nextPaycheckDate.Day;

        if (nextDay == days[0])
        {
            // Previous paycheck was days[1] of the prior month
            var prevMonthStart = new DateTime(nextPaycheckDate.Year, nextPaycheckDate.Month, 1).AddMonths(-1);
            int prevDay = Math.Min(days[1], DateTime.DaysInMonth(prevMonthStart.Year, prevMonthStart.Month));
            return new DateTime(prevMonthStart.Year, prevMonthStart.Month, prevDay);
        }
        else
        {
            // Previous paycheck was days[0] of the same month
            int prevDay = Math.Min(days[0], DateTime.DaysInMonth(nextPaycheckDate.Year, nextPaycheckDate.Month));
            return new DateTime(nextPaycheckDate.Year, nextPaycheckDate.Month, prevDay);
        }
    }

    // ─── Budget Calculation (no proration) ───────────────────────────────────

    /// <summary>
    /// Calculates how much the user has left to spend until the next paycheck.
    ///
    /// This is NOT a monthly budget and does NOT prorate by time.
    /// The question answered is: "How much can I spend before I get paid again?"
    ///
    /// Step 1: totalObligations = fixedBills + savings + debt
    /// Step 2: remainingToSpend = paycheckAmount - totalObligations
    /// Step 3: return structured result + human-readable explanation
    /// </summary>
    public BudgetCalculationResult CalculateDynamicBudget(BudgetCalculationRequest req)
    {
        // Step 1 — Sum all obligations due before next paycheck
        decimal fixedCostsRemaining = Math.Round(req.TotalFixedBills, 2);
        decimal savingsContribution = Math.Round(req.SavingsContribution, 2);
        decimal debtPerPaycheck = Math.Round(req.DebtPerPaycheck, 2);

        // Step 2 — Remaining to spend (no proration)
        decimal remainingToSpend = Math.Round(
            req.PaycheckAmount - fixedCostsRemaining - savingsContribution - debtPerPaycheck, 2);

        // Step 3 — Build explanation
        var explanationLines = new List<string>
        {
            $"Income:       ${req.PaycheckAmount:0.00}"
        };

        if (fixedCostsRemaining > 0)
            explanationLines.Add($"Fixed costs:  −${fixedCostsRemaining:0.00}");

        if (debtPerPaycheck > 0)
            explanationLines.Add($"Debt payoff:  −${debtPerPaycheck:0.00}");

        if (savingsContribution > 0)
            explanationLines.Add($"Savings:      −${savingsContribution:0.00}");

        explanationLines.Add($"──────────────────────────");
        explanationLines.Add($"Remaining:     ${remainingToSpend:0.00}");

        string explanation =
            $"You have ${remainingToSpend:0.00} to spend until your next paycheck.\n\n" +
            string.Join("\n", explanationLines);

        return new BudgetCalculationResult
        {
            PaycheckAmount     = Math.Round(req.PaycheckAmount, 2),
            FixedCostsRemaining = fixedCostsRemaining,
            DebtPerPaycheck    = debtPerPaycheck,
            SavingsContribution = savingsContribution,
            RemainingToSpend   = remainingToSpend,
            Explanation        = explanation
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static bool IsWithinPaydayWindow(DateTime date, int payDay1, int payDay2, int daysWindow)
    {
        var d = date.Day;
        return Math.Abs(d - payDay1) <= daysWindow || Math.Abs(d - payDay2) <= daysWindow;
    }
}