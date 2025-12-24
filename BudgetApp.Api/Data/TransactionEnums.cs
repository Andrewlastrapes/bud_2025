namespace BudgetApp.Api.Data;

public enum TransactionSuggestedKind
{
    Unknown = 0,
    Paycheck = 1,
    Windfall = 2,
    InternalTransfer = 3,
    Refund = 4,
}

public enum TransactionUserDecision
{
    // Original decisions
    Undecided = 0,
    TreatAsIncome = 1,
    IgnoreForDynamic = 2,
    DebtPayment = 3,
    SavingsFunded = 4,

    // Large-expense decisions
    TreatAsVariableSpend = 10,     // “Just count it as normal spending”
    LargeExpenseFromSavings = 11,  // “It actually came from savings – refund this period”
    LargeExpenseToFixedCost = 12   // “Turn this into an installment / fixed cost plan”
}
