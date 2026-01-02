using BudgetApp.Api.Data;
using Going.Plaid;
using Going.Plaid.Categories;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Microsoft.EntityFrameworkCore;
using Refit;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using BudgetApp.Api.Services;
using System.Linq;
using FirebaseAdmin.Auth;


// --- App Setup ---
var builder = WebApplication.CreateBuilder(args);

FirebaseApp.Create(new AppOptions()
{
    Credential = GoogleCredential.FromFile("firebase-service-account.json")
});

// --- SERVICE CONFIGURATION ---
var plaidConfig = builder.Configuration.GetSection("Plaid");
var plaidEnv = builder.Environment.IsDevelopment()
    ? Going.Plaid.Environment.Sandbox
    : Going.Plaid.Environment.Production;

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddSingleton(new PlaidClient(
    clientId: plaidConfig["ClientId"],
    secret: plaidConfig["Secret"],
    environment: plaidEnv
));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddScoped<INotificationService, ExpoNotificationService>();



builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddSingleton<IDynamicBudgetEngine, DynamicBudgetEngine>();


builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.AllowAnyOrigin()
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                      });
});

// --- APP BUILD & MIDDLEWARE ---
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(MyAllowSpecificOrigins);

// --- API ENDPOINTS ---

// POST: /api/users/register
app.MapPost("/api/users/register", async (ApiDbContext dbContext, UserRegistrationRequest requestBody) =>
{
    try
    {
        var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == requestBody.Email);
        if (existingUser != null)
        {
            return Results.Conflict(new { message = "User with this email already exists." });
        }

        var newUser = new User
        {
            Name = requestBody.Name,
            Email = requestBody.Email,
            FirebaseUuid = requestBody.FirebaseUuid ?? Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await dbContext.Users.AddAsync(newUser);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "User registered successfully",
            userId = newUser.Id,
            name = newUser.Name,
            email = newUser.Email
        });
    }
    catch (Exception e)
    {
        return Results.Problem($"Registration failed: {e.Message}");
    }
})
.WithName("RegisterUser")
.WithOpenApi();

// GET: /api/plaid/categories
app.MapGet("/api/plaid/categories", async (PlaidClient plaidClient) =>
{
    try
    {
        var request = new CategoriesGetRequest();
        var response = await plaidClient.CategoriesGetAsync(request);
        return Results.Ok(response.Categories);
    }
    catch (ApiException e)
    {
        return Results.Problem(e.Content);
    }
})
.WithName("GetPlaidCategories")
.WithOpenApi();

// POST: /api/plaid/create_link_token
app.MapPost("/api/plaid/create_link_token", async (PlaidClient plaidClient, IConfiguration config, CreateLinkTokenRequest requestBody) =>
{
    var user = new LinkTokenCreateRequestUser { ClientUserId = requestBody.FirebaseUserId };

    var productsString = config["Plaid:Products"] ?? "transactions";
    var products = productsString.Split(',')
        .Select(p => Enum.Parse<Products>(p.Trim(), true))
        .ToList();

    var countryCodesString = config["Plaid:CountryCodes"] ?? "US";
    var countryCodes = countryCodesString.Split(',')
        .Select(c => Enum.Parse<CountryCode>(c.Trim(), true))
        .ToList();

    var plaidRequest = new LinkTokenCreateRequest
    {
        ClientName = "Dynamic Budget App",
        Language = Language.English,
        CountryCodes = countryCodes,
        User = user,
        Products = products,
        Webhook = config["Plaid:WebhookUrl"]
    };

    try
    {
        var response = await plaidClient.LinkTokenCreateAsync(plaidRequest);
        return Results.Ok(new { linkToken = response.LinkToken });
    }
    catch (ApiException e)
    {
        Console.WriteLine($"Plaid API Error: {e.Content}");
        return Results.Problem($"Plaid API Error: {e.Content}");
    }
})
.WithName("CreateLinkToken")
.WithOpenApi();

