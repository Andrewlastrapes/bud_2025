using Microsoft.EntityFrameworkCore;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Microsoft.Extensions.Logging;

public interface IPaycheckSummaryService
{
    /// <summary>
    /// Schedule-based trigger: called from the Plaid webhook after transaction sync.
    ///
    /// Checks whether today falls inside a payday window for the user linked to the
    /// given Plaid item.  If it does, creates or updates (recalculates) a
    /// <see cref="PaycheckSummary"/> keyed to the <em>nominal payday</em> for that
    /// window — not the literal date this method was called.
    ///
    /// Using the nominal payday as the key means that any call made on any day within
    /// the same payday window (e.g. the 12th, 14th, or 17th when nominal = 15th) all
    /// resolve to the same DB row, giving full idempotency.
    ///
    /// Returns the summary if created/updated, or null when skipped (not inside a
    /// payday window, incomplete onboarding, or missing settings).
    /// </summary>
    Task<PaycheckSummary?> CreateOrUpdateSummaryIfPaycheckDayAsync(string plaidItemId, DateTime utcNow);

    /// <summary>
    /// Paycheck-deposit fallback trigger: called after each Plaid sync.
    ///
    /// Queries the DB for <see cref="TransactionSuggestedKind.Paycheck"/> transactions
    /// that were saved during the current sync batch (scoped by UserId + CreatedAt ≥
    /// syncStartedAt minus 1-minute buffer).  For each distinct nominal payday that is
    /// not yet covered by an existing summary, creates or updates a PaycheckSummary.
    ///
    /// <para>
    /// Why DB query instead of sync-response list: <c>SyncAndProcessTransactions</c>
    /// returns <c>Going.Plaid.Transactions.TransactionsSyncResponse</c> — the raw
    /// Plaid response — which has no <c>SuggestedKind</c>.  The only reliable source
    /// of classified transaction data is our own <c>Transactions</c> table.
    /// </para>
    ///
    /// <para>
    /// Why no PlaidItemId filter: our <c>Transaction</c> model stores the Plaid
    /// <c>AccountId</c> string but has no FK to <c>PlaidItems</c>.  Scoping by
    /// UserId + CreatedAt is tight enough for a single sync batch.
    /// </para>
    /// </summary>
    Task TriggerSummaryForRecentPaycheckTransactionsAsync(
        string plaidItemId,
        DateTime syncStartedAt,
        DateTime utcNow);
}

public class PaycheckSummaryService : IPaycheckSummaryService
{
    private readonly ApiDbContext _db;
    private readonly DynamicBudgetEngine _engine;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaycheckSummaryService> _logger;

    public PaycheckSummaryService(
        ApiDbContext db,
        DynamicBudgetEngine engine,
        INotificationService notificationService,
        ILogger<PaycheckSummaryService> logger)
    {
        _db = db;
        _engine = engine;
        _notificationService = notificationService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Schedule-based trigger
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PaycheckSummary?> CreateOrUpdateSummaryIfPaycheckDayAsync(
        string plaidItemId,
        DateTime utcNow)
    {
        var user = await LoadUserForPlaidItemAsync(plaidItemId);
        if (user == null) return null;

        // ── Guard: onboarding must be complete ──────────────────────────────
        if (!user.OnboardingComplete)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: onboarding incomplete for userId={user.Id}");
            return null;
        }

        // ── Guard: paycheck settings must be configured ──────────────────────
        if (user.PayDay1 == 0 && user.PayDay2 == 0)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: no paydays configured for userId={user.Id}");
            return null;
        }

