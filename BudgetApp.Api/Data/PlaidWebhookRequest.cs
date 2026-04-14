using System.Text.Json.Serialization;

namespace BudgetApp.Api.Data
{
    public class PlaidWebhookRequest
    {
        [JsonPropertyName("webhook_type")]
        public string WebhookType { get; set; } = string.Empty;

        [JsonPropertyName("webhook_code")]
        public string WebhookCode { get; set; } = string.Empty;

        [JsonPropertyName("item_id")]
        public string ItemId { get; set; } = string.Empty;
    }
}