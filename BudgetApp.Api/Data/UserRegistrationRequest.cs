namespace BudgetApp.Api.Data;

public class UserRegistrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirebaseUuid { get; set; }
}
