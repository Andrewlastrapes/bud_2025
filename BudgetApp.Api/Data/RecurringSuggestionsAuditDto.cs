namespace BudgetApp.Api.Data;

/// <summary>
/// Per-group diagnostic record returned by
/// GET /api/admin/debug/users/{userId}/recurring-suggestions-audit.
///
/// Contains every intermediate value computed by RecurringSuggestionsAnalyzer
/// so the caller can determine exactly why a group was included or rejected.
/// Never exposes EF entities or navigation properties.
/// </summary>
public record RecurringSuggestionAuditGroupDto(
    /// <summary>Normalized name used for grouping (uppercase, trailing IDs stripped).</summary>
    string NormalizedName,

    /// <summary>Best display name from the most recent transaction in the group.</summary>
    string DisplayName,

    /// <summary>Number of matching transactions found in the look-back window.</summary>
    int OccurrenceCount,

    /// <summary>Transaction dates (yyyy-MM-dd), sorted ascending.</summary>
    IReadOnlyList<string> Dates,

    /// <summary>Transaction amounts, sorted ascending by date (parallel to Dates).</summary>
    IReadOnlyList<decimal> Amounts,

    decimal AmountMin,
    decimal AmountMax,
    double AmountAverage,
    decimal AmountMedian,

    /// <summary>
    /// Consecutive day-gaps between sorted transaction dates.
    /// Count = OccurrenceCount - 1 (positive gaps only).
    /// </summary>
    IReadOnlyList<int> DayGaps,

    /// <summary>Best-detected frequency: "Weekly" | "Biweekly" | "Monthly" | "SemiMonthly" | "Quarterly" | null.</summary>
    string? LikelyFrequency,

    /// <summary>Ratio of gaps that fell within the detected frequency's window (0.0–1.0).</summary>
    double GapMatchRatio,

    /// <summary>
    /// Coefficient of variation of transaction amounts (stdDev / mean).
    /// Higher values indicate variable amounts which lower confidence.
    /// </summary>
    double AmountCoefficientOfVariation,

    /// <summary>Raw computed confidence score (0–100) before threshold gate.</summary>
    int Confidence,

    bool IsHardExcluded,
    bool IsSoftExcluded,
    bool IsPaymentApp,

    /// <summary>True if this group passed all gates and appears in suggestions.</summary>
    bool Included,

    /// <summary>
    /// Human-readable reason this group was rejected, or null if included.
    /// Examples:
    ///   "hard-excluded: majority-income (3/3 transactions have SuggestedKind != Unknown)"
    ///   "hard-excluded: keyword 'FIDELITY'"
    ///   "no-positive-gaps"
    ///   "no-matching-frequency (gaps: [1, 45, 3])"
    ///   "too-few-occurrences (count=2, required=3, freq=Monthly)"
    ///   "confidence-below-threshold (score=42, threshold=60)"
    /// </summary>
    string? RejectionReason,

    /// <summary>Advisory message for payment-app groups (Venmo, Zelle, etc.).</summary>
    string? Warning,

    /// <summary>Up to 5 sample raw transaction Name values for this group.</summary>
    IReadOnlyList<string> SampleTransactionNames,

    /// <summary>Up to 5 sample MerchantName values for this group (may contain duplicates).</summary>
    IReadOnlyList<string> SampleMerchantNames,

    /// <summary>Distinct SuggestedKind enum names seen in this group.</summary>
    IReadOnlyList<string> SampleSuggestedKinds
);
