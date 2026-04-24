using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Going.Plaid;
using Going.Plaid.Transactions;
using Sentry;
using Microsoft.Extensions.Logging;

namespace BudgetApp.Api.Services
{
    public interface ITransactionService
    {
        /// <param name="itemId">Plaid item ID to sync.</param>
        /// <param name="webhookCode">
        /// The Plaid webhook code that triggered this sync, e.g. "INITIAL_UPDATE",
        /// "HISTORICAL_UPDATE", "DEFAULT_UPDATE", "SYNC_UPDATES_AVAILABLE".
        /// Null when the sync is triggered manually (e.g. from the /api/transactions/sync
        /// endpoint). Any value other than INITIAL_UPDATE / HISTORICAL_UPDATE is treated
        /// as a live sync and enables notifications for this item going forward.
        /// </param>
        Task<TransactionsSyncResponse> SyncAndProcessTransactions(
            string itemId,
            string? webhookCode = null);
    }

    public class TransactionService : ITransactionService
    {
        private readonly PlaidClient _plaidClient;
        private readonly ApiDbContext _dbContext;
        private readonly IConfiguration _config;
        private readonly IDynamicBudgetEngine _budgetEngine;
        private readonly INotificationService _notificationService;
        private readonly ILogger<TransactionService> _logger;

        // Plaid webhook codes that indicate an initial history backfill.
        // During these calls budget calculations run normally, but notifications
        // are never sent and NotificationsEnabledAt is not set.
        private static readonly HashSet<string> BackfillCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "INITIAL_UPDATE",
            "HISTORICAL_UPDATE",
        };

        public TransactionService(
            ApiDbContext dbContext,
            PlaidClient plaidClient,
            IConfiguration config,
            IDynamicBudgetEngine budgetEngine,
            INotificationService notificationService,
            ILogger<TransactionService> logger)
        {
            _dbContext = dbContext;
            _plaidClient = plaidClient;
            _config = config;
            _budgetEngine = budgetEngine;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<TransactionsSyncResponse> SyncAndProcessTransactions(
            string itemId,
            string? webhookCode = null)
        {
            var syncStartedAt = DateTime.UtcNow;

            try
            {
                // ── 1. Find Plaid item + user ─────────────────────────────────────────
                var plaidItem = await _dbContext.PlaidItems
                    .FirstOrDefaultAsync(p => p.ItemId == itemId);

                if (plaidItem == null)
                {
                    _logger.LogError(
                        "SYNC ERROR: Plaid item not found. itemId={ItemId} webhookCode={WebhookCode}",
                        itemId, webhookCode ?? "(manual)");
                    throw new InvalidOperationException($"Plaid Item {itemId} not found.");
                }

                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == plaidItem.UserId);

                if (user == null)
                {
                    _logger.LogError(
                        "SYNC ERROR: User not found for Plaid item. itemId={ItemId} userId={UserId}",
                        itemId, plaidItem.UserId);
                    throw new UnauthorizedAccessException("User linked to this Item not found.");
                }

                var fixedCosts = await _dbContext.FixedCosts
                    .Where(fc => fc.UserId == user.Id)
                    .ToListAsync();

                // Is this sync part of the initial Plaid backfill?
                bool isBackfill = webhookCode != null && BackfillCodes.Contains(webhookCode);

                // Capture pre-sync state for logging / burst-detection
                string? cursorBeforeSync = plaidItem.Cursor;
                DateTime? notificationsEnabledAtBefore = plaidItem.NotificationsEnabledAt;

                // ── LOG SYNC START ────────────────────────────────────────────────────
                _logger.LogInformation(
                    "Sync start: itemId={ItemId} webhookCode={WebhookCode} isBackfill={IsBackfill} " +
                    "userId={UserId} userEmail={UserEmail} " +
                    "cursorBefore={CursorBefore} " +
                    "notificationsEnabledAt={NotificationsEnabledAt} " +
                    "syncStartedAt={SyncStartedAt}",
                    itemId,
                    webhookCode ?? "(manual)",
                    isBackfill,
                    user.Id,
                    user.Email,
                    cursorBeforeSync ?? "(null/first-sync)",
                    notificationsEnabledAtBefore?.ToString("O") ?? "(null — not yet set)",
                    syncStartedAt.ToString("O"));

                SentrySdk.AddBreadcrumb(
                    $"Sync start: itemId={itemId} webhookCode={webhookCode ?? "(manual)"} " +
                    $"isBackfill={isBackfill} userEmail={user.Email} " +
                    $"cursorBefore={cursorBeforeSync ?? "(null)"} " +
                    $"notificationsEnabledAt={notificationsEnabledAtBefore?.ToString("O") ?? "(null)"}",
                    level: BreadcrumbLevel.Info);

                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetTag("sync.itemId", itemId);
                    scope.SetTag("sync.webhookCode", webhookCode ?? "(manual)");
                    scope.SetTag("sync.isBackfill", isBackfill.ToString());
                    scope.SetTag("user.email", user.Email ?? "unknown");
                    scope.SetExtra("sync.userId", user.Id);
                    scope.SetExtra("sync.cursorBefore", cursorBeforeSync ?? "(null)");
                    scope.SetExtra("sync.notificationsEnabledAtBefore",
                        notificationsEnabledAtBefore?.ToString("O") ?? "(null)");
                    scope.SetExtra("sync.startedAt", syncStartedAt.ToString("O"));
                });

