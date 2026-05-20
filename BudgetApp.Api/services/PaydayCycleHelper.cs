// ─── PaydayCycleHelper.cs ─────────────────────────────────────────────────────
// Pure static helper for pay-cycle date math.
// No database access — every method is deterministic and independently testable.
//
// Default payday window: 3 days before nominal payday through 2 days after.
//
//   Nominal = 1  → window = prev-month-28/29/30/31 through 3rd of month
//   Nominal = 15 → window = 12th through 17th
//   Nominal = 5  → window = 2nd through 7th
//   Nominal = 20 → window = 17th through 22nd
//
// All methods work in DateOnly.  Callers convert DateTime.UtcNow.Date to DateOnly
// before calling.
// ─────────────────────────────────────────────────────────────────────────────

namespace BudgetApp.Api.Services;

public static class PaydayCycleHelper
{
    /// <summary>Days before nominal payday that are still "in window".</summary>
    public const int DefaultDaysBefore = 3;

    /// <summary>Days after nominal payday that are still "in window".</summary>
    public const int DefaultDaysAfter = 2;

    // ─────────────────────────────────────────────────────────────────────────
    // GetNominalPaydaysForMonth
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the concrete nominal payday <see cref="DateOnly"/> values for the
    /// given calendar month, in ascending order.
    ///
    /// If <paramref name="payDay1"/> or <paramref name="payDay2"/> exceeds the
    /// number of days in the month (e.g. payDay2 = 31 in February), the value
    /// is clamped to the last day of the month.
    ///
    /// A value of 0 is treated as "not configured" and is skipped.
    /// Duplicate days (payDay1 == payDay2) produce only one entry.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetNominalPaydaysForMonth(
        int year, int month, int payDay1, int payDay2)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        var result = new List<DateOnly>(2);

        if (payDay1 > 0)
            result.Add(new DateOnly(year, month, Math.Min(payDay1, daysInMonth)));

        if (payDay2 > 0 && payDay2 != payDay1)
            result.Add(new DateOnly(year, month, Math.Min(payDay2, daysInMonth)));

        result.Sort();
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPaydayWindow
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the inclusive (<c>Start</c>, <c>End</c>) date range for a nominal
    /// payday.  Both endpoints are inside the window.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetPaydayWindow(
        DateOnly nominalPayday,
        int daysBefore = DefaultDaysBefore,
        int daysAfter = DefaultDaysAfter)
        => (nominalPayday.AddDays(-daysBefore), nominalPayday.AddDays(daysAfter));

    // ─────────────────────────────────────────────────────────────────────────
    // IsDateInPaydayWindow
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="date"/> falls within any payday
    /// window for the user's configured pay schedule.
    /// Correctly handles windows that cross month boundaries.
    /// </summary>
    public static bool IsDateInPaydayWindow(
        DateOnly date,
        int payDay1,
        int payDay2,
        int daysBefore = DefaultDaysBefore,
        int daysAfter = DefaultDaysAfter)
        => GetNominalPaydayForDate(date, payDay1, payDay2, daysBefore, daysAfter) is not null;

    // ─────────────────────────────────────────────────────────────────────────
    // GetNominalPaydayForDate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// If <paramref name="date"/> is inside a payday window, returns the
    /// corresponding nominal payday.  Returns <c>null</c> when outside all windows.
    ///
    /// Checks the previous, current, and next calendar month so that windows that
    /// cross month boundaries are handled correctly (e.g. April 29 is inside the
    /// May 1 payday window when payDay1 = 1 and daysBefore = 3).
    /// </summary>
    public static DateOnly? GetNominalPaydayForDate(
        DateOnly date,
        int payDay1,
        int payDay2,
        int daysBefore = DefaultDaysBefore,
        int daysAfter = DefaultDaysAfter)
    {
        foreach (var (y, m) in GetMonthsToCheck(date))
        {
            foreach (var nominal in GetNominalPaydaysForMonth(y, m, payDay1, payDay2))
            {
                var (start, end) = GetPaydayWindow(nominal, daysBefore, daysAfter);
                if (date >= start && date <= end)
                    return nominal;
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetNearestNominalPaydayForDate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the nominal payday that <paramref name="date"/> belongs to when
    /// it is inside a window (fast path).
    ///
    /// When outside all windows — for example, a paycheck deposit that arrived
    /// far from any scheduled payday — returns the nearest nominal payday by
    /// absolute calendar distance.  This is used for the paycheck-fallback
    /// trigger so that an unexpected deposit can still be associated with a
    /// sensible pay period.
    ///
    /// Never returns null.
    /// </summary>
    public static DateOnly GetNearestNominalPaydayForDate(
        DateOnly date,
        int payDay1,
        int payDay2,
        int daysBefore = DefaultDaysBefore,
        int daysAfter = DefaultDaysAfter)
    {
        // Fast path: already in a window
        var inWindow = GetNominalPaydayForDate(date, payDay1, payDay2, daysBefore, daysAfter);
        if (inWindow is not null)
            return inWindow.Value;

        // Fallback: collect candidates from the surrounding months and pick the
        // one with the smallest absolute distance.
        var candidates = new List<DateOnly>();
        for (int monthOffset = -2; monthOffset <= 2; monthOffset++)
        {
            var target = date.AddMonths(monthOffset);
            candidates.AddRange(
                GetNominalPaydaysForMonth(target.Year, target.Month, payDay1, payDay2));
        }

        return candidates
            .OrderBy(d => Math.Abs(
                (d.ToDateTime(TimeOnly.MinValue) - date.ToDateTime(TimeOnly.MinValue)).TotalDays))
            .First();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Yields the (year, month) tuples for the previous, current, and next
    /// calendar months relative to <paramref name="date"/>, so window lookups
    /// handle month-boundary crossings.
    /// </summary>
    private static IEnumerable<(int Year, int Month)> GetMonthsToCheck(DateOnly date)
    {
        yield return StepMonth(date.Year, date.Month, -1);
        yield return (date.Year, date.Month);
        yield return StepMonth(date.Year, date.Month, +1);
    }

    private static (int Year, int Month) StepMonth(int year, int month, int delta)
    {
        var dt = new DateTime(year, month, 1).AddMonths(delta);
        return (dt.Year, dt.Month);
    }
}
