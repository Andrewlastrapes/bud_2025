namespace BudgetApp.Api.Services;

/// <summary>
/// Pure, stateless calculator for the debt payoff summary shown on the home dashboard.
/// All methods are static so they can be unit-tested without any infrastructure.
/// </summary>
public static class DebtSummaryCalculator
{
    /// <summary>
    /// Calculates how many paychecks are required to pay off a debt at a fixed payment rate.
    ///
    /// Returns null (card hidden) in the following cases:
    ///   - <paramref name="totalDebt"/> is null (data not captured — old user)
    ///   - <paramref name="totalDebt"/> is &lt;= 0 (no debt)
    ///   - <paramref name="debtPerPaycheck"/> is null or 0 (no payoff plan set)
    ///
    /// Formula: Math.Ceiling(totalDebt / debtPerPaycheck)
    /// No interest is modelled — this is a simple payoff estimate.
    /// </summary>
    public static int? CalculatePaychecksRemaining(decimal? totalDebt, decimal? debtPerPaycheck)
    {
        if (totalDebt is null or <= 0m) return null;
        if (debtPerPaycheck is null or <= 0m) return null;

        return (int)Math.Ceiling((double)(totalDebt.Value / debtPerPaycheck.Value));
    }

    /// <summary>
    /// Result of a net-debt calculation showing how much of the user's checking/savings
    /// cash can safely be applied toward credit card debt after keeping a cash cushion.
    /// </summary>
    /// <param name="AvailableForDebt">
    /// Cash available to put toward debt (cash balance − cushion, never negative).
    /// </param>
    /// <param name="EffectiveCashApplied">
    /// The actual amount applied — the user's requested amount clamped to
    /// min(availableForDebt, totalCreditCardDebt) so it is always safe.
    /// </param>
    /// <param name="NetDebt">
    /// Remaining debt after the cash application
    /// (totalCreditCardDebt − effectiveCashApplied, never negative).
    /// </param>
    public record NetDebtResult(
        decimal AvailableForDebt,
        decimal EffectiveCashApplied,
        decimal NetDebt);

    /// <summary>
    /// Calculates the remaining debt after optionally applying some checking/savings cash.
    ///
    /// Rules enforced:
    /// - All inputs are normalised to ≥ 0 (negative inputs are treated as 0).
    /// - <paramref name="cashToApplyNow"/> is clamped to
    ///   min(availableForDebt, totalCreditCardDebt) so the user can never apply more
    ///   cash than they have available, or more than the debt itself.
    /// - Negative depository balances should be clamped to 0 by the caller before
    ///   passing to this method (Plaid overdraft accounts must not inflate cash).
    /// </summary>
    /// <param name="totalCreditCardDebt">
    /// Total outstanding credit card balance (positive = amount owed).
    /// </param>
    /// <param name="totalCashBalance">
    /// Sum of checking + savings balances (each clamped ≥ 0 before summing).
    /// </param>
    /// <param name="cashCushion">
    /// Amount the user wants to keep as a buffer for bills and emergencies.
    /// Subtracted from cash before computing available-for-debt.
    /// </param>
    /// <param name="cashToApplyNow">
    /// Amount the user chose to apply toward debt. Clamped internally.
    /// </param>
    public static NetDebtResult CalculateNetDebt(
        decimal totalCreditCardDebt,
        decimal totalCashBalance,
        decimal cashCushion,
        decimal cashToApplyNow)
    {
        // Normalise all inputs — never operate on negative values.
        totalCreditCardDebt = Math.Max(0m, totalCreditCardDebt);
        totalCashBalance = Math.Max(0m, totalCashBalance);
        cashCushion = Math.Max(0m, cashCushion);
        cashToApplyNow = Math.Max(0m, cashToApplyNow);

        // available = cash after keeping the cushion (never negative)
        var available = Math.Max(0m, totalCashBalance - cashCushion);

        // effective = user's request clamped so it can't exceed available cash
        //             or the total debt (no point applying more than you owe)
        var effective = Math.Min(cashToApplyNow, Math.Min(available, totalCreditCardDebt));

        // net debt = what remains after applying the effective cash (never negative)
        var netDebt = Math.Max(0m, totalCreditCardDebt - effective);

        return new NetDebtResult(available, effective, netDebt);
    }
}
