// ─── DebtSummaryCalculationTests.cs ──────────────────────────────────────────
// Unit tests for DebtSummaryCalculator.CalculatePaychecksRemaining.
// All tests are pure (no DB, no Plaid) — the calculator is a static helper.
// ──────────────────────────────────────────────────────────────────────────────

using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests;

public class DebtSummaryCalculationTests
{
    // ── Positive / happy-path cases ───────────────────────────────────────────

    [Fact]
    public void Returns4_WhenDebt1000_Payment250_ExactDivision()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(1000m, 250m);
        Assert.Equal(4, result);
    }

    [Fact]
    public void Returns4_WhenDebt1000_Payment300_CeilingOf333()
    {
        // 1000 / 300 = 3.333… → ceiling = 4
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(1000m, 300m);
        Assert.Equal(4, result);
    }

    [Fact]
    public void Returns4_WhenDebt1000_Payment333_CeilingOf3003()
    {
        // 1000 / 333 = 3.003… → ceiling = 4
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(1000m, 333m);
        Assert.Equal(4, result);
    }

    [Fact]
    public void Returns1_WhenDebtEqualsPayment()
    {
        // Exactly one paycheck to pay off
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(500m, 500m);
        Assert.Equal(1, result);
    }

    [Fact]
    public void Returns1_WhenPaymentExceedsDebt()
    {
        // Payment bigger than balance → ceiling(0.5) = 1
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(500m, 1000m);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ReturnsLargeValue_WhenPaymentIsVerySmall()
    {
        // 10000 / 1 = 10000
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(10_000m, 1m);
        Assert.Equal(10_000, result);
    }

    // ── Zero / null debt cases — card must be hidden ──────────────────────────

    [Fact]
    public void ReturnsNull_WhenTotalDebtIsZero()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(0m, 250m);
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenTotalDebtIsNull()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(null, 250m);
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenTotalDebtIsNegative()
    {
        // Defensive: negative debt makes no sense, treat as no-debt
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(-100m, 250m);
        Assert.Null(result);
    }

    // ── Zero / null payment cases — no payoff plan set ────────────────────────

    [Fact]
    public void ReturnsNull_WhenDebtPerPaycheckIsNull()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(1000m, null);
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenDebtPerPaycheckIsZero()
    {
        // No divide-by-zero: 0 payment → cannot calculate payoff → null
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(1000m, 0m);
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenBothAreNull()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(null, null);
        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenBothAreZero()
    {
        var result = DebtSummaryCalculator.CalculatePaychecksRemaining(0m, 0m);
        Assert.Null(result);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Unit tests for DebtSummaryCalculator.CalculateNetDebt
// All inputs/outputs are pure math — no DB, no Plaid.
// ──────────────────────────────────────────────────────────────────────────────
public class NetDebtCalculationTests
{
    // ── AvailableForDebt ──────────────────────────────────────────────────────

    [Fact]
    public void Available_IsZero_WhenCushionExceedsCash()
    {
        // cushion $2,000 > cash $1,500 → available = 0 (can't go negative)
        var r = DebtSummaryCalculator.CalculateNetDebt(
            totalCreditCardDebt: 8_420m,
            totalCashBalance: 1_500m,
            cashCushion: 2_000m,
            cashToApplyNow: 0m);

        Assert.Equal(0m, r.AvailableForDebt);
        Assert.Equal(0m, r.EffectiveCashApplied);
        Assert.Equal(8_420m, r.NetDebt);
    }

    [Fact]
    public void Available_IsPositive_WhenCashExceedsCushion()
    {
        // cash $3,200 − cushion $1,500 = $1,700 available
        var r = DebtSummaryCalculator.CalculateNetDebt(8_420m, 3_200m, 1_500m, 0m);

        Assert.Equal(1_700m, r.AvailableForDebt);
        Assert.Equal(0m, r.EffectiveCashApplied); // user asked for $0
        Assert.Equal(8_420m, r.NetDebt);
    }

    // ── EffectiveCashApplied clamping ─────────────────────────────────────────

    [Fact]
    public void Effective_ClampsToAvailable_WhenRequestExceedsAvailable()
    {
        // available $1,700, user requests $2,000 → effective = $1,700
        var r = DebtSummaryCalculator.CalculateNetDebt(8_420m, 3_200m, 1_500m, 2_000m);

        Assert.Equal(1_700m, r.AvailableForDebt);
        Assert.Equal(1_700m, r.EffectiveCashApplied);
        Assert.Equal(6_720m, r.NetDebt);
    }

    [Fact]
    public void Effective_ClampsToDebt_WhenRequestExceedsDebt()
    {
        // debt $500, available $3,200, user wants $3,200 → effective = $500 (can't overpay)
        var r = DebtSummaryCalculator.CalculateNetDebt(500m, 3_200m, 0m, 3_200m);

        Assert.Equal(3_200m, r.AvailableForDebt);
        Assert.Equal(500m, r.EffectiveCashApplied);
        Assert.Equal(0m, r.NetDebt);
    }

    [Fact]
    public void Effective_IsExact_WhenRequestIsWithinBounds()
    {
        // available $1,700, user requests $1,000 → effective = $1,000 (no clamping)
        var r = DebtSummaryCalculator.CalculateNetDebt(8_420m, 3_200m, 1_500m, 1_000m);

        Assert.Equal(1_700m, r.AvailableForDebt);
        Assert.Equal(1_000m, r.EffectiveCashApplied);
        Assert.Equal(7_420m, r.NetDebt);
    }

    // ── NetDebt ───────────────────────────────────────────────────────────────

    [Fact]
    public void NetDebt_NeverBelowZero()
    {
        // cashToApply > debt → netDebt clamped to 0
        var r = DebtSummaryCalculator.CalculateNetDebt(500m, 10_000m, 0m, 9_999m);

        Assert.Equal(0m, r.NetDebt);
    }

    [Fact]
    public void NetDebt_IsReducedByEffectiveCash()
    {
        // $8,420 debt − $1,700 applied = $6,720 remaining
        var r = DebtSummaryCalculator.CalculateNetDebt(8_420m, 3_200m, 1_500m, 1_700m);

        Assert.Equal(6_720m, r.NetDebt);
    }

    [Fact]
    public void NetDebt_IsZero_WhenAllDebtCovered()
    {
        // debt $1,000 fully covered by $1,000 cash applied
        var r = DebtSummaryCalculator.CalculateNetDebt(1_000m, 5_000m, 0m, 1_000m);

        Assert.Equal(0m, r.NetDebt);
    }

    // ── Zero / null cash ──────────────────────────────────────────────────────

    [Fact]
    public void ZeroCash_NoChange_ToNetDebt()
    {
        // No checking/savings → net debt = original credit card debt
        var r = DebtSummaryCalculator.CalculateNetDebt(8_420m, 0m, 0m, 0m);

        Assert.Equal(0m, r.AvailableForDebt);
        Assert.Equal(0m, r.EffectiveCashApplied);
        Assert.Equal(8_420m, r.NetDebt);
    }

    // ── Negative input normalisation ──────────────────────────────────────────

    [Fact]
    public void NegativeInputs_AreNormalisedToZero()
    {
        // Caller passes negative balances (e.g. Plaid overdraft not yet clamped)
        // CalculateNetDebt normalises them internally.
        var r = DebtSummaryCalculator.CalculateNetDebt(
            totalCreditCardDebt: -500m,  // nonsense — treated as 0
            totalCashBalance: -200m,     // overdraft — treated as 0
            cashCushion: -100m,          // nonsense — treated as 0
            cashToApplyNow: -50m);       // nonsense — treated as 0

        Assert.Equal(0m, r.AvailableForDebt);
        Assert.Equal(0m, r.EffectiveCashApplied);
        Assert.Equal(0m, r.NetDebt);
    }

    // ── PaychecksRemaining uses netDebt ───────────────────────────────────────

    [Fact]
    public void PaychecksRemaining_UsesNetDebt_NotRawDebt()
    {
        // credit card debt $8,420; user applied $1,700 cash → net debt $6,720
        // at $300/paycheck → ceil(6720/300) = 22.4 → 23
        var netResult = DebtSummaryCalculator.CalculateNetDebt(8_420m, 3_200m, 1_500m, 1_700m);
        var paychecks = DebtSummaryCalculator.CalculatePaychecksRemaining(
            netResult.NetDebt, 300m);

        Assert.Equal(23, paychecks);

        // Contrast: using raw debt would give ceil(8420/300) = 29
        var paychecksRaw = DebtSummaryCalculator.CalculatePaychecksRemaining(8_420m, 300m);
        Assert.Equal(29, paychecksRaw);
    }
}