// POST: /api/plaid/exchange_public_token
app.MapPost("/api/plaid/exchange_public_token",
    async (PlaidClient plaidClient, ApiDbContext dbContext, ExchangeTokenRequest requestBody) =>
    {
        var plaidRequest = new ItemPublicTokenExchangeRequest { PublicToken = requestBody.PublicToken };

        try
        {
            var response = await plaidClient.ItemPublicTokenExchangeAsync(plaidRequest);

            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == requestBody.FirebaseUuid);

            if (user == null)
            {
                return Results.NotFound(new { message = "User not found." });
            }

            var newItem = new PlaidItem
            {
                UserId = user.Id,
                AccessToken = response.AccessToken,
                ItemId = response.ItemId,
                InstitutionName = "New Institution",
                InstitutionLogo = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await dbContext.PlaidItems.AddAsync(newItem);
            await dbContext.SaveChangesAsync();

            return Results.Ok(new { message = "Public token exchanged and saved successfully." });
        }
        catch (ApiException e)
        {
            return Results.Problem(e.Content);
        }
    })
.WithName("ExchangePublicToken")
.WithOpenApi();

// GET: /api/plaid/accounts
app.MapGet("/api/plaid/accounts", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken))
        {
            return Results.Unauthorized();
        }

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
            .VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null)
        {
            return Results.NotFound(new { message = "User not found." });
        }

        var accounts = await dbContext.PlaidItems
            .Where(item => item.UserId == user.Id)
            .Select(item => new
            {
                item.Id,
                item.InstitutionName,
                item.InstitutionLogo
            })
            .ToListAsync();

        return Results.Ok(accounts);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetUserPlaidAccounts")
.WithOpenApi();

// GET: /api/balance
app.MapGet("/api/balance", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        return Results.Ok(new { amount = balanceRecord?.BalanceAmount ?? 0 });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetBalance")
.WithOpenApi();

// POST: /api/balance
app.MapPost("/api/balance", async (ApiDbContext dbContext, HttpContext httpContext, SetBalanceRequest request) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        if (balanceRecord == null)
        {
            balanceRecord = new Balance
            {
                UserId = user.Id,
                BalanceAmount = request.Amount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbContext.Balances.AddAsync(balanceRecord);
        }
        else
        {
            balanceRecord.BalanceAmount = request.Amount;
            balanceRecord.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();

        return Results.Ok(new { message = "Balance updated successfully", amount = balanceRecord.BalanceAmount });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("SetBalance")
.WithOpenApi();

// POST: /api/transactions/sync
app.MapPost("/api/transactions/sync", async (ITransactionService transactionService, ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        var plaidItem = await dbContext.PlaidItems.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (plaidItem == null) return Results.BadRequest("No Plaid item linked for this user.");

        var response = await transactionService.SyncAndProcessTransactions(plaidItem.ItemId);

        return Results.Ok(new
        {
            message = "Sync complete",
            added = response.Added.Count,
            hasMore = response.HasMore
        });
    }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException e) { return Results.Problem(e.Message); }
    catch (Exception e) { return Results.Problem(e.Message); }
})
.WithName("SyncTransactions")
.WithOpenApi();



// GET: /api/transactions
app.MapGet("/api/transactions", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var transactions = await dbContext.Transactions
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Results.Ok(transactions);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetTransactions")
.WithOpenApi();




// GET: /api/fixed-costs
app.MapGet("/api/fixed-costs", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var costs = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id)
            .OrderBy(fc => fc.Name)
            .ToListAsync();

        return Results.Ok(costs);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetFixedCosts")
.WithOpenApi();

// POST: /api/fixed-costs
app.MapPost("/api/fixed-costs", async (ApiDbContext dbContext, HttpContext httpContext, FixedCost requestBody) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var newCost = new FixedCost
        {
            UserId = user.Id,
            Name = requestBody.Name,
            Amount = requestBody.Amount,
            Category = requestBody.Category ?? "other",
            Type = requestBody.Type ?? "manual",
            PlaidMerchantName = requestBody.PlaidMerchantName,
            PlaidAccountId = requestBody.PlaidAccountId,
            UserHasApproved = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            NextDueDate = requestBody.NextDueDate
        };

        await dbContext.FixedCosts.AddAsync(newCost);
        await dbContext.SaveChangesAsync();

        return Results.Ok(newCost);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("AddFixedCost")
.WithOpenApi();

// DELETE: /api/fixed-costs/{id}
app.MapDelete("/api/fixed-costs/{id}", async (ApiDbContext dbContext, HttpContext httpContext, int id) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var cost = await dbContext.FixedCosts.FirstOrDefaultAsync(fc => fc.Id == id && fc.UserId == user.Id);
        if (cost == null)
        {
            return Results.NotFound("Cost not found or you do not have permission.");
        }

        dbContext.FixedCosts.Remove(cost);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new { message = "Fixed cost deleted." });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("DeleteFixedCost")
.WithOpenApi();

// GET: /api/users/profile
app.MapGet("/api/users/profile", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var userProfile = await dbContext.Users.AsNoTracking()
            .Where(u => u.FirebaseUuid == firebaseUuid)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.FirebaseUuid,
                u.OnboardingComplete
            })
            .FirstOrDefaultAsync();

        if (userProfile == null)
        {
            return Results.NotFound(new { message = "User not found in database." });
        }

        return Results.Ok(userProfile);
    }
    catch (Exception e)
    {
        return Results.Problem($"Error fetching profile: {e.Message}");
    }
})
.WithName("GetUserProfile")
.WithOpenApi();

