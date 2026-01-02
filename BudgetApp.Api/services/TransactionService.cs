using BudgetApp.Api.Data;
using Going.Plaid;
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BudgetApp.Api.Services;

public interface ITransactionService
{
    Task<TransactionsSyncResponse> SyncAndProcessTransactions(string itemId);
}

public class TransactionService : ITransactionService
{
    private readonly PlaidClient _plaidClient;
    private readonly ApiDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly IDynamicBudgetEngine _budgetEngine;
    private readonly INotificationService _notificationService;

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

    public async Task<TransactionsSyncResponse> SyncAndProcessTransactions(string itemId)
    {
        try
        {
            // --- 1. FIND PLAID ITEM AND USER ---
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

            // --- 2. Call Plaid API ---
            var request = new TransactionsSyncRequest
            {
                AccessToken = plaidItem.AccessToken,
                Cursor = plaidItem.Cursor,
                Count = 100
            };

            var response = await _plaidClient.TransactionsSyncAsync(request);

            decimal variableSpendDelta = 0m;
            var notificationCandidates = new List<Transaction>();

            // --- 3. Process "Added" Transactions ---
            foreach (var t in response.Added)
            {
                var exists = await _dbContext.Transactions
                    .AnyAsync(x => x.PlaidTransactionId == t.TransactionId);
                if (exists) continue;

                DateTime txDate = t.Date.GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow))
                    .ToDateTime(TimeOnly.MinValue);
                DateTime txDateUtc = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);
                string merchantName = t.MerchantName ?? t.Name;

                var rawAmount = t.Amount ?? 0m;
                bool isCredit = rawAmount < 0m;  // Plaid: negative = inflow
                decimal absAmount = Math.Abs(rawAmount);

                var newTx = new Transaction
                {
                    UserId = user.Id,
                    PlaidTransactionId = t.TransactionId,
                    AccountId = t.AccountId,
                    Amount = absAmount,
                    Date = txDateUtc,
                    Name = t.Name,
                    MerchantName = t.MerchantName,
                    Pending = t.Pending ?? false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsLargeExpenseCandidate = false,
                    LargeExpenseHandled = false,
                    SuggestedKind = TransactionSuggestedKind.Unknown,
                    UserDecision = TransactionUserDecision.Undecided
                };

                if (isCredit)
                {
                    // Deposit: classify, but DO NOT change balance yet
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
                }
                else
                {
                    // Outflow: variable spend unless it matches a fixed cost merchant
                    bool isFixed = fixedCosts.Any(fc =>
                        !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                        fc.PlaidMerchantName.Equals(merchantName, StringComparison.OrdinalIgnoreCase));

                    if (!isFixed)
                    {
                        variableSpendDelta += absAmount;

                        // Large expense detection (Feature 3)
                        if (_budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                        {
                            newTx.IsLargeExpenseCandidate = true;
                            newTx.LargeExpenseHandled = false;
                        }
                    }
                }

                await _dbContext.Transactions.AddAsync(newTx);

                // Decide whether this one should trigger a notification
                bool shouldNotify =
                    isCredit ||                             // deposits go through user decision flow
                    newTx.IsLargeExpenseCandidate ||        // large expense flow
                    (!isCredit && !string.IsNullOrEmpty(merchantName)); // generic spend / recurring prompt

                if (shouldNotify)
                {
                    notificationCandidates.Add(newTx);
                }
            }

            // --- 4. Update Balance ONLY with Variable Spend ---
            Balance? balanceRecord = null;

            if (variableSpendDelta > 0)
            {
                balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);

                if (balanceRecord != null)
                {
                    balanceRecord.BalanceAmount -= variableSpendDelta;
                    balanceRecord.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);
            }

            // --- 5. Update Cursor and Save ---
            plaidItem.Cursor = response.NextCursor;
            await _dbContext.SaveChangesAsync();

            var currentBalance = balanceRecord?.BalanceAmount ?? 0m;

            // --- 6. Fire notifications (best effort) ---
            foreach (var tx in notificationCandidates)
            {
                await _notificationService.SendNewTransactionNotification(tx, currentBalance);
            }

            return response;
        }
        catch (Exception e)
        {
            // Propagate the exception up to the public endpoint
            throw new Exception("Error syncing and processing transactions.", e);
        }
    }
}
