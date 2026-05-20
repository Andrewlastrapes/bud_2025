using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("Users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("firebase_uuid")]
    public string FirebaseUuid { get; set; } = string.Empty;

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; }

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [Column("onboarding_complete")]
    public bool OnboardingComplete { get; set; } = false;

    [Column("pay_day_1")]
    public int PayDay1 { get; set; } = 1;

    [Column("pay_day_2")]
    public int PayDay2 { get; set; } = 15;

    [Column("expected_paycheck_amount")]
    public decimal ExpectedPaycheckAmount { get; set; } = 0m;

    [Column("debt_per_paycheck")]
    public decimal? DebtPerPaycheck { get; set; }

    /// <summary>
    /// Total credit card debt captured from the Plaid snapshot at the time the user
    /// completed onboarding. Null for users who onboarded before this field existed or
    /// who had no linked credit accounts at onboarding time.
    /// This is a historical starting balance used for the payoff plan — it is NOT
    /// updated automatically as the user pays down their debt.
    /// </summary>
    [Column("debt_starting_balance")]
    public decimal? DebtStartingBalance { get; set; }



    // ...

    /// <summary>Savings contribution per paycheck. Stub [NotMapped] until a DB migration adds the column.</summary>
    [NotMapped]
    public decimal SavingsContributionAmount { get; set; } = 0m;

    public virtual ICollection<PlaidItem> PlaidItems { get; set; } = new List<PlaidItem>();
    public virtual ICollection<Balance> Balances { get; set; } = new List<Balance>();
    public virtual ICollection<FixedCost> FixedCosts { get; set; } = new List<FixedCost>();
}
