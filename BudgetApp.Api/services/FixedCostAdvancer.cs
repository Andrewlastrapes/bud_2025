using BudgetApp.Api.Data;

namespace BudgetApp.Api.Services;

/// <summary>
/// Advances the <c>NextDueDate</c> of a recurring fixed cost past a matched
/// transaction date using the cost's <c>RecurrenceFrequency</c>.
///
/// Design decisions:
///
///   Monthly day preservation
///   ───────────────────────
///   <see cref="DateTime.AddMonths"/> alone does NOT preserve the original
///   day-of-month across short months:
///     Jan 31 → AddMonths(1) → Feb 28  (correct — clamped)
///     Feb 28 → AddMonths(1) → Mar 28  (WRONG — original day was 31, not 28)
///
///   To get the true Jan 31 → Feb 28 → Mar 31 behaviour this class uses
///   <see cref="FixedCost.OriginalDueDayOfMonth"/> as the authoritative day
///   and reconstructs the correct date each cycle:
///     targetDay  = OriginalDueDayOfMonth ?? currentNextDue.Day
///     daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth)
///     nextDue.Day = Math.Min(targetDay, daysInMonth)
///
///   If <c>OriginalDueDayOfMonth</c> is null (rows pre-dating the column),
///   the method falls back to the current <c>NextDueDate.Day</c>, which gives
///   standard <c>AddMonths(1)</c> behaviour — acceptable for those rows.
///
///   OneTime costs
///   ─────────────
///   When <c>RecurrenceFrequency</c> is <c>"OneTime"</c> the current
///   <c>NextDueDate</c> is returned unchanged.  The transaction matched the
///   single scheduled occurrence; the fixed cost should remain visible but
///   will not produce another due date.
///
///   Catch-up / multi-cycle gap
///   ──────────────────────────
///   The advancement loop runs until <c>nextDue > transactionDate</c>, so a
///   fixed cost that has not been advanced for several periods (e.g. the app
///   was offline for two months) will be fast-forwarded correctly in one call.
///   A hard cap of 120 iterations prevents infinite loops on corrupted data.
/// </summary>
public static class FixedCostAdvancer
{
    /// <summary>Maximum number of advancement cycles before we bail out.</summary>
    private const int MaxIterations = 120;

    /// <summary>
    /// Returns the new <c>NextDueDate</c> after advancing <paramref name="currentNextDue"/>
    /// past <paramref name="transactionDate"/> using <paramref name="recurrenceFrequency"/>.
    /// </summary>
    /// <param name="currentNextDue">
    /// The current value of <c>FixedCost.NextDueDate</c>.
    /// </param>
    /// <param name="recurrenceFrequency">
    /// One of: <c>"Weekly"</c>, <c>"Biweekly"</c>, <c>"Monthly"</c>,
    /// <c>"Yearly"</c>, <c>"OneTime"</c>.
    /// Unknown values are treated as <c>"Monthly"</c>.
    /// </param>
    /// <param name="transactionDate">
    /// The UTC date of the matching transaction.  Advancement continues until
    /// the resulting date is strictly AFTER this date (same-day counts as matched).
    /// </param>
    /// <param name="originalDayOfMonth">
    /// <see cref="FixedCost.OriginalDueDayOfMonth"/> — the intended calendar day
    /// (1–31) for monthly bills.  Pass <c>null</c> to fall back to
    /// <c>currentNextDue.Day</c>.
    /// </param>
    /// <returns>
    /// A new <c>DateTime</c> strictly after <paramref name="transactionDate"/>,
    /// or <paramref name="currentNextDue"/> unchanged when the frequency is
    /// <c>"OneTime"</c>.
    /// </returns>
    public static DateTime AdvanceNextDueDate(
        DateTime currentNextDue,
        string recurrenceFrequency,
        DateTime transactionDate,
        int? originalDayOfMonth = null)
    {
        // Normalise — treat null/empty/unknown as Monthly.
        string freq = string.IsNullOrWhiteSpace(recurrenceFrequency)
            ? "Monthly"
            : recurrenceFrequency.Trim();

        // OneTime: return current date unchanged — caller decides what to do.
        if (string.Equals(freq, "OneTime", StringComparison.OrdinalIgnoreCase))
            return currentNextDue;

        DateTime nextDue = currentNextDue;
        int iterations = 0;

        // Advance until nextDue is strictly after the transaction date.
        while (nextDue.Date <= transactionDate.Date)
        {
            if (++iterations > MaxIterations)
                break; // safety valve — should never be reached with real data

            nextDue = AdvanceOneCycle(nextDue, freq, originalDayOfMonth);
        }

        return nextDue;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Advances <paramref name="date"/> by exactly one recurrence cycle.
    /// </summary>
    private static DateTime AdvanceOneCycle(
        DateTime date,
        string freq,
        int? originalDayOfMonth)
    {
        return freq.ToUpperInvariant() switch
        {
            "WEEKLY" => date.AddDays(7),
            "BIWEEKLY" => date.AddDays(14),
            "YEARLY" => date.AddYears(1),
            _ => AdvanceOneMonth(date, originalDayOfMonth) // Monthly (default)
        };
    }

    /// <summary>
    /// Advances <paramref name="current"/> by one month while preserving the
    /// original day-of-month intent.
    ///
    /// If <paramref name="originalDayOfMonth"/> is provided, the result uses
    /// that day (clamped to the last day of the target month when necessary).
    /// Otherwise falls back to <c>AddMonths(1)</c> which may silently reduce
    /// the day after a short-month clamp.
    ///
    /// Examples with <c>originalDayOfMonth = 31</c>:
    ///   Jan 31 → Feb 28  (Feb only has 28 days in 2026)
    ///   Feb 28 → Mar 31  (target day 31, March has 31 days — fully restored)
    ///   Mar 31 → Apr 30  (April only has 30 days)
    ///   Apr 30 → May 31  (May has 31 days — restored again)
    /// </summary>
    private static DateTime AdvanceOneMonth(DateTime current, int? originalDayOfMonth)
    {
        if (originalDayOfMonth is null)
        {
            // No original day stored — use standard library behaviour.
            // AddMonths correctly handles short months (e.g. Jan 31 → Feb 28)
            // but the resulting day may "drift" on subsequent months.
            return current.AddMonths(1);
        }

        int targetDay = originalDayOfMonth.Value;

        // Move to the first of next month, then apply the target day.
        int targetYear = current.Year;
        int targetMonth = current.Month + 1;

        if (targetMonth > 12)
        {
            targetMonth = 1;
            targetYear++;
        }

        // Clamp to the number of days in the target month.
        int daysInTarget = DateTime.DaysInMonth(targetYear, targetMonth);
        int actualDay = Math.Min(targetDay, daysInTarget);

        return new DateTime(targetYear, targetMonth, actualDay,
                            current.Hour, current.Minute, current.Second,
                            current.Kind);
    }
}
