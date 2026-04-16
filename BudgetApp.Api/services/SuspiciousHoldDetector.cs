namespace BudgetApp.Api.Services;

/// <summary>
/// Detects pending pre-auth holds that are likely to be much larger than the
/// final posted charge. Gas stations, hotels, and rental car companies are
/// the three most common sources of suspiciously-high pending holds.
///
/// Rules are deliberately simple: keyword match on the display name + a
/// per-category dollar threshold. Both constants are easy to tune.
/// </summary>
public static class SuspiciousHoldDetector
{
    // ─── Thresholds ───────────────────────────────────────────────────────────
    // A pending charge above these amounts for the relevant category is flagged.

    private const decimal GasThreshold       = 150m;  // Most fill-ups are < $150
    private const decimal HotelThreshold     = 300m;  // Pre-auth holds vary widely
    private const decimal RentalCarThreshold = 200m;  // Rental deposits/holds

    // ─── Merchant keyword lists ───────────────────────────────────────────────

    private static readonly string[] GasKeywords = new[]
    {
        "gas", "fuel", "shell", "bp", "chevron", "exxon", "mobil", "sunoco",
        "arco", "citgo", "76 ", " 76", "speedway", "wawa", "circle k",
        "marathon", "pilot flying", "love's", "casey's", "kwik trip",
        "kwik fill", "holiday station", "racetrac", "raceway", "getgo",
        "meijer gas", "sheetz", "thorntons", "stripes", "buc-ee"
    };

    private static readonly string[] HotelKeywords = new[]
    {
        "hotel", "marriott", "hilton", "hyatt", "holiday inn", "sheraton",
        "westin", "motel", "inn", "resort", "airbnb", "vrbo", "lodging",
        "suites", "hampton inn", "doubletree", "courtyard", "fairfield",
        "renaissance", "w hotel", "st. regis", "ritz carlton", "four seasons",
        "ihg", "radisson", "wyndham", "la quinta", "best western",
        "comfort inn", "quality inn", "days inn", "super 8", "extended stay"
    };

    private static readonly string[] RentalCarKeywords = new[]
    {
        "hertz", "avis", "enterprise", "national car", "alamo", "thrifty",
        "dollar rent", "sixt", "budget car", "car rental", "auto rental",
        "vehicle rental", "ace rent", "fox rent"
    };

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this pending transaction looks like a suspicious pre-auth hold
    /// that is likely much larger than what will actually be charged.
    /// </summary>
    /// <param name="merchantName">Plaid merchant_name (may be null)</param>
    /// <param name="transactionName">Plaid transaction name / description</param>
    /// <param name="amount">Absolute value of the charge (positive = outflow)</param>
    /// <param name="isPending">Must be true; posted transactions are never flagged</param>
    public static bool IsSuspiciousHold(
        string? merchantName,
        string transactionName,
        decimal amount,
        bool isPending)
    {
        if (!isPending || amount <= 0)
            return false;

        // Use the richer of the two name fields
        var name = (merchantName ?? transactionName ?? string.Empty).ToLowerInvariant();

        if (MatchesKeywords(name, GasKeywords) && amount > GasThreshold)
            return true;

        if (MatchesKeywords(name, HotelKeywords) && amount > HotelThreshold)
            return true;

        if (MatchesKeywords(name, RentalCarKeywords) && amount > RentalCarThreshold)
            return true;

        return false;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static bool MatchesKeywords(string name, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}