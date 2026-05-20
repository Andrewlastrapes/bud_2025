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
