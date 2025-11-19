using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("FixedCosts")]
public class FixedCost
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("amount")]
    public decimal Amount { get; set; }

    // --- NEW PROPERTIES ---

    [Column("category")]
    public string Category { get; set; } = "other";

    [Column("type")]
    public string Type { get; set; } = "manual"; // 'manual' or 'plaid_discovered'

    [Column("plaid_merchant_name")]
    public string? PlaidMerchantName { get; set; }

    [Column("plaid_account_id")]
    public string? PlaidAccountId { get; set; }

    [Column("user_has_approved")]
    public bool UserHasApproved { get; set; } = true;

    // ----------------------

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}