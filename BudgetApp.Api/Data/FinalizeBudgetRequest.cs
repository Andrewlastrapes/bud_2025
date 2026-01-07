namespace BudgetApp.Api.Data;

public class FinalizeBudgetRequest
{
    public decimal PaycheckAmount { get; set; }
    public int PayDay1 { get; set; }
    public int PayDay2 { get; set; }
    public DateTime NextPaycheckDate { get; set; }

    // NEW: how much of each paycheck you’re dedicating to existing credit debt
    public decimal? DebtPerPaycheck { get; set; }
}
