namespace BudgetApp.Api.Data;

/// <summary>
/// Request body for POST /api/transactions/{id}/hold-override.
/// Allows the user to set the amount that should count against the dynamic budget
/// instead of the full suspicious hold amount.
/// </summary>
public class HoldOverrideRequest
{
    /// <summary>
    /// The amount to reserve in the budget for this hold.
    /// Must be > 0 and <= the original hold amount.
    /// </summary>
    public decimal OverrideAmount { get; set; }
}