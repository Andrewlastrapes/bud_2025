using BudgetApp.Api.Data;
using System.Text.RegularExpressions;

namespace BudgetApp.Api.Services;

/// <summary>
/// Pure static analyzer. No EF, no Plaid, no DI.
/// Detects likely recurring fixed costs from stored transaction history.
/// Called by GET /api/recurring/suggestions — never from GET /api/plaid/recurring.
/// </summary>
public static class RecurringSuggestionsAnalyzer
{
    // ── Frequency gap ranges (inclusive, in days) ─────────────────────────────
    private const int WeeklyMin = 5, WeeklyMax = 9;
    private const int BiweeklyMin = 11, BiweeklyMax = 17;
    private const int MonthlyMin = 25, MonthlyMax = 37;
    private const int QuarterlyMin = 78, QuarterlyMax = 104;

    // ── Minimum occurrences required per frequency ────────────────────────────
    private const int WeeklyMinOcc = 4;
    private const int BiweeklyMinOcc = 3;
    private const int MonthlyMinOcc = 3;
    private const int SemiMonthlyMinOcc = 3; // 3 months of 2-per-month = 6 occurrences minimum
    private const int QuarterlyMinOcc = 2;

    // ── Confidence thresholds ─────────────────────────────────────────────────
    private const int NormalThreshold = 60;
    private const int ElevatedThreshold = 80; // soft-exclude groups + payment apps

    // ── Hard-exclude keyword fragments ───────────────────────────────────────
    // These are clearly not user-controlled recurring fixed costs.
    // Payment apps (Venmo, Zelle, Cash App, PayPal) are intentionally absent —
    // they may represent real recurring fixed costs like rent or daycare.
    private static readonly string[] HardExcludeFragments =
    {
        "ATM WITHDRAWAL", "ATM CASH", "ATM FEE", "CASH WITHDRAWAL",
        "WIRE TRANSFER", "WIRE XFER",
        "SAVINGS TRANSFER", "TRANSFER TO SAVINGS", "TRANSFER TO CHECKING",
        "ACCOUNT TRANSFER", "INTERNAL TRANSFER", "OWN ACCOUNT",
        "BROKERAGE TRANSFER", "INVESTMENT TRANSFER",
        "FIDELITY", "VANGUARD", "SCHWAB", "ETRADE", "E*TRADE", "ROBINHOOD",
        "CREDIT CARD PAYMENT", "CREDIT CARD PMT",
        "AUTOPAY", "AUTO-PAY",
        "LOAN PAYMENT", "LOAN PMT",
        "MORTGAGE PAYMENT",
    };

    // ── Payment-app keyword fragments — ambiguous, higher threshold required ──
    private static readonly string[] PaymentAppFragments =
    {
        "VENMO", "ZELLE", "CASH APP", "CASHAPP", "PAYPAL",
    };

    // ── Soft-exclude keyword fragments — higher threshold required ────────────
    private static readonly string[] SoftExcludeFragments =
    {
        // Restaurants / fast food
        "RESTAURANT", "CAFE", "COFFEE", "BURGER", "PIZZA", "TACO", "SUSHI",
        "DINER", "GRILL", "KITCHEN", "CHIPOTLE", "SUBWAY", "WENDY",
        "CHICK-FIL", "DUNKIN", "DOMINO", "IHOP", "APPLEBEE", "PANERA",
        "ARBY", "PANDA EXPRESS", "FIVE GUYS", "STARBUCKS", "MCDONALD",
        // Grocery
        "GROCERY", "GROCER", "WHOLE FOODS", "TRADER JOE", "KROGER", "PUBLIX",
        "SAFEWAY", "ALDI", "SPROUTS", "WEGMANS",
        // Gas / fuel
        "SHELL", "EXXON", "CHEVRON", "SUNOCO", "MOBIL", "CITGO",
        "SPEEDWAY", "WAWA", "CIRCLE K", "QUIKTRIP", "MARATHON OIL",
        "GAS STATION", "FUEL STATION",
        // General shopping
        "AMAZON", "WALMART", "TARGET", "BEST BUY", "HOME DEPOT",
        "LOWE", "WALGREENS", "CVS PHARMACY", "DOLLAR TREE", "DOLLAR GENERAL",
        "ROSS STORES", "MARSHALLS", "TJ MAXX", "KOHLS", "MACY",
    };

