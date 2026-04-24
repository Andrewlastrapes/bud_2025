// File: Data/NotificationService.cs

using System.Net.Http;
using System.Net.Http.Json;
using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sentry;

namespace BudgetApp.Api.Services;

public interface INotificationService
{
    Task SendNewTransactionNotification(Transaction tx, decimal dynamicBalance, string? userEmail);
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

    public async Task SendNewTransactionNotification(
        Transaction tx,
        decimal dynamicBalance,
        string? userEmail)
    {
        _logger.LogInformation(
            "SendNewTransactionNotification called: " +
            "txId={TxId} plaidTxId={PlaidTxId} userId={UserId} userEmail={UserEmail} " +
            "amount={Amount} merchant={Merchant} pending={Pending} " +
            "suggestedKind={SuggestedKind} isLargeExpense={IsLargeExpense} " +
            "largeExpenseHandled={LargeExpenseHandled} dynamicBalance={DynamicBalance}",
            tx.Id, tx.PlaidTransactionId, tx.UserId, userEmail ?? "(null)",
            tx.Amount, tx.MerchantName ?? tx.Name, tx.Pending,
            tx.SuggestedKind, tx.IsLargeExpenseCandidate, tx.LargeExpenseHandled,
            dynamicBalance);

        SentrySdk.AddBreadcrumb(
            $"Notification attempt: txId={tx.Id} plaidTxId={tx.PlaidTransactionId} " +
            $"userEmail={userEmail ?? "(null)"} " +
            $"merchant={tx.MerchantName ?? tx.Name} amount={tx.Amount}",
            level: BreadcrumbLevel.Info);

        // ── Look up devices ───────────────────────────────────────────────────
        var userDevices = await _dbContext.UserDevices
            .Where(d => d.UserId == tx.UserId && d.IsActive)
            .ToListAsync();

        _logger.LogInformation(
            "Device lookup result: txId={TxId} userId={UserId} userEmail={UserEmail} " +
            "activeDeviceCount={DeviceCount}",
            tx.Id, tx.UserId, userEmail ?? "(null)", userDevices.Count);

        if (!userDevices.Any())
        {
            _logger.LogWarning(
                "Notification skipped — no active devices: " +
                "txId={TxId} plaidTxId={PlaidTxId} userId={UserId} userEmail={UserEmail}",
                tx.Id, tx.PlaidTransactionId, tx.UserId, userEmail ?? "(null)");

            SentrySdk.AddBreadcrumb(
                $"Notification skipped (no active devices): " +
                $"txId={tx.Id} userId={tx.UserId} userEmail={userEmail ?? "(null)"}",
                level: BreadcrumbLevel.Warning);
            return;
        }

        // ── Build title / body based on transaction type ──────────────────────
        string title;
        string body;
        string type;

        if (tx.SuggestedKind == TransactionSuggestedKind.Paycheck)
        {
            type  = "deposit";
            title = "New paycheck detected";
            body  =
                $"Your Period Spend Limit is currently ${dynamicBalance:0.00}. " +
                "Tap to decide how to use this deposit.";
        }
        else if (tx.IsLargeExpenseCandidate && !tx.LargeExpenseHandled)
        {
            type  = "large-expense";
            title = "Large purchase spotted";
            body  =
                $"${tx.Amount:0.00} at {tx.MerchantName ?? tx.Name}. " +
                $"Period Spend Limit is now ${dynamicBalance:0.00}. " +
                "Tap to choose: pay from savings, convert to fixed cost, or treat as normal spend.";
        }
        else
        {
            type  = "spend";
            title = $"New charge: {tx.MerchantName ?? tx.Name}";
            body  =
                $"-${tx.Amount:0.00}. Period Spend Limit is now ${dynamicBalance:0.00}. " +
                "Tap to mark this as a recurring bill or review your spending.";
        }

        // Prepend user email so notifications are identifiable during diagnosis
        var emailPrefix = string.IsNullOrWhiteSpace(userEmail) ? "unknown@email" : userEmail;
        title = $"{emailPrefix} | {title}";

        bool canMarkRecurring =
            type == "spend" &&
            !tx.IsLargeExpenseCandidate &&
            tx.SuggestedKind == TransactionSuggestedKind.Unknown;

        _logger.LogInformation(
            "Notification payload built: " +
            "txId={TxId} type={Type} title={Title} bodyPreview={BodyPreview} " +
            "canMarkRecurring={CanMarkRecurring} deviceCount={DeviceCount} userEmail={UserEmail}",
            tx.Id, type, title,
            body.Length > 80 ? body[..80] + "…" : body,
            canMarkRecurring, userDevices.Count, userEmail ?? "(null)");

        SentrySdk.AddBreadcrumb(
            $"Notification sending: txId={tx.Id} type={type} " +
            $"deviceCount={userDevices.Count} userEmail={userEmail ?? "(null)"} title={title}",
            level: BreadcrumbLevel.Info);

        // ── SENTRY: capture every notification send as a message ──────────────
        // This is intentionally aggressive so we can count notification events in Sentry
        // and correlate with webhook bursts.
        SentrySdk.CaptureMessage(
            $"Notification sent: userEmail={userEmail ?? "(null)"} " +
            $"txId={tx.Id} plaidTxId={tx.PlaidTransactionId} " +
            $"type={type} title={title} " +
            $"merchant={tx.MerchantName ?? tx.Name} amount={tx.Amount:0.00} " +
            $"deviceCount={userDevices.Count}",
            scope =>
            {
                scope.Level = SentryLevel.Info;
                scope.SetTag("notification.type", type);
                scope.SetTag("user.email", userEmail ?? "unknown");
                scope.SetExtra("txId", tx.Id);
                scope.SetExtra("plaidTxId", tx.PlaidTransactionId);
                scope.SetExtra("merchant", tx.MerchantName ?? tx.Name ?? "(null)");
                scope.SetExtra("amount", tx.Amount);
                scope.SetExtra("title", title);
                scope.SetExtra("body", body);
                scope.SetExtra("deviceCount", userDevices.Count);
                scope.SetExtra("dynamicBalance", dynamicBalance);
                scope.SetExtra("canMarkRecurring", canMarkRecurring);
            });

        var payloads = userDevices.Select(d => new
        {
            to    = d.ExpoPushToken,
            sound = "default",
            title,
            body,
            data  = new
            {
                type,
                transactionId  = tx.Id,
                dynamicBalance,
                canMarkRecurring
            }
        });

        await PostToExpoAsync(
            payloads,
            txId:      tx.Id,
            plaidTxId: tx.PlaidTransactionId,
            userEmail: userEmail);
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

        _logger.LogInformation(
            "SendGenericNotification: userId={UserId} title={Title} deviceCount={DeviceCount}",
            userId, title, userDevices.Count);

        if (!userDevices.Any())
        {
            _logger.LogWarning(
                "Generic notification skipped — no active devices: userId={UserId}",
                userId);
            return;
        }

        var payloads = userDevices.Select(d => new
        {
            to    = d.ExpoPushToken,
            sound = "default",
            title,
            body,
            data
        });

        await PostToExpoAsync(payloads, txId: 0, plaidTxId: null, userEmail: null);
    }

