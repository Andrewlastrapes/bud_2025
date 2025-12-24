// File: services/TransactionService.cs

using System;
using System.Linq;
using System.Threading.Tasks;
using BudgetApp.Api.Data;
using Going.Plaid;                 // PlaidClient
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Api.Services;

// Public interface used by Program.cs and elsewhere
public interface ITransactionService
{
    /// <summary>
    /// Syncs transactions for the given Plaid ItemId and updates our DB +
    /// dynamic budget balance based on new spending.
    /// </summary>
    Task<TransactionsSyncResponse> SyncAndProcessTransactions(string itemId);
}

public class TransactionService : ITransactionService
{
    private readonly PlaidClient _plaidClient;
    private readonly ApiDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly IDynamicBudgetEngine _budgetEngine;

    public TransactionService(
        ApiDbContext dbContext,
        PlaidClient plaidClient,
        IConfiguration config,
        IDynamicBudgetEngine budgetEngine)
    {
        _dbContext = dbContext;
        _plaidClient = plaidClient;
        _config = config;
        _budgetEngine = budgetEngine;
    }

    /// <summary>
    /// Syncs new Plaid transactions for this ItemId, stores them, and
    /// decrements the dynamic balance by variable spend only.
    /// Deposits are classified but do not immediately change the balance.
    /// </summary>
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

            decimal variableSpend = 0m;

            // --- 3. Process "Added" Transactions ---
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

                var rawAmount = t.Amount ?? 0m;
                bool isCredit = rawAmount < 0m;   // Plaid: negative = inflow
                decimal absAmount = Math.Abs(rawAmount);

                var newTx = new BudgetApp.Api.Data.Transaction
                {
                    UserId = user.Id,
                    PlaidTransactionId = t.TransactionId,
                    AccountId = t.AccountId,
                    Amount = absAmount,      // store magnitude only
                    Date = txDateUtc,
                    Name = t.Name,           // yes, this is obsolete in the model, but still there
                    MerchantName = t.MerchantName,
                    Pending = t.Pending ?? false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsLargeExpenseCandidate = false,
                    LargeExpenseHandled = false
                };

                if (isCredit)
                {
                    // Deposit: classify, but DO NOT change balance yet.
                    var ctx = new DepositContext
                    {
                        Amount = absAmount,
                        Date = txDateUtc,
                        MerchantName = merchantName,
                        PayDay1 = user.PayDay1,
                        PayDay2 = user.PayDay2,
                        ExpectedPaycheckAmount = user.ExpectedPaycheckAmount
                    };

                    // NOTE: TransactionSuggestedKind here is from BudgetApp.Api.Data
                    newTx.SuggestedKind = _budgetEngine.ClassifyDeposit(ctx);
                }
                else
                {
                    // Outflow: treat as variable spend unless it matches a fixed cost merchant
                    bool isFixed = fixedCosts.Any(fc =>
                        !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                        fc.PlaidMerchantName.Equals(merchantName, StringComparison.OrdinalIgnoreCase));

                    if (!isFixed)
                    {
                        variableSpend += absAmount;

                        // Large expense detection
                        // ExpectedPaycheckAmount is a non-nullable decimal on User
                        if (user.ExpectedPaycheckAmount > 0 &&
                            _budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                        {
                            newTx.IsLargeExpenseCandidate = true;
                            newTx.LargeExpenseHandled = false;
                        }
                    }
                }

                await _dbContext.Transactions.AddAsync(newTx);
            }

            // --- 4. Update Balance ONLY with Variable Spend ---
            if (variableSpend > 0)
            {
                var balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);

                if (balanceRecord != null)
                {
                    balanceRecord.BalanceAmount -= variableSpend;
                    balanceRecord.UpdatedAt = DateTime.UtcNow;
                }
            }

            // --- 5. Update Cursor and Save ---
            plaidItem.Cursor = response.NextCursor;
            await _dbContext.SaveChangesAsync();

            return response;
        }
        catch (Exception e)
        {
            // Surface a single wrapped exception to the controller / endpoint
            throw new Exception("Error syncing and processing transactions.", e);
        }
    }
}
