using BudgetApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Going.Plaid;
using Going.Plaid.Transactions;

namespace BudgetApp.Api.Services
{
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
                // 1. Find Plaid item + user
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

                // 2. Call Plaid
                var request = new TransactionsSyncRequest
                {
                    AccessToken = plaidItem.AccessToken,
                    Cursor = plaidItem.Cursor,
                    Count = 100
                };

                var response = await _plaidClient.TransactionsSyncAsync(request);

                decimal variableSpend = 0m;

                // We’ll notify AFTER SaveChanges
                var newPostedTransactions = new List<Transaction>();

                // 3. Process "Added" transactions
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
                    bool isCredit = rawAmount < 0m;  // Plaid: negative = inflow
                    decimal absAmount = Math.Abs(rawAmount);

                    var newTx = new Transaction
                    {
                        UserId = user.Id,
                        PlaidTransactionId = t.TransactionId,
                        AccountId = t.AccountId,
                        Amount = absAmount,
                        Date = txDateUtc,
#pragma warning disable CS0612
                        Name = t.Name,
#pragma warning restore CS0612
                        MerchantName = t.MerchantName,
                        Pending = t.Pending ?? false,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsLargeExpenseCandidate = false,
                        LargeExpenseHandled = false
                    };

                    if (isCredit)
                    {
                        // Deposit: classify only
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
                        // Outflow – treat as variable spend unless it's a known fixed cost
                        bool isFixed = fixedCosts.Any(fc =>
                            !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                            fc.PlaidMerchantName.Equals(
                                merchantName,
                                StringComparison.OrdinalIgnoreCase
                            ));

                        if (!isFixed)
                        {
                            variableSpend += absAmount;

                            if (user.ExpectedPaycheckAmount > 0 &&
                                _budgetEngine.IsLargeExpense(absAmount, user.ExpectedPaycheckAmount))
                            {
                                newTx.IsLargeExpenseCandidate = true;
                                newTx.LargeExpenseHandled = false;
                            }
                        }
                    }

                    await _dbContext.Transactions.AddAsync(newTx);

                    // Only push for settled transactions
                    if (!newTx.Pending)
                    {
                        newPostedTransactions.Add(newTx);
                    }
                }

                // 4. Update balance only with variable spend
                var balanceRecord = await _dbContext.Balances
                    .FirstOrDefaultAsync(b => b.UserId == user.Id);

                if (variableSpend > 0 && balanceRecord != null)
                {
                    balanceRecord.BalanceAmount -= variableSpend;
                    balanceRecord.UpdatedAt = DateTime.UtcNow;
                }

                // 5. Update cursor + save
                plaidItem.Cursor = response.NextCursor;
                await _dbContext.SaveChangesAsync();

                var currentDynamicBalance = balanceRecord?.BalanceAmount ?? 0m;

                // 6. Fire notifications via ExpoNotificationService
                foreach (var tx in newPostedTransactions)
                {
                    try
                    {
                        await _notificationService
                            .SendNewTransactionNotification(tx, currentDynamicBalance);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Failed to send notification for tx {tx.Id}: {ex.Message}"
                        );
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
