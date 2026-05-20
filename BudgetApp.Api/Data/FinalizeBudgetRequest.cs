namespace BudgetApp.Api.Data;

public class FinalizeBudgetRequest
{
    public decimal PaycheckAmount { get; set; }
    public int PayDay1 { get; set; }
    public int PayDay2 { get; set; }
    public DateTime NextPaycheckDate { get; set; }

    /// <summary>How much of each paycheck is dedicated to existing credit debt.</summary>
    public decimal? DebtPerPaycheck { get; set; }

    /// <summary>
    /// Total credit card debt captured from the Plaid snapshot shown during onboarding.
    /// Null  = value was not available (legacy client, no Plaid connection).
    /// 0     = user genuinely had no credit card debt at the time.
    /// Saved once and used for the payoff plan estimate on the home dashboard.
    /// </summary>
    public decimal? DebtStartingBalance { get; set; }

    // ── Cash-cushion fields captured during debt onboarding ──────────────────
    // All nullable: null = not provided (legacy client or no Plaid depository link).
    // Real 0 is valid and meaningful (e.g. user had no cash, or applied $0 toward debt).
    // The backend recalculates and clamps these values using DebtSummaryCalculator
    // before persisting, so the stored values are always safe even if the client
    // sends an inconsistent combination.

    /// <summary>
    /// Total checking + savings balance at the time the user completed debt onboarding.
    /// Captured from the Plaid snapshot. Null for legacy users or those without linked
    /// depository accounts.
    /// </summary>
    public decimal? CashBalanceAtOnboarding { get; set; }

    /// <summary>
    /// Cash cushion the user chose to keep for bills, rent, and emergencies.
    /// This amount is NOT applied toward debt.
    /// </summary>
    public decimal? CashCushionAtOnboarding { get; set; }

    /// <summary>
    /// Amount of cash the user explicitly chose to apply toward credit card debt now.
    /// The backend clamps this to min(availableForDebt, totalCreditCardDebt).
    /// </summary>
    public decimal? CashAppliedToDebtAtOnboarding { get; set; }

    /// <summary>
    /// Remaining debt after applying the chosen cash amount
    /// (= DebtStartingBalance − CashAppliedToDebtAtOnboarding, clamped ≥ 0).
    /// Computed by the frontend for display purposes; the backend recalculates and
    /// persists its own server-side result.
    /// </summary>
    public decimal? NetDebtStartingBalance { get; set; }
}
