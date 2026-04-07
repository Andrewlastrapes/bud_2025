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
    public DateTime PreviousPaycheckDate { get; init; }
    public DateTime NextPaycheckDate { get; init; }

    /// <summary>Sum of fixed-cost bills due within [Today, NextPaycheckDate].</summary>
    public decimal TotalFixedBills { get; init; }

    /// <summary>Sum of all savings fixed costs (always included, no date filter).</summary>
    public decimal SavingsContribution { get; init; }

    /// <summary>Optional per-paycheck debt payoff allocation.</summary>
    public decimal DebtPerPaycheck { get; init; }
}

/// <summary>
/// Full deterministic result from the 6-step budget engine.
/// All monetary values are rounded to 2 decimal places.
/// </summary>
public record BudgetCalculationResult
{
    public decimal PaycheckAmount { get; init; }
    public decimal TotalRecurringCosts { get; init; }
    public decimal DebtPerPaycheck { get; init; }
    public decimal SavingsContribution { get; init; }
    public decimal EffectivePaycheck { get; init; }
    public decimal ProrateFactor { get; init; }
    public decimal DynamicSpendableAmount { get; init; }
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
    /// Executes the 6-step dynamic budget calculation.
    /// Pure arithmetic — no side effects, no I/O.
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

    // ─── Pay Cycle ────────────────────────────────────────────────────────────

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

    // ─── Budget Calculation (6-step engine) ──────────────────────────────────

    /// <summary>
    /// Step 1: payCycleDays  = nextPaycheck − previousPaycheck
    /// Step 2: daysUntilNext = nextPaycheck − today
    /// Step 3: totalRecurringCosts = fixedBills + savings + debt
    /// Step 4: effectivePaycheck   = paycheckAmount − totalRecurringCosts
    /// Step 5: prorateFactor       = daysUntilNext / payCycleDays
    ///         dynamicSpendable    = effectivePaycheck × prorateFactor
    /// Step 6: return structured result + human-readable explanation
    /// </summary>
    public BudgetCalculationResult CalculateDynamicBudget(BudgetCalculationRequest req)
    {
        // Step 1 — Pay cycle
        int payCycleDays = (int)(req.NextPaycheckDate - req.PreviousPaycheckDate).TotalDays;
        int daysUntilNextPaycheck = (int)(req.NextPaycheckDate - req.Today).TotalDays;

        // Step 3 — Total obligations
        decimal totalRecurringCosts = Math.Round(
            req.TotalFixedBills + req.SavingsContribution + req.DebtPerPaycheck, 2);

        // Step 4 — Effective paycheck
        decimal effectivePaycheck = Math.Round(req.PaycheckAmount - totalRecurringCosts, 2);

        // Step 5 — Prorate
        decimal prorateFactor = payCycleDays > 0
            ? Math.Round((decimal)daysUntilNextPaycheck / payCycleDays, 4)
            : 0m;

        decimal dynamicSpendableAmount = Math.Round(effectivePaycheck * prorateFactor, 2);

        // Step 6 — Build explanation
        var explanationLines = new List<string>
        {
            $"- ${req.PaycheckAmount:0.00} paycheck"
        };

        if (req.TotalFixedBills > 0)
            explanationLines.Add($"- minus ${req.TotalFixedBills:0.00} in upcoming bills");

        if (req.DebtPerPaycheck > 0)
            explanationLines.Add($"- minus ${req.DebtPerPaycheck:0.00} toward debt");

        if (req.SavingsContribution > 0)
            explanationLines.Add($"- minus ${req.SavingsContribution:0.00} in savings");

        int proratePercent = (int)Math.Round(prorateFactor * 100);
        string explanation =
            $"You'll have ${dynamicSpendableAmount:0.00} to spend before your next paycheck.\n\n" +
            $"This is based on:\n{string.Join("\n", explanationLines)}\n\n" +
            $"Adjusted for time remaining in this pay cycle ({proratePercent}%).";

        return new BudgetCalculationResult
        {
            PaycheckAmount        = Math.Round(req.PaycheckAmount, 2),
            TotalRecurringCosts   = totalRecurringCosts,
            DebtPerPaycheck       = Math.Round(req.DebtPerPaycheck, 2),
            SavingsContribution   = Math.Round(req.SavingsContribution, 2),
            EffectivePaycheck     = effectivePaycheck,
            ProrateFactor         = prorateFactor,
            DynamicSpendableAmount = dynamicSpendableAmount,
            Explanation           = explanation
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static bool IsWithinPaydayWindow(DateTime date, int payDay1, int payDay2, int daysWindow)
    {
        var d = date.Day;
        return Math.Abs(d - payDay1) <= daysWindow || Math.Abs(d - payDay2) <= daysWindow;
    }
}