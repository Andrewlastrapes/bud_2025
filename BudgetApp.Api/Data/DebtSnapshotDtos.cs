namespace BudgetApp.Api.Data;

/// <summary>
/// Details for a single credit card account included in the debt snapshot.
/// </summary>
public class DebtSnapshotAccountDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Mask { get; set; }
    public decimal CurrentBalance { get; set; }
}

/// <summary>
/// Details for a single checking or savings account included in the cash snapshot.
/// </summary>
public class CashAccountDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Mask { get; set; }
    /// <summary>Plaid account subtype in lowercase: "checking", "savings", "money market", etc.</summary>
    public string SubType { get; set; } = string.Empty;
    /// <summary>Available (preferred) or current balance, clamped to ≥ 0.</summary>
    public decimal Balance { get; set; }
}

public class DebtSnapshotResponse
{
    /// <summary>
    /// Backward-compatible alias for <see cref="TotalCreditCardDebt"/>.
    /// Existing frontend code that reads <c>snapshot.totalDebt</c> continues to work unchanged.
    /// </summary>
    public decimal TotalDebt => TotalCreditCardDebt;

    /// <summary>
    /// Total outstanding credit card balance across all linked credit accounts.
    /// Always a positive number (amount owed). 0 when no credit cards are linked or all balances are 0.
    /// </summary>
    public decimal TotalCreditCardDebt { get; set; }

    /// <summary>
    /// Sum of all linked checking account available balances.
    /// Negative individual balances (overdrafts) are clamped to 0 before summing.
    /// </summary>
    public decimal TotalCheckingBalance { get; set; }

    /// <summary>
    /// Sum of all linked savings account available balances.
    /// Negative individual balances are clamped to 0 before summing.
    /// </summary>
    public decimal TotalSavingsBalance { get; set; }

    /// <summary>
    /// TotalCheckingBalance + TotalSavingsBalance.
    /// This is the total liquid cash the user has available before applying any cushion.
    /// </summary>
    public decimal TotalCashBalance { get; set; }

    /// <summary>Credit card accounts detail list (unchanged from original shape).</summary>
    public List<DebtSnapshotAccountDto> Accounts { get; set; } = new();

    /// <summary>Checking and savings account detail list. Empty when no depository accounts are linked.</summary>
    public List<CashAccountDto> CashAccounts { get; set; } = new();
}