// GET: /api/plaid/recurring
app.MapGet("/api/plaid/recurring", async (ApiDbContext dbContext, PlaidClient plaidClient, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var plaidItem = await dbContext.PlaidItems.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (plaidItem == null) return Results.Ok(new { message = "No bank linked." });

        var request = new Going.Plaid.Transactions.TransactionsRecurringGetRequest
        {
            AccessToken = plaidItem.AccessToken,
        };

        var response = await plaidClient.TransactionsRecurringGetAsync(request);

        return Results.Ok(new
        {
            inflow_streams = response.InflowStreams,
            outflow_streams = response.OutflowStreams
        });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetPlaidRecurring")
.WithOpenApi();

// POST: /api/budget/finalize
app.MapPost("/api/budget/finalize", async (ApiDbContext dbContext, HttpContext httpContext, FinalizeBudgetRequest request) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        if (user.OnboardingComplete) return Results.Conflict("Onboarding already complete.");

        DateTime today = DateTime.UtcNow.Date;
        DateTime nextPaycheck = request.NextPaycheckDate.Date;

        if (nextPaycheck <= today)
        {
            return Results.BadRequest("Next paycheck date must be in the future.");
        }

        DateTime CalculatePreviousPaycheckDate(int day1, int day2, DateTime nextPay)
        {
            var days = new[] { day1, day2 }.OrderBy(d => d).ToArray();
            int nextDay = nextPay.Day;

            if (nextDay == days[0])
            {
                var prevMonthFirst = new DateTime(nextPay.Year, nextPay.Month, 1).AddMonths(-1);
                int prevDay = days[1];
                int daysInPrevMonth = DateTime.DaysInMonth(prevMonthFirst.Year, prevMonthFirst.Month);
                if (prevDay > daysInPrevMonth) prevDay = daysInPrevMonth;

                return new DateTime(prevMonthFirst.Year, prevMonthFirst.Month, prevDay);
            }
            else
            {
                int prevDay = days[0];
                int daysInThisMonth = DateTime.DaysInMonth(nextPay.Year, nextPay.Month);
                if (prevDay > daysInThisMonth) prevDay = daysInThisMonth;

                return new DateTime(nextPay.Year, nextPay.Month, prevDay);
            }
        }

        var previousPaycheck = CalculatePreviousPaycheckDate(request.PayDay1, request.PayDay2, nextPaycheck);

        int payCycleDays = (int)(nextPaycheck - previousPaycheck).TotalDays;
        if (payCycleDays <= 0)
        {
            return Results.BadRequest("Invalid pay cycle detected.");
        }

        int daysUntilNextPaycheck = (int)(nextPaycheck - today).TotalDays;
        if (daysUntilNextPaycheck < 0 || daysUntilNextPaycheck > payCycleDays)
        {
            return Results.BadRequest("Next paycheck date is inconsistent with pay days / current date.");
        }

        var fixedBillsThisPeriod = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id
                && fc.NextDueDate.HasValue
                && fc.NextDueDate.Value.Date >= today
                && fc.NextDueDate.Value.Date <= nextPaycheck)
            .ToListAsync();

        var savingsThisPeriod = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id
                && fc.Category == "Savings")
            .ToListAsync();

        var totalRecurringCosts = fixedBillsThisPeriod.Sum(fc => fc.Amount)
                             + savingsThisPeriod.Sum(fc => fc.Amount);

        decimal effectivePaycheck = request.PaycheckAmount - totalRecurringCosts;

        decimal prorateFactor = (decimal)daysUntilNextPaycheck / payCycleDays;
        decimal finalDynamicBalance = effectivePaycheck * prorateFactor;

        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);
        if (balanceRecord == null)
        {
            balanceRecord = new Balance
            {
                UserId = user.Id,
                BalanceAmount = finalDynamicBalance,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbContext.Balances.AddAsync(balanceRecord);
        }
        else
        {
            balanceRecord.BalanceAmount = finalDynamicBalance;
            balanceRecord.UpdatedAt = DateTime.UtcNow;
        }

        user.OnboardingComplete = true;
        user.PayDay1 = request.PayDay1;
        user.PayDay2 = request.PayDay2;
        user.ExpectedPaycheckAmount = request.PaycheckAmount;

        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Setup complete.",
            dynamicBalance = finalDynamicBalance.ToString("0.00"),
            prorateFactor = prorateFactor.ToString("0.00")
        });
    }
    catch (Exception e)
    {
        return Results.Problem($"Finalization failed: {e.Message}");
    }
})
.WithName("FinalizeBudget")
.WithOpenApi();