        if (user.ExpectedPaycheckAmount <= 0)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: ExpectedPaycheckAmount not set for userId={user.Id}");
            return null;
        }

        // ── Window-based trigger (replaces the old exact-day check) ─────────
        // Old code: today.Day == user.PayDay1 || today.Day == user.PayDay2
        // New code: is today inside the ±window around any configured nominal payday?
        var todayOnly = DateOnly.FromDateTime(utcNow.Date);
        var nominalPayday = PaydayCycleHelper.GetNominalPaydayForDate(
            todayOnly, user.PayDay1, user.PayDay2);

        if (nominalPayday is null)
        {
            Console.WriteLine(
                $"[PaycheckSummary] Skipped (scheduled): {todayOnly:yyyy-MM-dd} is not inside any " +
                $"payday window for userId={user.Id} (PayDay1={user.PayDay1}, PayDay2={user.PayDay2})");
            return null;
        }

        return await BuildAndSavePaycheckSummaryAsync(user, nominalPayday.Value, utcNow, "scheduled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Paycheck-deposit fallback trigger
    // ─────────────────────────────────────────────────────────────────────────

    public async Task TriggerSummaryForRecentPaycheckTransactionsAsync(
        string plaidItemId,
        DateTime syncStartedAt,
        DateTime utcNow)
    {
        var user = await LoadUserForPlaidItemAsync(plaidItemId);
        if (user == null) return;

        if (!user.OnboardingComplete) return;
        if (user.PayDay1 == 0 && user.PayDay2 == 0) return;
        if (user.ExpectedPaycheckAmount <= 0) return;

        // ── Query tightly scoped to this sync batch ──────────────────────────
        // Transaction has no PlaidItemId FK.  Scope: UserId + SuggestedKind
        // + CreatedAt >= (syncStartedAt − 1 min to absorb minor clock skew).
        var cutoff = syncStartedAt.AddMinutes(-1);

        var recentPaycheckDates = await _db.Transactions
            .Where(t =>
                t.UserId == user.Id
                && t.SuggestedKind == TransactionSuggestedKind.Paycheck
                && t.CreatedAt >= cutoff)
            .Select(t => t.Date)
            .Distinct()
            .ToListAsync();

        if (!recentPaycheckDates.Any())
        {
            Console.WriteLine(
                $"[PaycheckSummary] Fallback: no recent Paycheck transactions found for " +
                $"userId={user.Id} since {cutoff:yyyy-MM-dd HH:mm:ss}Z");
            return;
        }

        Console.WriteLine(
            $"[PaycheckSummary] Fallback: found {recentPaycheckDates.Count} Paycheck " +
            $"transaction date(s) for userId={user.Id}");

        foreach (var txDate in recentPaycheckDates)
        {
            var txDateOnly = DateOnly.FromDateTime(txDate.Date);

            // Resolve to the nearest nominal payday (in-window = fast path;
            // out-of-window = nearest by calendar distance).
            var nominalPayday = PaydayCycleHelper.GetNearestNominalPaydayForDate(
                txDateOnly, user.PayDay1, user.PayDay2);

            Console.WriteLine(
                $"[PaycheckSummary] Fallback trigger: txDate={txDateOnly} → " +
                $"nominalPayday={nominalPayday} for userId={user.Id}");

            // BuildAndSavePaycheckSummaryAsync handles the idempotency check
            // (including window-based lookup for migrating old today-keyed rows).
            await BuildAndSavePaycheckSummaryAsync(user, nominalPayday, utcNow, "paycheck-fallback");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the <see cref="User"/> (with FixedCosts) linked to a Plaid item.
    /// Returns null and logs a message when the item is not found.
    /// </summary>
    private async Task<User?> LoadUserForPlaidItemAsync(string plaidItemId)
    {
        var plaidItem = await _db.PlaidItems
            .Include(p => p.User)
                .ThenInclude(u => u.FixedCosts)
            .FirstOrDefaultAsync(p => p.ItemId == plaidItemId);

        if (plaidItem == null)
        {
            Console.WriteLine(
                $"[PaycheckSummary] Skipped: PlaidItem not found for itemId={plaidItemId}");
            return null;
        }

        return plaidItem.User;
    }

    /// <summary>
    /// Core logic: computes all payday-summary metrics for the pay period whose
    /// nominal payday is <paramref name="nominalPayday"/>, then upserts the
    /// <see cref="PaycheckSummary"/> row.
    ///
    /// <para>
    /// The nominal payday date is used as the canonical <c>PaycheckDate</c> key.
    /// If an existing row is found with a different date inside the same window
    /// (pre-migration row keyed to the literal date), its <c>PaycheckDate</c> is
    /// updated to the nominal date so future lookups find it correctly.
    /// </para>
    /// </summary>
    private async Task<PaycheckSummary?> BuildAndSavePaycheckSummaryAsync(
        User user,
        DateOnly nominalPayday,
        DateTime utcNow,
        string trigger)
    {
        // Canonical UTC midnight for the nominal payday — used as the DB key.
        var paycheckDate = nominalPayday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // ── Period dates ─────────────────────────────────────────────────────
        var periodStartDate = _engine.GetPreviousPaycheckDate(user, paycheckDate);
        var periodEndDate = paycheckDate;
        var nextPaycheckDate = _engine.GetNextPaycheckDate(user, paycheckDate);

        // ── Prior-period spend ───────────────────────────────────────────────
        // Settled (non-pending) debit transactions in the prior period, excluding
        // income and any the user marked IgnoreForDynamic.

        _logger.LogInformation(
            "PaycheckSummary prior-period spend query: userId={UserId} " +
            "periodStartDate={PeriodStartDate} periodStartDateType={PeriodStartDateType} " +
            "paycheckDate={PaycheckDate} paycheckDateType={PaycheckDateType} " +
            "nominalPayday={NominalPayday}",
            user.Id,
            periodStartDate.ToString("O"),
            periodStartDate.GetType().Name,
            paycheckDate.ToString("O"),
            paycheckDate.GetType().Name,
            nominalPayday.ToString("yyyy-MM-dd"));

        decimal priorPeriodSpend;
        try
        {
            priorPeriodSpend = await _db.Transactions
                .Where(t =>
                    t.UserId == user.Id
                    && !t.Pending
                    && t.Amount > 0                             // debit / expense
                    && !t.CountedAsIncome
                    && t.UserDecision != TransactionUserDecision.IgnoreForDynamic
                    && t.Date >= periodStartDate
                    && t.Date < paycheckDate)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            _logger.LogInformation(
                "PaycheckSummary prior-period spend query succeeded: userId={UserId} " +
                "priorPeriodSpend={PriorPeriodSpend}",
                user.Id,
                priorPeriodSpend);
        }
        catch (Exception queryEx)
        {
            _logger.LogError(
                queryEx,
                "PaycheckSummary prior-period spend query FAILED: userId={UserId} " +
                "periodStartDate={PeriodStartDate} paycheckDate={PaycheckDate} " +
                "exceptionType={ExceptionType} message={Message} innerMessage={InnerMessage}",
                user.Id,
                periodStartDate.ToString("O"),
                paycheckDate.ToString("O"),
                queryEx.GetType().Name,
                queryEx.Message,
                queryEx.InnerException?.Message ?? "(none)");

            // Rethrow so Program.cs can capture it in Sentry with full webhook context
            throw;
        }

        // ── Prior-period budget & under/over metrics ─────────────────────────
        decimal priorPeriodStartingBudget = _engine.ComputeBudgetForUser(user, periodStartDate);
        decimal priorPeriodRemaining = priorPeriodStartingBudget - priorPeriodSpend;
        bool wasUnderBudget = priorPeriodRemaining >= 0;
        decimal leftoverAmount = wasUnderBudget ? priorPeriodRemaining : 0m;
        decimal overBudgetAmount = wasUnderBudget ? 0m : Math.Abs(priorPeriodRemaining);

        // ── New-period budget values ─────────────────────────────────────────
        decimal fixedCostsUntilNextPaycheck = _engine.ComputeFixedCostsUntilNextPaycheck(user, paycheckDate);
        decimal newDynamicBudgetAmount = _engine.ComputeBudgetForUser(user, paycheckDate);
        decimal savingsContribution = user.SavingsContributionAmount;
        decimal debtPaymentAmount = user.DebtPerPaycheck ?? 0m;
        decimal paycheckAmount = user.ExpectedPaycheckAmount;

        // ── Compatibility-aware upsert ───────────────────────────────────────
        // Look for an existing summary keyed to the exact nominal date first.
        // If not found, search within the full payday window to catch rows that
        // were written before this code change used `today` instead of the
        // nominal date (migration compatibility).
        var windowStart = nominalPayday
            .AddDays(-PaydayCycleHelper.DefaultDaysBefore)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Exclusive upper bound: the day after the window end.
        var windowEndExclusive = nominalPayday
            .AddDays(PaydayCycleHelper.DefaultDaysAfter + 1)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var existing = await _db.PaycheckSummaries
            .FirstOrDefaultAsync(s =>
                s.UserId == user.Id &&
                (s.PaycheckDate == paycheckDate ||
                 (s.PaycheckDate >= windowStart && s.PaycheckDate < windowEndExclusive)));

        bool isNewRecord = existing == null;
        var summary = existing ?? new PaycheckSummary
        {
            UserId = user.Id,
            PaycheckDate = paycheckDate,   // canonical nominal-date key
            CreatedAt = utcNow
        };

        // Migrate a pre-existing row that was keyed to a non-nominal date.
        if (!isNewRecord && summary.PaycheckDate != paycheckDate)
        {
            Console.WriteLine(
                $"[PaycheckSummary] Migrating PaycheckDate {summary.PaycheckDate:yyyy-MM-dd} → " +
                $"{paycheckDate:yyyy-MM-dd} for userId={user.Id} (nominal-payday alignment)");
            summary.PaycheckDate = paycheckDate;
        }

        // ── Update all computed fields (same for insert and update) ──────────
        summary.PeriodStartDate = periodStartDate;
        summary.PeriodEndDate = periodEndDate;
        summary.NextPaycheckDate = nextPaycheckDate;
        summary.PaycheckAmount = paycheckAmount;
        summary.PriorPeriodStartingBudget = priorPeriodStartingBudget;
        summary.PriorPeriodSpend = priorPeriodSpend;
        summary.PriorPeriodRemaining = priorPeriodRemaining;
        summary.WasUnderBudget = wasUnderBudget;
        summary.LeftoverAmount = leftoverAmount;
        summary.OverBudgetAmount = overBudgetAmount;
        summary.FixedCostsUntilNextPaycheck = fixedCostsUntilNextPaycheck;
        summary.SavingsContribution = savingsContribution;
        summary.DebtPaymentAmount = debtPaymentAmount;
        summary.NewDynamicBudgetAmount = newDynamicBudgetAmount;
        summary.UpdatedAt = utcNow;

        if (isNewRecord)
            _db.PaycheckSummaries.Add(summary);

        await _db.SaveChangesAsync();

        if (isNewRecord)
        {
            Console.WriteLine(
                $"[PaycheckSummary] Created ({trigger}) for userId={user.Id} " +
                $"on {paycheckDate:yyyy-MM-dd} | spend={priorPeriodSpend:F2} " +
                $"| budget={priorPeriodStartingBudget:F2} | underBudget={wasUnderBudget}");

            // Push notification only on first creation — recalculations are silent.
            try
            {
                string notifBody = wasUnderBudget
                    ? $"You were under budget by ${leftoverAmount:F2}. Review your options."
                    : $"You were over budget by ${overBudgetAmount:F2}. Tap to review.";

                await _notificationService.SendGenericNotificationForUser(
                    user.Id,
                    "📬 Paycheck Summary Ready",
                    notifBody);

                Console.WriteLine($"[PaycheckSummary] Push notification sent to userId={user.Id}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[PaycheckSummary] Push notification failed for userId={user.Id}: {ex.Message}");
                // Do not rethrow — notification failure must not block summary creation.
            }
        }
        else
        {
            Console.WriteLine(
                $"[PaycheckSummary] Updated ({trigger}) for userId={user.Id} " +
                $"on {paycheckDate:yyyy-MM-dd} | spend={priorPeriodSpend:F2} " +
                $"| budget={priorPeriodStartingBudget:F2}");
        }

        return summary;
    }
}
