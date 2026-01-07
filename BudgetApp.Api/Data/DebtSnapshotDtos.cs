namespace BudgetApp.Api.Data;

public class DebtSnapshotAccountDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Mask { get; set; }
    public decimal CurrentBalance { get; set; }
}

public class DebtSnapshotResponse
{
    public decimal TotalDebt { get; set; }
    public List<DebtSnapshotAccountDto> Accounts { get; set; } = new();
}
