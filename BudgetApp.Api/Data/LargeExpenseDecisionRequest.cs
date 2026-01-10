namespace BudgetApp.Api.Data;

public enum LargeExpenseDecisionOption
{
    TreatAsNormal = 0,       // Option A
    FromSavings = 1,         // Option B
    ConvertToFixedCost = 2   // Option C
}

public class LargeExpenseDecisionRequest
{
    public LargeExpenseDecisionOption Option { get; set; }

    /// <summary>
    /// Number of future periods to spread this over when Option == ConvertToFixedCost.
    /// Example: 2 => split into 2 future fixed-cost payments.
    /// </summary>
    public int? SplitOverPeriods { get; set; }
}