    // ── Payment-app warning message ───────────────────────────────────────────
    private const string PaymentAppWarning =
        "Recurring payment-app transaction detected. Confirm what this is before adding it as a fixed cost.";

    // ── Name normalization regex (compiled once) ──────────────────────────────
    // Strips trailing IDs / reference tokens: e.g. "NETFLIX *Q4XR1", "SPOTIFY 7F3A9B", "ACH #00129"
    private static readonly Regex TrailingTokenRegex = new(
        @"[\s\*\-#_]+[A-Z0-9]{4,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MultiSpaceRegex = new(
        @"\s{2,}",
        RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyze a list of transactions and return likely recurring fixed-cost suggestions.
    /// </summary>
    /// <param name="transactions">
    ///   All non-pending transactions for the user within the look-back window.
    ///   Caller must NOT pre-filter by SuggestedKind — the analyzer handles income exclusion.
    /// </param>
    /// <param name="cutoffDate">
    ///   Earliest date to include (typically DateTime.UtcNow.Date.AddMonths(-6)).
    ///   Transactions before this date are excluded.
    /// </param>
    public static List<RecurringSuggestionDto> Analyze(
        IReadOnlyList<Transaction> transactions,
        DateTime cutoffDate)
    {
        if (transactions == null || transactions.Count == 0)
            return new List<RecurringSuggestionDto>();

        // ── 1. Pre-filter: non-pending, within window, positive amount ─────────
        // All stored amounts are positive (Math.Abs applied during Plaid sync).
        // Income/credit transactions have SuggestedKind != Unknown — we let the
        // hard-exclude step inside the analyzer filter those rather than doing
        // it here, so the analyzer always sees the full picture.
        var candidates = transactions
            .Where(t => !t.Pending && t.Date >= cutoffDate && t.Amount > 0)
            .ToList();

        if (candidates.Count == 0)
            return new List<RecurringSuggestionDto>();

        // ── 2. Group by normalized name ───────────────────────────────────────
        var groups = candidates
            .GroupBy(t => NormalizeName(t.MerchantName ?? t.Name ?? string.Empty))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= 2)
            .ToList();

        var results = new List<RecurringSuggestionDto>();

        foreach (var group in groups)
        {
            string normalizedName = group.Key;
            var txList = group.ToList();

            // ── 3. Hard exclude — income, ATM, auto-transfers, brokerage ────────
            if (IsHardExcluded(normalizedName, txList))
                continue;

            // ── 4. Classify group sensitivity ─────────────────────────────────
            bool isPaymentApp = ContainsAny(normalizedName, PaymentAppFragments);
            bool isSoftExclude = !isPaymentApp && ContainsAny(normalizedName, SoftExcludeFragments);

            // ── 5. Sort by date and compute consecutive day-gaps ───────────────
            var sorted = txList.OrderBy(t => t.Date).ToList();
            var gaps = ComputeGaps(sorted);

            if (gaps.Count == 0) continue;

            // ── 6. Detect best frequency ───────────────────────────────────────
            var (frequency, matchingGapCount) = DetectFrequency(sorted, gaps);
            if (frequency == null) continue;

            // ── 7. Enforce minimum occurrence counts ───────────────────────────
            int occurrences = sorted.Count;
            if (occurrences < GetMinOccurrences(frequency)) continue;

            // ── 8. Confidence scoring ─────────────────────────────────────────
            double gapMatchRatio = gaps.Count > 0
                ? (double)matchingGapCount / gaps.Count
                : 0.0;

            double baseScore = gapMatchRatio * 100.0;

            // Penalize high amount variability (CV > 0.1 starts costing points)
            var amounts = sorted.Select(t => t.Amount).ToList();
            double cv = ComputeCoefficientOfVariation(amounts);
            double amountPenalty = Math.Max(0.0, cv * 50.0);

            // Penalize being barely at the minimum occurrence count
            double requiredForFullScore = GetMinOccurrences(frequency) * 1.5 + 1.0;
            double occurrenceRatio = Math.Min(1.0, occurrences / requiredForFullScore);
            double occurrencePenalty = Math.Max(0.0, (1.0 - occurrenceRatio) * 15.0);

            // Extra penalty for soft-excluded and payment-app groups
            double categoryPenalty = isSoftExclude ? 15.0 : isPaymentApp ? 10.0 : 0.0;

            double rawConfidence = baseScore - amountPenalty - occurrencePenalty - categoryPenalty;
            int confidence = (int)Math.Round(Math.Clamp(rawConfidence, 0.0, 100.0));

            // ── 9. Threshold gate ─────────────────────────────────────────────
            int threshold = (isSoftExclude || isPaymentApp) ? ElevatedThreshold : NormalThreshold;
            if (confidence < threshold) continue;

            // ── 10. Build DTO ─────────────────────────────────────────────────
            decimal estimatedAmount = Median(amounts);
            decimal amountMin = amounts.Min();
            decimal amountMax = amounts.Max();

            // Use the most recently seen original merchant name for display
            string displayName = sorted.Last().MerchantName
                              ?? sorted.Last().Name
                              ?? normalizedName;

            string lastSeenDate = sorted.Last().Date.ToString("yyyy-MM-dd");
            string? nextDate = ComputeNextDate(sorted.Last().Date, frequency, sorted);
            string? warning = isPaymentApp ? PaymentAppWarning : null;

            results.Add(new RecurringSuggestionDto(
                MerchantName: displayName,
                NormalizedName: normalizedName,
                EstimatedAmount: estimatedAmount,
                AmountMin: amountMin,
                AmountMax: amountMax,
                Frequency: frequency,
                Confidence: confidence,
                OccurrenceCount: occurrences,
                LastSeenDate: lastSeenDate,
                NextEstimatedDate: nextDate,
                Warning: warning
            ));
        }

        // ── 11. Sort: confidence descending, then amount descending ────────────
        return results
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.EstimatedAmount)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Normalization
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalize a merchant/transaction name for grouping.
    /// Strips trailing IDs/tokens, uppercases, collapses whitespace.
    /// </summary>
    internal static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var n = name.ToUpperInvariant().Trim();

