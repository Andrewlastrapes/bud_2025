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



    // ...

    public virtual ICollection<PlaidItem> PlaidItems { get; set; } = new List<PlaidItem>();
    public virtual ICollection<Balance> Balances { get; set; } = new List<Balance>();
}