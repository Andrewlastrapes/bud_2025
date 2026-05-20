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
}
