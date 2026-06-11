using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Going.Plaid;
using Going.Plaid.Transactions;
using Sentry;
using Microsoft.Extensions.Logging;
// Alias to avoid clash with BudgetApp.Api.Data.Transaction when using
// Going.Plaid.Entity types (e.g. TransactionsGetRequestOptions) below.
using PlaidTransactionEntity = Going.Plaid.Entity.Transaction;
using PlaidTxGetOptions = Going.Plaid.Entity.TransactionsGetRequestOptions;

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

        /// <summary>
        /// Fetches up to <paramref name="monthsBack"/> months of historical transactions
        /// from Plaid using date-range mode (TransactionsGetAsync — NOT cursor-based).
        /// The live sync cursor is never touched.
        ///
        /// Each imported row is tagged:
        ///   IsHistoricalBackfill = true
        ///   BudgetImpactEligible = false
        ///
        /// This means the rows are invisible to all live-budget queries (balance, deposit
        /// review, large-expense review, suspicious-hold review, notifications) but are
        /// included in GET /api/recurring/suggestions for pattern detection.
        ///
        /// Only outflow transactions (raw Plaid Amount > 0) are imported.
        /// Credits/income (raw Plaid Amount &lt;= 0) are skipped.
        /// Idempotent: rows already stored for this user (by PlaidTransactionId) are skipped.
        /// </summary>
        /// <returns>Number of new rows inserted.</returns>
        Task<int> BackfillHistoricalTransactionsForRecurringAnalysis(
            string itemId,
            int monthsBack = 6);
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

                // ── Per-item advisory lock ─────────────────────────────────────────────
                // Prevents concurrent syncs for the same Plaid item (same or different ECS
                // task) from racing on Removed/Added/Modified processing.
                //
                // We use pg_try_advisory_xact_lock, which is:
                //   • transaction-scoped — auto-released on commit or rollback;
                //     safe with EF connection pooling
                //   • non-blocking — returns false immediately if another holder exists
                //   • database-level — works across multiple ECS tasks sharing one PG
                //
                // Key: PlaidItem.Id (stable int PK, fits safely in bigint lock keyspace).
                // Different items have different IDs → syncs for different items are independent.
                //
                // TRADEOFF NOTE: The lock is acquired before the Plaid API network call
                // (below) so it is held across the outbound HTTP request.  This is
                // intentional — it prevents two concurrent webhooks from both calling
                // Plaid with the same cursor and then racing on the resulting write set.
                // A large refactor would be required to acquire the lock after the API
                // call, and the risk/benefit is not worth it in this pass.
                await using var syncTx = await _dbContext.Database.BeginTransactionAsync();

                long advisoryLockKey = (long)plaidItem.Id;

                // EF Core's SqlQuery<T> wraps the raw SQL in a subquery and selects
                // s."Value" from it:
                //   SELECT s."Value" FROM ( <your sql> ) AS s LIMIT 1
                // The inner query MUST alias the scalar result as "Value" — EF requires
                // that exact column name. Without the alias, Postgres names the column
                // after the function ("pg_try_advisory_xact_lock") and the outer
                // SELECT s."Value" throws 42703: column s.Value does not exist.
                bool lockAcquired = await _dbContext.Database
                    .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({advisoryLockKey}) AS \"Value\"")
                    .FirstAsync();

                if (!lockAcquired)
                {
                    _logger.LogWarning(
                        "SYNC_LOCK_BUSY: advisory lock already held for this Plaid item — " +
                        "skipping duplicate concurrent sync. Returning empty response (200). " +
                        "itemId={ItemId} plaidItemDbId={PlaidItemDbId} webhookCode={WebhookCode}",
                        itemId, plaidItem.Id, webhookCode ?? "(manual)");

                    SentrySdk.AddBreadcrumb(
                        $"sync-lock-busy: itemId={itemId} plaidItemDbId={plaidItem.Id} " +
                        $"webhookCode={webhookCode ?? "(manual)"}",
                        level: BreadcrumbLevel.Warning);

                    SentrySdk.CaptureMessage(
                        $"SYNC_LOCK_BUSY: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                        scope =>
                        {
                            scope.Level = SentryLevel.Warning;
                            scope.SetTag("event.type", "sync_lock_busy");
                            scope.SetTag("sync.itemId", itemId);
                            scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                            scope.SetTag("sync.webhookCode", webhookCode ?? "(manual)");
                            scope.SetExtra("sync.userId", plaidItem.UserId);
                        });

                    // Return a safe empty response — the webhook endpoint returns 200.
                    // No transaction mutations occurred; the outer try/finally will roll
                    // back (nothing to roll back) and the caller is unaffected.
                    return new TransactionsSyncResponse();
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
                    scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                    scope.SetTag("sync.webhookCode", webhookCode ?? "(manual)");
                    scope.SetTag("sync.isBackfill", isBackfill.ToString());
                    scope.SetTag("user.email", user.Email ?? "unknown");
                    scope.SetExtra("sync.userId", user.Id);
                    scope.SetExtra("sync.plaidItemDbId", plaidItem.Id);
                    scope.SetExtra("sync.cursorBefore", cursorBeforeSync ?? "(null)");
                    scope.SetExtra("sync.notificationsEnabledAtBefore",
                        notificationsEnabledAtBefore?.ToString("O") ?? "(null)");
                    scope.SetExtra("sync.startedAt", syncStartedAt.ToString("O"));
                });

                // ── 2. Call Plaid (loop until HasMore == false) ───────────────────────
                // Previously this method called TransactionsSyncAsync only once and returned
                // immediately, even when HasMore == true. This left remaining pages unsynced.
                // Now we loop through all pages until Plaid reports HasMore == false.
                //
                // The cursor is advanced after each page, but the PlaidItem.Cursor column
                // is only updated after ALL pages have been processed successfully.
                // This ensures that if processing fails mid-batch, the next sync resumes
                // from the correct position without skipping or duplicating transactions.

                string? workingCursor = plaidItem.Cursor;
                int pagesFetched = 0;
                int totalAddedCount = 0;
                int totalModifiedCount = 0;
                int totalRemovedCount = 0;
                bool finalHasMore = false;
                string? finalCursor = null;

                decimal balanceDelta = 0m;

                // ── Load balance record early for per-transaction notification balance ──
                // Needed to pass the correct post-transaction balance to each notification,
                // not the final post-batch balance.
                var balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);
                decimal notifyRunningBalance = balanceRecord?.BalanceAmount ?? 0m;

                // ── Loop through all Plaid pages until HasMore == false ───────────────
                do
                {
                    var request = new TransactionsSyncRequest
                    {
                        AccessToken = plaidItem.AccessToken,
                        Cursor = workingCursor,
                        Count = 100
                    };

                    var response = await _plaidClient.TransactionsSyncAsync(request);

                    int addedCount = response.Added?.Count ?? 0;
                    int modifiedCount = response.Modified?.Count ?? 0;
                    int removedCount = response.Removed?.Count ?? 0;

                    totalAddedCount += addedCount;
                    totalModifiedCount += modifiedCount;
                    totalRemovedCount += removedCount;
                    pagesFetched++;

                    _logger.LogInformation(
                        "Plaid sync page: userId={UserId} plaidItemDbId={PlaidItemDbId} itemId={ItemId} " +
                        "webhookCode={WebhookCode} page={Page} cursorPresent={CursorPresent} " +
                        "added={Added} modified={Modified} removed={Removed} " +
                        "hasMore={HasMore} nextCursorPresent={NextCursorPresent}",
                        user.Id, plaidItem.Id, itemId, webhookCode ?? "(manual)", pagesFetched,
                        !string.IsNullOrEmpty(workingCursor),
                        addedCount, modifiedCount, removedCount,
                        response.HasMore, !string.IsNullOrEmpty(response.NextCursor));

                    SentrySdk.AddBreadcrumb(
                        $"Plaid sync page {pagesFetched}: itemId={itemId} added={addedCount} " +
                        $"modified={modifiedCount} removed={removedCount} hasMore={response.HasMore}",
                        level: BreadcrumbLevel.Info);

                    finalHasMore = response.HasMore;
                    finalCursor = response.NextCursor;

                    // Transactions eligible for notifications are collected here and
                    // dispatched AFTER SaveChanges so they have valid IDs.
                    // Each entry stores the transaction and the balance immediately after it was applied.
                    var notifyQueue = new List<(Transaction tx, decimal balanceAfterTx)>();

                    // Recurring/fixed-cost matches: no budget impact but user can override.
                    // Each entry stores the balance at the time the transaction was classified.
                    var recurringNotifyQueue = new List<(Transaction tx, int? fixedCostId, string? fixedCostName, decimal balanceAfterTx)>();

                    // Cutoff: the transaction's real date (Plaid Date field, in the transaction's
                    // local timezone — effectively Eastern for US accounts) must be on or after
                    // NotificationsEnabledAt AND fall within today or yesterday in America/New_York.
                    // NOTE: We intentionally do NOT use Transaction.CreatedAt for this check —
                    // CreatedAt is when our backend discovered the transaction, not when it occurred.
                    DateTime notifyCutoff = plaidItem.NotificationsEnabledAt ?? DateTime.MaxValue;

                    // America/New_York — all "today / yesterday" recency comparisons use this zone.
                    // TimeZoneInfo resolves IANA IDs natively on .NET 8 / Linux (Docker runtime image).
                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                    var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone);
                    var todayEastern = DateOnly.FromDateTime(nowEastern);
                    var yesterdayEastern = todayEastern.AddDays(-1);

                    // Registration cutoff: skip any transaction whose real date precedes the moment
                    // the user created their account — prevents flooding new users with history.
                    DateTime userRegisteredAt = user.CreatedAt; // UTC

                    _logger.LogInformation(
                        "Notification gate state: itemId={ItemId} " +
                        "notifyCutoff={NotifyCutoff} isBackfill={IsBackfill} " +
                        "todayEastern={TodayEastern} yesterdayEastern={YesterdayEastern} " +
                        "userRegisteredAt={UserRegisteredAt}",
                        itemId,
                        notifyCutoff == DateTime.MaxValue
                            ? "(MaxValue — notifications not yet enabled)"
                            : notifyCutoff.ToString("O"),
                        isBackfill,
                        todayEastern.ToString("yyyy-MM-dd"),
                        yesterdayEastern.ToString("yyyy-MM-dd"),
                        userRegisteredAt.ToString("O"));

                    // ── 3. Removed ─────────────────────────────────────────────────────────
                    // Plaid removes a pending row when it posts (replaced by an added posted
                    // row) or when it disappears entirely. Either way we reverse the amount
                    // that was applied to the budget for this transaction.
                    if (response.Removed != null)
                    {
                        foreach (var removed in response.Removed)
                        {
                            var existing = await _dbContext.Transactions
                                .FirstOrDefaultAsync(x => x.UserId == user.Id && x.PlaidTransactionId == removed.TransactionId);

                            if (existing == null)
                            {
                                _logger.LogInformation(
                                    "Removed tx not in DB (skip): userId={UserId} plaidItemDbId={PlaidItemDbId} " +
                                    "itemId={ItemId} webhookCode={WebhookCode} plaidTxId={PlaidTxId}",
                                    user.Id, plaidItem.Id, itemId, webhookCode ?? "(manual)", removed.TransactionId);
                                continue;
                            }

                            _logger.LogInformation(
                                "Removing transaction: userId={UserId} plaidItemDbId={PlaidItemDbId} itemId={ItemId} " +
                                "webhookCode={WebhookCode} plaidTxId={PlaidTxId} localTxId={TxId} " +
                                "accountId={AccountId} date={Date} merchant={Merchant} amount={Amount} " +
                                "pending={Pending} budgetAppliedAmount={BudgetAppliedAmount}",
                                user.Id, plaidItem.Id, itemId, webhookCode ?? "(manual)",
                                removed.TransactionId, existing.Id, existing.AccountId,
                                existing.Date.ToString("yyyy-MM-dd"), existing.MerchantName ?? existing.Name,
                                existing.Amount, existing.Pending, existing.BudgetAppliedAmount);

                            if (existing.BudgetAppliedAmount.HasValue && existing.BudgetAppliedAmount.Value != 0)
                            {
                                balanceDelta += existing.BudgetAppliedAmount.Value;
                                notifyRunningBalance += existing.BudgetAppliedAmount.Value;
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
                                .FirstOrDefaultAsync(x => x.UserId == user.Id && x.PlaidTransactionId == modified.TransactionId);

                            if (existing == null)
                            {
                                _logger.LogInformation(
                                    "Modified tx not in DB (skip): userId={UserId} plaidItemDbId={PlaidItemDbId} " +
                                    "itemId={ItemId} webhookCode={WebhookCode} plaidTxId={PlaidTxId}",
                                    user.Id, plaidItem.Id, itemId, webhookCode ?? "(manual)", modified.TransactionId);
                                continue;
                            }

                            var rawAmount = modified.Amount ?? 0m;
                            bool isCredit = rawAmount < 0m;
                            decimal newAbs = Math.Abs(rawAmount);

                            _logger.LogInformation(
                                "Modifying transaction: userId={UserId} plaidItemDbId={PlaidItemDbId} itemId={ItemId} " +
                                "webhookCode={WebhookCode} plaidTxId={PlaidTxId} localTxId={TxId} " +
                                "oldAmount={OldAmount} newAmount={NewAmount} isCredit={IsCredit} " +
                                "oldPending={OldPending} newPending={NewPending}",
                                user.Id, plaidItem.Id, itemId, webhookCode ?? "(manual)",
                                modified.TransactionId, existing.Id,
                                existing.Amount, newAbs, isCredit,
                                existing.Pending, modified.Pending ?? existing.Pending);

                            if (!isCredit && existing.BudgetAppliedAmount.HasValue)
                            {
                                decimal delta = existing.BudgetAppliedAmount.Value - newAbs;
                                balanceDelta += delta;
                                notifyRunningBalance += delta;
                                existing.BudgetAppliedAmount = newAbs;
                            }

                            existing.Amount = newAbs;
                            existing.Pending = modified.Pending ?? existing.Pending;
                            existing.UpdatedAt = DateTime.UtcNow;

                            _logger.LogInformation(
                                "Modified transaction updated: localTxId={TxId} accountId={AccountId} " +
                                "date={Date} merchant={Merchant} amount={Amount} pending={Pending} " +
                                "pendingTransactionId={PendingTxId} budgetAppliedAmount={BudgetAppliedAmount}",
                                existing.Id, existing.AccountId, existing.Date.ToString("yyyy-MM-dd"),
                                existing.MerchantName ?? existing.Name, existing.Amount, existing.Pending,
                                existing.PendingTransactionId ?? "(null)", existing.BudgetAppliedAmount);
                        }
                    }

                    // ── Commit removals and modifications before the Added per-transaction loop ──
                    // A ChangeTracker.Clear() triggered by a 23505 race recovery in the Added loop
                    // would otherwise discard these pending deletions and updates. Committing them
                    // first ensures they survive any per-transaction race-condition recovery.
                    //
                    // Defense-in-depth: if DbUpdateConcurrencyException still fires here despite
                    // the advisory lock (e.g. due to an out-of-order webhook that somehow bypassed
                    // the lock), we capture it clearly and rethrow rather than silently continuing
                    // with a potentially inconsistent balance state.
                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        try
                        {
                            await _dbContext.SaveChangesAsync();
                        }
                        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException concEx)
                        {
                            // Another sync already deleted or modified the same rows.
                            // This should not occur when the advisory lock is working correctly.
                            // Log at Error (not Warning) so it is immediately visible in Sentry
                            // — if this fires it means the lock did not prevent the race.
                            _logger.LogError(concEx,
                                "SYNC_CONCURRENCY_DEFENSE: DbUpdateConcurrencyException during " +
                                "Removed/Modified save — advisory lock may not have prevented the race. " +
                                "itemId={ItemId} userId={UserId} " +
                                "removedCount={RemovedCount} modifiedCount={ModifiedCount}",
                                itemId, user.Id, removedCount, modifiedCount);

                            SentrySdk.CaptureException(concEx, scope =>
                            {
                                scope.Level = SentryLevel.Error;
                                scope.SetTag("event.type", "sync_concurrency_defense");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                                scope.SetExtra("sync.userId", user.Id);
                                scope.SetExtra("sync.removedCount", removedCount);
                                scope.SetExtra("sync.modifiedCount", modifiedCount);
                                scope.SetExtra("sync.webhookCode", webhookCode ?? "(manual)");
                            });

                            // Clear stale tracked entities — do NOT apply further balance or
                            // cursor mutations from the now-inconsistent EF state.
                            _dbContext.ChangeTracker.Clear();

                            // Rethrow so the outer catch logs SYNC EXCEPTION and the webhook
                            // handler returns a 500, causing Plaid to retry this sync.
                            throw;
                        }
                    }

                    // ── 5. Added ───────────────────────────────────────────────────────────
                    if (response.Added != null)
                    {
                        // seenInThisBatch guards against the rare case where Plaid includes the
                        // same plaid_transaction_id twice within a single sync response.
                        // This is the first line of defense; the DB unique constraint on
                        // (user_id, plaid_transaction_id) is the authoritative guard.
                        var seenInThisBatch = new HashSet<string>(StringComparer.Ordinal);

                        foreach (var t in response.Added)
                        {
                            // ── Guard 1: duplicate within this batch ──────────────────────
                            if (!seenInThisBatch.Add(t.TransactionId ?? string.Empty))
                            {
                                _logger.LogWarning(
                                    "Duplicate plaidTxId within single sync batch — skipping second occurrence: " +
                                    "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                    "itemId={ItemId} webhookCode={WebhookCode}",
                                    t.TransactionId, t.MerchantName ?? t.Name,
                                    Math.Abs(t.Amount ?? 0m), itemId, webhookCode ?? "(manual)");

                                SentrySdk.AddBreadcrumb(
                                    $"tx-batch-duplicate-skip: plaidTxId={t.TransactionId}",
                                    level: BreadcrumbLevel.Warning);

                                continue;
                            }

                            // ── Parse date / amount first — needed by both update and insert paths ──
                            DateTime txDate = t.Date
                                .GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow))
                                .ToDateTime(TimeOnly.MinValue);

                            DateTime txDateUtc = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);

                            var userCreatedDateUtc = DateOnly.FromDateTime(user.CreatedAt.ToUniversalTime());
                            var txDateOnlyUtc = DateOnly.FromDateTime(txDateUtc);

                            if (txDateOnlyUtc < userCreatedDateUtc)
                            {
                                _logger.LogInformation(
                                    "Skipping pre-registration transaction: plaidTxId={PlaidTxId} txDate={TxDate} userCreatedAt={UserCreatedAt} userId={UserId} userEmail={UserEmail}",
                                    t.TransactionId,
                                    txDateOnlyUtc.ToString("yyyy-MM-dd"),
                                    user.CreatedAt.ToString("O"),
                                    user.Id,
                                    user.Email);

                                continue;
                            }

                            string merchantName = t.MerchantName ?? t.Name ?? "(unknown)";

                            var rawAmount = t.Amount ?? 0m;
                            bool isCredit = rawAmount < 0m;
                            decimal absAmount = Math.Abs(rawAmount);
                            bool isPending = t.Pending ?? false;
                            DateTime createdAt = DateTime.UtcNow;

                            // ── Guard 2 → Upsert: check for existing row by UserId + PlaidTransactionId ──
                            // If a row already exists (e.g. repeated webhook, prior race-condition insert),
                            // update it with the latest Plaid data instead of inserting a duplicate.
                            // Preserves all user/manual fields. The DB unique index on
                            // (user_id, plaid_transaction_id) is the authoritative duplicate guard.
                            var existingTx = await _dbContext.Transactions
                                .FirstOrDefaultAsync(x => x.UserId == user.Id && x.PlaidTransactionId == t.TransactionId);

                            if (existingTx != null)
                            {
                                // ── UPSERT UPDATE PATH ─────────────────────────────────────────────────
                                // Update Plaid-sourced fields. Preserve all user/manual decision fields.
                                //
                                // Updated from Plaid:   Amount, Date, Name, MerchantName, Pending,
                                //                       AccountId, UpdatedAt
                                // Preserved always:     UserDecision, CountedAsIncome, LargeExpenseHandled,
                                //                       HoldReviewed, HoldOverrideAmount,
                                //                       BudgetImpactEligible, IsHistoricalBackfill
                                // Recalculated safely:  BudgetAppliedAmount (delta only, no double-apply)

                                decimal oldApplied = existingTx.BudgetAppliedAmount ?? 0m;
                                bool hadBudgetApplied = existingTx.BudgetAppliedAmount.HasValue;

#pragma warning disable CS0612
                                existingTx.Name = t.Name ?? existingTx.Name;
#pragma warning restore CS0612
                                existingTx.MerchantName = t.MerchantName ?? existingTx.MerchantName;
                                existingTx.Amount = absAmount;
                                existingTx.Date = txDateUtc;
                                existingTx.Pending = isPending;
                                existingTx.AccountId = t.AccountId ?? existingTx.AccountId;
                                existingTx.UpdatedAt = DateTime.UtcNow;

                                // Adjust balance only when all of:
                                //   • this is a spend row (not credit)
                                //   • it previously had a budget impact (BudgetAppliedAmount was set)
                                //   • user hasn't already reviewed/overridden the hold
                                //   • the amount actually changed (avoid a no-op delta)
                                //
                                // Formula mirrors the existing Modified handler:
                                //   delta = oldApplied - newAbsAmount
                                //   positive delta → balance goes up   (old over-deducted)
                                //   negative delta → balance goes down  (new deduction is larger)
                                if (!isCredit && hadBudgetApplied && !existingTx.HoldReviewed
                                    && oldApplied != absAmount)
                                {
                                    decimal delta = oldApplied - absAmount;
                                    balanceDelta += delta;
                                    notifyRunningBalance += delta;
                                    existingTx.BudgetAppliedAmount = absAmount;
                                }

                                await _dbContext.SaveChangesAsync();

                                _logger.LogInformation(
                                    "Upsert-updated existing transaction from Plaid added list: " +
                                    "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                    "itemId={ItemId} webhookCode={WebhookCode}",
                                    t.TransactionId, merchantName, absAmount,
                                    itemId, webhookCode ?? "(manual)");

                                SentrySdk.AddBreadcrumb(
                                    $"tx-upsert-updated: plaidTxId={t.TransactionId} " +
                                    $"merchant={merchantName} amt={absAmount}",
                                    level: BreadcrumbLevel.Info);

                                // Upsert-updated rows are not re-queued for notifications.
                                // Notifications were already sent on the original insertion.
                                continue;
                            }

                            // ── INSERT PATH ────────────────────────────────────────────────────────
                            // No existing row found — classify and insert as new.

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
                                UserId = user.Id,
                                PlaidTransactionId = t.TransactionId,
                                // Populated on posted transactions — references the PlaidTransactionId
                                // of the prior pending row this posted transaction replaced.
                                // Null for pending transactions and for posted transactions with no
                                // prior pending row.
                                PendingTransactionId = t.PendingTransactionId,
                                AccountId = t.AccountId,
                                Amount = absAmount,
                                Date = txDateUtc,
