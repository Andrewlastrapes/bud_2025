using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("PaycheckSummaries")]
public class PaycheckSummary
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    /// <summary>UTC date this paycheck arrived (stored as midnight UTC).</summary>
    [Column("paycheck_date")]
    public DateTime PaycheckDate { get; set; }

    [Column("period_start_date")]
    public DateTime PeriodStartDate { get; set; }

    [Column("period_end_date")]
    public DateTime PeriodEndDate { get; set; }

    [Column("next_paycheck_date")]
    public DateTime NextPaycheckDate { get; set; }

    [Column("paycheck_amount")]
    public decimal PaycheckAmount { get; set; }

    /// <summary>
    /// Dynamic budget for the prior period, approximated from CURRENT user settings.
    /// TODO: For full accuracy, a future BudgetSnapshot table should capture settings
    /// per pay period. If user settings changed mid-period this value will be incorrect.
    /// </summary>
    [Column("prior_period_starting_budget")]
    public decimal? PriorPeriodStartingBudget { get; set; }

    [Column("prior_period_spend")]
    public decimal PriorPeriodSpend { get; set; }

    [Column("prior_period_remaining")]
    public decimal PriorPeriodRemaining { get; set; }

    [Column("was_under_budget")]
    public bool WasUnderBudget { get; set; }

    /// <summary>How much was left over (0 if over budget).</summary>
    [Column("leftover_amount")]
    public decimal LeftoverAmount { get; set; }

    /// <summary>How much over budget (0 if under budget).</summary>
    [Column("over_budget_amount")]
    public decimal OverBudgetAmount { get; set; }

    [Column("fixed_costs_until_next_paycheck")]
    public decimal FixedCostsUntilNextPaycheck { get; set; }

    [Column("savings_contribution")]
    public decimal SavingsContribution { get; set; }

    [Column("debt_payment_amount")]
    public decimal DebtPaymentAmount { get; set; }

    [Column("recommended_debt_payment_amount")]
    public decimal? RecommendedDebtPaymentAmount { get; set; }

    [Column("new_dynamic_budget_amount")]
    public decimal NewDynamicBudgetAmount { get; set; }

    /// <summary>
    /// Decision recorded by the user.
    /// Valid values: AddToBudget | TransferToSavings | ExtraDebtPayment | KeepAsBuffer | Dismiss
    /// </summary>
    [Column("user_decision")]
    public string? UserDecision { get; set; }

    [Column("is_dismissed")]
    public bool IsDismissed { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}