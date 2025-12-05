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

builder.Services.AddScoped<ITransactionService, TransactionService>();

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

// --- RUN THE APP ---
app.Run();

// --- TYPE DECLARATIONS (if you still keep them here) ---

// Used for POST /api/users/register
public record UserRegistrationRequest(string Name, string Email, string FirebaseUuid);

// Used for POST /api/plaid/create_link_token
public record CreateLinkTokenRequest(string FirebaseUserId);

// Used for POST /api/plaid/exchange_public_token
public record ExchangeTokenRequest(string PublicToken, string FirebaseUuid);
