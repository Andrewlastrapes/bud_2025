// File: Services/NotificationService.cs

using System.Net.Http;
using System.Net.Http.Json;
using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BudgetApp.Api.Services;

public interface INotificationService
{
    Task SendNewTransactionNotification(Transaction tx, decimal dynamicBalance);
    Task SendGenericNotificationForUser(int userId, string title, string body, object? data = null);
}

public class ExpoNotificationService : INotificationService
{
    private readonly ApiDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExpoNotificationService> _logger;

    private const string ExpoPushUrl = "https://exp.host/--/api/v2/push/send";

    public ExpoNotificationService(
        ApiDbContext dbContext,
        ILogger<ExpoNotificationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(nameof(ExpoNotificationService));
    }

    public async Task SendNewTransactionNotification(Transaction tx, decimal dynamicBalance)
    {
        var userDevices = await _dbContext.UserDevices
            .Where(d => d.UserId == tx.UserId && d.IsActive)
            .ToListAsync();

        if (!userDevices.Any())
            return;

        string title;
        string body;
        string type;

        // Deposit (paycheck-style)
        if (tx.SuggestedKind == TransactionSuggestedKind.Paycheck)
        {
            type = "deposit";
            title = "New paycheck detected";
            body =
                $"Your Period Spend Limit is currently ${dynamicBalance:0.00}. " +
                "Tap to decide how to use this deposit.";
        }
        // Large expense path
        else if (tx.IsLargeExpenseCandidate && !tx.LargeExpenseHandled)
        {
            type = "large-expense";
            title = "Large purchase spotted";
            body =
                $"${tx.Amount:0.00} at {tx.MerchantName ?? tx.Name}. " +
                $"Period Spend Limit is now ${dynamicBalance:0.00}. " +
                "Tap to choose: pay from savings, convert to fixed cost, or treat as normal spend.";
        }
        // Generic spend – ask about recurring
        else
        {
            type = "spend";
            title = $"New charge: {tx.MerchantName ?? tx.Name}";
            body =
                $"-${tx.Amount:0.00}. Period Spend Limit is now ${dynamicBalance:0.00}. " +
                "Tap to mark this as a recurring bill or review your spending.";
        }

        // Flag “normal spend” notifications as eligible for the recurring flow
        bool canMarkRecurring =
            type == "spend" &&
            !tx.IsLargeExpenseCandidate &&
            tx.SuggestedKind == TransactionSuggestedKind.Unknown;

        var payloads = userDevices.Select(d => new
        {
            to = d.ExpoPushToken,
            sound = "default",
            title,
            body,
            data = new
            {
                type,
                transactionId = tx.Id,
                dynamicBalance,
                canMarkRecurring
            }
        });

        await PostToExpoAsync(payloads);
    }

    public async Task SendGenericNotificationForUser(
        int userId,
        string title,
        string body,
        object? data = null)
    {
        var userDevices = await _dbContext.UserDevices
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync();

        if (!userDevices.Any())
            return;

        var payloads = userDevices.Select(d => new
        {
            to = d.ExpoPushToken,
            sound = "default",
            title,
            body,
            data
        });

        await PostToExpoAsync(payloads);
    }

    private async Task PostToExpoAsync(IEnumerable<object> payloads)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(ExpoPushUrl, payloads);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Expo push error: {Status} {Body}", response.StatusCode, text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Expo push notification");
        }
    }
}