    /// <param name="txId">App transaction ID (0 for generic notifications).</param>
    /// <param name="plaidTxId">Plaid transaction ID, for correlation in logs.</param>
    /// <param name="userEmail">User email, for correlation in logs.</param>
    private async Task PostToExpoAsync(
        IEnumerable<object> payloads,
        int txId,
        string? plaidTxId,
        string? userEmail)
    {
        _logger.LogInformation(
            "Posting to Expo Push API: txId={TxId} plaidTxId={PlaidTxId} userEmail={UserEmail}",
            txId, plaidTxId ?? "(null)", userEmail ?? "(null)");

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(ExpoPushUrl, payloads);

            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync();

                _logger.LogWarning(
                    "Expo push FAILED: txId={TxId} plaidTxId={PlaidTxId} userEmail={UserEmail} " +
                    "httpStatus={Status} expoResponseBody={Body}",
                    txId, plaidTxId ?? "(null)", userEmail ?? "(null)",
                    response.StatusCode, text);

                SentrySdk.CaptureMessage(
                    $"Expo push failed: txId={txId} userEmail={userEmail ?? "(null)"} " +
                    $"httpStatus={response.StatusCode} body={text}",
                    scope =>
                    {
                        scope.Level = SentryLevel.Warning;
                        scope.SetTag("expo.result", "failed");
                        scope.SetExtra("txId", txId);
                        scope.SetExtra("plaidTxId", plaidTxId ?? "(null)");
                        scope.SetExtra("userEmail", userEmail ?? "(null)");
                        scope.SetExtra("expoHttpStatus", response.StatusCode.ToString());
                        scope.SetExtra("expoResponseBody", text);
                    });
            }
            else
            {
                // Read body even on success — Expo may report per-token errors inside a 200
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation(
                    "Expo push succeeded: txId={TxId} plaidTxId={PlaidTxId} userEmail={UserEmail} " +
                    "httpStatus={Status} expoResponseBody={Body}",
                    txId, plaidTxId ?? "(null)", userEmail ?? "(null)",
                    response.StatusCode, responseBody);

                SentrySdk.AddBreadcrumb(
                    $"Expo push succeeded: txId={txId} userEmail={userEmail ?? "(null)"} " +
                    $"response={responseBody}",
                    level: BreadcrumbLevel.Info);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Expo push EXCEPTION: txId={TxId} plaidTxId={PlaidTxId} userEmail={UserEmail}",
                txId, plaidTxId ?? "(null)", userEmail ?? "(null)");

            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("expo.result", "exception");
                scope.SetExtra("txId", txId);
                scope.SetExtra("plaidTxId", plaidTxId ?? "(null)");
                scope.SetExtra("userEmail", userEmail ?? "(null)");
            });
        }
    }
}