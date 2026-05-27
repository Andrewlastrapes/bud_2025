namespace BudgetApp.Api.Data;

public record ExchangeTokenRequest(
    string PublicToken,
    string FirebaseUuid,
    string? InstitutionName = null,
    string? InstitutionId = null
);
