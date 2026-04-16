using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("Transactions")]
public class Transaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("plaid_transaction_id")]
    public string PlaidTransactionId { get; set; } = string.Empty;

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("merchant_name")]
    public string? MerchantName { get; set; }

    [Column("pending")]
    public bool Pending { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("suggested_kind")]
    public TransactionSuggestedKind SuggestedKind { get; set; } = TransactionSuggestedKind.Unknown;

    [Column("user_decision")]
    public TransactionUserDecision UserDecision { get; set; } = TransactionUserDecision.Undecided;

    [Column("counted_as_income")]
    public bool CountedAsIncome { get; set; } = false;

    [Column("IsLargeExpenseCandidate")]
    public bool IsLargeExpenseCandidate { get; set; }

    [Column("LargeExpenseHandled")]
    public bool LargeExpenseHandled { get; set; }

    // ─── Suspicious hold fields ───────────────────────────────────────────────
    // Pending charges from hold-prone merchants (gas, hotel, rental car) that
    // are unusually high get flagged here so the user can review them and
    // optionally override the amount used in the dynamic budget calculation.

    /// <summary>
    /// True if this pending transaction was detected as a suspicious pre-auth hold
    /// (gas station, hotel, rental car above category-specific thresholds).
    /// </summary>
    [Column("is_suspicious_hold")]
    public bool IsSuspiciousHold { get; set; } = false;

    /// <summary>
    /// True once the user has reviewed (and optionally overridden) this hold.
    /// </summary>
    [Column("hold_reviewed")]
    public bool HoldReviewed { get; set; } = false;

    /// <summary>
    /// Amount the user chose to reserve in the budget instead of the full hold amount.
    /// Null until the user sets an override.
    /// </summary>
    [Column("hold_override_amount")]
    public decimal? HoldOverrideAmount { get; set; }

    /// <summary>
    /// The dollar amount that was actually subtracted from the dynamic balance for this
    /// transaction. Used to reverse the budget impact when Plaid removes the pending row
    /// (either because it posted or because it disappeared).
    /// Null for credits and fixed-cost outflows that don't touch the dynamic balance.
    /// </summary>
    [Column("budget_applied_amount")]
    public decimal? BudgetAppliedAmount { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}