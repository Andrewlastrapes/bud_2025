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
    Undecided = 0,
    TreatAsIncome = 1,
    IgnoreForDynamic = 2,
    DebtPayment = 3,
    SavingsFunded = 4
}
