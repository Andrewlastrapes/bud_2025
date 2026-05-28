namespace BudgetApp.Api.Services;

/// <summary>
/// Pure, stateless helper that encapsulates the two key formulas used during
/// pending-to-posted transaction reconciliation.
///
/// Having this logic in a separate, dependency-free class lets unit tests verify
/// every reconciliation scenario without standing up a database or a Plaid client.
///
/// ── Plaid amount convention (reminder) ────────────────────────────────────────
///   raw Plaid Amount > 0  →  spending / outflow
///   raw Plaid Amount &lt;= 0 →  credit / income
///   The app stores Math.Abs(rawAmount), so all amounts here are already positive.
/// </summary>
public static class PendingReconciliationCalculator
{
    // ─── Desired budget impact ────────────────────────────────────────────────

    /// <summary>
    /// Returns the dollar amount that SHOULD currently affect the dynamic balance
    /// for the given outflow transaction.
    ///
    /// Decision table:
    ///   fixed-cost match   → 0       (already reserved in the paycheck budget formula)
    ///   suspicious hold    → holdOverrideAmount ?? 0
    ///                        (do not blindly subtract a $500 gas-station pre-auth;
    ///                         wait until the user sets a realistic override via the
    ///                         POST /api/transactions/{id}/hold-override endpoint)
    ///   all other spending → absAmount
    /// </summary>
    /// <param name="absAmount">Absolute (positive) transaction amount.</param>
    /// <param name="isFixedCostMatch">True when the transaction matched a known fixed cost.</param>
    /// <param name="isSuspiciousHold">True when SuspiciousHoldDetector flagged this pending tx.</param>
    /// <param name="holdOverrideAmount">
    ///     The user's override amount (from HoldOverrideAmount), or null if not yet reviewed.
    /// </param>
    public static decimal ComputeDesiredBudgetImpact(
        decimal absAmount,
        bool isFixedCostMatch,
        bool isSuspiciousHold,
        decimal? holdOverrideAmount)
    {
        if (isFixedCostMatch)
            return 0m;

        if (isSuspiciousHold)
            return holdOverrideAmount ?? 0m;

        return absAmount;
    }

    // ─── Balance delta ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the net change that should be added to <c>balanceDelta</c> (which is then
    /// applied to Balance.BalanceAmount at the end of the sync loop).
    ///
    /// Formula:
    ///   netDelta = -(desiredImpact - existingApplied)
    ///
    /// Sign convention (matches the rest of TransactionService):
    ///   negative result  →  balance decreases  (more spending applied)
    ///   positive result  →  balance increases   (less spending applied than before,
    ///                                            or prior over-deduction reversed)
    ///
    /// Examples:
    ///   Normal new pending spend ($50):
    ///     ComputeBalanceDelta(50, 0)  =  -(50-0)  =  -50   ✓ balance drops $50
    ///
    ///   Posted same amount as pending ($50 → $50):
    ///     ComputeBalanceDelta(50, 50) =  -(50-50) =   0    ✓ no additional change
    ///
    ///   Posted lower than pending ($50 pending, $40 posted):
    ///     ComputeBalanceDelta(40, 50) =  -(40-50) =  +10   ✓ balance rises $10
    ///
    ///   Posted higher than pending ($50 pending, $52 posted):
    ///     ComputeBalanceDelta(52, 50) =  -(52-50) =   -2   ✓ balance drops extra $2
    ///
    ///   Expired / reversed pending ($50 was applied, now desired=0):
    ///     ComputeBalanceDelta(0, 50)  =  -(0-50)  =  +50   ✓ balance rises $50
    /// </summary>
    /// <param name="desiredImpact">
    ///     The amount that SHOULD be applied to the balance going forward
    ///     (result of <see cref="ComputeDesiredBudgetImpact"/>).
    /// </param>
    /// <param name="existingApplied">
    ///     The amount already applied to the balance (BudgetAppliedAmount ?? 0).
    /// </param>
    public static decimal ComputeBalanceDelta(decimal desiredImpact, decimal existingApplied)
        => -(desiredImpact - existingApplied);
}