                // ── 2. Call Plaid ─────────────────────────────────────────────────────
                var request = new TransactionsSyncRequest
                {
                    AccessToken = plaidItem.AccessToken,
                    Cursor = plaidItem.Cursor,
                    Count = 100
                };

                var response = await _plaidClient.TransactionsSyncAsync(request);

                int addedCount    = response.Added?.Count    ?? 0;
                int modifiedCount = response.Modified?.Count ?? 0;
                int removedCount  = response.Removed?.Count  ?? 0;

                _logger.LogInformation(
                    "Plaid sync response: itemId={ItemId} webhookCode={WebhookCode} " +
                    "added={Added} modified={Modified} removed={Removed} " +
                    "hasMore={HasMore} nextCursor={NextCursor}",
                    itemId, webhookCode ?? "(manual)",
                    addedCount, modifiedCount, removedCount,
                    response.HasMore, response.NextCursor ?? "(null)");

                SentrySdk.AddBreadcrumb(
                    $"Plaid sync response: itemId={itemId} added={addedCount} " +
                    $"modified={modifiedCount} removed={removedCount} hasMore={response.HasMore}",
                    level: BreadcrumbLevel.Info);

                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetExtra("sync.addedCount",    addedCount);
                    scope.SetExtra("sync.modifiedCount", modifiedCount);
                    scope.SetExtra("sync.removedCount",  removedCount);
                    scope.SetExtra("sync.hasMore",       response.HasMore);
                });

                decimal balanceDelta = 0m;

                // Transactions eligible for notifications are collected here and
                // dispatched AFTER SaveChanges so they have valid IDs.
                var notifyQueue = new List<Transaction>();

                // Cutoff: a transaction must have been created at or after this time
                // AND within the last 24 hours to be eligible for a push notification.
                DateTime notifyCutoff    = plaidItem.NotificationsEnabledAt ?? DateTime.MaxValue;
                DateTime freshnessWindow = DateTime.UtcNow.AddHours(-24);

                _logger.LogInformation(
                    "Notification gate state: itemId={ItemId} " +
                    "notifyCutoff={NotifyCutoff} freshnessWindow={FreshnessWindow} isBackfill={IsBackfill}",
                    itemId,
                    notifyCutoff == DateTime.MaxValue
                        ? "(MaxValue — notifications not yet enabled)"
                        : notifyCutoff.ToString("O"),
                    freshnessWindow.ToString("O"),
                    isBackfill);

                // ── 3. Removed ─────────────────────────────────────────────────────────
                // Plaid removes a pending row when it posts (replaced by an added posted
                // row) or when it disappears entirely. Either way we reverse the amount
                // that was applied to the budget for this transaction.
                if (response.Removed != null)
                {
                    foreach (var removed in response.Removed)
                    {
                        var existing = await _dbContext.Transactions
                            .FirstOrDefaultAsync(x => x.PlaidTransactionId == removed.TransactionId);

                        if (existing == null)
                        {
                            _logger.LogDebug(
                                "Removed tx not in DB (skip): plaidTxId={PlaidTxId}",
                                removed.TransactionId);
                            continue;
                        }

                        _logger.LogInformation(
                            "Removing transaction: txId={TxId} plaidTxId={PlaidTxId} " +
                            "amount={Amount} budgetAppliedAmount={BudgetAppliedAmount}",
                            existing.Id, removed.TransactionId,
                            existing.Amount, existing.BudgetAppliedAmount);

                        if (existing.BudgetAppliedAmount.HasValue && existing.BudgetAppliedAmount.Value != 0)
                        {
                            balanceDelta += existing.BudgetAppliedAmount.Value;
                        }

                        _dbContext.Transactions.Remove(existing);
                    }
                }

                // ── 4. Modified ────────────────────────────────────────────────────────
                if (response.Modified != null)
                {
                    foreach (var modified in response.Modified)
                    {
                        var existing = await _dbContext.Transactions
                            .FirstOrDefaultAsync(x => x.PlaidTransactionId == modified.TransactionId);

                        if (existing == null)
                        {
                            _logger.LogDebug(
                                "Modified tx not in DB (skip): plaidTxId={PlaidTxId}",
                                modified.TransactionId);
                            continue;
                        }

                        var rawAmount  = modified.Amount ?? 0m;
                        bool isCredit  = rawAmount < 0m;
                        decimal newAbs = Math.Abs(rawAmount);

                        _logger.LogInformation(
                            "Modifying transaction: txId={TxId} plaidTxId={PlaidTxId} " +
                            "oldAmount={OldAmount} newAmount={NewAmount} isCredit={IsCredit}",
                            existing.Id, modified.TransactionId,
                            existing.Amount, newAbs, isCredit);

                        if (!isCredit && existing.BudgetAppliedAmount.HasValue)
                        {
                            decimal delta = existing.BudgetAppliedAmount.Value - newAbs;
                            balanceDelta += delta;
                            existing.BudgetAppliedAmount = newAbs;
                        }

                        existing.Amount    = newAbs;
                        existing.Pending   = modified.Pending ?? existing.Pending;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                }

                // ── 5. Added ───────────────────────────────────────────────────────────
                if (response.Added != null)
                {
                    foreach (var t in response.Added)
                    {
                        var exists = await _dbContext.Transactions
                            .AnyAsync(x => x.PlaidTransactionId == t.TransactionId);

                        if (exists)
                        {
                            // ⚠️  DUPLICATE: This is important for diagnosing repeated notifications.
                            // If the same plaidTxId shows up across multiple webhook calls, it means
                            // Plaid is replaying the same transaction — but we guard correctly here.
                            _logger.LogWarning(
                                "Duplicate transaction skipped (already in DB): " +
                                "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                "itemId={ItemId} webhookCode={WebhookCode}",
                                t.TransactionId, t.MerchantName ?? t.Name,
                                Math.Abs(t.Amount ?? 0m), itemId, webhookCode ?? "(manual)");
                            continue;
                        }

                        DateTime txDate = t.Date
                            .GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow))
                            .ToDateTime(TimeOnly.MinValue);

                        DateTime txDateUtc    = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);
                        string   merchantName = t.MerchantName ?? t.Name ?? "(unknown)";

                        var     rawAmount  = t.Amount ?? 0m;
                        bool    isCredit   = rawAmount < 0m;
                        decimal absAmount  = Math.Abs(rawAmount);
                        bool    isPending  = t.Pending ?? false;
                        DateTime createdAt = DateTime.UtcNow;

                        _logger.LogInformation(
                            "Processing added tx: plaidTxId={PlaidTxId} merchant={Merchant} " +
                            "amount={Amount} isCredit={IsCredit} isPending={IsPending} " +
                            "txDate={TxDate} createdAt={CreatedAt} " +
                            "itemId={ItemId} webhookCode={WebhookCode}",
                            t.TransactionId, merchantName, absAmount, isCredit, isPending,
                            txDateUtc.ToString("yyyy-MM-dd"), createdAt.ToString("O"),
                            itemId, webhookCode ?? "(manual)");

                        var newTx = new Transaction
                        {
                            UserId             = user.Id,
                            PlaidTransactionId = t.TransactionId,
                            AccountId          = t.AccountId,
                            Amount             = absAmount,
                            Date               = txDateUtc,
#pragma warning disable CS0612
                            Name               = t.Name,
#pragma warning restore CS0612
                            MerchantName       = t.MerchantName,
                            Pending            = isPending,
                            CreatedAt          = createdAt,
                            UpdatedAt          = createdAt,
                            IsLargeExpenseCandidate = false,
                            LargeExpenseHandled     = false
                        };

                        if (isCredit)
                        {
                            var ctx = new DepositContext
                            {
                                Amount                 = absAmount,
                                Date                   = txDateUtc,
                                MerchantName           = merchantName,
                                PayDay1                = user.PayDay1,
                                PayDay2                = user.PayDay2,
                                ExpectedPaycheckAmount = user.ExpectedPaycheckAmount
                            };

                            newTx.SuggestedKind = _budgetEngine.ClassifyDeposit(ctx);
                            _logger.LogInformation(
                                "Credit tx classified: plaidTxId={PlaidTxId} suggestedKind={SuggestedKind}",
                                t.TransactionId, newTx.SuggestedKind);
                        }
                        else
                        {
                            bool isFixed = fixedCosts.Any(fc =>
                                !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                                fc.PlaidMerchantName.Equals(
                                    merchantName,
                                    StringComparison.OrdinalIgnoreCase));

                            if (isFixed)
                            {
                                _logger.LogInformation(
                                    "Transaction is a known fixed cost (no budget impact): " +
                                    "plaidTxId={PlaidTxId} merchant={Merchant}",
                                    t.TransactionId, merchantName);
                            }
                            else
                            {
                                bool isSuspiciousHold = SuspiciousHoldDetector.IsSuspiciousHold(
                                    t.MerchantName, t.Name, absAmount, isPending);

                                if (isSuspiciousHold)
                                {
                                    newTx.IsSuspiciousHold = true;
                                    newTx.HoldReviewed     = false;
                                    _logger.LogInformation(
                                        "Transaction flagged as suspicious hold: " +
                                        "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount}",
                                        t.TransactionId, merchantName, absAmount);
                                }

                                newTx.BudgetAppliedAmount = absAmount;
                                balanceDelta -= absAmount;

                                if (user.ExpectedPaycheckAmount > 0 &&
                                    _budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                                {
                                    newTx.IsLargeExpenseCandidate = true;
                                    newTx.LargeExpenseHandled     = false;
                                    _logger.LogInformation(
                                        "Transaction flagged as large expense candidate: " +
                                        "plaidTxId={PlaidTxId} amount={Amount}",
                                        t.TransactionId, absAmount);
                                }
                            }
                        }

                        await _dbContext.Transactions.AddAsync(newTx);

                        // ── Notification eligibility ──────────────────────────────────
                        // A transaction qualifies for a push notification only when ALL of:
                        //   a) This is NOT a backfill sync
                        //   b) The item's NotificationsEnabledAt cutoff has been set
                        //   c) The transaction was created at or after that cutoff
                        //   d) The transaction is within the 24-hour freshness window
                        //   e) It is a posted (non-pending) transaction
                        bool gate_notBackfill          = !isBackfill;
                        bool gate_notificationsEnabled = plaidItem.NotificationsEnabledAt.HasValue;
                        bool gate_createdAfterCutoff   = createdAt >= notifyCutoff;
                        bool gate_within24Hours        = createdAt >= freshnessWindow;
                        bool gate_notPending           = !newTx.Pending;

                        bool notifyEligible =
                            gate_notBackfill &&
                            gate_notificationsEnabled &&
                            gate_createdAfterCutoff &&
                            gate_within24Hours &&
                            gate_notPending;

                        // ── CRITICAL LOG: full eligibility breakdown for every transaction ──
                        _logger.LogInformation(
                            "Transaction notification evaluation: " +
                            "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                            "createdAt={CreatedAt} txDate={TxDate} pending={Pending} " +
                            "isBackfill={IsBackfill} " +
                            "notificationsEnabledAt={NotificationsEnabledAt} " +
                            "notifyCutoff={NotifyCutoff} " +
                            "freshnessWindow={FreshnessWindow} " +
                            "gate_notBackfill={GateNotBackfill} " +
                            "gate_notificationsEnabled={GateNotificationsEnabled} " +
                            "gate_createdAfterCutoff={GateCreatedAfterCutoff} " +
                            "gate_within24Hours={GateWithin24Hours} " +
                            "gate_notPending={GateNotPending} " +
                            "shouldNotify={ShouldNotify}",
                            t.TransactionId, merchantName, absAmount,
                            createdAt.ToString("O"),
                            txDateUtc.ToString("yyyy-MM-dd"),
                            isPending,
                            isBackfill,
                            plaidItem.NotificationsEnabledAt?.ToString("O") ?? "(null)",
                            notifyCutoff == DateTime.MaxValue
                                ? "(MaxValue/not-set)"
                                : notifyCutoff.ToString("O"),
                            freshnessWindow.ToString("O"),
                            gate_notBackfill,
                            gate_notificationsEnabled,
                            gate_createdAfterCutoff,
                            gate_within24Hours,
                            gate_notPending,
                            notifyEligible);

                        SentrySdk.AddBreadcrumb(
                            $"Tx eval: plaidTxId={t.TransactionId} merchant={merchantName} " +
                            $"shouldNotify={notifyEligible} " +
                            $"isBackfill={isBackfill} notificationsEnabled={gate_notificationsEnabled} " +
                            $"createdAfterCutoff={gate_createdAfterCutoff} within24h={gate_within24Hours} " +
                            $"notPending={gate_notPending}",
                            level: notifyEligible ? BreadcrumbLevel.Info : BreadcrumbLevel.Debug);

                        if (notifyEligible)
                        {
                            notifyQueue.Add(newTx);
                            _logger.LogInformation(
                                "Transaction queued for notification: " +
                                "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                "itemId={ItemId}",
                                t.TransactionId, merchantName, absAmount, itemId);
                        }
                        else
                        {
                            // Identify the first failing gate to give a clear skip reason
                            string skipReason = !gate_notBackfill          ? "isBackfill"              :
                                                !gate_notificationsEnabled  ? "notificationsNotEnabled" :
                                                !gate_createdAfterCutoff    ? "createdBeforeCutoff"     :
                                                !gate_within24Hours         ? "olderThan24h"            :
                                                !gate_notPending            ? "isPending"               :
                                                                              "unknown";

                            _logger.LogInformation(
                                "Notification skipped: plaidTxId={PlaidTxId} merchant={Merchant} " +
                                "reason={Reason} itemId={ItemId}",
                                t.TransactionId, merchantName, skipReason, itemId);
                        }
                    }
                }

                // ── 6. Apply net balance delta ─────────────────────────────────────────
                var balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);

                if (balanceDelta != 0m && balanceRecord != null)
                {
                    _logger.LogInformation(
                        "Applying balance delta: userId={UserId} userEmail={UserEmail} " +
                        "delta={Delta} balanceBefore={BalanceBefore} balanceAfter={BalanceAfter}",
                        user.Id, user.Email,
                        balanceDelta,
                        balanceRecord.BalanceAmount,
                        balanceRecord.BalanceAmount + balanceDelta);

                    balanceRecord.BalanceAmount += balanceDelta;
                    balanceRecord.UpdatedAt      = DateTime.UtcNow;
                }

                // ── 7. Advance the cursor ──────────────────────────────────────────────
                string? cursorAfterSync = response.NextCursor;
                plaidItem.Cursor = cursorAfterSync;

                _logger.LogInformation(
                    "Cursor advanced: itemId={ItemId} cursorBefore={CursorBefore} cursorAfter={CursorAfter}",
                    itemId,
                    cursorBeforeSync ?? "(null)",
                    cursorAfterSync  ?? "(null)");

                // ── 8. Set NotificationsEnabledAt on first live sync ───────────────────
                // Done after processing so backfill runs never set this field.
                // Once set it is never cleared or changed.
                bool notificationsJustEnabled = false;
                if (!isBackfill && plaidItem.NotificationsEnabledAt == null)
                {
                    plaidItem.NotificationsEnabledAt = DateTime.UtcNow;
                    // Refresh the local variable so the notify-eligibility check above
                    // works correctly in the same sync call (CreatedAt == NotificationsEnabledAt
                    // is acceptable — we do want live-sync transactions to be notifiable).
                    notifyCutoff = plaidItem.NotificationsEnabledAt.Value;
                    notificationsJustEnabled = true;

                    _logger.LogInformation(
                        "NotificationsEnabledAt SET for the first time: " +
                        "itemId={ItemId} userEmail={UserEmail} enabledAt={EnabledAt} " +
                        "webhookCode={WebhookCode}",
                        itemId, user.Email,
                        plaidItem.NotificationsEnabledAt.Value.ToString("O"),
                        webhookCode ?? "(manual)");

                    SentrySdk.CaptureMessage(
                        $"NotificationsEnabledAt first set: itemId={itemId} userEmail={user.Email} " +
                        $"enabledAt={plaidItem.NotificationsEnabledAt.Value:O} " +
                        $"webhookCode={webhookCode ?? "(manual)"}",
                        scope =>
                        {
                            scope.Level = SentryLevel.Info;
                            scope.SetTag("event.type", "notifications_enabled");
                            scope.SetTag("user.email", user.Email ?? "unknown");
                            scope.SetExtra("itemId", itemId);
                            scope.SetExtra("enabledAt", plaidItem.NotificationsEnabledAt.Value.ToString("O"));
                            scope.SetExtra("webhookCode", webhookCode ?? "(manual)");
                        });
                }

                await _dbContext.SaveChangesAsync();

                var currentDynamicBalance = balanceRecord?.BalanceAmount ?? 0m;

                _logger.LogInformation(
                    "Sync DB saved: itemId={ItemId} webhookCode={WebhookCode} " +
                    "notifyQueueSize={NotifyQueueSize} notificationsJustEnabled={NotificationsJustEnabled} " +
                    "currentBalance={CurrentBalance} cursorAfter={CursorAfter}",
                    itemId, webhookCode ?? "(manual)",
                    notifyQueue.Count, notificationsJustEnabled,
                    currentDynamicBalance, cursorAfterSync ?? "(null)");

                SentrySdk.CaptureMessage(
                    $"Sync complete: itemId={itemId} userEmail={user.Email} " +
                    $"webhookCode={webhookCode ?? "(manual)"} isBackfill={isBackfill} " +
                    $"added={addedCount} modified={modifiedCount} removed={removedCount} " +
                    $"notifyQueueSize={notifyQueue.Count} currentBalance={currentDynamicBalance:0.00}",
                    scope =>
                    {
                        scope.Level = SentryLevel.Info;
                        scope.SetTag("sync.complete", "true");
                        scope.SetTag("user.email", user.Email ?? "unknown");
                        scope.SetTag("sync.isBackfill", isBackfill.ToString());
                        scope.SetExtra("notifyQueueSize", notifyQueue.Count);
                        scope.SetExtra("addedCount", addedCount);
                        scope.SetExtra("modifiedCount", modifiedCount);
                        scope.SetExtra("removedCount", removedCount);
                        scope.SetExtra("currentBalance", currentDynamicBalance);
                        scope.SetExtra("notificationsJustEnabled", notificationsJustEnabled);
                    });

                // ── 9. Fire push notifications ─────────────────────────────────────────
                if (notifyQueue.Count == 0)
                {
                    _logger.LogInformation(
                        "No notifications to send: itemId={ItemId} webhookCode={WebhookCode} " +
                        "userEmail={UserEmail}",
                        itemId, webhookCode ?? "(manual)", user.Email);
                }
                else
                {
                    _logger.LogInformation(
                        "Firing {Count} notification(s): itemId={ItemId} webhookCode={WebhookCode} " +
                        "userEmail={UserEmail}",
                        notifyQueue.Count, itemId, webhookCode ?? "(manual)", user.Email);
                }

                foreach (var tx in notifyQueue)
                {
                    _logger.LogInformation(
                        "Sending notification: txId={TxId} plaidTxId={PlaidTxId} " +
                        "merchant={Merchant} amount={Amount} " +
                        "userId={UserId} userEmail={UserEmail} itemId={ItemId} " +
                        "webhookCode={WebhookCode}",
                        tx.Id, tx.PlaidTransactionId,
                        tx.MerchantName ?? tx.Name, tx.Amount,
                        user.Id, user.Email, itemId,
                        webhookCode ?? "(manual)");

                    try
                    {
                        await _notificationService
                            .SendNewTransactionNotification(tx, currentDynamicBalance, user.Email);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Notification send failed: txId={TxId} plaidTxId={PlaidTxId} " +
                            "userEmail={UserEmail} itemId={ItemId}",
                            tx.Id, tx.PlaidTransactionId, user.Email, itemId);
                        SentrySdk.CaptureException(ex);
                    }
                }

                var syncDuration = DateTime.UtcNow - syncStartedAt;
                _logger.LogInformation(
                    "Sync finished: itemId={ItemId} webhookCode={WebhookCode} userEmail={UserEmail} " +
                    "durationMs={DurationMs} notificationsSent={NotificationsSent}",
                    itemId, webhookCode ?? "(manual)", user.Email,
                    (int)syncDuration.TotalMilliseconds, notifyQueue.Count);

                return response;
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "SYNC EXCEPTION: itemId={ItemId} webhookCode={WebhookCode}",
                    itemId, webhookCode ?? "(manual)");
                throw new Exception("Error syncing and processing transactions.", e);
            }
        }
    }
}