        // Strip common trailing reference tokens (e.g. "NETFLIX *Q4XR1", "SPOTIFY 7F3A9B")
        n = TrailingTokenRegex.Replace(n, string.Empty).Trim();

        // Collapse multiple spaces
        n = MultiSpaceRegex.Replace(n, " ").Trim();

        return n;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Exclusion helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsHardExcluded(string normalizedName, List<Transaction> group)
    {
        // Income / credit transactions (Paycheck, Windfall, InternalTransfer, Refund)
        // are identified by SuggestedKind != Unknown.  A group is considered income
        // when the majority of its transactions are classified that way.
        int incomeCount = group.Count(t => t.SuggestedKind != TransactionSuggestedKind.Unknown);
        if (incomeCount > group.Count / 2) return true;

        // Keyword-based hard exclusions
        return ContainsAny(normalizedName, HardExcludeFragments);
    }

    private static bool ContainsAny(string name, string[] fragments)
    {
        foreach (var f in fragments)
        {
            if (name.Contains(f, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap computation
    // ─────────────────────────────────────────────────────────────────────────

    private static List<int> ComputeGaps(List<Transaction> sorted)
    {
        var gaps = new List<int>(sorted.Count - 1);
        for (int i = 1; i < sorted.Count; i++)
        {
            int dayGap = (int)(sorted[i].Date.Date - sorted[i - 1].Date.Date).TotalDays;
            if (dayGap > 0) gaps.Add(dayGap);
        }
        return gaps;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frequency detection
    // ─────────────────────────────────────────────────────────────────────────

    private static (string? Frequency, int MatchingGaps) DetectFrequency(
        List<Transaction> sorted,
        List<int> gaps)
    {
        // ── Semi-monthly check first (calendar-based, not purely gap-based) ───
        if (TryDetectSemiMonthly(sorted))
        {
            // Use gaps in the semi-monthly range for confidence ratio
            int semiGaps = CountInRange(gaps, 10, 20);
            return ("SemiMonthly", semiGaps);
        }

        // ── Standard gap-bucket scoring ───────────────────────────────────────
        var candidates = new (string Freq, int Matches)[]
        {
            ("Weekly",    CountInRange(gaps, WeeklyMin,    WeeklyMax)),
            ("Biweekly",  CountInRange(gaps, BiweeklyMin,  BiweeklyMax)),
            ("Monthly",   CountInRange(gaps, MonthlyMin,   MonthlyMax)),
            ("Quarterly", CountInRange(gaps, QuarterlyMin, QuarterlyMax)),
        };

        // Pick the bucket with the highest match ratio; ties broken by tighter (more frequent) range
        var best = candidates
            .Where(c => c.Matches > 0)
            .OrderByDescending(c => (double)c.Matches / gaps.Count)
            .FirstOrDefault();

        return best == default ? (null, 0) : (best.Freq, best.Matches);
    }

    /// <summary>
    /// Returns true when transactions show a semi-monthly pattern:
    /// at least 3 calendar months each containing exactly 2 occurrences,
    /// with the first and second day-of-month each having low variance (≤ 5 days).
    /// </summary>
    private static bool TryDetectSemiMonthly(List<Transaction> sorted)
    {
        var byMonth = sorted
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Where(g => g.Count() == 2)
            .ToList();

        if (byMonth.Count < SemiMonthlyMinOcc) return false;

        var days1 = byMonth.Select(g => (double)g.OrderBy(t => t.Date).First().Date.Day).ToList();
        var days2 = byMonth.Select(g => (double)g.OrderBy(t => t.Date).Last().Date.Day).ToList();

        return ComputeStdDev(days1) <= 5.0 && ComputeStdDev(days2) <= 5.0;
    }

    private static int CountInRange(List<int> gaps, int min, int max) =>
        gaps.Count(g => g >= min && g <= max);

    private static int GetMinOccurrences(string frequency) => frequency switch
    {
        "Weekly" => WeeklyMinOcc,
        "Biweekly" => BiweeklyMinOcc,
        "Monthly" => MonthlyMinOcc,
        "SemiMonthly" => SemiMonthlyMinOcc * 2, // 3 pairs = 6 total occurrences
        "Quarterly" => QuarterlyMinOcc,
        _ => 3
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Next-date estimation
    // ─────────────────────────────────────────────────────────────────────────

    private static string? ComputeNextDate(
        DateTime lastDate,
        string frequency,
        List<Transaction> sorted) => frequency switch
        {
            "Weekly" => lastDate.AddDays(7).ToString("yyyy-MM-dd"),
            "Biweekly" => lastDate.AddDays(14).ToString("yyyy-MM-dd"),
            "Monthly" => lastDate.AddMonths(1).ToString("yyyy-MM-dd"),
            "SemiMonthly" => ComputeNextSemiMonthlyDate(lastDate, sorted),
            "Quarterly" => lastDate.AddMonths(3).ToString("yyyy-MM-dd"),
            _ => null
        };

    private static string? ComputeNextSemiMonthlyDate(
        DateTime lastDate,
        List<Transaction> sorted)
    {
        var byMonth = sorted
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Where(g => g.Count() >= 2)
            .ToList();

        if (byMonth.Count == 0)
            return lastDate.AddDays(15).ToString("yyyy-MM-dd");

        int avgDay1 = (int)Math.Round(
            byMonth.Average(g => g.OrderBy(t => t.Date).First().Date.Day));
        int avgDay2 = (int)Math.Round(
            byMonth.Average(g => g.OrderBy(t => t.Date).Last().Date.Day));

        int lastDay = lastDate.Day;

        // Determine which cluster day the last occurrence was closest to,
        // then compute the next occurrence.
        bool closerToDay1 = Math.Abs(lastDay - avgDay1) <= Math.Abs(lastDay - avgDay2);

        DateTime candidate;
        if (closerToDay1)
        {
            // Last seen was near avgDay1; next is avgDay2 this month (if still ahead)
            if (avgDay2 > lastDay)
            {
                int d = Math.Min(avgDay2, DateTime.DaysInMonth(lastDate.Year, lastDate.Month));
                candidate = new DateTime(lastDate.Year, lastDate.Month, d);
            }
            else
            {
                // avgDay2 already passed — next is avgDay1 next month
                var nm = new DateTime(lastDate.Year, lastDate.Month, 1).AddMonths(1);
                int d = Math.Min(avgDay1, DateTime.DaysInMonth(nm.Year, nm.Month));
                candidate = new DateTime(nm.Year, nm.Month, d);
            }
        }
        else
        {
            // Last seen was near avgDay2; next is avgDay1 next month
            var nm = new DateTime(lastDate.Year, lastDate.Month, 1).AddMonths(1);
            int d = Math.Min(avgDay1, DateTime.DaysInMonth(nm.Year, nm.Month));
            candidate = new DateTime(nm.Year, nm.Month, d);
        }

        return candidate.ToString("yyyy-MM-dd");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Statistics helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0m;
        var s = values.OrderBy(v => v).ToList();
        int mid = s.Count / 2;
        return s.Count % 2 == 0 ? (s[mid - 1] + s[mid]) / 2m : s[mid];
    }

    private static double ComputeCoefficientOfVariation(List<decimal> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = (double)values.Average();
        if (mean == 0.0) return 0.0;
        return ComputeStdDev(values.Select(v => (double)v).ToList()) / mean;
    }

    private static double ComputeStdDev(List<double> values)
    {
        if (values.Count < 2) return 0.0;
        double mean = values.Average();
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Debug / audit entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the same detection pipeline as <see cref="Analyze"/> but returns a
    /// per-group diagnostic record for EVERY normalized group encountered,
    /// including groups that were rejected and the reason they were rejected.
    ///
    /// Used exclusively by the admin/debug recurring-suggestions-audit endpoint.
    /// Never called from the public suggestions endpoint.
    /// </summary>
    public static List<RecurringSuggestionAuditGroupDto> AnalyzeGroupsForDebug(
        IReadOnlyList<Transaction> transactions,
        DateTime cutoffDate)
    {
        var results = new List<RecurringSuggestionAuditGroupDto>();

        if (transactions == null || transactions.Count == 0)
            return results;

        // ── Pre-filter (same as Analyze): non-pending, in window, amount > 0 ──
        var candidates = transactions
            .Where(t => !t.Pending && t.Date >= cutoffDate && t.Amount > 0)
            .ToList();

        if (candidates.Count == 0)
            return results;

        // ── Group by normalized name (include singletons so caller sees them) ──
        var groups = candidates
            .GroupBy(t => NormalizeName(t.MerchantName ?? t.Name ?? string.Empty))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        foreach (var group in groups)
        {
            string normalizedName = group.Key;
            var txList = group.ToList();

            // ── Build sample fields (independent of rejection) ─────────────────
            var sorted = txList.OrderBy(t => t.Date).ToList();
            var amounts = sorted.Select(t => t.Amount).ToList();
            var dates = sorted.Select(t => t.Date.ToString("yyyy-MM-dd")).ToList();
            var gaps = ComputeGaps(sorted);

            string displayName = sorted.Last().MerchantName
                              ?? sorted.Last().Name
                              ?? normalizedName;

            var sampleNames = sorted
                .Select(t => t.Name ?? string.Empty)
                .Distinct()
                .Take(5)
                .ToList();

            var sampleMerchants = sorted
                .Select(t => t.MerchantName ?? string.Empty)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .Take(5)
                .ToList();

            var sampleKinds = sorted
                .Select(t => t.SuggestedKind.ToString())
                .Distinct()
                .ToList();

            decimal amtMin = amounts.Count > 0 ? amounts.Min() : 0m;
            decimal amtMax = amounts.Count > 0 ? amounts.Max() : 0m;
            double amtAvg = amounts.Count > 0 ? (double)amounts.Average() : 0.0;
            decimal amtMedian = Median(amounts);
            double cv = ComputeCoefficientOfVariation(amounts);

            // ── Gate 1: singleton groups (too few to group in Analyze) ─────────
            if (txList.Count < 2)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: txList.Count,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: null,
                    GapMatchRatio: 0.0,
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: 0,
                    IsHardExcluded: false,
                    IsSoftExcluded: false,
                    IsPaymentApp: false,
                    Included: false,
                    RejectionReason: $"singleton-group (count={txList.Count}, minimum=2)",
                    Warning: null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Gate 2: hard exclude ───────────────────────────────────────────
            bool isHardExcluded = false;
            string? hardExcludeReason = null;

            int incomeCount = txList.Count(t => t.SuggestedKind != TransactionSuggestedKind.Unknown);
            if (incomeCount > txList.Count / 2)
            {
                isHardExcluded = true;
                hardExcludeReason =
                    $"hard-excluded: majority-income ({incomeCount}/{txList.Count} transactions have SuggestedKind != Unknown)";
            }
            else
            {
                foreach (var frag in HardExcludeFragments)
                {
                    if (normalizedName.Contains(frag, StringComparison.OrdinalIgnoreCase))
                    {
                        isHardExcluded = true;
                        hardExcludeReason = $"hard-excluded: keyword '{frag}'";
                        break;
                    }
                }
            }

            if (isHardExcluded)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: txList.Count,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: null,
                    GapMatchRatio: 0.0,
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: 0,
                    IsHardExcluded: true,
                    IsSoftExcluded: false,
                    IsPaymentApp: false,
                    Included: false,
                    RejectionReason: hardExcludeReason,
                    Warning: null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Classify sensitivity (same as Analyze) ─────────────────────────
            bool isPaymentApp = ContainsAny(normalizedName, PaymentAppFragments);
            bool isSoftExclude = !isPaymentApp && ContainsAny(normalizedName, SoftExcludeFragments);

            // ── Gate 3: no positive gaps ───────────────────────────────────────
            if (gaps.Count == 0)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: txList.Count,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: null,
                    GapMatchRatio: 0.0,
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: 0,
                    IsHardExcluded: false,
                    IsSoftExcluded: isSoftExclude,
                    IsPaymentApp: isPaymentApp,
                    Included: false,
                    RejectionReason: "no-positive-gaps (all occurrences on same date)",
                    Warning: null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Gate 4: frequency detection ────────────────────────────────────
            var (frequency, matchingGapCount) = DetectFrequency(sorted, gaps);
            double gapMatchRatio = gaps.Count > 0 ? (double)matchingGapCount / gaps.Count : 0.0;

            if (frequency == null)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: txList.Count,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: null,
                    GapMatchRatio: 0.0,
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: 0,
                    IsHardExcluded: false,
                    IsSoftExcluded: isSoftExclude,
                    IsPaymentApp: isPaymentApp,
                    Included: false,
                    RejectionReason: $"no-matching-frequency (gaps: [{string.Join(", ", gaps)}])",
                    Warning: null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Gate 5: minimum occurrence count ───────────────────────────────
            int occurrences = sorted.Count;
            int requiredOcc = GetMinOccurrences(frequency);

            if (occurrences < requiredOcc)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: occurrences,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: frequency,
                    GapMatchRatio: Math.Round(gapMatchRatio, 4),
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: 0,
                    IsHardExcluded: false,
                    IsSoftExcluded: isSoftExclude,
                    IsPaymentApp: isPaymentApp,
                    Included: false,
                    RejectionReason: $"too-few-occurrences (count={occurrences}, required={requiredOcc}, freq={frequency})",
                    Warning: null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Confidence scoring (same formula as Analyze) ───────────────────
            double baseScore = gapMatchRatio * 100.0;
            double amountPenalty = Math.Max(0.0, cv * 50.0);
            double requiredForFullScore = GetMinOccurrences(frequency) * 1.5 + 1.0;
            double occurrenceRatio = Math.Min(1.0, occurrences / requiredForFullScore);
            double occurrencePenalty = Math.Max(0.0, (1.0 - occurrenceRatio) * 15.0);
            double categoryPenalty = isSoftExclude ? 15.0 : isPaymentApp ? 10.0 : 0.0;
            double rawConfidence = baseScore - amountPenalty - occurrencePenalty - categoryPenalty;
            int confidence = (int)Math.Round(Math.Clamp(rawConfidence, 0.0, 100.0));

            // ── Gate 6: confidence threshold ───────────────────────────────────
            int threshold = (isSoftExclude || isPaymentApp) ? ElevatedThreshold : NormalThreshold;
            if (confidence < threshold)
            {
                results.Add(new RecurringSuggestionAuditGroupDto(
                    NormalizedName: normalizedName,
                    DisplayName: displayName,
                    OccurrenceCount: occurrences,
                    Dates: dates,
                    Amounts: amounts,
                    AmountMin: amtMin,
                    AmountMax: amtMax,
                    AmountAverage: Math.Round(amtAvg, 4),
                    AmountMedian: amtMedian,
                    DayGaps: gaps,
                    LikelyFrequency: frequency,
                    GapMatchRatio: Math.Round(gapMatchRatio, 4),
                    AmountCoefficientOfVariation: Math.Round(cv, 4),
                    Confidence: confidence,
                    IsHardExcluded: false,
                    IsSoftExcluded: isSoftExclude,
                    IsPaymentApp: isPaymentApp,
                    Included: false,
                    RejectionReason: $"confidence-below-threshold (score={confidence}, threshold={threshold})",
                    Warning: isPaymentApp ? PaymentAppWarning : null,
                    SampleTransactionNames: sampleNames,
                    SampleMerchantNames: sampleMerchants,
                    SampleSuggestedKinds: sampleKinds
                ));
                continue;
            }

            // ── Passed all gates — included ────────────────────────────────────
            results.Add(new RecurringSuggestionAuditGroupDto(
                NormalizedName: normalizedName,
                DisplayName: displayName,
                OccurrenceCount: occurrences,
                Dates: dates,
                Amounts: amounts,
                AmountMin: amtMin,
                AmountMax: amtMax,
                AmountAverage: Math.Round(amtAvg, 4),
                AmountMedian: amtMedian,
                DayGaps: gaps,
                LikelyFrequency: frequency,
                GapMatchRatio: Math.Round(gapMatchRatio, 4),
                AmountCoefficientOfVariation: Math.Round(cv, 4),
                Confidence: confidence,
                IsHardExcluded: false,
                IsSoftExcluded: isSoftExclude,
                IsPaymentApp: isPaymentApp,
                Included: true,
                RejectionReason: null,
                Warning: isPaymentApp ? PaymentAppWarning : null,
                SampleTransactionNames: sampleNames,
                SampleMerchantNames: sampleMerchants,
                SampleSuggestedKinds: sampleKinds
            ));
        }

        // Return: included groups first, then rejected; within each bucket sort by confidence desc
        return results
            .OrderByDescending(r => r.Included ? 1 : 0)
            .ThenByDescending(r => r.Confidence)
            .ThenBy(r => r.NormalizedName)
            .ToList();
    }
}
