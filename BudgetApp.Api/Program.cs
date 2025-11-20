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
using BudgetApp.Api.Services; // <-- This is the missing line

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
        Products = products
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

app.MapGet("/api/plaid/accounts", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        // 1. Get the token from the Authorization header
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken))
        {
            return Results.Unauthorized();
        }

        // 2. Verify the token with Firebase
        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
            .VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        // 3. Find the user in your database
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null)
        {
            return Results.NotFound(new { message = "User not found." });
        }

        // 4. Fetch the user's Plaid items
        var accounts = await dbContext.PlaidItems
            .Where(item => item.UserId == user.Id)
            .Select(item => new
            { // Only return safe data
                item.Id,
                item.InstitutionName,
                item.InstitutionLogo
            })
            .ToListAsync();

        return Results.Ok(accounts);
    }
    catch (Exception e)
    {
        // Handle token verification errors or database errors
        return Results.Problem(e.Message);
    }
})
.WithName("GetUserPlaidAccounts")
.WithOpenApi();

// ... after your Plaid endpoints ...

// GET: /api/balance (Get the current dynamic amount)
app.MapGet("/api/balance", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        // 1. Securely get the user from the token
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        // 2. Get the balance
        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        // If no balance exists yet, return 0
        return Results.Ok(new { amount = balanceRecord?.BalanceAmount ?? 0 });
    }
    catch (Exception e)
    {
        return Results.Problem(e.Message);
    }
})
.WithName("GetBalance")
.WithOpenApi();

// POST: /api/balance (Set the initial paycheck amount)
app.MapPost("/api/balance", async (ApiDbContext dbContext, HttpContext httpContext, SetBalanceRequest request) =>
{
    try
    {
        // 1. Securely get the user
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        // 2. Find existing balance or create new one
        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);

        if (balanceRecord == null)
        {
            // Create new
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
            // Update existing
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
app.MapPost("/api/transactions/sync", async (ITransactionService transactionService, HttpContext httpContext) =>
{
    try
    {
        // 1. Auth: Securely get the User's UID from the token
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        // 2. Call the reusable service logic
        var response = await transactionService.SyncAndProcessTransactions(firebaseUuid);

        // This response no longer contains 'variableSpend', so return simplified stats
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
// GET: /api/transactions (Get all transactions for the user)
app.MapGet("/api/transactions", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        // 1. Auth: Get the User
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // 2. Fetch all transactions for this user, newest first
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

// --- Endpoint to GET all fixed costs for the user ---
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

// --- Endpoint to ADD a new MANUAL fixed cost ---
app.MapPost("/api/fixed-costs", async (ApiDbContext dbContext, HttpContext httpContext, FixedCost requestBody) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // Create new FixedCost from the request body
        var newCost = new FixedCost
        {
            UserId = user.Id,
            Name = requestBody.Name,
            Amount = requestBody.Amount,
            Category = requestBody.Category ?? "other",
            Type = "manual", // Explicitly set as manual
            PlaidMerchantName = requestBody.PlaidMerchantName,
            UserHasApproved = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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

// --- Endpoint to DELETE a fixed cost ---
app.MapDelete("/api/fixed-costs/{id}", async (ApiDbContext dbContext, HttpContext httpContext, int id) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // Find the cost by its ID and make sure it belongs to this user
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

        // --- FIX: Use Projection to select only simple fields ---
        var userProfile = await dbContext.Users.AsNoTracking()
            .Where(u => u.FirebaseUuid == firebaseUuid)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.FirebaseUuid,
                u.OnboardingComplete // The critical flag for the frontend
            })
            .FirstOrDefaultAsync();

        if (userProfile == null)
        {
            return Results.NotFound(new { message = "User not found in database." });
        }

        return Results.Ok(userProfile); // This will now return 200 OK with flat JSON data
    }
    catch (Exception e)
    {
        return Results.Problem($"Error fetching profile: {e.Message}");
    }
})
.WithName("GetUserProfile")
.WithOpenApi();

// GET: /api/plaid/recurring (Fetches Plaid's AI-detected recurring expenses)
app.MapGet("/api/plaid/recurring", async (ApiDbContext dbContext, PlaidClient plaidClient, HttpContext httpContext) =>
{
    try
    {
        // 1. Auth: Securely get the User
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // 2. Get the Plaid Item (Access Token)
        var plaidItem = await dbContext.PlaidItems.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (plaidItem == null) return Results.Ok(new { message = "No bank linked." });

        // 3. Call Plaid /transactions/recurring/get (Plaid recommends 180 days of history)
        var request = new Going.Plaid.Transactions.TransactionsRecurringGetRequest
        {
            AccessToken = plaidItem.AccessToken,
        };

        var response = await plaidClient.TransactionsRecurringGetAsync(request);

        // 4. Return the recurring streams (subscriptions)
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

// POST: /api/budget/finalize (Calculates prorated balance and completes onboarding)
app.MapPost("/api/budget/finalize", async (ApiDbContext dbContext, HttpContext httpContext, FinalizeBudgetRequest request) =>
{
    try
    {
        // 1. Auth and User Lookup
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        if (user.OnboardingComplete) return Results.Conflict("Onboarding already complete.");

        // 2. Calculate Total Recurring Costs (Rent, Loans, Savings Goal, etc.)
        var totalRecurringCosts = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id)
            .SumAsync(fc => fc.Amount);

        decimal effectivePaycheck = request.PaycheckAmount - totalRecurringCosts;

        // 3. Prorate Calculation (The Tricky Part)
        // Assume a 15-day cycle for simplicity, as the user gets paid twice monthly.
        const int PayCycleDays = 15;
        DateTime today = DateTime.UtcNow.Date;
        DateTime nextPaycheck = request.NextPaycheckDate.Date;

        int daysUntilNextPaycheck = (int)(nextPaycheck - today).TotalDays;

        if (daysUntilNextPaycheck < 0 || daysUntilNextPaycheck > PayCycleDays)
        {
            return Results.BadRequest("Next Paycheck Date is invalid for a bi-monthly cycle (must be 1-15 days away).");
        }

        // Prorate Factor: 1.0 if the cycle is starting, 0.1 if only 1 day is left
        decimal prorateFactor = (decimal)daysUntilNextPaycheck / PayCycleDays;
        decimal finalDynamicBalance = effectivePaycheck * prorateFactor;

        // 4. Save Final Balance (Upsert logic)
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

        // 5. Flip Onboarding Flag to TRUE

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
// --- RUN THE APP ---
app.Run();

// --- TYPE DECLARATIONS (MUST COME AFTER TOP-LEVEL STATEMENTS) ---
// These classes/records define the expected JSON data for your API endpoints.

// Used for POST /api/users/register
public record UserRegistrationRequest(string Name, string Email, string FirebaseUuid);

// Used for POST /api/plaid/create_link_token
public record CreateLinkTokenRequest(string FirebaseUserId);

// Used for POST /api/plaid/exchange_public_token
public record ExchangeTokenRequest(string PublicToken, string FirebaseUuid);
