using BudgetApp.Api.Data;
using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using Going.Plaid; // <--- FIX 1: Add the root namespace
using Going.Plaid.Transactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// REMOVED: using Going.Plaid.Client; // This was causing the build error

namespace BudgetApp.Api.Services;

// Interface is now defined in the same file
public interface ITransactionService
{
    Task<TransactionsSyncResponse> SyncAndProcessTransactions(string itemId);
}

public class TransactionService : ITransactionService
{
    private readonly PlaidClient _plaidClient; // FIX 2: This type is now resolved by 'using Going.Plaid;'
    private readonly ApiDbContext _dbContext;
    private readonly IConfiguration _config;

    public TransactionService(ApiDbContext dbContext, PlaidClient plaidClient, IConfiguration config)
    {
        _dbContext = dbContext;
        _plaidClient = plaidClient;
        _config = config;
    }

    // File: Services/TransactionService.cs (inside TransactionService class)

    // FIX: Method signature accepts ItemId
    public async Task<TransactionsSyncResponse> SyncAndProcessTransactions(string itemId)
    {
        try
        {
            // --- 1. FIND PLID ITEM AND USER ---
            // Find Plaid Item using ItemId first
            var plaidItem = await _dbContext.PlaidItems.FirstOrDefaultAsync(p => p.ItemId == itemId);
            if (plaidItem == null) throw new InvalidOperationException($"Plaid Item {itemId} not found.");

            // Find User using the PlaidItem's UserId
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == plaidItem.UserId);
            if (user == null) throw new UnauthorizedAccessException("User linked to this Item not found.");
            // ------------------------------------

            var fixedCosts = await _dbContext.FixedCosts.Where(fc => fc.UserId == user.Id).ToListAsync();

            // 2. Call Plaid API
            var request = new TransactionsSyncRequest
            {
                AccessToken = plaidItem.AccessToken,
                Cursor = plaidItem.Cursor,
                Count = 100
            };
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