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




    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }


    
}