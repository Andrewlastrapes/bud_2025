using System;
using System.Collections.Generic;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests;

/// <summary>
/// Tests for <see cref="FixedCostMatcher.TryMatch"/>.
///
/// Sign convention reminder:
///   Transaction.Amount is always stored as a positive absolute value.
///   FixedCostMatcher operates on positive amounts only.
///
/// Tolerance reminder:
///   max($2.00, 2% of fixed-cost amount)
///   e.g. fc.Amount=$598 → tolerance=max($2, $11.96)=$11.96
///        fc.Amount=$120  → tolerance=max($2, $2.40)=$2.40
///        fc.Amount=$650  → tolerance=max($2, $13.00)=$13.00
/// </summary>
public class FixedCostMatchingTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static FixedCost MakeManual(
        string name,
        decimal amount,
        DateTime? nextDueDate = null,
        string? plaidMerchantName = null,
        string? plaidAccountId = null,
        int id = 1)
    {
        return new FixedCost
        {
            Id = id,
            UserId = 46,
            Name = name,
            Amount = amount,
            Type = "manual",
            PlaidMerchantName = plaidMerchantName,
            PlaidAccountId = plaidAccountId,
            NextDueDate = nextDueDate,
            Category = "transportation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static FixedCost MakePlaidDiscovered(
        string name,
        decimal amount,
        string plaidMerchantName,
        int id = 99)
    {
        return new FixedCost
        {
            Id = id,
            UserId = 46,
            Name = name,
            Amount = amount,
            Type = "plaid_discovered",
            PlaidMerchantName = plaidMerchantName,
            NextDueDate = null,
            Category = "subscription",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    // ── Test 1: Toyota car payment — amount + date window ────────────────────
    // fc: Car Payment $598, NextDueDate 2026-05-15
    // tx: Toyota Financial Services $598.38, date 2026-05-18 (3 days after due)
    // tolerance: max($2, 2%×$598) = max($2, $11.96) = $11.96
    // diff: |$598.38 − $598| = $0.38 ≤ $11.96 ✓
    // date diff: |May 18 − May 15| = 3 days ≤ 7 ✓
    [Fact]
    public void Toyota_CarPayment_MatchesByAmountAndDate()
    {
        var fc = MakeManual("Car Payment", 598m, nextDueDate: new DateTime(2026, 5, 15));
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Toyota Financial Services",
            transactionAmount: 598.38m,
            transactionDate: new DateTime(2026, 5, 18));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("manual-amount-date", matchType);
    }

    // ── Test 2: AT&T phone bill — amount + exact due date ────────────────────
    // fc: Phone $120, NextDueDate 2026-05-18
    // tx: AT&T $119.76, date 2026-05-18
    // tolerance: max($2, 2%×$120) = max($2, $2.40) = $2.40
    // diff: |$119.76 − $120| = $0.24 ≤ $2.40 ✓
    // date diff: 0 days ≤ 7 ✓
    [Fact]
    public void ATT_PhoneBill_MatchesByAmountAndExactDueDate()
    {
        var fc = MakeManual("Phone", 120m, nextDueDate: new DateTime(2026, 5, 18), id: 2);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "AT&T",
            transactionAmount: 119.76m,
            transactionDate: new DateTime(2026, 5, 18));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("manual-amount-date", matchType);
    }

    // ── Test 3: Soulshine — amount EXCEEDS tolerance, no match ───────────────
    // fc: Soulshine $650, NextDueDate 2026-05-15
    // tx: Brightwheel $669.76
    // tolerance: max($2, 2%×$650) = max($2, $13.00) = $13.00
    // diff: |$669.76 − $650| = $19.76 > $13.00 ✗
    // Documented: 3.04% variance is intentionally rejected.
    // User should update the fixed-cost amount to the actual billed amount.
    [Fact]
    public void Soulshine_ExceedsAmountTolerance_NoMatch()
    {
        var fc = MakeManual("Soulshine", 650m, nextDueDate: new DateTime(2026, 5, 15), id: 3);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Brightwheel",
            transactionAmount: 669.76m,
            transactionDate: new DateTime(2026, 5, 15));

        Assert.Null(match);
        Assert.Equal(string.Empty, matchType);
    }

    // ── Test 4: plaid_discovered — Priority 1 merchant name match ────────────
    // Even if amount would also match, PlaidMerchantName takes priority.
    [Fact]
    public void PlaidDiscovered_MatchesByMerchantName_Priority1()
    {
        var fc = MakePlaidDiscovered("Prime Video", 7.99m, plaidMerchantName: "Prime Video");
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Prime Video",
            transactionAmount: 7.99m,
            transactionDate: new DateTime(2026, 5, 10));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("merchant-name", matchType);
    }

    // ── Test 5: Amount within tolerance but date is 40 days outside window ───
    // The date guard should reject this even though the amount matches.
    [Fact]
    public void AmountMatchButDateFarOutsideWindow_NoMatch()
    {
        var fc = MakeManual("Car Payment", 598m, nextDueDate: new DateTime(2026, 5, 15), id: 5);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Toyota Financial Services",
            transactionAmount: 599.00m,
            transactionDate: new DateTime(2026, 4, 1)); // 44 days before due date

        Assert.Null(match);
        Assert.Equal(string.Empty, matchType);
    }

    // ── Test 6: NextDueDate = null, no name overlap → NO match ───────────────
    // Amount-only matching is too risky without a date anchor or name signal.
    // "Gym" does not appear in "LA Fitness" → rejected.
    [Fact]
    public void NullDueDate_NoNameOverlap_NoMatch()
    {
        var fc = MakeManual("Gym", 50m, nextDueDate: null, id: 6);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "LA Fitness",
            transactionAmount: 50.00m,
            transactionDate: new DateTime(2026, 5, 10));

        Assert.Null(match);
        Assert.Equal(string.Empty, matchType);
    }

    // ── Test 7: NextDueDate = null, name overlap present → MATCH ─────────────
    // "Netflix" appears in "Netflix" → sufficient secondary signal.
    [Fact]
    public void NullDueDate_NameOverlapPresent_Matches()
    {
        var fc = MakeManual("Netflix", 15.49m, nextDueDate: null, id: 7);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Netflix",
            transactionAmount: 15.49m,
            transactionDate: new DateTime(2026, 5, 10));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("manual-amount-name-token", matchType);
    }

    // ── Test 8: $2 absolute floor tolerance — small amount bill ──────────────
    // fc: $10.00, tx: $9.91 → diff $0.09 ≤ $2 (floor applies, 2% of $10 = $0.20 < $2)
    [Fact]
    public void SmallAmountBill_TwoDoller_FloorTolerance_Matches()
    {
        var fc = MakeManual("App Subscription", 10.00m,
            nextDueDate: new DateTime(2026, 5, 12), id: 8);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "SomeApp",
            transactionAmount: 9.91m,
            transactionDate: new DateTime(2026, 5, 12));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("manual-amount-date", matchType);
    }

    // ── Test 9: MatchType is "merchant-name" for previously enriched manual ──
    // After a first sync enriches a manual fc with PlaidMerchantName,
    // subsequent syncs should hit Priority 1 (merchant name) not Priority 2.
    [Fact]
    public void PreviouslyEnrichedManualCost_MatchesByMerchantName()
    {
        // Simulate a manual cost that was enriched on a previous sync
        var fc = MakeManual(
            "Car Payment", 598m,
            nextDueDate: new DateTime(2026, 5, 15),
            plaidMerchantName: "Toyota Financial Services",  // ← enriched
            id: 9);
        var costs = new List<FixedCost> { fc };

        var (match, matchType) = FixedCostMatcher.TryMatch(
            costs,
            merchantName: "Toyota Financial Services",
            transactionAmount: 620.00m, // Different amount — but merchant name overrides
            transactionDate: new DateTime(2026, 6, 18));

        Assert.NotNull(match);
        Assert.Equal(fc.Id, match.Id);
        Assert.Equal("merchant-name", matchType); // Priority 1, NOT manual-amount-date
    }

    // ── HasNameTokenOverlap helpers ────────────────────────────────────────────

    // Token overlap analysis (Tokenise splits on & ' - _ . / \ ( )):
    //   "Netflix"          → ["netflix"]        in "netflix"           → true
    //   "Netflix Subs…"    → ["netflix","subs…"] "netflix" in "netflix" → true
    //   "AT&T Wireless"    → ["wireless"]        not in "at&t"          → false
    //                        ("AT"=2 chars, "T"=1 char, "Wireless"=8 chars; "wireless" ∉ "at&t")
    //   "AT&T Wireless"    → ["wireless"]        in "at&t wireless"     → true
    //   "Gym"              → ["gym"]             not in "la fitness"    → false
    //   "Car Payment"      → ["car","payment"]   neither in "toyota…"  → false
    //   "Phone"            → ["phone"]           not in "at&t"          → false
    //   "Hulu"             → ["hulu"]            in "hulu"              → true
    //   "Amazon Prime"     → ["amazon","prime"]  "amazon" in "amazon"  → true
    [Theory]
    [InlineData("Netflix", "Netflix", true)]
    [InlineData("Netflix Subscription", "Netflix", true)]
    [InlineData("AT&T Wireless", "AT&T", false)]          // "wireless" not in "at&t"
    [InlineData("AT&T Wireless", "AT&T Wireless", true)]  // "wireless" in "at&t wireless"
    [InlineData("Gym", "LA Fitness", false)]               // "gym" (3 chars) not in "la fitness"
    [InlineData("Car Payment", "Toyota Financial", false)]
    [InlineData("Phone", "AT&T", false)]                   // "phone" not in "at&t"
    [InlineData("Hulu", "Hulu", true)]
    [InlineData("Amazon Prime", "Amazon", true)]
    public void HasNameTokenOverlap_Theory(string fcName, string merchant, bool expected)
    {
        Assert.Equal(expected, FixedCostMatcher.HasNameTokenOverlap(fcName, merchant));
    }
}
