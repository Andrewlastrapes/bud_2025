using BudgetApp.Api.Data;
using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using Going.Plaid; // This root using is what resolves PlaidClient in your version
using Going.Plaid.Transactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// REMOVED: using Going.Plaid.Client; // This line was causing the build error

namespace BudgetApp.Api.Services;

public interface ITransactionService
{
    // Use the class names directly
    Task<TransactionsSyncResponse> SyncAndProcessTransactions(string firebaseUuid);
}

public class TransactionService : ITransactionService
{
    // FIX 1: Use PlaidClient directly (resolved by the root 'using Going.Plaid;')
    private readonly PlaidClient _plaidClient;
    private readonly ApiDbContext _dbContext;
    private readonly IConfiguration _config;

    // FIX 2: Use PlaidClient directly in the constructor
    public TransactionService(ApiDbContext dbContext, PlaidClient plaidClient, IConfiguration config)
    {
        _dbContext = dbContext;
        _plaidClient = plaidClient;
        _config = config;
    }

    // Core logic to fetch, process, and update the balance
    public async Task<TransactionsSyncResponse> SyncAndProcessTransactions(string firebaseUuid)
    {
        try
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
            if (user == null) throw new UnauthorizedAccessException("User not found.");

            var plaidItem = await _dbContext.PlaidItems.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (plaidItem == null) throw new InvalidOperationException("No bank linked for this user.");

            var fixedCosts = await _dbContext.FixedCosts.Where(fc => fc.UserId == user.Id).ToListAsync();

            // 2. Call Plaid API
            var request = new TransactionsSyncRequest
            {
                AccessToken = plaidItem.AccessToken,
                Cursor = plaidItem.Cursor,
                Count = 100
            };
            // FIX 3: Call PlaidClient directly
            var response = await _plaidClient.TransactionsSyncAsync(request);

            decimal variableSpend = 0;

            // 3. Process "Added" Transactions
            foreach (var t in response.Added)
            {
                var exists = await _dbContext.Transactions.AnyAsync(x => x.PlaidTransactionId == t.TransactionId);
                if (!exists)
                {
                    DateTime txDate = t.Date.GetValueOrDefault(DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue);
                    DateTime txDateUtc = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);
                    string merchantName = t.MerchantName ?? t.Name;

                    var newTx = new BudgetApp.Api.Data.Transaction
                    {
                        UserId = user.Id,
                        PlaidTransactionId = t.TransactionId,
                        AccountId = t.AccountId,
                        Amount = t.Amount ?? 0m,
                        Date = txDateUtc,
                        Name = merchantName,
                        MerchantName = t.MerchantName,
                        Pending = t.Pending ?? false,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _dbContext.Transactions.AddAsync(newTx);

                    // --- SMART LOGIC: Check Fixed Costs ---
                    bool isFixed = fixedCosts.Any(fc =>
                        (!string.IsNullOrEmpty(fc.PlaidMerchantName) && fc.PlaidMerchantName.Equals(merchantName, StringComparison.OrdinalIgnoreCase))
                    );

                    if (!isFixed)
                    {
                        variableSpend += newTx.Amount;
                    }
                }
            }

            // 4. Update Balance ONLY with Variable Spend
            if (variableSpend > 0)
            {
                var balanceRecord = await _dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);
                if (balanceRecord != null)
                {
                    balanceRecord.BalanceAmount -= variableSpend;
                    balanceRecord.UpdatedAt = DateTime.UtcNow;
                }
            }

            // 5. Update Cursor and Save
            plaidItem.Cursor = response.NextCursor;
            await _dbContext.SaveChangesAsync();

            return response;
        }
        catch (Exception e)
        {
            // Propagate the exception up to the public endpoint
            throw new Exception("Error syncing and processing transactions.", e);
        }
    }
}