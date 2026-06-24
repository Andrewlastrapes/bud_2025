using System;

namespace BudgetApp.Api.Services;

/// <summary>
/// Classification subtype for transfer-like transactions.
/// Used to determine budget impact policy and recurring suggestion exclusion.
/// </summary>
public enum TransferSubtype
{
    /// <summary>Not a transfer-like transaction.</summary>
    None,

    /// <summary>
    /// Credit card payment confirmation (e.g., "ONLINE PAYMENT - THANK YOU").
    /// Budget policy: zero impact (underlying purchases already counted).
    /// </summary>
    CreditCardPayment,

    /// <summary>
    /// Wallet or account load (e.g., "AMEX SEND: ADD MONEY").
    /// Budget policy: zero impact (account movement, not merchant spend).
    /// </summary>
    WalletLoad,

    /// <summary>
    /// Transfer between user's own accounts (e.g., "TRANSFER TO SAVINGS").
    /// Budget policy: deferred to separate transfer-review feature.
    /// </summary>
    OwnAccountTransfer,

    /// <summary>
    /// Brokerage or investment transfer (e.g., "FIDELITY", "VANGUARD").
    /// Budget policy: deferred to separate transfer-review feature.
    /// </summary>
    InvestmentTransfer
}

/// <summary>
/// Result of transfer-like transaction classification.
/// </summary>
public record TransferClassification
{
    /// <summary>True if this transaction matches any transfer-like pattern.</summary>
    public bool IsTransferLike { get; init; }

    /// <summary>The specific transfer subtype, or None if not transfer-like.</summary>
    public TransferSubtype Subtype { get; init; }

    /// <summary>
    /// True if this transaction should be excluded from recurring/fixed-cost suggestions.
    /// All transfer subtypes are excluded from recurring suggestions.
    /// </summary>
    public bool ShouldExcludeFromRecurringSuggestions { get; init; }

    /// <summary>
    /// True if this transaction should have zero budget impact.
    /// Only CreditCardPayment and WalletLoad are zero-impact.
    /// OwnAccountTransfer and InvestmentTransfer are deferred to separate review feature.
    /// </summary>
    public bool ShouldZeroBudgetImpact { get; init; }

    /// <summary>Human-readable reason for the classification.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The specific keyword/pattern that triggered the classification, if any.</summary>
    public string? MatchedRule { get; init; }
}

