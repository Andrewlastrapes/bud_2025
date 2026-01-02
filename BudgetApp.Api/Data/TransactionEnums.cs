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
    // Default / no decision yet
    Undecided = 0,

    // --- Deposit decisions (credits) ---
    TreatAsIncome = 1,
    IgnoreForDynamic = 2,
    DebtPayment = 3,
    SavingsFunded = 4,

    // --- Large expense decisions (big debits) ---
    // “Just leave it as normal spending – the hit to this period is fine.”
    TreatAsVariableSpend = 10,

    // “This really came from savings; refund this period’s dynamic budget.”
    LargeExpenseFromSavings = 11,

    // “Turn this into an installment / fixed cost; refund this period.”
    LargeExpenseToFixedCost = 12
}
