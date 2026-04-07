namespace BudgetApp.Api.Data;

/// <summary>
/// Request body for POST /api/budget/base.
/// Returns baseRemaining = paycheck - fixedCosts (before debt/savings decisions).
/// </summary>
public class BaseBudgetHttpRequest
{
    public decimal PaycheckAmount { get; set; }
    public int PayDay1 { get; set; }
    public int PayDay2 { get; set; }
    public DateTime NextPaycheckDate { get; set; }
}