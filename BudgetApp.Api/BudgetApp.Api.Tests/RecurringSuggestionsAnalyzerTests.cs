using BudgetApp.Api.Data;
using BudgetApp.Api.Services;

namespace BudgetApp.Api.Tests;

/// <summary>
/// Unit tests for <see cref="RecurringSuggestionsAnalyzer"/>.
/// 
/// Tests the pure static analyzer without EF, Plaid, Firebase, Sentry, or network calls.
/// Uses fixed dates (Jan-Jun 2026) for stable regression testing.
/// </summary>
public class RecurringSuggestionsAnalyzerTests
{
    // Fixed cutoff date: 6 months back from June 2026
    private static readonly DateTime Cutoff = new DateTime(2025, 12, 1);

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: Create test transaction
    // ─────────────────────────────────────────────────────────────────────────

    private static Transaction CreateTransaction(
        string name,
        string? merchantName,
        decimal amount,
        DateTime date,
        bool pending = false,
        TransactionSuggestedKind suggestedKind = TransactionSuggestedKind.Unknown,
        bool isHistoricalBackfill = false)
    {
        return new Transaction
        {
            Id = 0, // Not used by analyzer
            PlaidTransactionId = Guid.NewGuid().ToString(),
            UserId = 1,
            AccountId = "test-account",
            Name = name,
            MerchantName = merchantName,
            Amount = amount,
            Date = date,
            Pending = pending,
            SuggestedKind = suggestedKind,
            IsHistoricalBackfill = isHistoricalBackfill,
            BudgetImpactEligible = !isHistoricalBackfill, // Backfill rows are not budget-eligible
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: Venmo rent-like monthly payments around the 1st for about $400
    // Expected: included as Monthly, warning present because it is a payment app
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_VenmoRentPayments_IncludedWithWarning()
    {
        // Arrange: 4 Venmo payments around the 1st of each month, ~$400
        var transactions = new List<Transaction>
        {
            CreateTransaction("VENMO *PAYMENT", "Venmo", 395.00m, new DateTime(2026, 1, 2)),
            CreateTransaction("VENMO *PAYMENT", "Venmo", 400.00m, new DateTime(2026, 2, 1)),
            CreateTransaction("VENMO *PAYMENT", "Venmo", 405.00m, new DateTime(2026, 3, 3)),
            CreateTransaction("VENMO *PAYMENT", "Venmo", 400.00m, new DateTime(2026, 4, 1)),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal("Monthly", suggestion.Frequency);
        Assert.True(suggestion.Confidence >= 80, $"Expected confidence >= 80 (payment app threshold), got {suggestion.Confidence}");
        Assert.NotNull(suggestion.Warning);
        Assert.Contains("payment-app", suggestion.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, suggestion.OccurrenceCount);
        Assert.True(suggestion.EstimatedAmount >= 395m && suggestion.EstimatedAmount <= 405m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Toyota car payment around the 15th for about $598
    // Expected: included as Monthly, no payment-app warning
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_ToyotaCarPayment_IncludedWithoutWarning()
    {
        // Arrange: 4 Toyota Financial payments around the 15th, ~$598
        var transactions = new List<Transaction>
        {
            CreateTransaction("TOYOTA FINANCIAL", "Toyota Financial Services", 598.00m, new DateTime(2026, 1, 15)),
            CreateTransaction("TOYOTA FINANCIAL", "Toyota Financial Services", 598.00m, new DateTime(2026, 2, 15)),
            CreateTransaction("TOYOTA FINANCIAL", "Toyota Financial Services", 598.00m, new DateTime(2026, 3, 15)),
            CreateTransaction("TOYOTA FINANCIAL", "Toyota Financial Services", 598.00m, new DateTime(2026, 4, 15)),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal("Monthly", suggestion.Frequency);
        Assert.True(suggestion.Confidence >= 60, $"Expected confidence >= 60 (normal threshold), got {suggestion.Confidence}");
        Assert.Null(suggestion.Warning);
        Assert.Equal(4, suggestion.OccurrenceCount);
        Assert.Equal(598.00m, suggestion.EstimatedAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: Soulshine monthly charge for about $650
    // Expected: included as Monthly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_SoulshineMonthly_Included()
    {
        // Arrange: 4 Soulshine payments, monthly pattern
        var transactions = new List<Transaction>
        {
            CreateTransaction("SOULSHINE", "Soulshine", 650.00m, new DateTime(2026, 1, 10)),
            CreateTransaction("SOULSHINE", "Soulshine", 650.00m, new DateTime(2026, 2, 10)),
            CreateTransaction("SOULSHINE", "Soulshine", 650.00m, new DateTime(2026, 3, 10)),
            CreateTransaction("SOULSHINE", "Soulshine", 650.00m, new DateTime(2026, 4, 10)),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal("Monthly", suggestion.Frequency);
        Assert.True(suggestion.Confidence >= 60, $"Expected confidence >= 60, got {suggestion.Confidence}");
        Assert.Equal(4, suggestion.OccurrenceCount);
        Assert.Equal(650.00m, suggestion.EstimatedAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: Netflix/Max subscriptions
    // Expected: both included as Monthly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_NetflixMaxSubscriptions_Included()
    {
        // Arrange: Netflix and Max, each with 4 monthly charges
        var transactions = new List<Transaction>
        {
            // Netflix
            CreateTransaction("NETFLIX.COM", "Netflix", 15.99m, new DateTime(2026, 1, 5)),
            CreateTransaction("NETFLIX.COM", "Netflix", 15.99m, new DateTime(2026, 2, 5)),
            CreateTransaction("NETFLIX.COM", "Netflix", 15.99m, new DateTime(2026, 3, 5)),
            CreateTransaction("NETFLIX.COM", "Netflix", 15.99m, new DateTime(2026, 4, 5)),
            // Max
            CreateTransaction("MAX *STREAMING", "Max", 9.99m, new DateTime(2026, 1, 12)),
            CreateTransaction("MAX *STREAMING", "Max", 9.99m, new DateTime(2026, 2, 12)),
            CreateTransaction("MAX *STREAMING", "Max", 9.99m, new DateTime(2026, 3, 12)),
            CreateTransaction("MAX *STREAMING", "Max", 9.99m, new DateTime(2026, 4, 12)),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert
        Assert.Equal(2, results.Count);

        // Both should be Monthly with high confidence
        foreach (var suggestion in results)
        {
            Assert.Equal("Monthly", suggestion.Frequency);
            Assert.True(suggestion.Confidence >= 60, $"Expected confidence >= 60 for {suggestion.MerchantName}, got {suggestion.Confidence}");
            Assert.Equal(4, suggestion.OccurrenceCount);
        }

        // Verify both merchants present
        var merchantNames = results.Select(r => r.MerchantName).ToList();
        Assert.Contains("Netflix", merchantNames);
        Assert.Contains("Max", merchantNames);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: Payment app recurring transaction with high enough confidence
    // Expected: included, but warning present
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_PaymentAppHighConfidence_IncludedWithWarning()
    {
        // Arrange: Zelle payments with tight monthly pattern, low variance
        var transactions = new List<Transaction>
        {
            CreateTransaction("ZELLE TRANSFER", "Zelle", 1200.00m, new DateTime(2026, 1, 1)),
            CreateTransaction("ZELLE TRANSFER", "Zelle", 1200.00m, new DateTime(2026, 2, 1)),
            CreateTransaction("ZELLE TRANSFER", "Zelle", 1200.00m, new DateTime(2026, 3, 1)),
            CreateTransaction("ZELLE TRANSFER", "Zelle", 1200.00m, new DateTime(2026, 4, 1)),
            CreateTransaction("ZELLE TRANSFER", "Zelle", 1200.00m, new DateTime(2026, 5, 1)),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal("Monthly", suggestion.Frequency);
        Assert.True(suggestion.Confidence >= 80, $"Expected confidence >= 80 (payment app threshold), got {suggestion.Confidence}");
        Assert.NotNull(suggestion.Warning);
        Assert.Contains("payment-app", suggestion.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, suggestion.OccurrenceCount);
        Assert.Equal(1200.00m, suggestion.EstimatedAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: Similar amounts that are not recurring
    // Expected: not included
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_SimilarAmountsNotRecurring_Excluded()
    {
        // Arrange: Same merchant, similar amounts, but irregular gaps (10, 45, 90 days)
        var transactions = new List<Transaction>
        {
            CreateTransaction("RANDOM STORE", "Random Store", 50.00m, new DateTime(2026, 1, 1)),
            CreateTransaction("RANDOM STORE", "Random Store", 52.00m, new DateTime(2026, 1, 11)),  // 10 days
            CreateTransaction("RANDOM STORE", "Random Store", 48.00m, new DateTime(2026, 2, 25)),  // 45 days
            CreateTransaction("RANDOM STORE", "Random Store", 51.00m, new DateTime(2026, 5, 25)),  // 90 days
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert: No frequency match, should be excluded
        Assert.Empty(results);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 7: Pending transactions
    // Expected: ignored by the analyzer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_PendingTransactions_Ignored()
    {
        // Arrange: Mix of pending and non-pending transactions
        var transactions = new List<Transaction>
        {
            // Non-pending (should be included)
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 1, 15), pending: false),
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 2, 15), pending: false),
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 3, 15), pending: false),
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 4, 15), pending: false),
            // Pending (should be ignored)
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 5, 15), pending: true),
            CreateTransaction("SPOTIFY", "Spotify", 9.99m, new DateTime(2026, 6, 15), pending: true),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert: Only 4 non-pending transactions should be counted
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal(4, suggestion.OccurrenceCount); // Pending transactions excluded
        Assert.Equal("Monthly", suggestion.Frequency);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 8: Non-pending historical/backfill-style transactions inside the cutoff
    // Expected: included if the pattern is valid
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_HistoricalBackfillTransactions_IncludedIfValid()
    {
        // Arrange: Historical backfill transactions with valid monthly pattern
        // Current analyzer does NOT filter by BudgetImpactEligible - only Pending and Date
        var transactions = new List<Transaction>
        {
            CreateTransaction("GYM MEMBERSHIP", "Gym", 45.00m, new DateTime(2026, 1, 1),
                pending: false, isHistoricalBackfill: true),
            CreateTransaction("GYM MEMBERSHIP", "Gym", 45.00m, new DateTime(2026, 2, 1),
                pending: false, isHistoricalBackfill: true),
            CreateTransaction("GYM MEMBERSHIP", "Gym", 45.00m, new DateTime(2026, 3, 1),
                pending: false, isHistoricalBackfill: true),
            CreateTransaction("GYM MEMBERSHIP", "Gym", 45.00m, new DateTime(2026, 4, 1),
                pending: false, isHistoricalBackfill: true),
        };

        // Act
        var results = RecurringSuggestionsAnalyzer.Analyze(transactions, Cutoff);

        // Assert: Historical backfill transactions ARE included (current behavior)
        Assert.Single(results);
        var suggestion = results[0];
        Assert.Equal("Monthly", suggestion.Frequency);
        Assert.Equal(4, suggestion.OccurrenceCount);
        Assert.Equal(45.00m, suggestion.EstimatedAmount);
        Assert.True(suggestion.Confidence >= 60, $"Expected confidence >= 60, got {suggestion.Confidence}");
    }
}
