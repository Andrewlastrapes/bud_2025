using Microsoft.EntityFrameworkCore;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;

public interface IPaycheckSummaryService
{
    /// <summary>
    /// Called from the Plaid webhook after transaction sync.
    /// Checks if today is a paycheck day for the user linked to the given Plaid item.
    /// If it is, creates or updates (recalculates) a PaycheckSummary for that user + date.
    /// Returns the summary if created/updated, or null if skipped (not a paycheck day,
    /// incomplete onboarding, or missing settings).
    /// </summary>
    Task<PaycheckSummary?> CreateOrUpdateSummaryIfPaycheckDayAsync(string plaidItemId, DateTime utcNow);
}

public class PaycheckSummaryService : IPaycheckSummaryService
{
    private readonly ApiDbContext _db;
    private readonly DynamicBudgetEngine _engine;
    private readonly INotificationService _notificationService;

    public PaycheckSummaryService(
        ApiDbContext db,
        DynamicBudgetEngine engine,
        INotificationService notificationService)
    {
        _db = db;
        _engine = engine;
        _notificationService = notificationService;
    }

    public async Task<PaycheckSummary?> CreateOrUpdateSummaryIfPaycheckDayAsync(
        string plaidItemId,
        DateTime utcNow)
    {
        // ── 1. Load PlaidItem → User (with FixedCosts) ──────────────────────────
        var plaidItem = await _db.PlaidItems
            .Include(p => p.User)
                .ThenInclude(u => u.FixedCosts)
            .FirstOrDefaultAsync(p => p.ItemId == plaidItemId);

        if (plaidItem == null)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: PlaidItem not found for itemId={plaidItemId}");
            return null;
        }

        var user = plaidItem.User;

        // ── 2. Guard: onboarding must be complete ───────────────────────────────
        if (!user.OnboardingComplete)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: onboarding incomplete for userId={user.Id}");
            return null;
        }

        // ── 3. Guard: paycheck settings must be configured ──────────────────────
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

        // ── 4. Guard: is today actually a paycheck day? ─────────────────────────
        var today = utcNow.Date; // UTC midnight
        int todayDay = today.Day;
        bool isPayDay1 = user.PayDay1 == todayDay;
        bool isPayDay2 = user.PayDay2 == todayDay;

        if (!isPayDay1 && !isPayDay2)
        {
            Console.WriteLine($"[PaycheckSummary] Skipped: today ({today:yyyy-MM-dd}) is not a paycheck day for userId={user.Id} " +
                $"(PayDay1={user.PayDay1}, PayDay2={user.PayDay2})");
            return null;
        }

        // ── 5. Compute period dates ─────────────────────────────────────────────
        var paycheckDate = today;
        var periodStartDate = _engine.GetPreviousPaycheckDate(user, paycheckDate);
        var periodEndDate = paycheckDate;
        var nextPaycheckDate = _engine.GetNextPaycheckDate(user, paycheckDate);

        // ── 6. Calculate prior-period spend ─────────────────────────────────────
        // Counts settled (non-pending), non-income expenses in the prior period.
        // Excludes transactions the user explicitly tagged IgnoreForDynamic.
        var priorPeriodSpend = await _db.Transactions
            .Where(t =>
                t.UserId == user.Id
                && !t.Pending                                                        // settled (not pending)
                && t.Amount > 0                                                      // debit / expense
                && !t.CountedAsIncome                                                // not income
                && t.UserDecision != TransactionUserDecision.IgnoreForDynamic        // not excluded
                && t.Date >= periodStartDate
                && t.Date < paycheckDate)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        // ── 7. Approximate prior-period starting budget ─────────────────────────
        decimal priorPeriodStartingBudget = _engine.ComputeBudgetForUser(user, periodStartDate);

        // ── 8. Derive under/over budget metrics ─────────────────────────────────
        decimal priorPeriodRemaining = priorPeriodStartingBudget - priorPeriodSpend;
        bool wasUnderBudget = priorPeriodRemaining >= 0;
        decimal leftoverAmount = wasUnderBudget ? priorPeriodRemaining : 0m;
        decimal overBudgetAmount = wasUnderBudget ? 0m : Math.Abs(priorPeriodRemaining);

        // ── 9. Compute new-period budget values ─────────────────────────────────
        decimal fixedCostsUntilNextPaycheck = _engine.ComputeFixedCostsUntilNextPaycheck(user, paycheckDate);
        decimal newDynamicBudgetAmount = _engine.ComputeBudgetForUser(user, paycheckDate);
        decimal savingsContribution = user.SavingsContributionAmount;
        decimal debtPaymentAmount = user.DebtPerPaycheck ?? 0m;
        decimal paycheckAmount = user.ExpectedPaycheckAmount;

        // ── 10. Upsert ──────────────────────────────────────────────────────────
        var existing = await _db.PaycheckSummaries
            .FirstOrDefaultAsync(s => s.UserId == user.Id && s.PaycheckDate == paycheckDate);

        bool isNewRecord = existing == null;
        var summary = existing ?? new PaycheckSummary
        {
            UserId = user.Id,
            PaycheckDate = paycheckDate,
            CreatedAt = utcNow
        };

        // Update all computed fields (same for create and update)
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
        {
            _db.PaycheckSummaries.Add(summary);
        }

        await _db.SaveChangesAsync();

        if (isNewRecord)
        {
            Console.WriteLine($"[PaycheckSummary] Created for userId={user.Id} on {paycheckDate:yyyy-MM-dd} " +
                $"| spend={priorPeriodSpend:F2} | budget={priorPeriodStartingBudget:F2} | underBudget={wasUnderBudget}");

            // Push notification only on first creation — same-day recalculations are silent
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
                Console.Error.WriteLine($"[PaycheckSummary] Push notification failed for userId={user.Id}: {ex.Message}");
                // Do not rethrow — notification failure must not block summary creation
            }
        }
        else
        {
            Console.WriteLine($"[PaycheckSummary] Updated (recalculated) for userId={user.Id} on {paycheckDate:yyyy-MM-dd} " +
                $"| spend={priorPeriodSpend:F2} | budget={priorPeriodStartingBudget:F2}");
        }

        return summary;
    }
}