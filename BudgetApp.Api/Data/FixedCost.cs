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

    // --- Category / type ---

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

    // --- Recurrence ---

    /// <summary>
    /// How often this bill recurs.
    /// Valid values: "Weekly" | "Biweekly" | "Monthly" | "Yearly" | "OneTime"
    /// Defaults to "Monthly" for all new and existing rows.
    /// </summary>
    [Column("recurrence_frequency")]
    public string RecurrenceFrequency { get; set; } = "Monthly";

    /// <summary>
    /// The intended calendar day of month for this bill (1–31).
    ///
    /// Stored separately from <see cref="NextDueDate"/> so that automatic
    /// recurrence advancement can restore the correct day after a short-month
    /// clamp.
    ///
    /// Example: a bill due on the 31st temporarily lands on Feb 28 after a
    /// monthly advance.  The NEXT advance should go to Mar 31, not Mar 28.
    /// Without this field the original day is lost after the first short-month
    /// clamp.
    ///
    /// Set when a due date is first provided (creation or explicit user edit).
    /// Never overwritten by automatic recurrence advancement — only the user can
    /// change this by editing the due date.
    ///
    /// Null for rows created before this migration that have no due date, or
    /// for rows where the migration backfill did not run (safe — FixedCostAdvancer
    /// falls back to NextDueDate.Day in that case, giving standard AddMonths(1)
    /// behaviour for those rows).
    /// </summary>
    [Column("original_due_day_of_month")]
    public int? OriginalDueDayOfMonth { get; set; }

    // --- Timestamps ---

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("next_due_date")]
    public DateTime? NextDueDate { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>Day-of-month the bill is due. Derived from NextDueDate; falls back to 1 if unset.</summary>
    [NotMapped]
    public int DayOfMonth => NextDueDate?.Day ?? 1;

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}
