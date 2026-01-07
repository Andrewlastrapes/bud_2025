namespace BudgetApp.Api.Data;

public class PlaidWebhookRequest
{
    public string WebhookType { get; set; } = string.Empty;  // "TRANSACTIONS"
    public string WebhookCode { get; set; } = string.Empty;  // "DEFAULT_UPDATE", etc.
    public string ItemId { get; set; } = string.Empty;       // plaid item_id

}
