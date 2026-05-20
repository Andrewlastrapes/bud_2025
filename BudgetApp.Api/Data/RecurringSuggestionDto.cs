namespace BudgetApp.Api.Data;

/// <summary>
/// DTO returned by GET /api/recurring/suggestions.
/// Built entirely from internal transaction-history analysis — never from Plaid recurring streams.
/// Contains no EF entities, no navigation properties.
/// </summary>
public record RecurringSuggestionDto(
    /// <summary>Display name (original merchant name from the most recent transaction in the group).</summary>
    string MerchantName,

    /// <summary>Normalized name used for grouping (uppercase, trailing IDs stripped).</summary>
    string NormalizedName,

    /// <summary>Median observed amount — best estimate for budget planning.</summary>
    decimal EstimatedAmount,

    /// <summary>Lowest observed amount across all occurrences in the look-back window.</summary>
    decimal AmountMin,

    /// <summary>Highest observed amount across all occurrences in the look-back window.</summary>
    decimal AmountMax,

    /// <summary>Detected recurrence pattern: "Weekly" | "Biweekly" | "Monthly" | "SemiMonthly" | "Quarterly".</summary>
    string Frequency,

    /// <summary>0–100 confidence score. Only suggestions with score ≥ 60 (normal) or ≥ 80 (soft-exclude / payment-app) are returned.</summary>
    int Confidence,

    /// <summary>Number of matching transactions found in the look-back window.</summary>
    int OccurrenceCount,

    /// <summary>Date of the most recent occurrence, formatted yyyy-MM-dd.</summary>
    string LastSeenDate,

    /// <summary>Estimated next occurrence date, formatted yyyy-MM-dd. Null if unknown.</summary>
    string? NextEstimatedDate,

    /// <summary>
    /// Optional advisory message for ambiguous recurring candidates (e.g. payment apps like Venmo/Zelle).
    /// Null for clearly identified recurring costs.
    /// </summary>
    string? Warning
);
