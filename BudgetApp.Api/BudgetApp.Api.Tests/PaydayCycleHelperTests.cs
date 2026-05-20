using BudgetApp.Api.Services;

namespace BudgetApp.Api.Tests;

/// <summary>
/// Unit tests for <see cref="PaydayCycleHelper"/>.
///
/// All tests use user-selected paydays from input (never hard-coded 1/15)
/// to verify that the helper works for any payday configuration.
/// </summary>
public class PaydayCycleHelperTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // GetNominalPaydaysForMonth
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetNominalPaydaysForMonth_ReturnsTwoDates_ForDifferentDays()
    {
        var paydays = PaydayCycleHelper.GetNominalPaydaysForMonth(2026, 5, 5, 20);

        Assert.Equal(2, paydays.Count);
        Assert.Equal(new DateOnly(2026, 5, 5), paydays[0]);
        Assert.Equal(new DateOnly(2026, 5, 20), paydays[1]);
    }

    [Fact]
    public void GetNominalPaydaysForMonth_ClampsPayDay31_InFebruary_ToLastDay()
    {
        // payDay2 = 31, but February 2026 only has 28 days
        var paydays = PaydayCycleHelper.GetNominalPaydaysForMonth(2026, 2, 15, 31);

        Assert.Equal(2, paydays.Count);
        Assert.Equal(new DateOnly(2026, 2, 15), paydays[0]);
        Assert.Equal(new DateOnly(2026, 2, 28), paydays[1]); // clamped to Feb 28
    }

    [Fact]
    public void GetNominalPaydaysForMonth_SkipsZeroPayday()
    {
        // payDay2 = 0 → only one payday this month
        var paydays = PaydayCycleHelper.GetNominalPaydaysForMonth(2026, 5, 15, 0);

        Assert.Single(paydays);
        Assert.Equal(new DateOnly(2026, 5, 15), paydays[0]);
    }

    [Fact]
    public void GetNominalPaydaysForMonth_DeduplicatesWhenBothDaysAreEqual()
    {
        var paydays = PaydayCycleHelper.GetNominalPaydaysForMonth(2026, 5, 15, 15);

        Assert.Single(paydays);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPaydayWindow
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetPaydayWindow_ReturnsCorrectRange_ForNominalDay15()
    {
        var nominal = new DateOnly(2026, 5, 15);
        var (start, end) = PaydayCycleHelper.GetPaydayWindow(nominal);

        Assert.Equal(new DateOnly(2026, 5, 12), start); // 15 - 3
        Assert.Equal(new DateOnly(2026, 5, 17), end);   // 15 + 2
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsDateInPaydayWindow — window boundaries
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NominalDay_IsAlwaysInsideOwnWindow()
    {
        // The nominal day itself must always be inside the window
        var date = new DateOnly(2026, 5, 15);
        Assert.True(PaydayCycleHelper.IsDateInPaydayWindow(date, 15, 1));
    }

    [Fact]
    public void Day_3_Before_Nominal_IsInsideWindow()
    {
        // payDay1 = 20; 3 days before = May 17
        var date = new DateOnly(2026, 5, 17);
        Assert.True(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    [Fact]
    public void Day_2_After_Nominal_IsInsideWindow()
    {
        // payDay1 = 5; 2 days after = May 7
        var date = new DateOnly(2026, 5, 7);
        Assert.True(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    [Fact]
    public void Day_4_Before_Nominal_IsOutsideWindow()
    {
        // payDay1 = 20; 4 days before = May 16 — one day outside window start (May 17)
        var date = new DateOnly(2026, 5, 16);
        Assert.False(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    [Fact]
    public void Day_3_After_Nominal_IsOutsideWindow()
    {
        // payDay1 = 5; 3 days after = May 8 — one day outside window end (May 7)
        var date = new DateOnly(2026, 5, 8);
        Assert.False(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // User-selected paydays — NOT hard-coded 1/15
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void User_PayDay5_20_Date_May2_IsInsideWindow_For_5th()
    {
        // User selected payDay1=5, payDay2=20.
        // May 5 window: May 2 (5-3) through May 7 (5+2).
        // May 2 is at the window start — must be inside.
        var date = new DateOnly(2026, 5, 2);
        Assert.True(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    [Fact]
    public void User_PayDay5_20_Date_May15_IsNotInAnyWindow()
    {
        // User selected payDay1=5, payDay2=20.
        // May 15 is NOT in the window for the 5th (ends May 7) NOR the 20th (starts May 17).
        var date = new DateOnly(2026, 5, 15);
        Assert.False(PaydayCycleHelper.IsDateInPaydayWindow(date, 5, 20));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Month-boundary crossing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PayDay1_Is1_April29_IsInsideMay1_Window()
    {
        // payDay1=1, so May 1 window = Apr 28 through May 3.
        // April 29 falls in this window.
        var date = new DateOnly(2026, 4, 29);
        Assert.True(PaydayCycleHelper.IsDateInPaydayWindow(date, 1, 15));
    }

    [Fact]
    public void PayDay1_Is1_April27_IsOutsideMay1_Window()
    {
        // payDay1=1, window starts Apr 28. Apr 27 is one day too early.
        var date = new DateOnly(2026, 4, 27);
        // Check only for the May 1 window. Apr 27 must NOT be in May 1 window.
        // Note: it might be in an April 30 or April 29 window if payDay2 has such a value —
        // but we're using payDay1=1, payDay2=15, so no confusion here.
        Assert.False(PaydayCycleHelper.IsDateInPaydayWindow(date, 1, 15));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetNominalPaydayForDate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetNominalPaydayForDate_ReturnsNominal_NotActualDate()
    {
        // payDay1=15. Date May 13 is in the window for the 15th.
        // The nominal returned must be May 15, not May 13.
        var date = new DateOnly(2026, 5, 13);
        var nominal = PaydayCycleHelper.GetNominalPaydayForDate(date, 15, 1);

        Assert.NotNull(nominal);
        Assert.Equal(new DateOnly(2026, 5, 15), nominal!.Value);
    }

    [Fact]
    public void GetNominalPaydayForDate_ReturnsNull_WhenOutsideAllWindows()
    {
        // payDay1=5, payDay2=20. May 10 is between windows.
        var date = new DateOnly(2026, 5, 10);
        var nominal = PaydayCycleHelper.GetNominalPaydayForDate(date, 5, 20);

        Assert.Null(nominal);
    }

    [Fact]
    public void GetNominalPaydayForDate_CrossBoundary_Returns_NextMonthNominal()
    {
        // payDay1=1: April 29 → nominal should be May 1 (not April 1)
        var date = new DateOnly(2026, 4, 29);
        var nominal = PaydayCycleHelper.GetNominalPaydayForDate(date, 1, 15);

        Assert.NotNull(nominal);
        Assert.Equal(new DateOnly(2026, 5, 1), nominal!.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetNearestNominalPaydayForDate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetNearestNominal_WhenInsideWindow_ReturnsThatNominal()
    {
        // May 14 is in the window for May 15 — fast path
        var date = new DateOnly(2026, 5, 14);
        var result = PaydayCycleHelper.GetNearestNominalPaydayForDate(date, 15, 1);

        Assert.Equal(new DateOnly(2026, 5, 15), result);
    }

    [Fact]
    public void GetNearestNominal_WhenOutsideWindow_ReturnsNearestByDistance()
    {
        // payDay1=5, payDay2=20. May 10 is between windows (7 ends, 17 starts).
        // Nearest: May 5 is 5 days away; May 20 is 10 days away → May 5 wins.
        var date = new DateOnly(2026, 5, 10);
        var result = PaydayCycleHelper.GetNearestNominalPaydayForDate(date, 5, 20);

        Assert.Equal(new DateOnly(2026, 5, 5), result);
    }

    [Fact]
    public void GetNearestNominal_NeverReturnsNull()
    {
        // Should always return a value, even on an unusual date
        var date = new DateOnly(2026, 6, 15);
        var result = PaydayCycleHelper.GetNearestNominalPaydayForDate(date, 3, 18);

        // Just assert it is a valid date (not default)
        Assert.NotEqual(default, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency / overlap: two paycheck dates in the same window
    // resolve to the SAME nominal payday (the primary deduplication guarantee)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoPaycheckDates_InSamePaydayWindow_BothMapToSameNominal()
    {
        // payDay1=15. Paycheck may arrive May 13 OR May 14 — both are in the
        // window for May 15. Both must return May 15 so the DB upsert is idempotent.
        var date1 = new DateOnly(2026, 5, 13);
        var date2 = new DateOnly(2026, 5, 14);

        var nominal1 = PaydayCycleHelper.GetNominalPaydayForDate(date1, 15, 1);
        var nominal2 = PaydayCycleHelper.GetNominalPaydayForDate(date2, 15, 1);

        Assert.NotNull(nominal1);
        Assert.NotNull(nominal2);
        Assert.Equal(nominal1!.Value, nominal2!.Value);
        Assert.Equal(new DateOnly(2026, 5, 15), nominal1.Value);
    }

    [Fact]
    public void PaycheckOutsideWindow_NearestNominalIsNotActualDate()
    {
        // payDay1=1, payDay2=15.
        // A paycheck arriving on May 9 (far from any window) should map to
        // the nearest nominal — May 15 (6 days away) rather than May 1 (8 days away).
        var date = new DateOnly(2026, 5, 9);
        var result = PaydayCycleHelper.GetNearestNominalPaydayForDate(date, 1, 15);

        Assert.Equal(new DateOnly(2026, 5, 15), result);
        // Result must be the nominal, not the actual date
        Assert.NotEqual(date, result);
    }
}
