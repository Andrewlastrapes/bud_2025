using BudgetApp.Api.Data;

namespace BudgetApp.Api.Services;

/// <summary>
/// Determines whether an incoming debit transaction corresponds to a known fixed/recurring cost.
///
/// Match priority:
///   1. plaid_discovered (or previously enriched manual): exact <c>PlaidMerchantName</c> match.
///   2. manual, NextDueDate set: amount within tolerance AND transaction date within ±7 days
///      of the stored due date.  This is the main path for user-entered bills like car payments,
///      phone bills, daycare, etc.
///   3. manual, NextDueDate null: amount within tolerance AND name-token overlap between
///      the fixed-cost name and the transaction merchant name.  Amount-only matching is
///      intentionally rejected here because the false-positive risk is too high without a
///      date anchor.
///
/// Amount tolerance: max($2.00, 2% of stored fixed-cost amount).
/// Due-date window:  ±7 calendar days from <c>NextDueDate</c>.
///
/// Returns the matched <c>FixedCost</c> and a human-readable <c>MatchType</c> string,
/// or <c>(null, "")</c> if no match is found.
///
/// The MatchType is intentionally captured BEFORE any PlaidMerchantName enrichment so that
/// callers can log the true reason for the match without the enriched value polluting it.
/// </summary>
public static class FixedCostMatcher
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Minimum absolute dollar tolerance when 2% is smaller than this.</summary>
    public const decimal AbsoluteTolerance = 2m;

    /// <summary>Relative tolerance: 2% of the fixed-cost amount.</summary>
    public const decimal RelativeTolerance = 0.02m;

    /// <summary>Max calendar days between transaction date and NextDueDate for a match.</summary>
    public const int DueDateWindowDays = 7;

    /// <summary>Minimum token length to consider in name-overlap check.</summary>
    private const int MinTokenLength = 3;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to match a debit transaction against the user's fixed costs.
    /// </summary>
    /// <param name="fixedCosts">All fixed costs for the user.</param>
    /// <param name="merchantName">Resolved merchant name from Plaid (MerchantName ?? Name).</param>
    /// <param name="transactionAmount">Absolute (positive) transaction amount.</param>
    /// <param name="transactionDate">UTC date of the transaction (Plaid Date field).</param>
    /// <returns>
    /// A tuple of the matched <see cref="FixedCost"/> and a match-type label,
    /// or <c>(null, "")</c> when no match is found.
    /// </returns>
    public static (FixedCost? Match, string MatchType) TryMatch(
        IEnumerable<FixedCost> fixedCosts,
        string merchantName,
        decimal transactionAmount,
        DateTime transactionDate)
    {
        var costs = fixedCosts as IList<FixedCost> ?? fixedCosts.ToList();

        // ── Priority 1: exact PlaidMerchantName match ──────────────────────────
        // Covers both plaid_discovered costs and manual costs that were previously
        // enriched with a merchant name after an earlier amount/date match.
        var byMerchant = costs.FirstOrDefault(fc =>
            !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
            fc.PlaidMerchantName.Equals(merchantName, StringComparison.OrdinalIgnoreCase));

        if (byMerchant != null)
            return (byMerchant, "merchant-name");

        // ── Priority 2: manual fixed costs — amount + date or name-token ───────
        foreach (var fc in costs.Where(fc => fc.Type == "manual"))
        {
            // Amount tolerance: max($2, 2% of stored amount)
            decimal tolerance = Math.Max(AbsoluteTolerance, fc.Amount * RelativeTolerance);
            if (Math.Abs(transactionAmount - fc.Amount) > tolerance)
                continue;

            // Amount passes — now check the secondary confidence signal.
            if (fc.NextDueDate.HasValue)
            {
                // Path 2a: due-date anchor — amount + date window is sufficient confidence.
                int daysDiff = (int)Math.Abs(
                    (transactionDate.Date - fc.NextDueDate.Value.Date).TotalDays);

                if (daysDiff <= DueDateWindowDays)
                    return (fc, "manual-amount-date");

                // Amount matches but date is too far from due date — skip this cost.
                continue;
            }
            else
            {
                // Path 2b: no due-date anchor — require name-token overlap to reduce
                // false positives from coincidentally similar amounts.
                if (HasNameTokenOverlap(fc.Name, merchantName))
                    return (fc, "manual-amount-name-token");

                // Amount matches but no other signal — too risky, skip.
                continue;
            }
        }

        return (null, string.Empty);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if at least one word (≥3 characters) from <paramref name="fixedCostName"/>
    /// appears anywhere in <paramref name="merchantName"/> (case-insensitive).
    ///
    /// Examples:
    ///   "Netflix"       vs "Netflix"              → true
    ///   "AT&amp;T Wireless" vs "AT&amp;T"         → true  ("att" or "wireless" in "at&amp;t")
    ///   "Gym"           vs "LA Fitness"            → false (no token from "Gym" in "la fitness")
    ///   "Car Payment"   vs "Toyota Financial"      → false
    /// </summary>
    public static bool HasNameTokenOverlap(string fixedCostName, string merchantName)
    {
        if (string.IsNullOrWhiteSpace(fixedCostName) || string.IsNullOrWhiteSpace(merchantName))
            return false;

        // Tokenise by whitespace and common separators; keep tokens ≥ MinTokenLength chars.
        var fcTokens = Tokenise(fixedCostName);
        if (fcTokens.Count == 0)
            return false;

        // Normalise the full merchant string once for substring searching.
        string normMerchant = merchantName.ToLowerInvariant();

        return fcTokens.Any(token => normMerchant.Contains(token));
    }

    private static HashSet<string> Tokenise(string value)
    {
        return value
            .Split(new[] { ' ', '-', '_', '&', '.', '/', '\\', '(', ')' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= MinTokenLength)
            .ToHashSet();
    }
}
