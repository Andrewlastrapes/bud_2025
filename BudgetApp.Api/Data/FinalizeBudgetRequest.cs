namespace BudgetApp.Api.Data;

public record FinalizeBudgetRequest(decimal PaycheckAmount, DateTime NextPaycheckDate, int PayDay1, int PayDay2);