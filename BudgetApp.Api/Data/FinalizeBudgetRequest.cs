namespace BudgetApp.Api.Data;

public class FinalizeBudgetRequest
{
    public decimal PaycheckAmount { get; set; }
    public int PayDay1 { get; set; }
    public int PayDay2 { get; set; }
    public DateTime NextPaycheckDate { get; set; }

    // How much of each paycheck is dedicated to existing credit debt
    public decimal? DebtPerPaycheck { get; set; }

    /// <summary>
    /// Total credit card debt captured from the Plaid snapshot shown during onboarding.
    /// Null  = value was not available (legacy client, no Plaid connection).
    /// 0     = user genuinely had no credit card debt at the time.
    /// Saved once and used for the payoff plan estimate on the home dashboard.
    /// </summary>
    public decimal? DebtStartingBalance { get; set; }
}
