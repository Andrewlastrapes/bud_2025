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

    // ── Cash-cushion onboarding fields (added in migration AddCashOnboardingFieldsToUser) ───
    // All nullable so old users are not affected.

    /// <summary>Total checking + savings balance captured from the Plaid snapshot at onboarding time.</summary>
    [Column("cash_balance_at_onboarding")]
    public decimal? CashBalanceAtOnboarding { get; set; }

    /// <summary>Cash cushion the user chose to keep for bills and emergencies (not applied toward debt).</summary>
    [Column("cash_cushion_at_onboarding")]
    public decimal? CashCushionAtOnboarding { get; set; }

    /// <summary>
    /// Amount of checking/savings cash the user applied toward credit card debt at onboarding.
    /// Clamped by the backend to min(availableForDebt, totalCreditCardDebt).
    /// </summary>
    [Column("cash_applied_to_debt_at_onboarding")]
    public decimal? CashAppliedToDebtAtOnboarding { get; set; }

    /// <summary>
    /// Remaining credit card debt after applying cash (= DebtStartingBalance − CashAppliedToDebtAtOnboarding).
    /// This is the figure used for the payoff estimate — more accurate than raw DebtStartingBalance
    /// when the user applied some cash at onboarding.
    /// Null for legacy users who onboarded before this field existed.
    /// </summary>
    [Column("net_debt_starting_balance")]
    public decimal? NetDebtStartingBalance { get; set; }

    // ...

    /// <summary>Savings contribution per paycheck. Stub [NotMapped] until a DB migration adds the column.</summary>
    [NotMapped]
    public decimal SavingsContributionAmount { get; set; } = 0m;

    public virtual ICollection<PlaidItem> PlaidItems { get; set; } = new List<PlaidItem>();
    public virtual ICollection<Balance> Balances { get; set; } = new List<Balance>();
    public virtual ICollection<FixedCost> FixedCosts { get; set; } = new List<FixedCost>();
}