// POST: /api/plaid/webhook
app.MapPost("/api/plaid/webhook", async (ITransactionService transactionService, PlaidWebhookRequest requestBody) =>
{
    if (requestBody.WebhookType == "TRANSACTIONS" &&
        (requestBody.WebhookCode == "DEFAULT_UPDATE" ||
         requestBody.WebhookCode == "TRANSACTIONS_REMOVED" ||
         requestBody.WebhookCode == "INITIAL_UPDATE"))
    {
        try
        {
            var response = await transactionService.SyncAndProcessTransactions(requestBody.ItemId);

            return Results.Ok(new { message = "Webhook processed and sync initiated.", added = response.Added.Count });
        }
        catch (Exception e)
        {
            Console.WriteLine($"WEBHOOK FAILED PROCESSING for Item {requestBody.ItemId}: {e.Message}");
            return Results.Ok(new { message = "Processing failed internally, but response sent." });
        }
    }

    return Results.Ok(new { message = "Webhook received, action not required for this type." });
})
.WithName("PlaidWebhookReceiver")
.WithOpenApi();

// Used to update how a transaction (typically a deposit) affects the dynamic balance

// GET: /api/transactions/deposits/pending
app.MapGet("/api/transactions/deposits/pending", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // "Deposit" = transaction where we set SuggestedKind (credits only),
        // and where the user has not made a decision yet.
        var pendingDeposits = await dbContext.Transactions
            .Where(t => t.UserId == user.Id
            && t.SuggestedKind != TransactionSuggestedKind.Unknown
            && t.UserDecision == TransactionUserDecision.Undecided)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Results.Ok(pendingDeposits);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetPendingDeposits")
.WithOpenApi();



// Used to update how a transaction (deposit or large expense) affects the dynamic balance
app.MapPost("/api/transactions/{id}/decision", async (int id, UpdateTransactionDecisionRequest body, ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        // --- Auth ---
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // --- Look up transaction for this user ---
        var tx = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
        if (tx == null) return Results.NotFound("Transaction not found.");

        var decision = body.Decision;
        if (!Enum.IsDefined(typeof(TransactionUserDecision), decision))
        {
            return Results.BadRequest("Invalid decision.");
        }

        // Single balance row per user
        var balance = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        // Helper: is this a deposit? (We only set SuggestedKind on credits when syncing)
        bool isDeposit =
            tx.SuggestedKind == TransactionSuggestedKind.Paycheck ||
            tx.SuggestedKind == TransactionSuggestedKind.Windfall ||
            tx.SuggestedKind == TransactionSuggestedKind.InternalTransfer ||
            tx.SuggestedKind == TransactionSuggestedKind.Refund;

        // =========================
        // 1) DEPOSIT DECISIONS
        // =========================
        if (isDeposit)
        {
            switch (decision)
            {
                case TransactionUserDecision.TreatAsIncome:
                    // Only count once
                    if (!tx.CountedAsIncome && balance != null)
                    {
                        balance.BalanceAmount += tx.Amount;
                        balance.UpdatedAt = DateTime.UtcNow;
                        tx.CountedAsIncome = true;
                    }
                    break;

                case TransactionUserDecision.IgnoreForDynamic:
                case TransactionUserDecision.DebtPayment:
                case TransactionUserDecision.SavingsFunded:
                    // If we had previously counted it as income and user changes their mind,
                    // remove it from the dynamic balance.
                    if (tx.CountedAsIncome && balance != null)
                    {
                        balance.BalanceAmount -= tx.Amount;
                        balance.UpdatedAt = DateTime.UtcNow;
                        tx.CountedAsIncome = false;
                    }
                    break;

                // Large-expense decisions don't make sense on deposits; treat as no-op for now.
                case TransactionUserDecision.TreatAsVariableSpend:
                case TransactionUserDecision.LargeExpenseFromSavings:
                case TransactionUserDecision.LargeExpenseToFixedCost:
                    break;
            }
        }
        // =========================
        // 2) LARGE EXPENSE DECISIONS (BIG DEBITS)
        // =========================
        else if (tx.IsLargeExpenseCandidate)
        {
            switch (decision)
            {
                case TransactionUserDecision.TreatAsVariableSpend:
                    // Do nothing to balance; the hit already happened when we synced.
                    tx.IsLargeExpenseCandidate = false;
                    tx.LargeExpenseHandled = true;
                    break;

                case TransactionUserDecision.LargeExpenseFromSavings:
                    // Refund this period’s dynamic balance, but only once.
                    if (!tx.LargeExpenseHandled && balance != null)
                    {
                        balance.BalanceAmount += tx.Amount;
                        balance.UpdatedAt = DateTime.UtcNow;
                    }

                    tx.IsLargeExpenseCandidate = false;
                    tx.LargeExpenseHandled = true;
                    break;

                case TransactionUserDecision.LargeExpenseToFixedCost:
                    // 1) Refund this period
                    if (!tx.LargeExpenseHandled && balance != null)
                    {
                        balance.BalanceAmount += tx.Amount;
                        balance.UpdatedAt = DateTime.UtcNow;
                    }

                    // 2) Create a FixedCost for future periods
                    //    If the client passed a specific installment amount, use it.
                    //    Otherwise, fall back to a simple 4-period split.
                    decimal installmentAmount;

                    if (body.FixedCostAmount.HasValue && body.FixedCostAmount.Value > 0)
                    {
                        installmentAmount = body.FixedCostAmount.Value;
                    }
                    else
                    {
                        installmentAmount = Math.Round(tx.Amount / 4m, 2);
                    }

                    var fixedCostName = !string.IsNullOrWhiteSpace(body.FixedCostName)
                        ? body.FixedCostName
                        : $"Installment: {tx.MerchantName ?? tx.Name ?? "Large Purchase"}";

                    var firstDueDate = (body.FirstDueDate ?? DateTime.UtcNow).Date;

                    var newFixedCost = new FixedCost
                    {
                        UserId = user.Id,
                        Name = fixedCostName,
                        Amount = installmentAmount,
                        Category = "Installment",
                        Type = "large_expense",
                        PlaidMerchantName = tx.MerchantName,
                        PlaidAccountId = tx.AccountId,
                        UserHasApproved = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        NextDueDate = firstDueDate,
                    };

                    await dbContext.FixedCosts.AddAsync(newFixedCost);

                    tx.IsLargeExpenseCandidate = false;
                    tx.LargeExpenseHandled = true;
                    break;

                // Deposit-only decisions on a debit = no-op, just store the choice.
                case TransactionUserDecision.TreatAsIncome:
                case TransactionUserDecision.IgnoreForDynamic:
                case TransactionUserDecision.DebtPayment:
                case TransactionUserDecision.SavingsFunded:
                    // no balance change
                    break;
            }
        }
        // Non-deposit, non-large-expense: we just record whatever they chose.
        // (This should be rare, mostly defensive.)
        else
        {
            // For now, we don't change the balance at all in this branch.
        }

        // Persist the choice
        tx.UserDecision = decision;
        tx.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Decision saved.",
            decision = tx.UserDecision.ToString(),
            countedAsIncome = tx.CountedAsIncome,
            balance = balance?.BalanceAmount,
            transactionId = tx.Id
        });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("DecideOnTransaction")
.WithOpenApi();

public record RegisterDeviceRequest(string ExpoPushToken, string? Platform);


// GET: /api/transactions/large-expenses
app.MapGet("/api/transactions/large-expenses/pending", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var txs = await dbContext.Transactions
            .Where(t => t.UserId == user.Id && t.IsLargeExpenseCandidate && !t.LargeExpenseHandled)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Results.Ok(txs);
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetLargeExpenses")
.WithOpenApi();

// POST: /api/transactions/{id}/large-expense-decision
app.MapPost("/api/transactions/{id}/large-expense-decision",
    async (int id, LargeExpenseDecisionRequest body, ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        // Auth
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var tx = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
        if (tx == null) return Results.NotFound("Transaction not found.");

        if (tx.Amount <= 0)
        {
            return Results.BadRequest("Large-expense decisions only apply to outflow transactions.");
        }

        var balance = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        switch (body.Option)
        {
            case LargeExpenseDecisionOption.TreatAsNormal:
                tx.UserDecision = TransactionUserDecision.TreatAsVariableSpend;
                break;

            case LargeExpenseDecisionOption.FromSavings:
                tx.UserDecision = TransactionUserDecision.LargeExpenseFromSavings;
                if (balance != null)
                {
                    // Refund this amount to the current period spend limit
                    balance.BalanceAmount += tx.Amount;
                    balance.UpdatedAt = DateTime.UtcNow;
                }
                break;

            case LargeExpenseDecisionOption.ConvertToFixedCost:
                if (body.SplitOverPeriods is null or <= 0)
                {
                    return Results.BadRequest("SplitOverPeriods must be at least 1 for ConvertToFixedCost.");
                }

                tx.UserDecision = TransactionUserDecision.LargeExpenseToFixedCost;

                if (balance != null)
                {
                    // We no longer want this full amount counted as current-period spend
                    balance.BalanceAmount += tx.Amount;
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                int periods = body.SplitOverPeriods.Value;
                decimal perPeriodAmount = Math.Round(tx.Amount / periods, 2);

                // Very simple bi-weekly approximation for due dates.
                var baseDate = tx.Date.Date;

                for (int i = 1; i <= periods; i++)
                {
                    var dueDate = baseDate.AddDays(14 * i);

                    var fixedCost = new FixedCost
                    {
                        UserId = user.Id,
                        Name = $"Installment: {tx.MerchantName ?? tx.Name ?? "Large purchase"}",
                        Amount = perPeriodAmount,
                        Category = "Installment",
                        Type = "large_expense_plan",
                        PlaidMerchantName = tx.MerchantName,
                        PlaidAccountId = tx.AccountId,
                        UserHasApproved = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        NextDueDate = dueDate
                    };

                    await dbContext.FixedCosts.AddAsync(fixedCost);
                }
                break;

            default:
                return Results.BadRequest("Unknown option.");
        }

        tx.IsLargeExpenseCandidate = false;
        tx.LargeExpenseHandled = true;
        tx.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Large expense decision saved.",
            option = body.Option.ToString(),
            newBalance = balance?.BalanceAmount
        });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("DecideOnLargeExpense")
.WithOpenApi();

app.MapPost("/api/notifications/register-device",
    async (ApiDbContext dbContext, HttpContext httpContext, RegisterDeviceRequest request) =>
    {
        try
        {
            string? idToken = httpContext.Request.Headers["Authorization"]
                .FirstOrDefault()
                ?.Split(" ")
                .Last();

            if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
            if (user == null) return Results.NotFound("User not found.");

            var existing = await dbContext.UserDevices
                .FirstOrDefaultAsync(d =>
                    d.UserId == user.Id &&
                    d.ExpoPushToken == request.ExpoPushToken);

            if (existing == null)
            {
                var device = new UserDevice
                {
                    UserId = user.Id,
                    ExpoPushToken = request.ExpoPushToken,
                    Platform = request.Platform,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await dbContext.UserDevices.AddAsync(device);
            }
            else
            {
                existing.IsActive = true;
                existing.Platform = request.Platform;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();

            return Results.Ok(new { message = "Device registered for notifications." });
        }
        catch (Exception e)
        {
            return Results.Problem(e.Message);
        }
    })
    .WithName("RegisterDeviceForNotifications")
    .WithOpenApi();





// --- RUN THE APP ---
app.Run();

// --- TYPE DECLARATIONS (if you still keep them here) ---

// Used for POST /api/users/register

