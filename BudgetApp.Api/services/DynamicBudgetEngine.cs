using System;
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

public interface IDynamicBudgetEngine
{
    TransactionSuggestedKind ClassifyDeposit(DepositContext ctx);

    /// <summary>
    /// Returns true if an outflow is "large" relative to the user's expected paycheck.
    /// For now: >= 30% of expected paycheck.
    /// </summary>
    bool IsLargeExpense(decimal amount, decimal expectedPaycheckAmount);
}

public class DynamicBudgetEngine : IDynamicBudgetEngine
{
    private const decimal PaycheckAmountTolerance = 0.15m; // 15%
    private const decimal LargeExpenseThresholdRatio = 0.30m; // 30%

    public TransactionSuggestedKind ClassifyDeposit(DepositContext ctx)
    {
        if (ctx.Amount <= 0)
            return TransactionSuggestedKind.Unknown;

        // If we don't know their expected paycheck, we can't be smart.
        if (ctx.ExpectedPaycheckAmount is null or <= 0)
        {
            // Heuristic: if it has a payroll-ish merchant name, call it paycheck.
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
        {
            // Not close enough in amount – probably a windfall / random deposit.
            return TransactionSuggestedKind.Windfall;
        }

        // Check timing against payday windows if provided.
        if (ctx.PayDay1.HasValue && ctx.PayDay2.HasValue)
        {
            var isOnOrNearPayday = IsWithinPaydayWindow(ctx.Date, ctx.PayDay1.Value, ctx.PayDay2.Value, daysWindow: 2);
            if (isOnOrNearPayday)
            {
                return TransactionSuggestedKind.Paycheck;
            }

            // Amount looks like paycheck, but timing is way off ⇒ treat as windfall for now.
            return TransactionSuggestedKind.Windfall;
        }

        // Fallback: amount matches but we don't have paydays.
        return TransactionSuggestedKind.Paycheck;
    }

    public bool IsLargeExpense(decimal amount, decimal expectedPaycheckAmount)
    {
        if (amount <= 0 || expectedPaycheckAmount <= 0)
            return false;

        var ratio = amount / expectedPaycheckAmount;
        return ratio >= LargeExpenseThresholdRatio;
    }

    private static bool IsWithinPaydayWindow(DateTime date, int payDay1, int payDay2, int daysWindow)
    {
        var d = date.Day;
        return Math.Abs(d - payDay1) <= daysWindow || Math.Abs(d - payDay2) <= daysWindow;
    }
}
