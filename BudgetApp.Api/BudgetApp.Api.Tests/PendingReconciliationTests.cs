using BudgetApp.Api.Services;

namespace BudgetApp.Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="PendingReconciliationCalculator"/>.
///
/// No database, no HTTP, no Plaid client — just the two formula methods
/// that drive pending-to-posted balance reconciliation.
///
/// Each test documents the invariant it enforces so future maintainers can
/// understand WHY each formula branch exists, not just that it works.
/// </summary>
public class PendingReconciliationTests
{
    // ─── ComputeDesiredBudgetImpact ───────────────────────────────────────────

    /// <summary>
    /// 1. Normal pending / posted spend
    ///    A regular coffee shop or grocery store charge should immediately reduce
    ///    the dynamic balance by the full transaction amount.
    ///    desiredImpact = absAmount
    /// </summary>
    [Fact]
    public void NormalSpend_DesiredImpactEqualsAbsAmount()
    {
        decimal impact = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 50m,
            isFixedCostMatch: false,
            isSuspiciousHold: false,
            holdOverrideAmount: null);

        Assert.Equal(50m, impact);
    }

    /// <summary>
    /// 2. Fixed-cost match
    ///    When a transaction is matched to a known fixed cost (rent, Netflix, etc.)
    ///    it has ALREADY been reserved in the paycheck budget formula.
    ///    desiredImpact = 0  (no additional deduction from dynamic balance)
    /// </summary>
    [Fact]
    public void FixedCostMatch_DesiredImpactIsZero()
    {
        decimal impact = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 120m,
            isFixedCostMatch: true,
            isSuspiciousHold: false,
            holdOverrideAmount: null);

        Assert.Equal(0m, impact);
    }

    /// <summary>
    /// 3. Suspicious hold — no user override yet
    ///    A gas-station $150 pre-auth or hotel $500 hold should NOT blindly drain
    ///    the dynamic balance.  desiredImpact = 0 until the user sets an override.
    /// </summary>
    [Fact]
    public void SuspiciousHold_NoOverride_DesiredImpactIsZero()
    {
        decimal impact = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 150m,
            isFixedCostMatch: false,
            isSuspiciousHold: true,
            holdOverrideAmount: null);

        Assert.Equal(0m, impact);
    }

    /// <summary>
    /// 4. Suspicious hold — user has set an override amount
    ///    Once the user reviews the hold and sets a realistic estimate ($40 fill-up)
    ///    that override amount becomes the desired budget impact.
    ///    desiredImpact = holdOverrideAmount
    /// </summary>
    [Fact]
    public void SuspiciousHold_WithOverride_DesiredImpactEqualsOverride()
    {
        decimal impact = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 150m,
            isFixedCostMatch: false,
            isSuspiciousHold: true,
            holdOverrideAmount: 40m);

        Assert.Equal(40m, impact);
    }

    /// <summary>
    /// 5. Fixed-cost flag takes priority over suspicious-hold flag
    ///    A transaction cannot be both a fixed cost AND a suspicious hold in practice,
    ///    but if both flags were somehow set the fixed-cost rule wins (→ 0 impact).
    /// </summary>
    [Fact]
    public void FixedCost_TakesPriorityOver_SuspiciousHold()
    {
        decimal impact = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 75m,
            isFixedCostMatch: true,
            isSuspiciousHold: true,
            holdOverrideAmount: 30m);

        Assert.Equal(0m, impact);
    }

    // ─── ComputeBalanceDelta ──────────────────────────────────────────────────

    /// <summary>
    /// 6. New normal pending spend — nothing previously applied
    ///    When a pending $50 coffee arrives with no prior budget impact,
    ///    the balance should drop by $50.
    ///    delta = -(50 - 0) = -50
    /// </summary>
    [Fact]
    public void NewPendingSpend_BalanceDeltaIsNegativeAbsAmount()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 50m,
            existingApplied: 0m);

        Assert.Equal(-50m, delta);
    }

    /// <summary>
    /// 7. Posted transaction for the SAME amount as the prior pending
    ///    The pending already subtracted $50 from the balance.
    ///    The posted transaction has the same desired impact ($50).
    ///    Net delta = 0 — no further change to the balance.
    ///    delta = -(50 - 50) = 0
    /// </summary>
    [Fact]
    public void PostedSameAmountAsPending_BalanceDeltaIsZero()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 50m,
            existingApplied: 50m);

        Assert.Equal(0m, delta);
    }

    /// <summary>
    /// 8. Posted transaction for LESS than the pending amount
    ///    Pending applied $50; posted is only $40 (e.g. smaller fill-up).
    ///    The balance should go UP by $10 to refund the over-deduction.
    ///    delta = -(40 - 50) = +10
    /// </summary>
    [Fact]
    public void PostedLowerThanPending_BalanceDeltaIsPositive()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 40m,
            existingApplied: 50m);

        Assert.Equal(10m, delta);
    }

    /// <summary>
    /// 9. Posted transaction for MORE than the pending amount
    ///    Pending applied $50; posted is $52 (e.g. tip added at restaurant).
    ///    The balance should drop an additional $2.
    ///    delta = -(52 - 50) = -2
    /// </summary>
    [Fact]
    public void PostedHigherThanPending_BalanceDeltaIsNegativeDifference()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 52m,
            existingApplied: 50m);

        Assert.Equal(-2m, delta);
    }

    /// <summary>
    /// 10. Expired / stale pending reversal
    ///     A pending row had $50 applied.  No posted replacement arrives.
    ///     The desired impact is now 0 (the pending has expired / been cancelled).
    ///     The balance should rise by $50 to restore what was over-deducted.
    ///     delta = -(0 - 50) = +50
    /// </summary>
    [Fact]
    public void ExpiredPending_ReversalRestoresBalance()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 0m,
            existingApplied: 50m);

        Assert.Equal(50m, delta);
    }

    /// <summary>
    /// 11. Suspicious hold override applied AFTER initial hold (desiredImpact changes)
    ///     Initially the hold applied $0 (no override set).
    ///     User later sets override = $40.
    ///     The reconciliation path should now subtract $40.
    ///     delta = -(40 - 0) = -40
    /// </summary>
    [Fact]
    public void HoldOverrideApplied_AfterZeroInitial_SubtractsOverrideAmount()
    {
        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: 40m,
            existingApplied: 0m);

        Assert.Equal(-40m, delta);
    }

    /// <summary>
    /// 12. Fixed-cost match zero-impact is also zero-delta when nothing was applied
    ///     A transaction that matched a fixed cost (desired = 0) with nothing
    ///     previously applied should produce no balance change at all.
    ///     delta = -(0 - 0) = 0
    /// </summary>
    [Fact]
    public void FixedCostMatch_ZeroDelta_WhenNothingPreviouslyApplied()
    {
        decimal desired = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            absAmount: 120m,
            isFixedCostMatch: true,
            isSuspiciousHold: false,
            holdOverrideAmount: null);

        decimal delta = PendingReconciliationCalculator.ComputeBalanceDelta(
            desiredImpact: desired,
            existingApplied: 0m);

        Assert.Equal(0m, desired);
        Assert.Equal(0m, delta);
    }

    // ─── Round-trip composition tests ────────────────────────────────────────

    /// <summary>
    /// 13. Full round-trip: normal pending spend then same-amount posted
    ///     Step 1 — pending arrives:
    ///       desired=50, existing=0, delta=-50  (balance drops 50)
    ///     Step 2 — posted arrives with same amount, prior pending neutralized:
    ///       desired=50, existing=50 (from pending), delta=0  (no further change)
    ///     Net balance change over both steps = -50  ✓
    /// </summary>
    [Fact]
    public void RoundTrip_PendingThenPostedSameAmount_NetDeltaIsSingleDeduction()
    {
        // Step 1: pending inserted
        decimal pendingDesired = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            50m, isFixedCostMatch: false, isSuspiciousHold: false, holdOverrideAmount: null);
        decimal pendingDelta = PendingReconciliationCalculator.ComputeBalanceDelta(pendingDesired, 0m);

        Assert.Equal(-50m, pendingDelta);
        // pendingRow.BudgetAppliedAmount = 50m  (what the service would set)

        // Step 2: posted arrives; prior pending BudgetAppliedAmount = 50m (not yet reversed)
        decimal postedDesired = PendingReconciliationCalculator.ComputeDesiredBudgetImpact(
            50m, isFixedCostMatch: false, isSuspiciousHold: false, holdOverrideAmount: null);
        decimal postedDelta = PendingReconciliationCalculator.ComputeBalanceDelta(postedDesired, existingApplied: 50m);

        Assert.Equal(0m, postedDelta);

        // Net effect on the balance across both sync events: -50 (correct, only deducted once)
        decimal netBalanceChange = pendingDelta + postedDelta;
        Assert.Equal(-50m, netBalanceChange);
    }

    /// <summary>
    /// 14. Full round-trip: pending $50, posted $40 (lower)
    ///     Step 1 — pending arrives:  delta = -50
    ///     Step 2 — posted $40 arrives, prior pending had $50 applied:
    ///       delta = -(40-50) = +10  (balance rises $10 to refund over-deduction)
    ///     Net = -40  ✓  (only the actual posted amount was ever deducted)
    /// </summary>
    [Fact]
    public void RoundTrip_PendingFifty_PostedForty_NetIsFortyDeduction()
    {
        decimal pendingDelta = PendingReconciliationCalculator.ComputeBalanceDelta(50m, 0m);
        decimal postedDelta = PendingReconciliationCalculator.ComputeBalanceDelta(40m, 50m);

        Assert.Equal(-50m, pendingDelta);
        Assert.Equal(10m, postedDelta);
        Assert.Equal(-40m, pendingDelta + postedDelta);
    }

    /// <summary>
    /// 15. Full round-trip: pending $50, posted $52 (higher — tip added)
    ///     Step 1 — pending arrives:  delta = -50
    ///     Step 2 — posted $52, prior $50 applied:  delta = -(52-50) = -2
    ///     Net = -52  ✓
    /// </summary>
    [Fact]
    public void RoundTrip_PendingFifty_PostedFiftyTwo_NetIsFiftyTwoDeduction()
    {
        decimal pendingDelta = PendingReconciliationCalculator.ComputeBalanceDelta(50m, 0m);
        decimal postedDelta = PendingReconciliationCalculator.ComputeBalanceDelta(52m, 50m);

        Assert.Equal(-50m, pendingDelta);
        Assert.Equal(-2m, postedDelta);
        Assert.Equal(-52m, pendingDelta + postedDelta);
    }
}
