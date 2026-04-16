using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Going.Plaid;
using Going.Plaid.Transactions;
using Sentry;

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
            INotificationService notificationService)
        {
            _dbContext = dbContext;
            _plaidClient = plaidClient;
            _config = config;
            _budgetEngine = budgetEngine;
            _notificationService = notificationService;
        }

        public async Task<TransactionsSyncResponse> SyncAndProcessTransactions(
            string itemId,
            string? webhookCode = null)
        {
            try
            {
                // ── 1. Find Plaid item + user ─────────────────────────────────────────
                var plaidItem = await _dbContext.PlaidItems
                    .FirstOrDefaultAsync(p => p.ItemId == itemId);

                if (plaidItem == null)
                    throw new InvalidOperationException($"Plaid Item {itemId} not found.");

                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == plaidItem.UserId);

                if (user == null)
                    throw new UnauthorizedAccessException("User linked to this Item not found.");

                var fixedCosts = await _dbContext.FixedCosts
                    .Where(fc => fc.UserId == user.Id)
                    .ToListAsync();

                // Is this sync part of the initial Plaid backfill?
                bool isBackfill = webhookCode != null && BackfillCodes.Contains(webhookCode);

                // ── 2. Call Plaid ─────────────────────────────────────────────────────
                var request = new TransactionsSyncRequest
                {
                    AccessToken = plaidItem.AccessToken,
                    Cursor = plaidItem.Cursor,
                    Count = 100
                };

                var response = await _plaidClient.TransactionsSyncAsync(request);

                decimal balanceDelta = 0m;

                // Transactions eligible for notifications are collected here and
                // dispatched AFTER SaveChanges so they have valid IDs.
                var notifyQueue = new List<Transaction>();

                // Cutoff: a transaction must have been created at or after this time
                // AND within the last 24 hours to be eligible for a push notification.
                DateTime notifyCutoff     = plaidItem.NotificationsEnabledAt ?? DateTime.MaxValue;
                DateTime freshnessWindow  = DateTime.UtcNow.AddHours(-24);

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

                        if (existing == null) continue;

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

                        if (existing == null) continue;

                        var rawAmount  = modified.Amount ?? 0m;
                        bool isCredit  = rawAmount < 0m;
                        decimal newAbs = Math.Abs(rawAmount);

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
                        if (exists) continue;

                        DateTime txDate = t.Date
                            .GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow))
                            .ToDateTime(TimeOnly.MinValue);

                        DateTime txDateUtc = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);
                        string merchantName = t.MerchantName ?? t.Name;

                        var rawAmount  = t.Amount ?? 0m;
                        bool isCredit  = rawAmount < 0m;
                        decimal absAmount = Math.Abs(rawAmount);
                        bool isPending = t.Pending ?? false;
                        DateTime createdAt = DateTime.UtcNow;

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
                        }
                        else
                        {
                            bool isFixed = fixedCosts.Any(fc =>
                                !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                                fc.PlaidMerchantName.Equals(
                                    merchantName,
                                    StringComparison.OrdinalIgnoreCase));

                            if (!isFixed)
                            {
                                bool isSuspiciousHold = SuspiciousHoldDetector.IsSuspiciousHold(
                                    t.MerchantName, t.Name, absAmount, isPending);

                                if (isSuspiciousHold)
                                {
                                    newTx.IsSuspiciousHold    = true;
                                    newTx.HoldReviewed        = false;
                                }

                                newTx.BudgetAppliedAmount = absAmount;
                                balanceDelta -= absAmount;

                                if (user.ExpectedPaycheckAmount > 0 &&
                                    _budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                                {
                                    newTx.IsLargeExpenseCandidate = true;
                                    newTx.LargeExpenseHandled     = false;
                                }
                            }
                        }

                        await _dbContext.Transactions.AddAsync(newTx);

                        // ── Notification eligibility ──────────────────────────────────
                        // A transaction qualifies for a push notification only when:
                        //   a) This is NOT a backfill sync
                        //   b) The item's NotificationsEnabledAt cutoff has been set
                        //   c) The transaction was created at or after that cutoff
                        //   d) The transaction is within the 24-hour freshness window
                        //   e) It is a posted (non-pending) transaction
                        bool notifyEligible =
                            !isBackfill &&
                            plaidItem.NotificationsEnabledAt.HasValue &&
                            createdAt >= notifyCutoff &&
                            createdAt >= freshnessWindow &&
                            !newTx.Pending;

                        if (notifyEligible)
                        {
                            notifyQueue.Add(newTx);
                        }
                    }
                }

                // ── 6. Apply net balance delta ─────────────────────────────────────────
                var balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);

                if (balanceDelta != 0m && balanceRecord != null)
                {
                    balanceRecord.BalanceAmount += balanceDelta;
                    balanceRecord.UpdatedAt      = DateTime.UtcNow;
                }

                // ── 7. Advance the cursor ──────────────────────────────────────────────
                plaidItem.Cursor = response.NextCursor;

                // ── 8. Set NotificationsEnabledAt on first live sync ───────────────────
                // Done after processing so backfill runs never set this field.
                // Once set it is never cleared or changed.
                if (!isBackfill && plaidItem.NotificationsEnabledAt == null)
                {
                    plaidItem.NotificationsEnabledAt = DateTime.UtcNow;
                    // Refresh the local variable so the notify-eligibility check above
                    // works correctly in the same sync call (CreatedAt == NotificationsEnabledAt
                    // is acceptable — we do want live-sync transactions to be notifiable).
                    notifyCutoff = plaidItem.NotificationsEnabledAt.Value;
                }

                await _dbContext.SaveChangesAsync();

                var currentDynamicBalance = balanceRecord?.BalanceAmount ?? 0m;

                // ── 9. Fire push notifications ─────────────────────────────────────────
                foreach (var tx in notifyQueue)
                {
                    try
                    {
                        await _notificationService
                            .SendNewTransactionNotification(tx, currentDynamicBalance);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.CaptureException(ex);
                    }
                }

                return response;
            }
            catch (Exception e)
            {
                throw new Exception("Error syncing and processing transactions.", e);
            }
        }
    }
}