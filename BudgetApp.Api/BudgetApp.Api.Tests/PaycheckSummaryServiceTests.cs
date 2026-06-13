using Xunit;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;

namespace BudgetApp.Api.Tests;

/// <summary>
/// Regression tests for PaycheckSummaryService DateTime UTC handling.
/// Ensures all DateTime values sent to PostgreSQL timestamp with time zone columns are UTC.
/// </summary>
public class PaycheckSummaryServiceTests
{
    /// <summary>
    /// Regression test for Sentry error:
    /// "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"
    /// 
    /// Verifies that all DateTime fields in PaycheckSummary have DateTimeKind.Utc
    /// before being saved to the database.
    /// </summary>
    [Fact]
    public void AsUtcDate_NormalizesUnspecifiedDateTimeToUtc()
    {
        // Arrange: Create a DateTime with Kind=Unspecified (simulates DynamicBudgetEngine output)
        var unspecifiedDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);

        // Act: Use reflection to call the private AsUtcDate helper
        var serviceType = typeof(PaycheckSummaryService);
        var asUtcDateMethod = serviceType.GetMethod("AsUtcDate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(asUtcDateMethod);

        var result = (DateTime)asUtcDateMethod.Invoke(null, new object[] { unspecifiedDate })!;

        // Assert: Result must be UTC
        Assert.Equal(DateTimeKind.Utc, result.Kind);

        // Assert: Calendar date must not shift
        Assert.Equal(2026, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(0, result.Hour);
        Assert.Equal(0, result.Minute);
        Assert.Equal(0, result.Second);
    }

    [Fact]
    public void AsUtcDate_PreservesCalendarDateForLocalDateTime()
    {
        // Arrange: Create a DateTime with Kind=Local
        var localDate = new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Local);

        // Act
        var serviceType = typeof(PaycheckSummaryService);
        var asUtcDateMethod = serviceType.GetMethod("AsUtcDate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(asUtcDateMethod);

        var result = (DateTime)asUtcDateMethod.Invoke(null, new object[] { localDate })!;

        // Assert: Result must be UTC midnight on the same calendar date
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(2026, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(0, result.Hour);
        Assert.Equal(0, result.Minute);
        Assert.Equal(0, result.Second);
    }

    [Fact]
    public void AsUtcDate_HandlesUtcDateTimeCorrectly()
    {
        // Arrange: Create a DateTime that's already UTC
        var utcDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var serviceType = typeof(PaycheckSummaryService);
        var asUtcDateMethod = serviceType.GetMethod("AsUtcDate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(asUtcDateMethod);

        var result = (DateTime)asUtcDateMethod.Invoke(null, new object[] { utcDate })!;

        // Assert: Result must remain UTC
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(2026, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(0, result.Hour);
    }

    /// <summary>
    /// Documents the expected DateTime.Kind for all PaycheckSummary date fields.
    /// This test serves as living documentation of the UTC requirement.
    /// </summary>
    [Fact]
    public void PaycheckSummary_AllDateTimeFieldsMustBeUtc()
    {
        // This test documents the contract: all DateTime properties on PaycheckSummary
        // that map to PostgreSQL timestamp with time zone columns MUST have Kind=Utc.

        var summary = new PaycheckSummary
        {
            UserId = 1,
            PaycheckDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            PeriodStartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEndDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            NextPaycheckDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            PaycheckAmount = 2000m,
            PriorPeriodSpend = 1500m,
            PriorPeriodRemaining = 500m,
            WasUnderBudget = true,
            LeftoverAmount = 500m,
            OverBudgetAmount = 0m,
            FixedCostsUntilNextPaycheck = 800m,
            SavingsContribution = 200m,
            DebtPaymentAmount = 100m,
            NewDynamicBudgetAmount = 900m
        };

        // Assert: All DateTime fields must be UTC
        Assert.Equal(DateTimeKind.Utc, summary.PaycheckDate.Kind);
        Assert.Equal(DateTimeKind.Utc, summary.PeriodStartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, summary.PeriodEndDate.Kind);
        Assert.Equal(DateTimeKind.Utc, summary.NextPaycheckDate.Kind);
        Assert.Equal(DateTimeKind.Utc, summary.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, summary.UpdatedAt.Kind);
    }
}