/// <summary>
/// Shared classifier for transfer-like transactions.
/// Identifies credit card payments, wallet loads, account transfers, and investment transfers.
/// Used by TransactionService, RecurringSuggestionsAnalyzer, and API endpoint guards.
/// </summary>
public static class TransferLikeClassifier
{
    /// <summary>
    /// Classifies a transaction as transfer-like based on name/merchant patterns.
    /// </summary>
    /// <param name="name">Transaction name from Plaid.</param>
    /// <param name="merchantName">Merchant name from Plaid (may be null).</param>
    /// <param name="amount">Transaction amount (positive absolute value).</param>
    /// <param name="isCredit">True if this is a credit/deposit, false if debit/spend.</param>
    /// <returns>Classification result with subtype and budget impact policy.</returns>
    public static TransferClassification Classify(
        string? name,
        string? merchantName,
        decimal amount,
        bool isCredit)
    {
        // Combine name and merchant for pattern matching
        var combined = $"{name ?? string.Empty} {merchantName ?? string.Empty}".ToUpperInvariant();

        // ── CreditCardPayment ──────────────────────────────────────────────────
        // Credit card payment confirmations — zero budget impact because the
        // underlying card purchases were or will be counted separately.
        if (ContainsPattern(combined, "ONLINE PAYMENT - THANK YOU", "ONLINE PAYMENT-THANK YOU"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "ONLINE PAYMENT - THANK YOU");

        if (ContainsPattern(combined, "PAYMENT THANK YOU"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "PAYMENT THANK YOU");

        if (ContainsPattern(combined, "CREDIT CARD PAYMENT"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "CREDIT CARD PAYMENT");

        if (ContainsPattern(combined, "CREDIT CARD PMT"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "CREDIT CARD PMT");

        if (ContainsPattern(combined, "CARD PAYMENT"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "CARD PAYMENT");

        if (ContainsPattern(combined, "PAYMENT FROM CHK"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "PAYMENT FROM CHK");

        // AUTOPAY is only high-confidence when combined with credit card / card / loan / mortgage
        if (ContainsPattern(combined, "CREDIT CARD AUTOPAY"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "CREDIT CARD AUTOPAY");

        if (ContainsPattern(combined, "CARD AUTOPAY"))
            return CreateClassification(TransferSubtype.CreditCardPayment, "CARD AUTOPAY");

        // Note: Standalone "AUTOPAY" is intentionally NOT classified as transfer-like.
        // It can appear on legitimate recurring bills, subscriptions, gym dues, utilities.

        // ── WalletLoad ─────────────────────────────────────────────────────────
        // Wallet or account loads — zero budget impact because this is account
        // movement, not merchant spend.
        if (ContainsPattern(combined, "AMEX SEND"))
            return CreateClassification(TransferSubtype.WalletLoad, "AMEX SEND");

        // Note: Generic "ADD MONEY" alone is intentionally NOT classified as transfer-like.
        // It's too broad and could match legitimate merchant names.

        // ── OwnAccountTransfer ─────────────────────────────────────────────────
        // Transfers between user's own accounts — excluded from recurring suggestions,
        // but budget impact handling is deferred to separate transfer-review feature.
        if (ContainsPattern(combined, "TRANSFER TO SAVINGS"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "TRANSFER TO SAVINGS", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "TRANSFER TO CHECKING"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "TRANSFER TO CHECKING", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "SAVINGS TRANSFER"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "SAVINGS TRANSFER", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "ACCOUNT TRANSFER"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "ACCOUNT TRANSFER", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "INTERNAL TRANSFER"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "INTERNAL TRANSFER", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "OWN ACCOUNT"))
            return CreateClassification(TransferSubtype.OwnAccountTransfer, "OWN ACCOUNT", zeroBudgetImpact: false);

        // ── InvestmentTransfer ─────────────────────────────────────────────────
        // Brokerage/investment transfers — excluded from recurring suggestions,
        // but budget impact handling is deferred to separate transfer-review feature.
        if (ContainsPattern(combined, "BROKERAGE TRANSFER"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "BROKERAGE TRANSFER", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "INVESTMENT TRANSFER"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "INVESTMENT TRANSFER", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "FIDELITY"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "FIDELITY", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "VANGUARD"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "VANGUARD", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "SCHWAB"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "SCHWAB", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "ROBINHOOD"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "ROBINHOOD", zeroBudgetImpact: false);

        if (ContainsPattern(combined, "ETRADE", "E*TRADE", "E-TRADE"))
            return CreateClassification(TransferSubtype.InvestmentTransfer, "ETRADE", zeroBudgetImpact: false);

        // ── Not transfer-like ──────────────────────────────────────────────────
        // Venmo/Zelle/PayPal are intentionally NOT classified as transfer-like.
        // They can represent real recurring rent/daycare payments.
        // TE Certified, electrical, utility, contractor names are NOT transfer-like.
        return new TransferClassification
        {
            IsTransferLike = false,
            Subtype = TransferSubtype.None,
            ShouldExcludeFromRecurringSuggestions = false,
            ShouldZeroBudgetImpact = false,
            Reason = "not-transfer-like",
            MatchedRule = null
        };
    }

    private static bool ContainsPattern(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static TransferClassification CreateClassification(
        TransferSubtype subtype,
        string matchedRule,
        bool zeroBudgetImpact = true)
    {
        return new TransferClassification
        {
            IsTransferLike = true,
            Subtype = subtype,
            ShouldExcludeFromRecurringSuggestions = true, // All transfer subtypes excluded
            ShouldZeroBudgetImpact = zeroBudgetImpact,
            Reason = $"transfer-like: {subtype}",
            MatchedRule = matchedRule
        };
    }
}
