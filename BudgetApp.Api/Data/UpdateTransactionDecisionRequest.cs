using System;

namespace BudgetApp.Api.Data
{
    public class UpdateTransactionDecisionRequest
    {
        public TransactionUserDecision Decision { get; set; }

        // Optional extras for LargeExpenseToFixedCost
        // If the client doesn’t send these, the backend will fall back to defaults.
        public decimal? FixedCostAmount { get; set; }   // per pay period
        public string? FixedCostName { get; set; }
        public DateTime? FirstDueDate { get; set; }
    }
}