#pragma warning disable CS0612
                                Name = t.Name,
#pragma warning restore CS0612
                                MerchantName = t.MerchantName,
                                Pending = isPending,
                                CreatedAt = createdAt,
                                UpdatedAt = createdAt,
                                IsLargeExpenseCandidate = false,
                                LargeExpenseHandled = false
                            };

                            // Outer-scope variables so the notification eligibility block
                            // (which is outside the isCredit/isFixed branches) can use them.
                            FixedCost? matchedFcForNotification = null;
                            bool isFixedCostMatch = false;

                            if (isCredit)
                            {
                                var ctx = new DepositContext
                                {
                                    Amount = absAmount,
                                    Date = txDateUtc,
                                    MerchantName = merchantName,
                                    PayDay1 = user.PayDay1,
                                    PayDay2 = user.PayDay2,
                                    ExpectedPaycheckAmount = user.ExpectedPaycheckAmount
                                };

                                newTx.SuggestedKind = _budgetEngine.ClassifyDeposit(ctx);
                                _logger.LogInformation(
                                    "Credit tx classified: plaidTxId={PlaidTxId} suggestedKind={SuggestedKind}",
                                    t.TransactionId, newTx.SuggestedKind);
                            }
                            else
                            {
                                // ── Fixed-cost / recurring match ───────────────────────────────
                                // Priority 1: exact PlaidMerchantName (plaid_discovered or previously
                                //   enriched manual cost).
                                // Priority 2: manual cost — amount within tolerance + date window OR
                                //   amount within tolerance + name-token overlap (null-due-date path).
                                // MatchType is captured BEFORE enrichment so logs are not polluted.
                                var (matchedFc, matchType) = FixedCostMatcher.TryMatch(
                                    fixedCosts, merchantName, absAmount, txDateUtc);

                                if (matchedFc != null)
                                {
                                    matchedFcForNotification = matchedFc;
                                    isFixedCostMatch = true;

                                    // Enrich manual cost with Plaid merchant info so future syncs
                                    // fall through to the faster Priority 1 merchant-name path.
                                    if (string.IsNullOrEmpty(matchedFc.PlaidMerchantName) &&
                                        !string.IsNullOrEmpty(t.MerchantName))
                                    {
                                        matchedFc.PlaidMerchantName = t.MerchantName;
                                        matchedFc.PlaidAccountId = t.AccountId;
                                    }

                                    // ── Advance NextDueDate for recurring fixed costs ───────────
                                    // Advance the due date past the matched transaction so the cost
                                    // participates correctly in the NEXT budget period and the
                                    // matcher can re-match next month.
                                    //
                                    // OneTime costs are NOT advanced — they retain their original
                                    // NextDueDate so they remain visible but won't auto-advance.
                                    //
                                    // OriginalDueDayOfMonth is used by FixedCostAdvancer to
                                    // restore the intended day after short-month clamping
                                    // (e.g. Jan 31 → Feb 28 → Mar 31).
                                    // We intentionally do NOT update OriginalDueDayOfMonth here —
                                    // only explicit user edits via PUT /api/fixed-costs/{id} may
                                    // change it.
                                    string fcFreq = matchedFc.RecurrenceFrequency ?? "Monthly";
                                    bool isRecurring = !string.Equals(
                                        fcFreq, "OneTime", StringComparison.OrdinalIgnoreCase);

                                    if (matchedFc.NextDueDate.HasValue && isRecurring)
                                    {
                                        DateTime oldDueDate = matchedFc.NextDueDate.Value;
                                        matchedFc.NextDueDate = FixedCostAdvancer.AdvanceNextDueDate(
                                            oldDueDate,
                                            fcFreq,
                                            txDateUtc,
                                            matchedFc.OriginalDueDayOfMonth);
                                        matchedFc.UpdatedAt = DateTime.UtcNow;

                                        _logger.LogInformation(
                                            "Fixed cost NextDueDate advanced: " +
                                            "fixedCostId={FixedCostId} fixedCostName={FixedCostName} " +
                                            "recurrenceFrequency={Frequency} " +
                                            "oldDueDate={OldDueDate} newDueDate={NewDueDate} " +
                                            "triggeredByTx={PlaidTxId}",
                                            matchedFc.Id, matchedFc.Name, fcFreq,
                                            oldDueDate.ToString("yyyy-MM-dd"),
                                            matchedFc.NextDueDate.Value.ToString("yyyy-MM-dd"),
                                            t.TransactionId);
                                    }

                                    _logger.LogInformation(
                                        "Transaction matched as fixed cost (no budget impact): " +
                                        "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                        "matchedFixedCostId={FixedCostId} matchedFixedCostName={FixedCostName} " +
                                        "matchType={MatchType}",
                                        t.TransactionId, merchantName, absAmount,
                                        matchedFc.Id, matchedFc.Name, matchType);
                                }
                                else
                                {
                                    bool isSuspiciousHold = SuspiciousHoldDetector.IsSuspiciousHold(
                                        t.MerchantName, t.Name, absAmount, isPending);

                                    if (isSuspiciousHold)
                                    {
                                        newTx.IsSuspiciousHold = true;
                                        newTx.HoldReviewed = false;
                                        _logger.LogInformation(
                                            "Transaction flagged as suspicious hold " +
                                            "(BudgetAppliedAmount=0, awaiting user override): " +
                                            "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount}",
                                            t.TransactionId, merchantName, absAmount);
                                    }

                                    // ── Pending-to-posted reconciliation ──────────────────────────────
                                    // A posted transaction that carries a PendingTransactionId is
                                    // replacing a prior pending row.  Normally the pending row also
                                    // appears in the Removed list and its BudgetAppliedAmount has
                                    // already been reversed above.  But if the pending row did NOT
                                    // appear in Removed (out-of-order webhook, prior sync gap, etc.)
                                    // we look it up and apply only the DELTA to prevent double-counting.
                                    Transaction? priorPending = null;
                                    if (!string.IsNullOrEmpty(t.PendingTransactionId))
                                    {
                                        priorPending = await _dbContext.Transactions
                                            .FirstOrDefaultAsync(x =>
                                                x.UserId == user.Id &&
                                                x.PlaidTransactionId == t.PendingTransactionId);
                                    }

                                    // The amount that SHOULD affect the dynamic balance going forward:
                                    //   suspicious hold → 0   (user sets override via hold review flow)
                                    //   normal spend    → absAmount
                                    decimal desiredImpact = PendingReconciliationCalculator
                                        .ComputeDesiredBudgetImpact(
                                            absAmount,
                                            isFixedCostMatch: false,
                                            isSuspiciousHold,
                                            holdOverrideAmount: null);

                                    // Amount already applied via the prior pending row (0 if none).
                                    decimal existingApplied = priorPending?.BudgetAppliedAmount ?? 0m;

                                    // Net balance change (negative = balance drops, positive = rises).
                                    decimal netDelta = PendingReconciliationCalculator
                                        .ComputeBalanceDelta(desiredImpact, existingApplied);

                                    newTx.BudgetAppliedAmount = desiredImpact;
                                    balanceDelta += netDelta;
                                    notifyRunningBalance += netDelta;

                                    if (priorPending != null)
                                    {
                                        // Neutralize prior pending so the Removed handler cannot
                                        // double-reverse it if Plaid sends it in a future Removed list.
                                        priorPending.BudgetAppliedAmount = 0m;

                                        _logger.LogInformation(
                                            "PENDING_TO_POSTED_RECONCILIATION: " +
                                            "postedPlaidTxId={PostedId} " +
                                            "priorPendingPlaidTxId={PendingId} " +
                                            "priorApplied={PriorApplied} " +
                                            "desiredImpact={Desired} " +
                                            "netBalanceDelta={Delta}",
                                            t.TransactionId, t.PendingTransactionId,
                                            existingApplied, desiredImpact, netDelta);

                                        SentrySdk.AddBreadcrumb(
                                            $"pending-to-posted-reconciliation: " +
                                            $"posted={t.TransactionId} " +
                                            $"pending={t.PendingTransactionId} " +
                                            $"priorApplied={existingApplied} " +
                                            $"desiredImpact={desiredImpact} " +
                                            $"netDelta={netDelta}",
                                            level: BreadcrumbLevel.Info);
                                    }

                                    if (user.ExpectedPaycheckAmount > 0 &&
                                        _budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                                    {
                                        newTx.IsLargeExpenseCandidate = true;
                                        newTx.LargeExpenseHandled = false;
                                        _logger.LogInformation(
                                            "Transaction flagged as large expense candidate: " +
                                            "plaidTxId={PlaidTxId} amount={Amount}",
                                            t.TransactionId, absAmount);
                                    }
                                }
                            }

                            await _dbContext.Transactions.AddAsync(newTx);

                            // ── Per-transaction save with 23505 race-condition recovery ─────────────
                            // Each new transaction is saved individually so the 23505 handler has
                            // full access to the current transaction's context without parsing
                            // pgEx.Detail. A ChangeTracker.Clear() here only discards the current
                            // entity — removals and modifications were already committed above.
                            try
                            {
                                await _dbContext.SaveChangesAsync();

                                SentrySdk.AddBreadcrumb(
                                    $"tx-insert: plaidTxId={t.TransactionId} " +
                                    $"merchant={merchantName} amt={absAmount}",
                                    level: BreadcrumbLevel.Info);
                            }
                            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
                                when (dbEx.InnerException is Npgsql.PostgresException pgEx
                                      && pgEx.SqlState == "23505")
                            {
                                // Concurrent webhook inserted this transaction between our
                                // FirstOrDefaultAsync check and our SaveChangesAsync.
                                // Clear the failed entity from the change tracker, then re-query
                                // and update the existing row using the same logic as the upsert
                                // update path above. Context is already available (t, user.Id, etc.)
                                // — no pgEx.Detail parsing needed.
                                _dbContext.ChangeTracker.Clear();

                                _logger.LogWarning(
                                    "23505 on insert — race condition: concurrent webhook inserted " +
                                    "this transaction first. Recovering by updating existing row: " +
                                    "plaidTxId={PlaidTxId} userId={UserId} itemId={ItemId} " +
                                    "constraintName={ConstraintName}",
                                    t.TransactionId, user.Id, itemId,
                                    pgEx.ConstraintName ?? "(unknown)");

                                SentrySdk.AddBreadcrumb(
                                    $"23505-recovery-start: plaidTxId={t.TransactionId} userId={user.Id}",
                                    level: BreadcrumbLevel.Warning);

                                SentrySdk.CaptureMessage(
                                    $"23505 insert race: plaidTxId={t.TransactionId} itemId={itemId}",
                                    scope =>
                                    {
                                        scope.Level = SentryLevel.Warning;
                                        scope.SetTag("event.type", "sync_23505_insert_race");
                                        scope.SetTag("sync.itemId", itemId ?? "unknown");
                                        scope.SetTag("sync.webhookCode", webhookCode ?? "(manual)");
                                        scope.SetExtra("plaidTxId", t.TransactionId);
                                        scope.SetExtra("userId", user.Id);
                                        scope.SetExtra("pg.constraintName", pgEx.ConstraintName);
                                    });

                                // Re-query by the known UserId + PlaidTransactionId — no Detail parsing.
                                var raceExisting = await _dbContext.Transactions
                                    .FirstOrDefaultAsync(x =>
                                        x.UserId == user.Id &&
                                        x.PlaidTransactionId == t.TransactionId);

                                if (raceExisting == null)
                                {
                                    // 23505 means a row MUST exist — if not found, something is wrong.
                                    _logger.LogError(
                                        "23505 recovery: existing row not found after conflict — " +
                                        "re-throwing. plaidTxId={PlaidTxId} userId={UserId}",
                                        t.TransactionId, user.Id);
                                    SentrySdk.CaptureMessage(
                                        $"23505 recovery failed: row not found plaidTxId={t.TransactionId}",
                                        s => s.Level = SentryLevel.Error);
                                    throw;
                                }

                                // Apply the same update field logic as the normal upsert update path.
                                // Preserves all user/manual fields. Adjusts BudgetAppliedAmount if changed.
                                decimal oldAppliedRace = raceExisting.BudgetAppliedAmount ?? 0m;
                                bool hadBudgetAppliedRace = raceExisting.BudgetAppliedAmount.HasValue;

#pragma warning disable CS0612
                                raceExisting.Name = t.Name ?? raceExisting.Name;
#pragma warning restore CS0612
                                raceExisting.MerchantName = t.MerchantName ?? raceExisting.MerchantName;
                                raceExisting.Amount = absAmount;
                                raceExisting.Date = txDateUtc;
                                raceExisting.Pending = isPending;
                                raceExisting.AccountId = t.AccountId ?? raceExisting.AccountId;
                                raceExisting.UpdatedAt = DateTime.UtcNow;

                                if (!isCredit && hadBudgetAppliedRace && !raceExisting.HoldReviewed
                                    && oldAppliedRace != absAmount)
                                {
                                    decimal recoveryDelta = oldAppliedRace - absAmount;
                                    balanceDelta += recoveryDelta;
                                    notifyRunningBalance += recoveryDelta;
                                    raceExisting.BudgetAppliedAmount = absAmount;
                                }

                                try
                                {
                                    await _dbContext.SaveChangesAsync();
                                }
                                catch (Exception recoveryEx)
                                {
                                    _logger.LogError(recoveryEx,
                                        "23505 recovery SaveChanges failed — re-throwing: " +
                                        "plaidTxId={PlaidTxId} userId={UserId} itemId={ItemId}",
                                        t.TransactionId, user.Id, itemId);
                                    SentrySdk.CaptureException(recoveryEx);
                                    throw;
                                }

                                SentrySdk.AddBreadcrumb(
                                    $"23505-recovery-complete: plaidTxId={t.TransactionId}",
                                    level: BreadcrumbLevel.Info);

                                _logger.LogInformation(
                                    "23505 recovery complete — existing row updated: " +
                                    "plaidTxId={PlaidTxId} userId={UserId} itemId={ItemId}",
                                    t.TransactionId, user.Id, itemId);

                                // Recovery path: no notification (existing row was already notified).
                                continue;
                            }

                            // ── Notification eligibility ──────────────────────────────────
                            // A transaction qualifies for a push notification only when ALL of:
                            //   a) This is NOT a backfill sync
                            //   b) The item's NotificationsEnabledAt cutoff has been set
                            //   c) The transaction's real date (Plaid Date field) is at or after
                            //      that cutoff  — NOT Transaction.CreatedAt
                            //   d) The transaction's real date is at or after the user's registration
                            //      (prevents new users from receiving pre-registration history)
                            //   e) The transaction's real date (in America/New_York) is today or
                            //      yesterday  — NOT a rolling 24-hour window from CreatedAt
                            //   f) It is a posted (non-pending) transaction

                            // Plaid's Date field is a DateOnly in the transaction's local timezone.
                            // For US accounts this is effectively Eastern / merchant-local time.
                            // We treat it as an Eastern date for the recency window.
                            DateOnly? plaidTxDate = t.Date;

                            // Convert the Plaid date to an approximate UTC instant (Eastern midnight)
                            // for comparisons against UTC-based cutoffs (NotificationsEnabledAt,
                            // user.CreatedAt). Falls back to CreatedAt only when Plaid sends no date.
                            DateTime txDateAsEasternMidnightUtc = plaidTxDate.HasValue
                                ? TimeZoneInfo.ConvertTimeToUtc(
                                      plaidTxDate.Value.ToDateTime(TimeOnly.MinValue),
                                      easternZone)
                                : createdAt; // last resort: backend discovery time

                            bool gate_notBackfill = !isBackfill;
                            bool gate_notificationsEnabled = plaidItem.NotificationsEnabledAt.HasValue;

                            // Gate c: real tx date ≥ NotificationsEnabledAt
                            bool gate_realDateAfterCutoff = txDateAsEasternMidnightUtc >= notifyCutoff;

                            // Gate d: real tx date ≥ user.CreatedAt  (skip pre-registration history)
                            var userRegisteredDate = DateOnly.FromDateTime(user.CreatedAt.ToUniversalTime());

                            bool gate_afterRegistration = plaidTxDate.HasValue &&
                            plaidTxDate.Value >= userRegisteredDate;

                            // Gate e: real tx date is today or yesterday in America/New_York
                            bool gate_todayOrYesterday = plaidTxDate.HasValue &&
                                (plaidTxDate.Value == todayEastern || plaidTxDate.Value == yesterdayEastern);

                            bool gate_notSuspiciousHold = !newTx.IsSuspiciousHold;

                            bool notifyEligible =
                                gate_notBackfill &&
                                gate_notificationsEnabled &&
                                gate_realDateAfterCutoff &&
                                gate_afterRegistration &&
                                gate_todayOrYesterday &&
                                gate_notSuspiciousHold;

                            // ── CRITICAL LOG: full eligibility breakdown for every transaction ──
                            _logger.LogInformation(
                                "Transaction notification evaluation: " +
                                "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                "createdAt={CreatedAt} plaidTxDate={PlaidTxDate} pending={Pending} " +
                                "isSuspiciousHold={IsSuspiciousHold} budgetAppliedAmount={BudgetAppliedAmount} " +
                                "isBackfill={IsBackfill} " +
                                "notificationsEnabledAt={NotificationsEnabledAt} " +
                                "notifyCutoff={NotifyCutoff} " +
                                "userRegisteredAt={UserRegisteredAt} " +
                                "todayEastern={TodayEastern} yesterdayEastern={YesterdayEastern} " +
                                "gate_notBackfill={GateNotBackfill} " +
                                "gate_notificationsEnabled={GateNotificationsEnabled} " +
                                "gate_realDateAfterCutoff={GateRealDateAfterCutoff} " +
                                "gate_afterRegistration={GateAfterRegistration} " +
                                "gate_todayOrYesterday={GateTodayOrYesterday} " +
                                "gate_notSuspiciousHold={GateNotSuspiciousHold} " +
                                "shouldNotify={ShouldNotify}",
                                t.TransactionId, merchantName, absAmount,
                                createdAt.ToString("O"),
                                plaidTxDate?.ToString("yyyy-MM-dd") ?? "(null)",
                                isPending,
                                newTx.IsSuspiciousHold,
                                newTx.BudgetAppliedAmount?.ToString("F2") ?? "null",
                                isBackfill,
                                plaidItem.NotificationsEnabledAt?.ToString("O") ?? "(null)",
                                notifyCutoff == DateTime.MaxValue
                                    ? "(MaxValue/not-set)"
                                    : notifyCutoff.ToString("O"),
                                userRegisteredAt.ToString("O"),
                                todayEastern.ToString("yyyy-MM-dd"),
                                yesterdayEastern.ToString("yyyy-MM-dd"),
                                gate_notBackfill,
                                gate_notificationsEnabled,
                                gate_realDateAfterCutoff,
                                gate_afterRegistration,
                                gate_todayOrYesterday,
                                gate_notSuspiciousHold,
                                notifyEligible);

                            SentrySdk.AddBreadcrumb(
                                $"Tx eval: plaidTxId={t.TransactionId} merchant={merchantName} " +
                                $"shouldNotify={notifyEligible} pending={isPending} " +
                                $"isSuspiciousHold={newTx.IsSuspiciousHold} budgetAppliedAmount={newTx.BudgetAppliedAmount?.ToString("F2") ?? "null"} " +
                                $"isBackfill={isBackfill} notificationsEnabled={gate_notificationsEnabled} " +
                                $"realDateAfterCutoff={gate_realDateAfterCutoff} " +
                                $"afterRegistration={gate_afterRegistration} " +
                                $"todayOrYesterday={gate_todayOrYesterday} " +
                                $"notSuspiciousHold={gate_notSuspiciousHold}",
                                level: notifyEligible ? BreadcrumbLevel.Info : BreadcrumbLevel.Debug);

                            if (notifyEligible)
                            {
                                if (isFixedCostMatch)
                                {
                                    recurringNotifyQueue.Add((newTx, matchedFcForNotification?.Id, matchedFcForNotification?.Name, notifyRunningBalance));
                                    _logger.LogInformation(
                                        "Transaction queued for recurring notification: " +
                                        "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                        "matchedFixedCostId={FixedCostId} balanceAtClassification={Balance} itemId={ItemId}",
                                        t.TransactionId, merchantName, absAmount,
                                        matchedFcForNotification?.Id.ToString() ?? "(none)",
                                        notifyRunningBalance, itemId);
                                }
                                else
                                {
                                    notifyQueue.Add((newTx, notifyRunningBalance));
                                    _logger.LogInformation(
                                        "Transaction queued for notification: " +
                                        "plaidTxId={PlaidTxId} merchant={Merchant} amount={Amount} " +
                                        "balanceAfterTx={BalanceAfterTx} itemId={ItemId}",
                                        t.TransactionId, merchantName, absAmount,
                                        notifyRunningBalance, itemId);
                                }
                            }
                            else
                            {
                                // Identify the first failing gate to give a clear skip reason
                                string skipReason = !gate_notBackfill ? "skipped: backfill" :
                                                    !gate_notificationsEnabled ? "skipped: notifications not enabled" :
                                                    !gate_realDateAfterCutoff ? "skipped: backfill (before cutoff)" :
                                                    !gate_afterRegistration ? "skipped: before user registration" :
                                                    !gate_todayOrYesterday ? "skipped: older than yesterday Eastern" :
                                                    !gate_notSuspiciousHold ? "skipped: suspicious hold" :
                                                                                  "skipped: unknown";

                                _logger.LogInformation(
                                    "Notification skipped: plaidTxId={PlaidTxId} merchant={Merchant} " +
                                    "reason={Reason} itemId={ItemId}",
                                    t.TransactionId, merchantName, skipReason, itemId);
                            }
                        }
                    }

                    // ── 6. Apply net balance delta ─────────────────────────────────────────
                    // (balanceRecord was loaded earlier for per-transaction notification balance tracking)

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
                        balanceRecord.UpdatedAt = DateTime.UtcNow;
                    }

                    // ── 7. Advance the cursor ──────────────────────────────────────────────
                    // Guard: never overwrite a valid existing cursor with null/empty.
                    // If Plaid returns null NextCursor we retain the prior cursor so the next
                    // sync can resume from the correct position rather than re-fetching from
                    // the beginning. Both cases (existing cursor present / no prior cursor)
                    // are logged and reported to Sentry for observability.
                    string? cursorAfterSync = response.NextCursor;

                    if (!string.IsNullOrEmpty(cursorAfterSync))
                    {
                        plaidItem.Cursor = cursorAfterSync;
                    }
                    else if (!string.IsNullOrEmpty(cursorBeforeSync))
                    {
                        // Plaid returned null but we have a valid existing cursor — retain it.
                        _logger.LogWarning(
                            "CURSOR_NULL_GUARD: retaining existing cursor because Plaid returned null NextCursor. " +
                            "itemId={ItemId} plaidItemDbId={PlaidItemDbId}",
                            itemId,
                            plaidItem.Id);

                        SentrySdk.CaptureMessage(
                            $"CURSOR_NULL_GUARD: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                            scope =>
                            {
                                scope.Level = SentryLevel.Info;
                                scope.SetTag("event.type", "cursor_null_guard_retained");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetExtra("plaidItemDbId", plaidItem.Id);
                                scope.SetExtra("hadExistingCursor", true);
                            });

                        // Do not assign plaidItem.Cursor — keep the existing value.
                    }
                    else
                    {
                        // Both before and after are null: first sync with no prior cursor.
                        // This is acceptable before notifications are live, but still logged
                        // so the post-save verification block can escalate to Error if needed.
                        _logger.LogWarning(
                            "CURSOR_NULL_FIRST_SYNC: Plaid returned null NextCursor and no previous cursor exists. " +
                            "itemId={ItemId} plaidItemDbId={PlaidItemDbId}",
                            itemId,
                            plaidItem.Id);

                        SentrySdk.CaptureMessage(
                            $"CURSOR_NULL_FIRST_SYNC: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                            scope =>
                            {
                                scope.Level = SentryLevel.Info;
                                scope.SetTag("event.type", "cursor_null_first_sync");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetExtra("plaidItemDbId", plaidItem.Id);
                                scope.SetExtra("hadExistingCursor", false);
                            });
                    }

                    _logger.LogInformation(
                        "Cursor advanced: itemId={ItemId} cursorBefore={CursorBefore} cursorAfter={CursorAfter}",
                        itemId,
                        cursorBeforeSync ?? "(null)",
                        cursorAfterSync ?? "(null)");

                    // ── DIAGNOSTIC: EF change tracker state for cursor ──────────────────────
                    // If cursor_is_modified=False and efEntityState=Unchanged, EF will not
                    // generate an UPDATE for the cursor column — even if we assigned a new value.
                    // This happens when old value == new value (both null, or same string).
                    var cursorEntryState = _dbContext.Entry(plaidItem).State.ToString();
                    var cursorIsModified = _dbContext.Entry(plaidItem).Property(p => p.Cursor).IsModified;

                    _logger.LogInformation(
                        "PRE-SAVE cursor state: itemId={ItemId} plaidItemDbId={PlaidItemDbId} " +
                        "cursorBefore={CursorBefore} cursorAfterSync={CursorAfterSync} " +
                        "efEntityState={EfEntityState} cursor_is_modified={CursorIsModified}",
                        itemId, plaidItem.Id,
                        cursorBeforeSync ?? "(null)", cursorAfterSync ?? "(null)",
                        cursorEntryState, cursorIsModified);

                    if (!cursorIsModified)
                    {
                        _logger.LogWarning(
                            "CURSOR_NOT_MARKED_MODIFIED: EF change tracker does not detect cursor change. " +
                            "itemId={ItemId} plaidItemDbId={PlaidItemDbId} userId={UserId} " +
                            "cursorBefore={CursorBefore} cursorAfterSync={CursorAfterSync} " +
                            "efEntityState={EfEntityState}",
                            itemId, plaidItem.Id, user.Id,
                            cursorBeforeSync ?? "(null)", cursorAfterSync ?? "(null)", cursorEntryState);

                        SentrySdk.CaptureMessage(
                            $"CURSOR_NOT_MARKED_MODIFIED: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                            scope =>
                            {
                                scope.Level = SentryLevel.Warning;
                                scope.SetTag("event.type", "cursor_not_marked_modified");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                                scope.SetExtra("sync.userId", user.Id);
                                scope.SetExtra("sync.cursorBefore", cursorBeforeSync ?? "(null)");
                                scope.SetExtra("sync.cursorAfterSync", cursorAfterSync ?? "(null)");
                                scope.SetExtra("sync.efEntityState", cursorEntryState);
                            });
                    }

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

                    // ── Final save: balance record + cursor ────────────────────────────────
                    // Added transactions were committed individually inside the loop above.
                    // This save commits the accumulated balance delta and the cursor advancement.
                    // Only saved here — AFTER all transaction processing (including 23505 recovery)
                    // has completed successfully. If this save fails, the cursor does not advance
                    // and Plaid will retry; no data is lost.
                    await _dbContext.SaveChangesAsync();

                    var currentDynamicBalance = balanceRecord?.BalanceAmount ?? 0m;

                    _logger.LogInformation(
                        "Sync DB saved: itemId={ItemId} webhookCode={WebhookCode} " +
                        "notifyQueueSize={NotifyQueueSize} notificationsJustEnabled={NotificationsJustEnabled} " +
                        "currentBalance={CurrentBalance} cursorAfter={CursorAfter}",
                        itemId, webhookCode ?? "(manual)",
                        notifyQueue.Count, notificationsJustEnabled,
                        currentDynamicBalance, cursorAfterSync ?? "(null)");

                    // ── DIAGNOSTIC: Re-read cursor from DB to confirm persistence ───────────
                    // After SaveChanges, re-query the PlaidItem row (AsNoTracking to bypass
                    // the in-memory cache) to verify whether the cursor column was actually
                    // written to the database.
                    var savedCursor = await _dbContext.PlaidItems
                        .AsNoTracking()
                        .Where(p => p.Id == plaidItem.Id)
                        .Select(p => p.Cursor)
                        .FirstOrDefaultAsync();

                    bool savedCursorIsNull = string.IsNullOrEmpty(savedCursor);

                    _logger.LogInformation(
                        "POST-SAVE cursor verification: itemId={ItemId} plaidItemDbId={PlaidItemDbId} " +
                        "savedCursor={SavedCursor} savedCursorIsNull={SavedCursorIsNull}",
                        itemId, plaidItem.Id,
                        savedCursor ?? "(null)", savedCursorIsNull);

                    if (savedCursorIsNull)
                    {
                        _logger.LogWarning(
                            "CURSOR_STILL_NULL_AFTER_SAVE: cursor remains null after SaveChanges. " +
                            "itemId={ItemId} plaidItemDbId={PlaidItemDbId} userId={UserId} " +
                            "webhookCode={WebhookCode} cursorBeforeSync={CursorBeforeSync} " +
                            "cursorAfterSync={CursorAfterSync}",
                            itemId, plaidItem.Id, user.Id, webhookCode ?? "(manual)",
                            cursorBeforeSync ?? "(null)", cursorAfterSync ?? "(null)");

                        SentrySdk.CaptureMessage(
                            $"CURSOR_STILL_NULL_AFTER_SAVE: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                            scope =>
                            {
                                scope.Level = SentryLevel.Warning;
                                scope.SetTag("event.type", "cursor_still_null_after_save");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                                scope.SetExtra("sync.userId", user.Id);
                                scope.SetExtra("sync.webhookCode", webhookCode ?? "(manual)");
                                scope.SetExtra("sync.cursorBeforeSync", cursorBeforeSync ?? "(null)");
                                scope.SetExtra("sync.cursorAfterSync", cursorAfterSync ?? "(null)");
                            });
                    }

                    // ── ESCALATION: notifications live but cursor still null ────────────────
                    // This is the dangerous combination: the next webhook will start from the
                    // beginning of Plaid's history (cursor=null) and may re-process and
                    // re-notify transactions that were already seen. Captured at Error level
                    // so it pages on-call regardless of general warning thresholds.
                    if (savedCursorIsNull && plaidItem.NotificationsEnabledAt.HasValue)
                    {
                        _logger.LogError(
                            "NOTIFICATIONS_ENABLED_BUT_CURSOR_NULL: PlaidItem has notifications enabled " +
                            "but cursor is null after save. The next sync may re-fetch from the beginning. " +
                            "itemId={ItemId} plaidItemDbId={PlaidItemDbId}",
                            itemId,
                            plaidItem.Id);

                        SentrySdk.CaptureMessage(
                            $"NOTIFICATIONS_ENABLED_BUT_CURSOR_NULL: itemId={itemId} plaidItemDbId={plaidItem.Id}",
                            scope =>
                            {
                                scope.Level = SentryLevel.Error;
                                scope.SetTag("event.type", "notifications_enabled_but_cursor_null");
                                scope.SetTag("sync.itemId", itemId);
                                scope.SetExtra("plaidItemDbId", plaidItem.Id);
                                scope.SetExtra("notificationsEnabledAt",
                                    plaidItem.NotificationsEnabledAt?.ToString("O"));
                            });
                    }

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
                    _logger.LogInformation(
                        "Notifications to send: itemId={ItemId} webhookCode={WebhookCode} " +
                        "userEmail={UserEmail} variableSpendCount={VarCount} recurringMatchCount={RecurringCount}",
                        itemId, webhookCode ?? "(manual)", user.Email,
                        notifyQueue.Count, recurringNotifyQueue.Count);

                    // ── 9a. Variable-spend / deposit / large-expense notifications ─────────
                    // Each notification uses the balance captured at the moment the transaction
                    // was applied — not the final post-batch balance.
                    foreach (var (tx, balanceAfterTx) in notifyQueue)
                    {
                        _logger.LogInformation(
                            "Sending notification: txId={TxId} plaidTxId={PlaidTxId} " +
                            "merchant={Merchant} amount={Amount} balanceAfterTransaction={Balance} " +
                            "notificationType=spend userId={UserId} userEmail={UserEmail} itemId={ItemId} " +
                            "webhookCode={WebhookCode}",
                            tx.Id, tx.PlaidTransactionId,
                            tx.MerchantName ?? tx.Name, tx.Amount, balanceAfterTx,
                            user.Id, user.Email, itemId,
                            webhookCode ?? "(manual)");

                        try
                        {
                            await _notificationService
                                .SendNewTransactionNotification(tx, balanceAfterTx, user.Email);
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

                    // ── 9b. Recurring / fixed-cost match notifications ────────────────────
                    // Balance shown is the balance at the moment the transaction was classified
                    // (before any subsequent variable-spend transactions in this batch).
                    foreach (var (tx, fixedCostId, fixedCostName, balanceAfterTx) in recurringNotifyQueue)
                    {
                        _logger.LogInformation(
                            "Sending recurring notification: txId={TxId} plaidTxId={PlaidTxId} " +
                            "merchant={Merchant} amount={Amount} balanceAfterTransaction={Balance} " +
                            "notificationType=recurring_transaction_detected " +
                            "matchedFixedCostId={FixedCostId} userId={UserId} userEmail={UserEmail} itemId={ItemId}",
                            tx.Id, tx.PlaidTransactionId,
                            tx.MerchantName ?? tx.Name, tx.Amount, balanceAfterTx,
                            fixedCostId?.ToString() ?? "(none)",
                            user.Id, user.Email, itemId);

                        try
                        {
                            await _notificationService.SendRecurringTransactionNotification(
                                tx, fixedCostId, fixedCostName, balanceAfterTx, user.Email);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Recurring notification send failed: txId={TxId} plaidTxId={PlaidTxId} " +
                                "userEmail={UserEmail} itemId={ItemId}",
                                tx.Id, tx.PlaidTransactionId, user.Email, itemId);
                            SentrySdk.CaptureException(ex);
                        }
                    }

                    var syncDuration = DateTime.UtcNow - syncStartedAt;
                    _logger.LogInformation(
                        "Sync finished: itemId={ItemId} webhookCode={WebhookCode} userEmail={UserEmail} " +
                        "durationMs={DurationMs} variableSpendNotificationsSent={VarCount} " +
                        "recurringNotificationsSent={RecurringCount}",
                        itemId, webhookCode ?? "(manual)", user.Email,
                        (int)syncDuration.TotalMilliseconds,
                        notifyQueue.Count, recurringNotifyQueue.Count);

                    // Advance working cursor for next iteration
                    workingCursor = finalCursor;

                } while (finalHasMore);

                // ── All pages processed successfully ───────────────────────────────────
                _logger.LogInformation(
                    "All Plaid pages fetched: itemId={ItemId} webhookCode={WebhookCode} " +
                    "pagesFetched={PagesFetched} totalAdded={TotalAdded} totalModified={TotalModified} " +
                    "totalRemoved={TotalRemoved}",
                    itemId, webhookCode ?? "(manual)", pagesFetched,
                    totalAddedCount, totalModifiedCount, totalRemovedCount);

                // ── Commit the advisory-lock transaction ───────────────────────────────
                // Committing here releases the pg_try_advisory_xact_lock and makes all
                // SaveChanges calls within the sync permanently visible.
                // The advisory lock is auto-released on commit regardless of connection
                // pooling — no explicit pg_advisory_unlock call needed.
                await syncTx.CommitAsync();

                // Return the final response from the last page
                return new TransactionsSyncResponse
                {
                    Added = new List<Going.Plaid.Entity.Transaction>(),
                    Modified = new List<Going.Plaid.Entity.Transaction>(),
                    Removed = new List<Going.Plaid.Entity.RemovedTransaction>(),
                    HasMore = false,
                    NextCursor = finalCursor
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "SYNC EXCEPTION: itemId={ItemId} webhookCode={WebhookCode}",
                    itemId, webhookCode ?? "(manual)");
                throw new Exception("Error syncing and processing transactions.", e);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Historical recurring-analysis backfill
        // ─────────────────────────────────────────────────────────────────────────────

        public async Task<int> BackfillHistoricalTransactionsForRecurringAnalysis(
            string itemId,
            int monthsBack = 6)
        {
            var plaidItem = await _dbContext.PlaidItems
                .FirstOrDefaultAsync(p => p.ItemId == itemId);

            if (plaidItem == null)
            {
                _logger.LogWarning(
                    "BackfillHistorical: PlaidItem not found. itemId={ItemId}", itemId);
                return 0;
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == plaidItem.UserId);

            if (user == null)
            {
                _logger.LogWarning(
                    "BackfillHistorical: User not found for PlaidItem. " +
                    "itemId={ItemId} userId={UserId}",
                    itemId, plaidItem.UserId);
                return 0;
            }

            var endDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var startDate = endDate.AddMonths(-monthsBack);

            _logger.LogInformation(
                "BackfillHistorical: Starting. itemId={ItemId} userId={UserId} " +
                "userEmail={UserEmail} startDate={StartDate} endDate={EndDate} monthsBack={MonthsBack}",
                itemId, user.Id, user.Email,
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), monthsBack);

            SentrySdk.AddBreadcrumb(
                $"BackfillHistorical start: itemId={itemId} userEmail={user.Email} " +
                $"startDate={startDate} endDate={endDate}",
                level: BreadcrumbLevel.Info);

            const int pageSize = 500;
            int offset = 0;
            int totalFromPlaid = 0;
            int inserted = 0;
            int skippedDuplicate = 0;
            int skippedCredit = 0;

            // ── Paginate through Plaid's date-range endpoint ───────────────────────────
            // Using TransactionsGetAsync (legacy date-range API) so we don't touch
            // the live sync cursor that TransactionsSyncAsync maintains.
            do
            {
                var req = new TransactionsGetRequest
                {
                    AccessToken = plaidItem.AccessToken,
                    StartDate = startDate,
                    EndDate = endDate,
                    Options = new PlaidTxGetOptions
                    {
                        Count = pageSize,
                        Offset = offset
                    }
                };

                var resp = await _plaidClient.TransactionsGetAsync(req);
                var page = resp.Transactions
                           ?? Array.Empty<PlaidTransactionEntity>();

                if (offset == 0)
                {
                    totalFromPlaid = resp.TotalTransactions;
                    _logger.LogInformation(
                        "BackfillHistorical: Plaid reports totalTransactions={Total} " +
                        "itemId={ItemId} userId={UserId}",
                        totalFromPlaid, itemId, user.Id);
                }

                foreach (var t in page)
                {
                    // ── Amount convention ──────────────────────────────────────────────
                    // Plaid raw positive amount = debit/outflow/spending (what we want).
                    // Plaid raw negative amount = credit/income (skip).
                    var rawAmount = t.Amount ?? 0m;
                    if (rawAmount <= 0m)
                    {
                        skippedCredit++;
                        continue;
                    }

                    decimal absAmount = rawAmount; // already positive

                    // ── Idempotency guard ──────────────────────────────────────────────
                    // Skip rows we have already stored for this user.
                    // This covers both previously-backfilled rows and live-synced rows
                    // (a transaction can exist from either source; we only store it once).
                    bool alreadyExists = await _dbContext.Transactions
                        .AnyAsync(x =>
                            x.UserId == user.Id &&
                            x.PlaidTransactionId == t.TransactionId);

                    if (alreadyExists)
                    {
                        skippedDuplicate++;
                        continue;
                    }

                    DateTime txDate = t.Date
                        .GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow))
                        .ToDateTime(TimeOnly.MinValue);
                    DateTime txDateUtc = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);

                    var backfillTx = new Transaction
                    {
                        UserId = user.Id,
                        PlaidTransactionId = t.TransactionId,
                        AccountId = t.AccountId ?? string.Empty,
                        Amount = absAmount,
                        Date = txDateUtc,
#pragma warning disable CS0612
                        Name = t.Name ?? string.Empty,
#pragma warning restore CS0612
                        MerchantName = t.MerchantName,
                        // Only import posted transactions — pending rows have unstable amounts
                        // and no consistent merchant name, making them useless for pattern analysis.
                        Pending = false,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        SuggestedKind = TransactionSuggestedKind.Unknown,
                        UserDecision = TransactionUserDecision.Undecided,
                        CountedAsIncome = false,
                        IsLargeExpenseCandidate = false,
                        LargeExpenseHandled = false,
                        IsSuspiciousHold = false,
                        HoldReviewed = false,
                        // Null: historical rows never alter the balance.
                        BudgetAppliedAmount = null,
                        // Tag as historical so all live-budget queries exclude this row.
                        IsHistoricalBackfill = true,
                        BudgetImpactEligible = false
                    };

                    await _dbContext.Transactions.AddAsync(backfillTx);
                    inserted++;
                }

                offset += page.Count;

                // Stop when we've received all reported transactions or got an empty page.
                if (page.Count == 0 || offset >= totalFromPlaid)
                    break;

            } while (true);

            // ── Persist to DB ─────────────────────────────────────────────────────────
            if (inserted > 0)
            {
                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
                    when (dbEx.InnerException is Npgsql.PostgresException pgEx
                          && pgEx.SqlState == "23505")
                {
                    // Concurrent request already inserted some of these rows.
                    // Roll back in-memory state; the next call will skip duplicates.
                    _dbContext.ChangeTracker.Clear();

                    _logger.LogWarning(
                        "BackfillHistorical: Unique constraint collision — batch rolled back. " +
                        "itemId={ItemId} userId={UserId} insertedAttempted={Inserted} " +
                        "constraintName={ConstraintName}",
                        itemId, user.Id, inserted,
                        pgEx.ConstraintName ?? "(unknown)");

                    SentrySdk.CaptureException(dbEx);
                    inserted = 0;
                }
            }

            _logger.LogInformation(
                "BackfillHistorical: Complete. itemId={ItemId} userId={UserId} " +
                "userEmail={UserEmail} totalFromPlaid={TotalFromPlaid} " +
                "inserted={Inserted} skippedDuplicate={SkippedDuplicate} " +
                "skippedCredit={SkippedCredit}",
                itemId, user.Id, user.Email,
                totalFromPlaid, inserted, skippedDuplicate, skippedCredit);

            SentrySdk.AddBreadcrumb(
                $"BackfillHistorical complete: itemId={itemId} userEmail={user.Email} " +
                $"totalFromPlaid={totalFromPlaid} inserted={inserted} " +
                $"skippedDuplicate={skippedDuplicate} skippedCredit={skippedCredit}",
                level: BreadcrumbLevel.Info);

            return inserted;
        }
    }
}
