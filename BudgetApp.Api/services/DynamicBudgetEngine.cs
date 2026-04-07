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
/// Input for the base budget calculation (no debt, no savings applied yet).
/// Used to answer: "Before debt and savings decisions, how much do I have?"
/// </summary>
public record BaseBudgetRequest
{
    public decimal PaycheckAmount { get; init; }
    public DateTime Today { get; init; }
    public DateTime NextPaycheckDate { get; init; }

    /// <summary>Sum of fixed-cost bills due within [Today, NextPaycheckDate].</summary>
    public decimal TotalFixedBills { get; init; }
}

/// <summary>
/// Result of the base budget calculation.
/// baseRemaining = paycheck - fixedCosts
/// Debt and savings are NOT subtracted — that happens after user decisions.
/// </summary>
public record BaseBudgetResult
{
    public decimal PaycheckAmount { get; init; }
    public decimal FixedCostsRemaining { get; init; }
    public decimal BaseRemaining { get; init; }
}

/// <summary>
/// Input to the full budget calculation engine.
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
    public decimal BaseRemaining { get; init; }
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
    /// Calculates the base budget BEFORE debt and savings decisions.
    ///
    /// This is the number shown on the Debt screen so users understand
    /// what they have available before choosing a debt payment amount.
    ///
    /// Formula: baseRemaining = paycheck - fixedCosts
    /// </summary>
    BaseBudgetResult CalculateBaseBudget(BaseBudgetRequest request);

    /// <summary>
    /// Calculates the final budget after all obligations (fixed + debt + savings).
    ///
    /// NO proration — the full paycheck minus obligations is available to spend,
    /// regardless of where we are in the pay cycle.
    ///
    /// Formula: remainingToSpend = paycheck - fixedBills - savings - debt
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

    // ─── Pay Cycle (used for date validation) ────────────────────────────────

    /// <summary>
    /// Given two pay-day-of-month numbers and the next paycheck date, returns the
    /// immediately preceding paycheck date.
    /// Clamps to last valid day of month to handle short months (e.g., Feb 28/29).
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

    // ─── Base Budget (paycheck minus fixed costs only) ────────────────────────

    /// <summary>
    /// Returns how much is available BEFORE the user decides on debt and savings.
    /// This is displayed on the Debt screen so the user can make an informed choice.
    ///
    /// Formula: baseRemaining = paycheck - fixedCosts
    /// </summary>
    public BaseBudgetResult CalculateBaseBudget(BaseBudgetRequest req)
    {
        decimal fixedCostsRemaining = Math.Round(req.TotalFixedBills, 2);
        decimal baseRemaining = Math.Round(req.PaycheckAmount - fixedCostsRemaining, 2);

        return new BaseBudgetResult
        {
            PaycheckAmount      = Math.Round(req.PaycheckAmount, 2),
            FixedCostsRemaining = fixedCostsRemaining,
            BaseRemaining       = baseRemaining
        };
    }

    // ─── Full Budget Calculation (paycheck minus ALL obligations) ─────────────

    /// <summary>
    /// Calculates how much the user has left to spend until the next paycheck,
    /// after applying debt and savings decisions.
    ///
    /// Formula: remainingToSpend = paycheck - fixedBills - debt - savings
    /// </summary>
    public BudgetCalculationResult CalculateDynamicBudget(BudgetCalculationRequest req)
    {
        decimal fixedCostsRemaining = Math.Round(req.TotalFixedBills, 2);
        decimal savingsContribution = Math.Round(req.SavingsContribution, 2);
        decimal debtPerPaycheck     = Math.Round(req.DebtPerPaycheck, 2);

        // Base: paycheck minus fixed costs (before debt/savings decisions)
        decimal baseRemaining = Math.Round(req.PaycheckAmount - fixedCostsRemaining, 2);

        // Final: subtract debt and savings
        decimal remainingToSpend = Math.Round(
            baseRemaining - debtPerPaycheck - savingsContribution, 2);

        // Build explanation
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
            PaycheckAmount      = Math.Round(req.PaycheckAmount, 2),
            FixedCostsRemaining = fixedCostsRemaining,
            BaseRemaining       = baseRemaining,
            DebtPerPaycheck     = debtPerPaycheck,
            SavingsContribution = savingsContribution,
            RemainingToSpend    = remainingToSpend,
            Explanation         = explanation
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static bool IsWithinPaydayWindow(DateTime date, int payDay1, int payDay2, int daysWindow)
    {
        var d = date.Day;
        return Math.Abs(d - payDay1) <= daysWindow || Math.Abs(d - payDay2) <= daysWindow;
    }
}