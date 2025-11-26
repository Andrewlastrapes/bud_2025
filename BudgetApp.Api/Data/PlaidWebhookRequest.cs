namespace BudgetApp.Api.Data;

public record PlaidWebhookRequest(string WebhookType, string WebhookCode, string ItemId);