using BudgetApp.Api.Data;
using Going.Plaid;
using Going.Plaid.Categories;
using Going.Plaid.Entity;
using Going.Plaid.Accounts;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Microsoft.EntityFrameworkCore;
using Refit;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using BudgetApp.Api.Services;
using System.Linq;
using FirebaseAdmin.Auth;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Npgsql;
using Sentry;



// --- App Setup ---
var builder = WebApplication.CreateBuilder(args);

// Simplified port configuration for AWS App Runner
var port = System.Environment.GetEnvironmentVariable("PORT") ?? "8080";
SentrySdk.AddBreadcrumb($"BOOT: Using port {port}", level: BreadcrumbLevel.Info);

// Only set ASPNETCORE_URLS if not already set (Dockerfile may have set it)
if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    System.Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{port}");
}

// ─── Sentry ASP.NET Core integration ─────────────────────────────────────────
// This hooks into the request pipeline, automatically capturing unhandled
// exceptions and forwarding ILogger.LogError / LogWarning to Sentry.
builder.WebHost.UseSentry(o =>
{
    o.Dsn = System.Environment.GetEnvironmentVariable("SENTRY_DSN")
            ?? builder.Configuration["Sentry:Dsn"]
            ?? string.Empty;
    o.TracesSampleRate = 1.0;
    o.MaxBreadcrumbs = 200;
    // Set to false once you've confirmed events appear in the Sentry dashboard
    o.Debug = true;
});

Console.WriteLine($"SENTRY_DSN present: {!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SENTRY_DSN"))}");

try
{
    var serviceAccountJson = System.Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");

    SentrySdk.AddBreadcrumb(
        $"BOOT: FIREBASE JSON present: {!string.IsNullOrEmpty(serviceAccountJson)}",
        level: BreadcrumbLevel.Info);

    if (!string.IsNullOrEmpty(serviceAccountJson))
    {
        SentrySdk.AddBreadcrumb("BOOT: Initializing Firebase from ENV...", level: BreadcrumbLevel.Info);

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(serviceAccountJson)
        });

        SentrySdk.AddBreadcrumb("BOOT: Firebase initialized from ENV", level: BreadcrumbLevel.Info);
    }
    else if (builder.Environment.IsDevelopment())
    {
        var firebasePath = "firebase-service-account.json";

        SentrySdk.AddBreadcrumb(
            $"BOOT: Dev mode. Checking file at: {firebasePath} — exists: {File.Exists(firebasePath)}",
            level: BreadcrumbLevel.Info);

        if (!File.Exists(firebasePath))
        {
            throw new Exception("Missing firebase-service-account.json in development");
        }

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(firebasePath)
        });

        SentrySdk.AddBreadcrumb("BOOT: Firebase initialized from file (dev)", level: BreadcrumbLevel.Info);
    }
    else
    {
        throw new Exception("FIREBASE_SERVICE_ACCOUNT_JSON is required in production");
    }
}
catch (Exception ex)
{
    SentrySdk.CaptureException(ex);
    throw; // always crash on Firebase init failure
}

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


SentrySdk.AddBreadcrumb("BOOT: startup begin", level: BreadcrumbLevel.Info);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
SentrySdk.AddBreadcrumb(
    $"BOOT: has DefaultConnection={!string.IsNullOrWhiteSpace(connectionString)}",
    level: BreadcrumbLevel.Info);

if (string.IsNullOrWhiteSpace(connectionString))
{
    var ex = new InvalidOperationException(
        "Database connection string 'DefaultConnection' is required but not found in configuration.");
    SentrySdk.CaptureException(ex);
    throw ex;
}

try
{
    var csb = new NpgsqlConnectionStringBuilder(connectionString);
    SentrySdk.AddBreadcrumb(
        $"BOOT: DB Host={csb.Host} Port={csb.Port} Name={csb.Database} User={csb.Username}",
        level: BreadcrumbLevel.Info);
}
catch (Exception ex)
{
    SentrySdk.CaptureException(ex);
    throw new InvalidOperationException($"Invalid database connection string format: {ex.Message}");
}

builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddScoped<INotificationService, ExpoNotificationService>();



builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddSingleton<IDynamicBudgetEngine, DynamicBudgetEngine>();
builder.Services.AddScoped<DynamicBudgetEngine>();
builder.Services.AddScoped<IPaycheckSummaryService, PaycheckSummaryService>();


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
SentrySdk.AddBreadcrumb("BOOT: before builder.Build()", level: BreadcrumbLevel.Info);
var app = builder.Build();
SentrySdk.AddBreadcrumb("BOOT: after builder.Build()", level: BreadcrumbLevel.Info);

// Verification — confirms DSN is wired correctly. Remove after first successful deploy.
SentrySdk.CaptureMessage("Hello Sentry — BudgetApp.Api started, and this is new");



if (!app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}



app.MapGet("/health", async (ApiDbContext dbContext) =>
{
    try
    {
        // Test database connectivity
        await dbContext.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        SentrySdk.CaptureException(ex);
        return Results.Problem(detail: "Database connectivity failed", statusCode: 503);
    }
});


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/health"), b =>
{
    b.UseHttpsRedirection();
});
app.UseCors(MyAllowSpecificOrigins);

// --- API ENDPOINTS ---

// POST: /api/users/register
// Requires Bearer token. Derives Firebase UID from token. Body: { email, name }
app.MapPost("/api/users/register", async (ApiDbContext dbContext, HttpContext httpContext, UserRegistrationRequest requestBody) =>
{
    try
    {

        SentrySdk.CaptureMessage("register Endpoint hit");

        // --- Verify Firebase token and derive UID ---
        string? idToken = httpContext.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Split(" ")
            .Last();

        if (string.IsNullOrEmpty(idToken))
            return Results.Unauthorized();

        FirebaseToken decodedToken;
        try
        {
            decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            return Results.Unauthorized();
        }

        var firebaseUid = decodedToken.Uid;
        SentrySdk.AddBreadcrumb(
            $"Register: uid={firebaseUid} email={requestBody.Email}",
            level: BreadcrumbLevel.Info);

        // --- Idempotent: return existing user if already registered ---
        var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUid);
        if (existingUser != null)
        {
            SentrySdk.AddBreadcrumb(
                $"Register: user already exists id={existingUser.Id}",
                level: BreadcrumbLevel.Info);
            return Results.Ok(new
            {
                message = "User already registered",
                userId = existingUser.Id,
                name = existingUser.Name,
                email = existingUser.Email
            });
        }

        var newUser = new User
        {
            Name = requestBody.Name,
            Email = requestBody.Email,
            FirebaseUuid = firebaseUid,
            OnboardingComplete = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await dbContext.Users.AddAsync(newUser);
        await dbContext.SaveChangesAsync();

        SentrySdk.AddBreadcrumb(
            $"Register: created user id={newUser.Id}",
            level: BreadcrumbLevel.Info);
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
        SentrySdk.CaptureException(e);
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
        SentrySdk.CaptureException(e);
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
        ClientName = "NearPath",
        Language = Language.English,
        CountryCodes = countryCodes,
        User = user,
        Products = products,
        Webhook = config["Plaid:WebhookUrl"],
        // RedirectUri is only needed for OAuth institutions in web/production flows.
        // In Sandbox (dev), setting it causes link token creation to fail unless the URI
        // is also registered in the Plaid Dashboard's Sandbox allowed-redirect-URI list.
        // For native mobile (react-native-plaid-link-sdk) no redirect URI is needed.
        RedirectUri = app.Environment.IsProduction()
            ? "https://plaid-redirect.dynamicbudgetapp.com"
            : null
    };

    SentrySdk.CaptureMessage($"Plaid webhook URL in create_link_token: {config["Plaid:WebhookUrl"]}");

    try
    {
        SentrySdk.CaptureMessage($"Plffffaid webhook URL in create_link_token: {config["Plaid:WebhookUrl"]}");
        var response = await plaidClient.LinkTokenCreateAsync(plaidRequest);
        return Results.Ok(new { linkToken = response.LinkToken });
    }
    catch (ApiException e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem($"Plaid API Error: {e.Content}");
    }
})
.WithName("CreateLinkToken")
.WithOpenApi();

app.MapPost("/api/plaid/exchange_public_token",
    async (PlaidClient plaidClient, ApiDbContext dbContext, ExchangeTokenRequest requestBody) =>
    {
        Console.WriteLine("🚀 HIT /exchange_public_token");
        SentrySdk.CaptureMessage("exhange public token endpoint hit.s");

        using (SentrySdk.PushScope())
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.SetTag("endpoint", "exchange_public_token");

                scope.SetExtra("firebaseUuid", requestBody.FirebaseUuid ?? "null");
                scope.SetExtra("hasPublicToken", !string.IsNullOrEmpty(requestBody.PublicToken));
            });

            SentrySdk.AddBreadcrumb("START exchange_public_token");

            Console.WriteLine($"📥 FirebaseUuid: {requestBody.FirebaseUuid}");
            Console.WriteLine($"📥 Has PublicToken: {!string.IsNullOrEmpty(requestBody.PublicToken)}");

            try
            {
                // --- STEP 1: Plaid exchange ---
                Console.WriteLine("➡️ Calling Plaid exchange...");
                SentrySdk.AddBreadcrumb("Calling Plaid exchange");
                SentrySdk.CaptureMessage("Calling plaid exchannge");


                var plaidRequest = new ItemPublicTokenExchangeRequest
                {
                    PublicToken = requestBody.PublicToken
                };

                var response = await plaidClient.ItemPublicTokenExchangeAsync(plaidRequest);

                Console.WriteLine("✅ Plaid exchange success");
                Console.WriteLine($"📦 ItemId: {response.ItemId}");
                Console.WriteLine($"📦 Has AccessToken: {!string.IsNullOrEmpty(response.AccessToken)}");
                SentrySdk.CaptureMessage("asdfaseasdafd");

                SentrySdk.AddBreadcrumb("Plaid exchange success");

                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetExtra("plaid_item_id", response.ItemId);
                    scope.SetExtra("has_access_token", !string.IsNullOrEmpty(response.AccessToken));
                });

                // --- STEP 2: Find user ---
                Console.WriteLine("➡️ Looking up user...");
                SentrySdk.AddBreadcrumb("Looking up user");
                SentrySdk.CaptureMessage("Looking up user");


                var user = await dbContext.Users
                    .FirstOrDefaultAsync(u => u.FirebaseUuid == requestBody.FirebaseUuid);

                if (user == null)
                {
                    Console.WriteLine("❌ USER NOT FOUND");

                    SentrySdk.CaptureMessage("USER NOT FOUND", scope =>
                    {
                        scope.Level = SentryLevel.Error;
                        scope.SetExtra("firebaseUuid", requestBody.FirebaseUuid);
                    });

                    return Results.NotFound(new { message = "User not found." });
                }

                Console.WriteLine($"✅ User found: {user.Id}");
                SentrySdk.AddBreadcrumb($"User found: {user.Id}");

                // --- STEP 3: Save item ---
                Console.WriteLine("➡️ Creating PlaidItem...");

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

                Console.WriteLine("➡️ Adding to DB...");
                SentrySdk.AddBreadcrumb("Adding PlaidItem to DB");

                await dbContext.PlaidItems.AddAsync(newItem);

                Console.WriteLine("➡️ Saving DB changes...");
                SentrySdk.AddBreadcrumb("Saving DB changes");

                await dbContext.SaveChangesAsync();

                Console.WriteLine("✅ DB save success");
                SentrySdk.AddBreadcrumb("DB save success");

                SentrySdk.CaptureMessage("exchange_public_token SUCCESS");

                Console.WriteLine("🎉 ENDPOINT SUCCESS");

                return Results.Ok(new { message = "Public token exchanged and saved successfully." });
            }
            catch (ApiException e)
            {
                Console.WriteLine("💥 PLAID ERROR");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.Content);

                SentrySdk.CaptureException(e);

                return Results.Problem($"Plaid error: {e.Content}");
            }
            catch (Exception e)
            {
                Console.WriteLine("💥 UNKNOWN ERROR");
                Console.WriteLine(e.ToString());

                SentrySdk.CaptureException(e);

                // In development, surface the real exception so it's visible in the response
                var detail = app.Environment.IsDevelopment()
                    ? $"[DEV] {e.GetType().Name}: {e.Message}\n{e.StackTrace}"
                    : "Unexpected error occurred.";

                return Results.Problem(detail);
            }
        }
    });
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
        SentrySdk.CaptureException(e);
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
        SentrySdk.CaptureException(e);
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
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("SetBalance")
.WithOpenApi();

// POST: /api/transactions/sync
// Syncs ALL PlaidItems for the authenticated user. Each item is wrapped in its own
// try/catch so a single failed item does not block the remaining items from syncing.
app.MapPost("/api/transactions/sync", async (
    ITransactionService transactionService,
    ApiDbContext dbContext,
    HttpContext httpContext,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("ManualTransactionsSync");

    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var firebaseUuid = decodedToken.Uid;

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == firebaseUuid);
        if (user == null) return Results.NotFound("User not found.");

        // Load ALL PlaidItems for this user — previously only FirstOrDefault was synced.
        var plaidItems = await dbContext.PlaidItems
            .Where(p => p.UserId == user.Id)
            .ToListAsync();

        if (!plaidItems.Any()) return Results.BadRequest("No Plaid item linked for this user.");

        logger.LogInformation(
            "Manual sync: userId={UserId} totalPlaidItems={Count} items=[{Items}]",
            user.Id, plaidItems.Count,
            string.Join("; ", plaidItems.Select(p =>
                $"dbId={p.Id} itemId={p.ItemId} cursor={p.Cursor ?? "(null)"}")));

        SentrySdk.AddBreadcrumb(
            $"Manual sync: userId={user.Id} plaidItemCount={plaidItems.Count} " +
            $"itemIds=[{string.Join(",", plaidItems.Select(p => p.ItemId))}]",
            level: BreadcrumbLevel.Info);

        var results = new List<object>();
        int successCount = 0;
        int failureCount = 0;

        foreach (var plaidItem in plaidItems)
        {
            try
            {
                logger.LogInformation(
                    "Manual sync starting item: userId={UserId} plaidItemDbId={PlaidItemDbId} " +
                    "itemId={ItemId} cursorBefore={Cursor}",
                    user.Id, plaidItem.Id, plaidItem.ItemId, plaidItem.Cursor ?? "(null)");

                // webhookCode = null → live sync, notifications eligible
                var response = await transactionService.SyncAndProcessTransactions(plaidItem.ItemId);

                int addedCount = response.Added?.Count ?? 0;
                int modifiedCount = response.Modified?.Count ?? 0;
                int removedCount = response.Removed?.Count ?? 0;

                logger.LogInformation(
                    "Manual sync item complete: userId={UserId} plaidItemDbId={PlaidItemDbId} " +
                    "itemId={ItemId} added={Added} modified={Modified} removed={Removed} hasMore={HasMore}",
                    user.Id, plaidItem.Id, plaidItem.ItemId,
                    addedCount, modifiedCount, removedCount, response.HasMore);

                successCount++;
                results.Add(new
                {
                    plaidItemDbId = plaidItem.Id,
                    itemId = plaidItem.ItemId,
                    success = true,
                    added = addedCount,
                    modified = modifiedCount,
                    removed = removedCount,
                    hasMore = response.HasMore,
                    error = (string?)null
                });
            }
            catch (Exception itemEx)
            {
                failureCount++;

                logger.LogError(itemEx,
                    "Manual sync item FAILED: userId={UserId} plaidItemDbId={PlaidItemDbId} " +
                    "itemId={ItemId}",
                    user.Id, plaidItem.Id, plaidItem.ItemId);

                SentrySdk.CaptureException(itemEx, scope =>
                {
                    scope.SetTag("endpoint", "manual_transactions_sync");
                    scope.SetTag("sync.itemId", plaidItem.ItemId);
                    scope.SetTag("sync.plaidItemDbId", plaidItem.Id.ToString());
                    scope.SetTag("sync.userId", user.Id.ToString());
                    scope.SetExtra("sync.itemId", plaidItem.ItemId);
                    scope.SetExtra("sync.plaidItemDbId", plaidItem.Id);
                    scope.SetExtra("sync.userId", user.Id);
                });

                results.Add(new
                {
                    plaidItemDbId = plaidItem.Id,
                    itemId = plaidItem.ItemId,
                    success = false,
                    added = 0,
                    modified = 0,
                    removed = 0,
                    hasMore = false,
                    error = itemEx.Message
                });
            }
        }

        logger.LogInformation(
            "Manual sync complete: userId={UserId} totalItems={Total} succeeded={Succeeded} failed={Failed}",
            user.Id, plaidItems.Count, successCount, failureCount);

        return Results.Ok(new
        {
            message = "Sync complete",
            totalItems = plaidItems.Count,
            succeeded = successCount,
            failed = failureCount,
            results
        });
    }
    catch (UnauthorizedAccessException e)
    {
        SentrySdk.CaptureException(e);
        return Results.Unauthorized();
    }
    catch (InvalidOperationException e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
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
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetTransactions")
.WithOpenApi();




// ─── Shared DTO helper — avoids the User navigation-property cycle ────────────
// Used by POST and PUT endpoints.  GET uses an inline EF projection instead
// so EF can translate the projection to SQL without loading the entity.
static object ToFixedCostDto(FixedCost fc) => new
{
    id = fc.Id,
    userId = fc.UserId,
    name = fc.Name,
    amount = fc.Amount,
    category = fc.Category,
    type = fc.Type,
    plaidMerchantName = fc.PlaidMerchantName,
    plaidAccountId = fc.PlaidAccountId,
    userHasApproved = fc.UserHasApproved,
    recurrenceFrequency = fc.RecurrenceFrequency,
    originalDueDayOfMonth = fc.OriginalDueDayOfMonth,
    nextDueDate = fc.NextDueDate,
    createdAt = fc.CreatedAt,
    updatedAt = fc.UpdatedAt
};

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

        // Inline EF projection — never loads the FixedCost entity into memory,
        // so the virtual User navigation property cannot trigger a serialization cycle.
        var costs = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id)
            .OrderBy(fc => fc.Name)
            .Select(fc => new
            {
                id = fc.Id,
                userId = fc.UserId,
                name = fc.Name,
                amount = fc.Amount,
                category = fc.Category,
                type = fc.Type,
                plaidMerchantName = fc.PlaidMerchantName,
                plaidAccountId = fc.PlaidAccountId,
                userHasApproved = fc.UserHasApproved,
                recurrenceFrequency = fc.RecurrenceFrequency,
                originalDueDayOfMonth = fc.OriginalDueDayOfMonth,
                nextDueDate = fc.NextDueDate,
                createdAt = fc.CreatedAt,
                updatedAt = fc.UpdatedAt
            })
            .ToListAsync();

        return Results.Ok(costs);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
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
            NextDueDate = requestBody.NextDueDate,
            // Recurrence — default Monthly if not provided
            RecurrenceFrequency = !string.IsNullOrWhiteSpace(requestBody.RecurrenceFrequency)
                ? requestBody.RecurrenceFrequency
                : "Monthly",
            // OriginalDueDayOfMonth: set once from the initial due date.
            // Never overwritten by automatic advancement — only user edits may change it.
            OriginalDueDayOfMonth = requestBody.NextDueDate?.Day
        };

        await dbContext.FixedCosts.AddAsync(newCost);
        await dbContext.SaveChangesAsync();

        return Results.Ok(ToFixedCostDto(newCost));
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
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
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("DeleteFixedCost")
.WithOpenApi();

// PUT: /api/fixed-costs/{id}
// Updates name, amount, category, nextDueDate, and optionally type of an existing fixed cost.
// Only the authenticated owner may update their own fixed costs.
app.MapPut("/api/fixed-costs/{id}", async (ApiDbContext dbContext, HttpContext httpContext, int id, FixedCost requestBody) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var cost = await dbContext.FixedCosts
            .FirstOrDefaultAsync(fc => fc.Id == id && fc.UserId == user.Id);

        if (cost == null)
            return Results.NotFound("Fixed cost not found or you do not have permission.");

        // Update user-editable fields — only overwrite if the caller provided a value.
        if (!string.IsNullOrWhiteSpace(requestBody.Name))
            cost.Name = requestBody.Name;

        if (requestBody.Amount > 0)
            cost.Amount = requestBody.Amount;

        if (!string.IsNullOrWhiteSpace(requestBody.Category))
            cost.Category = requestBody.Category;

        if (!string.IsNullOrWhiteSpace(requestBody.Type))
            cost.Type = requestBody.Type;

        // RecurrenceFrequency: update if the caller provides a non-empty value.
        if (!string.IsNullOrWhiteSpace(requestBody.RecurrenceFrequency))
            cost.RecurrenceFrequency = requestBody.RecurrenceFrequency;

        // NextDueDate: accept null (clears the date) or any valid DateTime from caller.
        // IMPORTANT: only update OriginalDueDayOfMonth when the user explicitly changes
        // the due date.  Automatic recurrence advancement NEVER calls this endpoint, so
        // any write here is always a deliberate user edit.
        //
        // Why this matters: if a bill is intended for the 31st but the current
        // NextDueDate landed on Feb 28 after a short-month clamp, we still need
        // OriginalDueDayOfMonth = 31 so the NEXT advance restores Mar 31.
        // Overwriting it with 28 here would permanently lose that intent.
        bool dueDateChanged = cost.NextDueDate?.Date != requestBody.NextDueDate?.Date;
        cost.NextDueDate = requestBody.NextDueDate;

        if (dueDateChanged)
        {
            // Capture the new intended day-of-month from the user's explicit edit.
            // Null if the user cleared the due date.
            cost.OriginalDueDayOfMonth = requestBody.NextDueDate?.Day;
        }
        // else: leave OriginalDueDayOfMonth unchanged — it reflects the user's original intent.

        cost.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        SentrySdk.AddBreadcrumb(
            $"FixedCost updated: id={cost.Id} name={cost.Name} amount={cost.Amount} " +
            $"category={cost.Category} recurrenceFrequency={cost.RecurrenceFrequency} " +
            $"nextDueDate={cost.NextDueDate?.ToString("yyyy-MM-dd") ?? "(null)"} " +
            $"originalDueDayOfMonth={cost.OriginalDueDayOfMonth?.ToString() ?? "(null)"} " +
            $"dueDateChanged={dueDateChanged} userId={user.Id}",
            level: BreadcrumbLevel.Info);

        return Results.Ok(ToFixedCostDto(cost));
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("UpdateFixedCost")
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
        SentrySdk.CaptureException(e);
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

        // Map each stream to a stable DTO with explicit snake_case field names.
        // This ensures:
        //   • next_projected_date is always populated (inferred from last_date + frequency when Plaid omits it)
        //   • confidence_level is always a normalised string (maps from older `confidence_level`
        //     or newer `status` field so the frontend works with both Plaid API versions)
        // Going.Plaid v6 TransactionStream facts (confirmed via reflection):
        //   PredictedNextDate  → DateOnly?  (nullable)  — the projected next occurrence
        //   LastDate           → DateOnly   (non-nullable)
        //   Frequency          → RecurringTransactionFrequency (non-nullable enum)
        //   Status             → TransactionStreamStatus       (non-nullable enum)
        //   LastAmount         → TransactionStreamAmount        (non-nullable)
        static string? InferNextDate(DateOnly lastDate, string freq)
        {
            DateOnly? next = freq.ToUpperInvariant() switch
            {
                "WEEKLY" => lastDate.AddDays(7),
                "BIWEEKLY" => lastDate.AddDays(14),
                "SEMIMONTHLY" => lastDate.AddDays(15),
                "SEMI_MONTHLY" => lastDate.AddDays(15),
                "MONTHLY" => lastDate.AddMonths(1),
                "ANNUALLY" => lastDate.AddYears(1),
                _ => null
            };
            return next?.ToString("yyyy-MM-dd");
        }

        static object MapStream(TransactionStream s)
        {
            var freqStr = s.Frequency.ToString();
            // PredictedNextDate is the correct property name in Going.Plaid v6
            // (not NextProjectedDate). It is nullable, so ?. is correct here.
            var nextDate = s.PredictedNextDate?.ToString("yyyy-MM-dd")
                           ?? InferNextDate(s.LastDate, freqStr);

            // Normalise confidence: older Plaid API surfaces `confidence_level`,
            // newer API uses `status` (MATURE / EARLY_DETECTION / TOMBSTONED).
            var statusStr = s.Status.ToString();
            var confidence =
                statusStr.Contains("Mature", StringComparison.OrdinalIgnoreCase) ? "HIGH" :
                statusStr.Contains("Early", StringComparison.OrdinalIgnoreCase) ? "MEDIUM" : "LOW";

            return new
            {
                stream_id = s.StreamId,
                description = s.Description ?? s.MerchantName ?? "Unknown",
                merchant_name = s.MerchantName,
                last_amount = new { amount = s.LastAmount.Amount },
                frequency = freqStr,
                next_projected_date = nextDate,
                last_date = s.LastDate.ToString("yyyy-MM-dd"),
                confidence_level = confidence,
                status = statusStr,
                is_active = s.IsActive
            };
        }

        return Results.Ok(new
        {
            inflow_streams = response.InflowStreams?.Select(MapStream).ToList(),
            outflow_streams = response.OutflowStreams?.Select(MapStream).ToList()
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetPlaidRecurring")
.WithOpenApi();

// POST: /api/budget/base
// Returns baseRemaining = paycheck - fixedCosts (before debt & savings decisions).
// Called by the Debt screen so users see the impact of their debt choices.
app.MapPost("/api/budget/base", async (
    ApiDbContext dbContext,
    HttpContext httpContext,
    IDynamicBudgetEngine budgetEngine,
    BaseBudgetHttpRequest request,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("BudgetBase");

    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        DateTime today = DateTime.UtcNow.Date;
        DateTime nextPaycheck = request.NextPaycheckDate.Date;

        if (nextPaycheck <= today)
            return Results.BadRequest("Next paycheck date must be in the future.");

        // Validate pay cycle
        var previousPaycheck = budgetEngine.CalculatePreviousPaycheckDate(
            request.PayDay1, request.PayDay2, nextPaycheck);

        int payCycleDays = (int)(nextPaycheck - previousPaycheck).TotalDays;
        if (payCycleDays <= 0)
            return Results.BadRequest("Invalid pay cycle detected.");

        // ── Load all fixed costs for the user so we can log inclusion/exclusion ──
        var allFixedCosts = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id)
            .ToListAsync();

        // Apply the same filter used by the budget engine
        var fixedBillsThisPeriod = allFixedCosts
            .Where(fc => fc.NextDueDate.HasValue
                && fc.NextDueDate.Value.Date >= today
                && fc.NextDueDate.Value.Date <= nextPaycheck
                && !string.Equals(fc.Category, "Savings", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal totalFixedBills = fixedBillsThisPeriod.Sum(fc => fc.Amount);

        // ── Log the fixed-cost filter results ──────────────────────────────────
        logger.LogInformation(
            "BudgetBase fixed-cost filter: userId={UserId} today={Today} nextPaycheck={NextPaycheck} " +
            "totalCosts={Total} includedCosts={Included} excludedCosts={Excluded} totalFixedBills={TotalFixedBills}",
            user.Id,
            today.ToString("yyyy-MM-dd"),
            nextPaycheck.ToString("yyyy-MM-dd"),
            allFixedCosts.Count,
            fixedBillsThisPeriod.Count,
            allFixedCosts.Count - fixedBillsThisPeriod.Count,
            totalFixedBills);

        foreach (var fc in allFixedCosts)
        {
            bool included = fixedBillsThisPeriod.Contains(fc);
            string excludeReason = included ? "" :
                !fc.NextDueDate.HasValue ? "no-due-date" :
                string.Equals(fc.Category, "Savings", StringComparison.OrdinalIgnoreCase) ? "savings-category" :
                fc.NextDueDate.Value.Date < today ? "before-today" :
                                                                         "after-next-paycheck";

            logger.LogInformation(
                "BudgetBase FixedCost: id={Id} name={Name} amount={Amount} category={Category} " +
                "nextDueDate={NextDueDate} included={Included} excludeReason={ExcludeReason}",
                fc.Id, fc.Name, fc.Amount, fc.Category,
                fc.NextDueDate?.ToString("yyyy-MM-dd") ?? "(null)",
                included, excludeReason);
        }

        // Calculate base budget: paycheck - fixedCosts only (NO debt, NO savings)
        var baseResult = budgetEngine.CalculateBaseBudget(new BaseBudgetRequest
        {
            PaycheckAmount = request.PaycheckAmount,
            Today = today,
            NextPaycheckDate = nextPaycheck,
            TotalFixedBills = totalFixedBills
        });

        return Results.Ok(new
        {
            paycheckAmount = baseResult.PaycheckAmount,
            fixedCostsRemaining = baseResult.FixedCostsRemaining,
            baseRemaining = baseResult.BaseRemaining
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem($"Base budget calculation failed: {e.Message}");
    }
})
.WithName("GetBaseBudget")
.WithOpenApi();

// POST: /api/budget/finalize
app.MapPost("/api/budget/finalize", async (
    ApiDbContext dbContext,
    HttpContext httpContext,
    IDynamicBudgetEngine budgetEngine,
    FinalizeBudgetRequest request,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("BudgetFinalize");

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
            return Results.BadRequest("Next paycheck date must be in the future.");

        // Step 1 — Validate pay cycle using the engine
        var previousPaycheck = budgetEngine.CalculatePreviousPaycheckDate(
            request.PayDay1, request.PayDay2, nextPaycheck);

        int payCycleDays = (int)(nextPaycheck - previousPaycheck).TotalDays;
        if (payCycleDays <= 0)
            return Results.BadRequest("Invalid pay cycle detected.");

        int daysUntilNextPaycheck = (int)(nextPaycheck - today).TotalDays;
        if (daysUntilNextPaycheck < 0 || daysUntilNextPaycheck > payCycleDays)
            return Results.BadRequest("Next paycheck date is inconsistent with pay days / current date.");

        // Step 2 — Load ALL fixed costs so we can log inclusion/exclusion detail,
        // then apply the same filter logic in memory.
        var allFixedCosts = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id)
            .ToListAsync();

        // Bills filter: must have a due date in [today, nextPaycheck] and not be Savings
        var fixedBillsThisPeriod = allFixedCosts
            .Where(fc => fc.NextDueDate.HasValue
                && fc.NextDueDate.Value.Date >= today
                && fc.NextDueDate.Value.Date <= nextPaycheck
                && !string.Equals(fc.Category, "Savings", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Savings: always included per paycheck (no date filter)
        var savingsThisPeriod = allFixedCosts
            .Where(fc => string.Equals(fc.Category, "Savings", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal totalFixedBills = fixedBillsThisPeriod.Sum(fc => fc.Amount);
        decimal savingsContribution = savingsThisPeriod.Sum(fc => fc.Amount);

        // ── Log the fixed-cost filter results ──────────────────────────────────
        logger.LogInformation(
            "BudgetFinalize fixed-cost filter: userId={UserId} today={Today} nextPaycheck={NextPaycheck} " +
            "totalCosts={Total} includedBills={IncludedBills} savingsCosts={Savings} " +
            "excludedCosts={Excluded} totalFixedBills={TotalFixedBills} savingsContribution={SavingsContribution}",
            user.Id,
            today.ToString("yyyy-MM-dd"),
            nextPaycheck.ToString("yyyy-MM-dd"),
            allFixedCosts.Count,
            fixedBillsThisPeriod.Count,
            savingsThisPeriod.Count,
            allFixedCosts.Count - fixedBillsThisPeriod.Count - savingsThisPeriod.Count,
            totalFixedBills,
            savingsContribution);

        foreach (var fc in allFixedCosts)
        {
            bool isSavings = string.Equals(fc.Category, "Savings", StringComparison.OrdinalIgnoreCase);
            bool includedAsBill = fixedBillsThisPeriod.Contains(fc);
            string role = isSavings ? "savings" : includedAsBill ? "included-bill" : "excluded-bill";

            string excludeReason = (isSavings || includedAsBill) ? "" :
                !fc.NextDueDate.HasValue ? "no-due-date" :
                fc.NextDueDate.Value.Date < today ? "before-today" :
                                                    "after-next-paycheck";

            logger.LogInformation(
                "BudgetFinalize FixedCost: id={Id} name={Name} amount={Amount} category={Category} " +
                "nextDueDate={NextDueDate} role={Role} excludeReason={ExcludeReason}",
                fc.Id, fc.Name, fc.Amount, fc.Category,
                fc.NextDueDate?.ToString("yyyy-MM-dd") ?? "(null)",
                role, excludeReason);
        }
        decimal debtPerPaycheck = request.DebtPerPaycheck ?? 0m;

        // Step 3 — Delegate to the pure budget engine (NO proration)
        // Formula: remainingToSpend = paycheckAmount - fixedBills - savings - debt
        var calcRequest = new BudgetCalculationRequest
        {
            PaycheckAmount = request.PaycheckAmount,
            Today = today,
            NextPaycheckDate = nextPaycheck,
            TotalFixedBills = totalFixedBills,
            SavingsContribution = savingsContribution,
            DebtPerPaycheck = debtPerPaycheck
        };

        var result = budgetEngine.CalculateDynamicBudget(calcRequest);

        // Persist the remaining-to-spend as the user's balance
        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);
        if (balanceRecord == null)
        {
            balanceRecord = new Balance
            {
                UserId = user.Id,
                BalanceAmount = result.RemainingToSpend,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbContext.Balances.AddAsync(balanceRecord);
        }
        else
        {
            balanceRecord.BalanceAmount = result.RemainingToSpend;
            balanceRecord.UpdatedAt = DateTime.UtcNow;
        }

        // Persist user settings
        user.OnboardingComplete = true;
        user.PayDay1 = request.PayDay1;
        user.PayDay2 = request.PayDay2;
        user.ExpectedPaycheckAmount = request.PaycheckAmount;
        user.DebtPerPaycheck = request.DebtPerPaycheck;
        // Persist the Plaid debt snapshot captured during onboarding.
        // Null means the client did not send it (old app version / no Plaid connection).
        // 0 means the user had no credit card debt — both are valid and both are saved.
        if (request.DebtStartingBalance.HasValue)
            user.DebtStartingBalance = request.DebtStartingBalance.Value;

        // ── Cash-cushion onboarding fields ──────────────────────────────────
        // Only persisted when the client sent at least one cash-related field.
        // The backend recalculates using DebtSummaryCalculator so stored values
        // are always consistent and properly clamped, even if the client sends
        // an inconsistent combination (e.g. cashApplied > available).
        if (request.CashBalanceAtOnboarding.HasValue || request.CashAppliedToDebtAtOnboarding.HasValue)
        {
            var creditDebt = Math.Max(0m, request.DebtStartingBalance ?? 0m);
            var cashBalance = Math.Max(0m, request.CashBalanceAtOnboarding ?? 0m);
            var cashCushion = Math.Max(0m, request.CashCushionAtOnboarding ?? 0m);
            var requestedApplied = Math.Max(0m, request.CashAppliedToDebtAtOnboarding ?? 0m);

            var netDebtResult = DebtSummaryCalculator.CalculateNetDebt(
                creditDebt, cashBalance, cashCushion, requestedApplied);

            user.CashBalanceAtOnboarding = cashBalance;
            user.CashCushionAtOnboarding = cashCushion;
            user.CashAppliedToDebtAtOnboarding = netDebtResult.EffectiveCashApplied;
            user.NetDebtStartingBalance = netDebtResult.NetDebt;
        }

        await dbContext.SaveChangesAsync();

        // Return structured response — no proration fields
        return Results.Ok(new
        {
            message = "Setup complete.",
            paycheckAmount = result.PaycheckAmount,
            fixedCostsRemaining = result.FixedCostsRemaining,
            debtPerPaycheck = result.DebtPerPaycheck,
            savingsContribution = result.SavingsContribution,
            remainingToSpend = result.RemainingToSpend,
            // Legacy alias kept so older clients still work
            dynamicSpendableAmount = result.DynamicSpendableAmount,
            dynamicBalance = result.RemainingToSpend.ToString("0.00"),
            explanation = result.Explanation
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem($"Finalization failed: {e.Message}");
    }
})
.WithName("FinalizeBudget")
.WithOpenApi();

// POST: /api/plaid/webhook
// POST: /api/plaid/webhook
app.MapPost("/api/plaid/webhook", async (
ITransactionService transactionService,
    ApiDbContext dbContext,
    PlaidWebhookRequest requestBody,
    ILoggerFactory loggerFactory,
    IPaycheckSummaryService paycheckSummaryService) =>
{
    var logger = loggerFactory.CreateLogger("PlaidWebhook");
    var receivedAt = DateTime.UtcNow;

    // ── Log every inbound webhook immediately ────────────────────────────────
    // These console lines appear in CloudWatch even if Sentry scrubs PII.
    Console.WriteLine(
        $"[WEBHOOK] receivedAt={receivedAt:O} " +
        $"type={requestBody.WebhookType} code={requestBody.WebhookCode} itemId={requestBody.ItemId}");

    logger.LogInformation(
        "Webhook received: type={WebhookType} code={WebhookCode} itemId={ItemId} receivedAt={ReceivedAt}",
        requestBody.WebhookType,
        requestBody.WebhookCode,
        requestBody.ItemId,
        receivedAt.ToString("O"));

    SentrySdk.ConfigureScope(scope =>
    {
        scope.SetTag("webhook.type", requestBody.WebhookType ?? "unknown");
        scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
        scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
        scope.SetExtra("webhook.receivedAt", receivedAt.ToString("O"));
    });

    SentrySdk.AddBreadcrumb(
        $"Webhook received: type={requestBody.WebhookType} code={requestBody.WebhookCode} itemId={requestBody.ItemId} receivedAt={receivedAt:O}",
        level: BreadcrumbLevel.Info);

    // ── Best-effort user lookup so CloudWatch/Sentry can identify the user ───
    string userEmail = "(unknown)";
    int userId = -1;

    try
    {
        var plaidItem = await dbContext.PlaidItems
            .FirstOrDefaultAsync(p => p.ItemId == requestBody.ItemId);

        if (plaidItem != null)
        {
            logger.LogInformation(
                "Webhook PlaidItem found: itemId={ItemId} plaidItemDbId={PlaidItemDbId} " +
                "plaidUserId={PlaidUserId} cursor={Cursor}",
                requestBody.ItemId, plaidItem.Id,
                plaidItem.UserId, plaidItem.Cursor ?? "(null)");

            SentrySdk.AddBreadcrumb(
                $"Webhook PlaidItem found: itemId={requestBody.ItemId} " +
                $"plaidItemDbId={plaidItem.Id} cursor={plaidItem.Cursor ?? "(null)"}",
                level: BreadcrumbLevel.Info);

            var webhookUser = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == plaidItem.UserId);

            if (webhookUser != null)
            {
                userEmail = webhookUser.Email ?? "(no email)";
                userId = webhookUser.Id;
            }
        }
        else
        {
            logger.LogWarning(
                "Webhook PlaidItem NOT FOUND: itemId={ItemId} webhookCode={WebhookCode} — sync will fail",
                requestBody.ItemId, requestBody.WebhookCode);

            SentrySdk.CaptureMessage(
                $"Webhook PlaidItem NOT FOUND: itemId={requestBody.ItemId ?? "(null)"}",
                scope =>
                {
                    scope.Level = SentryLevel.Warning;
                    scope.SetTag("event.type", "webhook_item_not_found");
                    scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
                    scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
                });
        }

        logger.LogInformation(
            "Webhook user context resolved: type={WebhookType} code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail}",
            requestBody.WebhookType,
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail);

        Console.WriteLine(
            $"[WEBHOOK] user resolved: itemId={requestBody.ItemId} userId={userId} userEmail={userEmail}");
    }
    catch (Exception lookupEx)
    {
        logger.LogWarning(
            lookupEx,
            "Webhook user lookup failed non-fatally: itemId={ItemId}",
            requestBody.ItemId);

        Console.WriteLine(
            $"[WEBHOOK] user lookup failed: itemId={requestBody.ItemId} error={lookupEx.Message}");
    }

    // ── Sentry user context ──────────────────────────────────────────────────
    // This is cleaner than setting a tag named user.email.
    // If Sentry still filters Email, your org-level scrubbers are still winning.
    SentrySdk.ConfigureScope(scope =>
    {
        if (userId > 0)
        {
            scope.User = new SentryUser
            {
                Id = userId.ToString(),
                Email = userEmail
            };
        }

        scope.SetTag("budget.userId", userId.ToString());
        scope.SetExtra("debug.rawUserEmail", userEmail);
        scope.SetExtra("webhook.userId", userId);
        scope.SetExtra("webhook.userEmail", userEmail);
    });

    // Do NOT put userEmail directly in the Sentry message string.
    // Sentry is much more likely to scrub raw email patterns inside message text.
    SentrySdk.CaptureMessage(
        $"Webhook received: type={requestBody.WebhookType} code={requestBody.WebhookCode} itemId={requestBody.ItemId} receivedAt={receivedAt:O}",
        scope =>
        {
            scope.Level = SentryLevel.Info;
            scope.SetTag("event.type", "webhook_received");
            scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
            scope.SetTag("budget.userId", userId.ToString());
            scope.SetExtra("debug.rawUserEmail", userEmail);
        });

    // ── Filter: only TRANSACTIONS webhooks trigger sync ───────────────────────
    if (requestBody.WebhookType != "TRANSACTIONS")
    {
        logger.LogInformation(
            "Webhook ignored because type is not TRANSACTIONS: type={WebhookType} code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail}",
            requestBody.WebhookType,
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail);

        SentrySdk.AddBreadcrumb(
            $"Webhook ignored: type={requestBody.WebhookType} is not TRANSACTIONS",
            level: BreadcrumbLevel.Info);

        return Results.Ok(new { message = "Ignored non-transaction webhook." });
    }

    // ── Warn on unexpected transaction webhook codes ─────────────────────────
    var knownTransactionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "INITIAL_UPDATE",
        "HISTORICAL_UPDATE",
        "DEFAULT_UPDATE",
        "SYNC_UPDATES_AVAILABLE",
        "TRANSACTIONS_REMOVED"
    };

    if (!knownTransactionCodes.Contains(requestBody.WebhookCode ?? ""))
    {
        logger.LogWarning(
            "Webhook has unexpected transaction code, but sync will still run: type={WebhookType} code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail}",
            requestBody.WebhookType,
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail);

        SentrySdk.CaptureMessage(
            $"Unexpected Plaid transaction webhook code: {requestBody.WebhookCode ?? "unknown"}",
            scope =>
            {
                scope.Level = SentryLevel.Warning;
                scope.SetTag("event.type", "unexpected_webhook_code");
                scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
                scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
                scope.SetTag("budget.userId", userId.ToString());
                scope.SetExtra("debug.rawUserEmail", userEmail);
            });
    }

    try
    {
        logger.LogInformation(
            "Webhook triggering transaction sync: code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail}",
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail);

        SentrySdk.AddBreadcrumb(
            $"Triggering sync: code={requestBody.WebhookCode} itemId={requestBody.ItemId} userId={userId}",
            level: BreadcrumbLevel.Info);

        var syncResponse = await transactionService.SyncAndProcessTransactions(
            requestBody.ItemId, requestBody.WebhookCode);

        var addedCount = syncResponse.Added?.Count ?? 0;
        var modifiedCount = syncResponse.Modified?.Count ?? 0;
        var removedCount = syncResponse.Removed?.Count ?? 0;
        var syncDuration = DateTime.UtcNow - receivedAt;

        logger.LogInformation(
            "Webhook sync complete: code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail} added={Added} modified={Modified} removed={Removed} hasMore={HasMore} durationMs={DurationMs}",
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail,
            addedCount,
            modifiedCount,
            removedCount,
            syncResponse.HasMore,
            (int)syncDuration.TotalMilliseconds);

        Console.WriteLine(
            $"[WEBHOOK] sync complete: code={requestBody.WebhookCode} itemId={requestBody.ItemId} " +
            $"userId={userId} userEmail={userEmail} added={addedCount} modified={modifiedCount} " +
            $"removed={removedCount} hasMore={syncResponse.HasMore} durationMs={(int)syncDuration.TotalMilliseconds}");

        SentrySdk.AddBreadcrumb(
            $"Webhook sync complete: code={requestBody.WebhookCode} itemId={requestBody.ItemId} " +
            $"userId={userId} added={addedCount} modified={modifiedCount} removed={removedCount} " +
            $"hasMore={syncResponse.HasMore} durationMs={(int)syncDuration.TotalMilliseconds}",
            level: BreadcrumbLevel.Info);

        SentrySdk.CaptureMessage(
            $"Webhook sync complete: code={requestBody.WebhookCode} itemId={requestBody.ItemId}",
            scope =>
            {
                scope.Level = SentryLevel.Info;
                scope.SetTag("event.type", "webhook_sync_complete");
                scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
                scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
                scope.SetTag("budget.userId", userId.ToString());
                scope.SetExtra("debug.rawUserEmail", userEmail);
                scope.SetExtra("sync.added", addedCount);
                scope.SetExtra("sync.modified", modifiedCount);
                scope.SetExtra("sync.removed", removedCount);
                scope.SetExtra("sync.hasMore", syncResponse.HasMore);
                scope.SetExtra("sync.durationMs", (int)syncDuration.TotalMilliseconds);
            });

        // ── Paycheck Summary: schedule-based trigger ─────────────────────────
        // Fires when today is inside the ±window around a configured nominal payday.
        // Uses the nominal payday date (not today) as the PaycheckSummary key,
        // so repeated webhook calls within the same window are idempotent.
        try
        {
            await paycheckSummaryService.CreateOrUpdateSummaryIfPaycheckDayAsync(
                requestBody.ItemId ?? string.Empty,
                DateTime.UtcNow);
        }
        catch (Exception pex)
        {
            Console.Error.WriteLine($"[PaycheckSummary] Scheduled trigger error for itemId={requestBody.ItemId}: {pex.Message}");
        }

        // ── Paycheck Summary: paycheck-deposit fallback trigger ───────────────
        // Queries the DB for Paycheck transactions created during THIS sync batch
        // (scoped by UserId + SuggestedKind + CreatedAt >= receivedAt − 1 min).
        // For each distinct nominal payday not yet covered, creates/updates a summary.
        // Non-fatal — failure here must not block the webhook response.
        try
        {
            await paycheckSummaryService.TriggerSummaryForRecentPaycheckTransactionsAsync(
                requestBody.ItemId ?? string.Empty,
                receivedAt,
                DateTime.UtcNow);
        }
        catch (Exception pfEx)
        {
            Console.Error.WriteLine($"[PaycheckSummary] Fallback trigger error for itemId={requestBody.ItemId}: {pfEx.Message}");
        }

        return Results.Ok(new
        {
            message = "Webhook processed successfully",
            added = addedCount,
            modified = modifiedCount,
            removed = removedCount,
            hasMore = syncResponse.HasMore
        });
    }
    catch (Exception e)
    {
        logger.LogError(
            e,
            "Webhook processing failed: code={WebhookCode} itemId={ItemId} userId={UserId} userEmail={UserEmail}",
            requestBody.WebhookCode,
            requestBody.ItemId,
            userId,
            userEmail);

        Console.WriteLine(
            $"[WEBHOOK] FAILED: code={requestBody.WebhookCode} itemId={requestBody.ItemId} " +
            $"userId={userId} userEmail={userEmail} error={e.Message}");

        SentrySdk.CaptureException(e, scope =>
        {
            scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
            scope.SetTag("webhook.type", requestBody.WebhookType ?? "unknown");
            scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
            scope.SetTag("budget.userId", userId.ToString());
            scope.SetExtra("webhook.receivedAt", receivedAt.ToString("O"));
            scope.SetExtra("webhook.userId", userId);
            scope.SetExtra("debug.rawUserEmail", userEmail);
        });

        // Still return 200 so Plaid does not retry forever.
        return Results.Ok(new
        {
            message = "Webhook received but processing failed internally"
        });
    }
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

        // "Unexpected deposit" = a credit that is NOT a normal paycheck.
        // Paychecks are expected income — the user entered their paycheck schedule
        // and expected amount during onboarding, so the dynamic budget already
        // accounts for them. Showing paychecks in "Review New Deposits" would be
        // confusing and redundant.
        //
        // Only unexpected inflows need user review:
        //   Windfall          — large one-off credit (e.g. tax return, bonus)
        //   InternalTransfer  — bank-to-bank transfer in
        //   Refund            — merchant or card refund
        //
        // Note: Transaction.Amount is always stored as a positive absolute value
        // (Math.Abs of the Plaid raw amount), so amount sign is NOT used here.
        var depositKinds = new[]
        {
            TransactionSuggestedKind.Windfall,
            TransactionSuggestedKind.InternalTransfer,
            TransactionSuggestedKind.Refund
        };

        var pendingDeposits = await dbContext.Transactions
            .Where(t => t.UserId == user.Id
                && depositKinds.Contains(t.SuggestedKind)
                && t.UserDecision == TransactionUserDecision.Undecided
                && !t.IsLargeExpenseCandidate)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Results.Ok(pendingDeposits);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
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
                    // Refund this period's dynamic balance, but only once.
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
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("DecideOnTransaction")
.WithOpenApi();



// GET: /api/transactions/suspicious-holds
// Returns pending holds flagged as suspicious (gas, hotel, rental car) that need user review.
app.MapGet("/api/transactions/suspicious-holds", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var holds = await dbContext.Transactions
            .Where(t => t.UserId == user.Id
                && t.IsSuspiciousHold
                && t.Pending
                && !t.HoldReviewed)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Results.Ok(holds);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetSuspiciousHolds")
.WithOpenApi();

// POST: /api/transactions/{id}/hold-override
// Lets the user replace the full hold amount with their expected actual charge.
// Adjusts the dynamic balance immediately by the difference.
app.MapPost("/api/transactions/{id}/hold-override",
    async (int id, HoldOverrideRequest body, ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var tx = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
        if (tx == null) return Results.NotFound("Transaction not found.");

        if (!tx.IsSuspiciousHold)
            return Results.BadRequest("Transaction is not a suspicious hold.");

        if (!tx.Pending)
            return Results.BadRequest("Hold has already posted or been removed; no adjustment needed.");

        if (body.OverrideAmount <= 0)
            return Results.BadRequest("Override amount must be greater than zero.");

        // Compute how much was previously reserved in the budget for this hold
        decimal previouslyApplied = tx.BudgetAppliedAmount ?? tx.Amount;

        // Adjust balance: give back the difference between what was reserved and the override
        decimal delta = previouslyApplied - body.OverrideAmount;

        var balance = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);
        if (balance != null && delta != 0)
        {
            balance.BalanceAmount += delta;
            balance.UpdatedAt = DateTime.UtcNow;
        }

        // Record the override so future reversals (when the pending is removed) use it
        tx.HoldOverrideAmount = body.OverrideAmount;
        tx.BudgetAppliedAmount = body.OverrideAmount;
        tx.HoldReviewed = true;
        tx.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Hold override saved.",
            transactionId = tx.Id,
            originalAmount = tx.Amount,
            overrideAmount = tx.HoldOverrideAmount,
            balanceAdjustment = delta,
            newBalance = balance?.BalanceAmount
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("OverrideHoldAmount")
.WithOpenApi();

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
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetLargeExpenses")
.WithOpenApi();

// GET: /api/transactions/large-expenses/summary
// Returns the count and total amount of unreviewed large expenses for the authenticated user.
// Used by the HomeScreen dashboard to show a subtle warning when the dynamic budget
// includes unusually large expenses that the user has not yet categorized.
// Does NOT change any large-expense behavior — large expenses still subtract from the
// budget immediately regardless of whether they have been reviewed.
app.MapGet("/api/transactions/large-expenses/summary", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        var unreviewedLargeExpenses = await dbContext.Transactions
            .Where(t => t.UserId == user.Id && t.IsLargeExpenseCandidate && !t.LargeExpenseHandled)
            .ToListAsync();

        var count = unreviewedLargeExpenses.Count;
        var totalAmount = unreviewedLargeExpenses.Sum(t => t.Amount);

        return Results.Ok(new { count, totalAmount });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetLargeExpenseSummary")
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
        SentrySdk.CaptureException(e);
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
            SentrySdk.CaptureException(e);
            return Results.Problem(e.Message);
        }
    })
    .WithName("RegisterDeviceForNotifications")
    .WithOpenApi();


// POST: /api/transactions/{id}/mark-recurring
app.MapPost("/api/transactions/{id}/mark-recurring",
    async (int id,
           MarkRecurringFromTransactionRequest request,
           ApiDbContext dbContext,
           HttpContext httpContext) =>
{
    try
    {
        // --- Auth ---
        string? idToken = httpContext.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Split(" ")
            .Last();

        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // --- Transaction lookup ---
        var tx = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);

        if (tx == null) return Results.NotFound("Transaction not found.");

        // We only support marking outflows as recurring
        if (tx.Amount <= 0m)
        {
            return Results.BadRequest("Only outflow transactions can be marked as recurring.");
        }

        // --- Build FixedCost from this transaction ---
        var fixedCostName = tx.MerchantName ?? tx.Name ?? "Recurring charge";

        // Naive guess: next due date = one month after transaction date,
        // unless the client passed something explicit.
        var firstDue = (request.FirstDueDate ?? tx.Date).Date.AddMonths(1);

        var fixedCost = new FixedCost
        {
            UserId = user.Id,
            Name = fixedCostName,
            Amount = tx.Amount,
            Category = "Recurring",
            Type = "from_transaction",
            PlaidMerchantName = tx.MerchantName,
            PlaidAccountId = tx.AccountId,
            UserHasApproved = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            NextDueDate = firstDue
        };

        await dbContext.FixedCosts.AddAsync(fixedCost);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Transaction marked as recurring.",
            fixedCostId = fixedCost.Id,
            fixedCost.Name,
            fixedCost.Amount,
            fixedCost.NextDueDate
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("MarkTransactionAsRecurring")
.WithOpenApi();

// GET: /api/debt/snapshot
app.MapGet("/api/debt/snapshot", async (ApiDbContext dbContext, PlaidClient plaidClient, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Split(" ")
            .Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decoded = await FirebaseAdmin.Auth.FirebaseAuth
            .DefaultInstance
            .VerifyIdTokenAsync(idToken);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUuid == decoded.Uid);

        if (user == null) return Results.NotFound("User not found.");

        var items = await dbContext.PlaidItems
            .Where(p => p.UserId == user.Id)
            .ToListAsync();

        var accounts = new List<DebtSnapshotAccountDto>();
        var cashAccounts = new List<CashAccountDto>();
        decimal totalCreditCardDebt = 0m;
        decimal totalCheckingBalance = 0m;
        decimal totalSavingsBalance = 0m;

        foreach (var item in items)
        {
            var req = new AccountsGetRequest
            {
                AccessToken = item.AccessToken
            };

            var resp = await plaidClient.AccountsGetAsync(req);

            foreach (var acct in resp.Accounts)
            {
                if (acct.Type == AccountType.Credit)
                {
                    // Credit cards: current balance = outstanding balance owed (positive = debt).
                    var bal = acct.Balances.Current ?? 0m;
                    if (bal <= 0) continue; // no debt on this card

                    accounts.Add(new DebtSnapshotAccountDto
                    {
                        InstitutionName = item.InstitutionName ?? "Unknown institution",
                        AccountName = acct.Name ?? acct.OfficialName ?? "Credit account",
                        Mask = acct.Mask,
                        CurrentBalance = bal
                    });

                    totalCreditCardDebt += bal;
                }
                else if (acct.Type == AccountType.Depository)
                {
                    // Checking/savings: prefer available balance (spendable funds);
                    // fall back to current if available is null.
                    // Negative balances (overdrafts) are clamped to 0 — an overdrawn account
                    // does not contribute positively to available cash.
                    var rawBal = acct.Balances.Available ?? acct.Balances.Current ?? 0m;
                    var bal = Math.Max(0m, rawBal);
                    var subType = acct.Subtype?.ToString()?.ToLowerInvariant() ?? "";

                    cashAccounts.Add(new CashAccountDto
                    {
                        InstitutionName = item.InstitutionName ?? "Unknown institution",
                        AccountName = acct.Name ?? acct.OfficialName ?? "Depository account",
                        Mask = acct.Mask,
                        SubType = subType,
                        Balance = bal
                    });

                    if (subType == "savings")
                        totalSavingsBalance += bal;
                    else
                        totalCheckingBalance += bal; // checking, money market, cd, etc.
                }
            }
        }

        var totalCashBalance = totalCheckingBalance + totalSavingsBalance;

        var result = new DebtSnapshotResponse
        {
            TotalCreditCardDebt = totalCreditCardDebt,
            TotalCheckingBalance = totalCheckingBalance,
            TotalSavingsBalance = totalSavingsBalance,
            TotalCashBalance = totalCashBalance,
            Accounts = accounts,
            CashAccounts = cashAccounts
        };

        return Results.Ok(result);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetDebtSnapshot")
.WithOpenApi();




// GET: /api/debt/summary
// Returns the stored debt payoff plan summary for the authenticated user.
// Reads only from our database — no Plaid API call is made.
// Used by the HomeScreen dashboard to display a lightweight payoff estimate.
// If DebtStartingBalance is null (old user / no Plaid at onboarding time), totalDebt
// is returned as null and the dashboard card is hidden.
app.MapGet("/api/debt/summary", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Split(" ")
            .Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decoded = await FirebaseAdmin.Auth.FirebaseAuth
            .DefaultInstance
            .VerifyIdTokenAsync(idToken);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUuid == decoded.Uid);

        if (user == null) return Results.NotFound("User not found.");

        // Use netDebtStartingBalance (remaining debt after cash was applied at onboarding) when
        // available; fall back to raw DebtStartingBalance for backward compatibility.
        // This ensures old users who never had the cash-cushion feature still see their estimate.
        var debtForEstimate = user.NetDebtStartingBalance ?? user.DebtStartingBalance;

        // Calculate how many paychecks remain to pay off the debt.
        // Uses the pure static helper so the formula is independently testable.
        var paychecksRemaining = DebtSummaryCalculator.CalculatePaychecksRemaining(
            debtForEstimate, user.DebtPerPaycheck);

        return Results.Ok(new
        {
            // null when old user / no Plaid snapshot captured → frontend hides the card
            totalDebt = user.DebtStartingBalance,
            debtPerPaycheck = user.DebtPerPaycheck,
            paychecksRemaining,
            // ── Cash-cushion fields (null for users who onboarded before this feature) ──
            // netDebtStartingBalance: what the payoff estimate is actually calculated from.
            // If null, the estimate uses totalDebt (backward-compatible).
            netDebtStartingBalance = user.NetDebtStartingBalance,
            cashAppliedAtOnboarding = user.CashAppliedToDebtAtOnboarding,
            cashBalanceAtOnboarding = user.CashBalanceAtOnboarding,
            cashCushionAtOnboarding = user.CashCushionAtOnboarding
        });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetDebtSummary")
.WithOpenApi();

// ─── AUDIT ENDPOINT ──────────────────────────────────────────────────────────
// GET /api/admin/debug/users/{userId}/transaction-balance-audit
//
// READ-ONLY: no writes to any table. Requires X-Debug-Secret header.
// Secret is read from config key "Debug:Secret" or env var DEBUG_SECRET.
// Returns 401 if header is missing, wrong, or if no secret is configured.
// ─────────────────────────────────────────────────────────────────────────────
app.MapGet("/api/admin/debug/users/{userId}/transaction-balance-audit",
    async (int userId, HttpContext httpContext, ApiDbContext dbContext, IConfiguration config,
           string? format,
           string? from,
           string? to,
           bool includeBeforeRegistration = false,
           bool includeNoImpact = false) =>
    {
        // ── 1. Security gate ──────────────────────────────────────────────────
        var configuredSecret =
            config["Debug:Secret"] ??
            System.Environment.GetEnvironmentVariable("DEBUG_SECRET");

        // Deny outright if no secret is configured — prevents accidental exposure.
        if (string.IsNullOrWhiteSpace(configuredSecret))
            return Results.Unauthorized();

        var providedSecret = httpContext.Request.Headers["X-Debug-Secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedSecret) ||
            !string.Equals(providedSecret, configuredSecret, StringComparison.Ordinal))
            return Results.Unauthorized();

        // ── 1b. Parse from/to date filters ────────────────────────────────────
        // Both are optional YYYY-MM-DD inclusive bounds. Invalid values → 400.
        DateOnly? fromDate = null;
        DateOnly? toDate = null;

        if (!string.IsNullOrEmpty(from))
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedFrom))
                return Results.BadRequest(new
                {
                    error = $"Invalid 'from' value '{from}'. Expected YYYY-MM-DD (e.g. 2026-05-01)."
                });
            fromDate = parsedFrom;
        }

        if (!string.IsNullOrEmpty(to))
        {
            if (!DateOnly.TryParseExact(to, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedTo))
                return Results.BadRequest(new
                {
                    error = $"Invalid 'to' value '{to}'. Expected YYYY-MM-DD (e.g. 2026-05-31)."
                });
            toDate = parsedTo;
        }

        // ── 2. Load user ──────────────────────────────────────────────────────
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Results.NotFound(new { error = $"User {userId} not found." });

        // ── 3. Load balance (single row per user) ─────────────────────────────
        var balance = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == userId);

        // ── 4. Load all transactions, sorted oldest-first ────────────────────
        var transactions = await dbContext.Transactions
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync();

        // ── 5. Load all fixed costs for matching ─────────────────────────────
        var fixedCosts = await dbContext.FixedCosts
            .Where(fc => fc.UserId == userId)
            .ToListAsync();

        // ── 6. Per-transaction audit ──────────────────────────────────────────
        // Running balance starts at $0. The onboarding balance is NOT included
        // because it is not recorded in the transaction history. We derive the
        // implied initial balance from: storedBalance - netTransactionEffect.
        // USD formatter used by the simple format branch.
        static string Usd(decimal v) =>
            v.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

        decimal runningBalance = 0m;

        var auditRows = new List<object>();
        var simpleRowsData = new List<AuditSimpleRowData>();

        int totalPending = 0;
        int totalPosted = 0;
        int totalBeforeReg = 0;
        int totalDepositsCountedAsIncome = 0;
        int totalVariableSpend = 0;
        int totalFixedCostMatches = 0;
        int totalLargeExpenseHandled = 0;
        int totalNoImpact = 0;

        var userRegisteredDate = DateOnly.FromDateTime(user.CreatedAt.ToUniversalTime());

        foreach (var tx in transactions)
        {
            if (tx.Pending) totalPending++; else totalPosted++;

            // ── Deposit classification ────────────────────────────────────────
            bool isConsideredDeposit =
                tx.SuggestedKind != TransactionSuggestedKind.Unknown;

            // ── Large expense classification ──────────────────────────────────
            bool isConsideredLargeExpense = tx.IsLargeExpenseCandidate;

            // ── Fixed-cost / recurring match (same logic as TransactionService)─
            string? merchantName = tx.MerchantName ?? tx.Name;
            FixedCost? matchedFc = fixedCosts.FirstOrDefault(fc =>
                !string.IsNullOrEmpty(fc.PlaidMerchantName) &&
                fc.PlaidMerchantName.Equals(merchantName, StringComparison.OrdinalIgnoreCase));

            bool isConsideredRecurring = matchedFc != null;

            // ── Before-registration check ─────────────────────────────────────
            var txDateOnly = DateOnly.FromDateTime(tx.Date.Date);
            bool isBeforeUserRegistration = txDateOnly < userRegisteredDate;
            if (isBeforeUserRegistration) totalBeforeReg++;

            // ── Net effect on dynamic balance ─────────────────────────────────
            // Priority order:
            //   1. Deposit counted as income  → +Amount
            //   2. Large expense refunded     → net $0 (BudgetApplied was reversed)
            //   3. BudgetAppliedAmount set    → -BudgetAppliedAmount
            //   4. Everything else            → $0

            decimal effect = 0m;
            string reason;

            if (isBeforeUserRegistration)
            {
                effect = 0m;
                reason = "before-user-registration";
                totalNoImpact++;
            }
            else if (isConsideredDeposit)
            {
                if (tx.CountedAsIncome)
                {
                    effect = tx.Amount;
                    reason = $"deposit-counted-as-income (suggestedKind={tx.SuggestedKind})";
                    totalDepositsCountedAsIncome++;
                }
                else
                {
                    effect = 0m;
                    reason = $"deposit-not-counted (decision={tx.UserDecision})";
                    totalNoImpact++;
                }
            }
            else if (tx.IsLargeExpenseCandidate && tx.LargeExpenseHandled &&
                     (tx.UserDecision == TransactionUserDecision.LargeExpenseFromSavings ||
                      tx.UserDecision == TransactionUserDecision.LargeExpenseToFixedCost))
            {
                // The balance was first debited (BudgetAppliedAmount), then refunded (+Amount).
                // Net = -BudgetApplied + Amount. If they're equal (the common case) net = $0.
                decimal applied = tx.BudgetAppliedAmount ?? 0m;
                effect = tx.Amount - applied; // typically $0
                reason = $"large-expense-refunded (decision={tx.UserDecision}, applied={applied:0.00}, refunded={tx.Amount:0.00})";
                totalLargeExpenseHandled++;
            }
            else if (tx.IsLargeExpenseCandidate && tx.LargeExpenseHandled &&
                     tx.UserDecision == TransactionUserDecision.TreatAsVariableSpend)
            {
                // User chose to keep it as normal spend — BudgetAppliedAmount is the impact.
                effect = -(tx.BudgetAppliedAmount ?? 0m);
                reason = $"large-expense-treated-as-variable-spend (applied={tx.BudgetAppliedAmount:0.00})";
                totalVariableSpend++;
            }
            else if (isConsideredRecurring && tx.BudgetAppliedAmount == null)
            {
                effect = 0m;
                reason = $"matched-fixed-cost — excluded from dynamic budget (fixedCost='{matchedFc!.Name}' id={matchedFc.Id})";
                totalFixedCostMatches++;
            }
            else if (tx.BudgetAppliedAmount.HasValue)
            {
                effect = -tx.BudgetAppliedAmount.Value;
                reason = tx.Pending
                    ? $"pending-variable-spend (budgetApplied={tx.BudgetAppliedAmount.Value:0.00})"
                    : $"variable-spend (budgetApplied={tx.BudgetAppliedAmount.Value:0.00})";
                totalVariableSpend++;
            }
            else
            {
                effect = 0m;
                string noImpactDetail =
                    tx.Pending ? "pending — no budget impact recorded yet" :
                    isConsideredRecurring ? "matched fixed-cost but BudgetAppliedAmount already null" :
                    "no budget impact (not a deposit, not variable spend)";
                reason = $"no-impact: {noImpactDetail}";
                totalNoImpact++;
            }

            decimal balanceBefore = runningBalance;
            runningBalance += effect;
            decimal balanceAfter = runningBalance;

            // ── Parallel simple-format row ────────────────────────────────────
            simpleRowsData.Add(new AuditSimpleRowData(
                tx.Id, tx.Date, tx.MerchantName, tx.Name ?? string.Empty,
                tx.Amount, tx.Pending, isBeforeUserRegistration, isConsideredRecurring,
                Math.Round(balanceBefore, 2), Math.Round(effect, 2), Math.Round(balanceAfter, 2),
                reason));

            // ── Matched fixed cost summary (no access tokens) ─────────────────
            object? matchedFcSummary = matchedFc == null ? null : new
            {
                id = matchedFc.Id,
                name = matchedFc.Name,
                amount = matchedFc.Amount,
                category = matchedFc.Category,
                type = matchedFc.Type,
                nextDueDate = matchedFc.NextDueDate?.ToString("yyyy-MM-dd"),
                plaidMerchantName = matchedFc.PlaidMerchantName,
                plaidAccountId = matchedFc.PlaidAccountId
            };

            auditRows.Add(new
            {
                // ── Identity ──
                id = tx.Id,
                plaidTransactionId = tx.PlaidTransactionId,
                name = tx.Name,
                merchantName = tx.MerchantName,
                amount = tx.Amount,
                accountId = tx.AccountId,

                // ── Timing / status ──
                date = tx.Date.ToString("yyyy-MM-dd"),
                pending = tx.Pending,
                createdAt = tx.CreatedAt.ToString("O"),
                updatedAt = tx.UpdatedAt.ToString("O"),
                isBeforeUserRegistration,

                // ── Decision flags ──
                suggestedKind = tx.SuggestedKind.ToString(),
                userDecision = tx.UserDecision.ToString(),
                countedAsIncome = tx.CountedAsIncome,
                isLargeExpenseCandidate = tx.IsLargeExpenseCandidate,
                largeExpenseHandled = tx.LargeExpenseHandled,
                isSuspiciousHold = tx.IsSuspiciousHold,
                holdReviewed = tx.HoldReviewed,
                holdOverrideAmount = tx.HoldOverrideAmount,
                budgetAppliedAmount = tx.BudgetAppliedAmount,

                // ── Classification ──
                isConsideredDeposit,
                isConsideredVariableSpend = !isConsideredDeposit && tx.BudgetAppliedAmount.HasValue,
                isConsideredLargeExpense,
                isConsideredRecurring,
                matchedFixedCost = matchedFcSummary,

                // ── Balance reconstruction ──
                balanceBefore = Math.Round(balanceBefore, 2),
                effect = Math.Round(effect, 2),
                balanceAfter = Math.Round(balanceAfter, 2),
                reason,
                reconstructionNote = "reconstructed-estimate — excludes onboarding initial balance; see impliedInitialBalance in summary"
            });
        }

        // ── 7. Build summary ──────────────────────────────────────────────────
        decimal storedBalance = balance?.BalanceAmount ?? 0m;
        decimal netTransactionEffect = Math.Round(runningBalance, 2);
        // impliedInitial = what the balance must have been right after onboarding
        // for the stored balance to be correct:
        //   storedBalance = impliedInitial + netTransactionEffect
        //   impliedInitial = storedBalance - netTransactionEffect
        decimal impliedInitialBalance = Math.Round(storedBalance - netTransactionEffect, 2);

        var summary = new
        {
            totalTransactions = transactions.Count,
            totalPending,
            totalPosted,
            totalBeforeUserRegistration = totalBeforeReg,
            totalDepositsCountedAsIncome,
            totalVariableSpend,
            totalFixedCostMatches,
            totalLargeExpenseHandled,
            totalNoImpact,
            netTransactionEffect,
            storedBalance,
            impliedInitialBalance,
            reconstructedEndingBalanceFromTransactionsOnly = netTransactionEffect,
            storedVsReconstructedDiff = Math.Round(storedBalance - netTransactionEffect, 2),
            balanceLastUpdated = balance?.UpdatedAt.ToString("O") ?? "(no balance record)",
            reconstructionLimitations = new[]
            {
                "1. The initial onboarding balance (set by /api/budget/finalize) is not stored in transaction history. " +
                "impliedInitialBalance = storedBalance - netTransactionEffect.",
                "2. Transactions removed by Plaid are hard-deleted. Their reversed impact is not visible here. " +
                "This can cause storedVsReconstructedDiff to be non-zero.",
                "3. Large-expense refunds are estimated: effect = Amount - BudgetAppliedAmount. " +
                "If the user changed a decision multiple times, the balance history is not reconstructable."
            }
        };

        // ── 8. Simple format branch ───────────────────────────────────────────
        if (string.Equals(format, "simple", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Apply from/to date range first (balance already accumulated for ALL rows,
                // so preTransactionDynamicBudget is correct even for the first row in range)
                var windowedRows = simpleRowsData.AsEnumerable();
                if (fromDate.HasValue)
                    windowedRows = windowedRows.Where(r =>
                        DateOnly.FromDateTime(r.Date.Date) >= fromDate.Value);
                if (toDate.HasValue)
                    windowedRows = windowedRows.Where(r =>
                        DateOnly.FromDateTime(r.Date.Date) <= toDate.Value);

                var windowedList = windowedRows.ToList();

                // Hidden counts computed inside the date window, before the hide filters
                int hiddenBeforeReg = !includeBeforeRegistration
                    ? windowedList.Count(r => r.IsBeforeUserRegistration)
                    : 0;
                int hiddenNoImpact = !includeNoImpact
                    ? windowedList.Count(r => r.Effect == 0m && !r.IsBeforeUserRegistration)
                    : 0;

                // Apply hide filters
                var displayedRows = windowedList.AsEnumerable();
                if (!includeBeforeRegistration)
                    displayedRows = displayedRows.Where(r => !r.IsBeforeUserRegistration);
                if (!includeNoImpact)
                    displayedRows = displayedRows.Where(r => r.Effect != 0m);

                var simpleList = displayedRows.ToList();

                // ── Empty-result guard: return 200 with null budgets, no transactions ──
                if (simpleList.Count == 0)
                {
                    return Results.Ok(new
                    {
                        generatedAt = DateTime.UtcNow.ToString("O"),
                        user = user.Email,
                        summary = new
                        {
                            transactionCount = 0,
                            startingDynamicBudget = (string?)null,
                            endingDynamicBudget = (string?)null,
                            hiddenBeforeRegistrationCount = hiddenBeforeReg,
                            hiddenNoImpactCount = hiddenNoImpact
                        },
                        transactions = Array.Empty<object>()
                    });
                }

                // simpleList.Count > 0 from here — First() and Last() are safe
                var simpleRows = simpleList.Select(r => new
                {
                    user = user.Email,
                    preTransactionDynamicBudget = Usd(r.BalanceBefore),
                    date = r.Date.ToString("MM/dd/yyyy"),
                    vendor = r.MerchantName ?? (string.IsNullOrEmpty(r.Name) ? "Unknown vendor" : r.Name),
                    charge = Usd(r.Amount),
                    status = r.Pending ? "Pending" : "Complete",
                    isRecurring = r.IsConsideredRecurring,
                    postTransactionDynamicBudget = Usd(r.BalanceAfter),
                    reason = r.Reason
                }).ToList();

                return Results.Ok(new
                {
                    generatedAt = DateTime.UtcNow.ToString("O"),
                    user = user.Email,
                    summary = new
                    {
                        transactionCount = simpleList.Count,
                        startingDynamicBudget = Usd(simpleList.First().BalanceBefore),
                        endingDynamicBudget = Usd(simpleList.Last().BalanceAfter),
                        hiddenBeforeRegistrationCount = hiddenBeforeReg,
                        hiddenNoImpactCount = hiddenNoImpact
                    },
                    transactions = simpleRows
                });
            }
            catch (Exception simpleEx)
            {
                SentrySdk.CaptureMessage(
                    $"audit simple-format error: userId={userId} " +
                    $"from={from ?? "(null)"} to={to ?? "(null)"}: " +
                    $"{simpleEx.GetType().Name}: {simpleEx.Message}",
                    scope =>
                    {
                        scope.Level = SentryLevel.Error;
                        scope.SetTag("event.type", "audit_simple_format_error");
                        scope.SetTag("audit.userId", userId.ToString());
                        scope.SetExtra("audit.from", from ?? "(null)");
                        scope.SetExtra("audit.to", to ?? "(null)");
                        scope.SetExtra("audit.includeBeforeRegistration", includeBeforeRegistration);
                        scope.SetExtra("audit.includeNoImpact", includeNoImpact);
                    });

                return Results.Problem(
                    $"Error generating simple audit (userId={userId}): " +
                    $"{simpleEx.GetType().Name}. Check Sentry event.type=audit_simple_format_error.");
            }
        }

        // ── 9. Full / debug format (default) ─────────────────────────────────
        return Results.Ok(new
        {
            generatedAt = DateTime.UtcNow.ToString("O"),
            user = new
            {
                id = user.Id,
                email = user.Email,
                name = user.Name,
                createdAt = user.CreatedAt.ToString("O"),
                onboardingComplete = user.OnboardingComplete,
                payDay1 = user.PayDay1,
                payDay2 = user.PayDay2,
                expectedPaycheckAmount = user.ExpectedPaycheckAmount,
                debtPerPaycheck = user.DebtPerPaycheck
                // firebaseUuid intentionally omitted
            },
            summary,
            transactions = auditRows
        });
    })
    .WithName("TransactionBalanceAudit");

app.Lifetime.ApplicationStarted.Register(() =>
{
    SentrySdk.AddBreadcrumb("BOOT: ApplicationStarted fired", level: BreadcrumbLevel.Info);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    SentrySdk.AddBreadcrumb("BOOT: ApplicationStopping fired", level: BreadcrumbLevel.Info);
});

app.Lifetime.ApplicationStopped.Register(() =>
{
    SentrySdk.AddBreadcrumb("BOOT: ApplicationStopped fired", level: BreadcrumbLevel.Info);
});

SentrySdk.AddBreadcrumb("BOOT: before app.Run()", level: BreadcrumbLevel.Info);

// ─── Paycheck Summary ─────────────────────────────────────────────────────────
app.MapGet("/api/paycheck-summary/current", async (HttpContext http, ApiDbContext db) =>
{
    var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId == null) return Results.Unauthorized();

    var summary = await db.PaycheckSummaries
        .Where(s => s.UserId == int.Parse(userId) && !s.IsDismissed)
        .OrderByDescending(s => s.PaycheckDate)
        .FirstOrDefaultAsync();

    if (summary == null) return Results.NoContent();

    return Results.Ok(new
    {
        summary.Id,
        summary.UserId,
        PaycheckDate = summary.PaycheckDate.ToString("yyyy-MM-dd"),
        PeriodStartDate = summary.PeriodStartDate.ToString("yyyy-MM-dd"),
        PeriodEndDate = summary.PeriodEndDate.ToString("yyyy-MM-dd"),
        NextPaycheckDate = summary.NextPaycheckDate.ToString("yyyy-MM-dd"),
        summary.PaycheckAmount,
        summary.PriorPeriodStartingBudget,
        summary.PriorPeriodSpend,
        summary.PriorPeriodRemaining,
        summary.WasUnderBudget,
        summary.LeftoverAmount,
        summary.OverBudgetAmount,
        summary.FixedCostsUntilNextPaycheck,
        summary.SavingsContribution,
        summary.DebtPaymentAmount,
        summary.RecommendedDebtPaymentAmount,
        summary.NewDynamicBudgetAmount,
        summary.UserDecision,
        summary.IsDismissed,
        summary.CreatedAt,
        summary.UpdatedAt
    });
});

app.MapPost("/api/paycheck-summary/{id}/decision", async (
    HttpContext http,
    ApiDbContext db,
    int id,
    [Microsoft.AspNetCore.Mvc.FromBody] PaycheckDecisionRequest req) =>
{
    var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId == null) return Results.Unauthorized();

    var summary = await db.PaycheckSummaries
        .FirstOrDefaultAsync(s => s.Id == id && s.UserId == int.Parse(userId));

    if (summary == null) return Results.NotFound();

    var validDecisions = new[] { "AddToBudget", "TransferToSavings", "ExtraDebtPayment", "KeepAsBuffer", "Dismiss" };
    if (!validDecisions.Contains(req.Decision))
        return Results.BadRequest($"Unknown decision '{req.Decision}'.");

    summary.UserDecision = req.Decision;
    summary.UpdatedAt = DateTime.UtcNow;
    if (req.Decision == "Dismiss") summary.IsDismissed = true;

    await db.SaveChangesAsync();
    return Results.Ok(new { summary.Id, summary.UserDecision, summary.IsDismissed });
});


// ─────────────────────────────────────────────────────────────────────────────
// POST /api/admin/debug/users/{userId}/reconcile-fixed-costs?dryRun=true
//
// ADMIN / DEBUG ONLY.  Gated by the same X-Debug-Secret header as the
// transaction-balance-audit endpoint.  Default is dryRun=true — writes are
// only applied when the caller explicitly passes ?dryRun=false.
//
// Purpose: re-evaluate existing stored transactions for user {userId} against
// their current fixed costs using FixedCostMatcher.  Transactions that now
// match a fixed cost but were previously charged against the dynamic budget
// will have their BudgetAppliedAmount cleared and the balance restored.
//
// Safety rules (all applied together):
//   - Deposits (SuggestedKind != Unknown) are never touched.
//   - Large expenses that have already been handled (LargeExpenseHandled=true) are skipped.
//   - Suspicious holds that have been reviewed (HoldReviewed=true) are skipped.
//   - Transactions the user has manually decided (UserDecision != Undecided) are skipped.
//   - Only transactions with BudgetAppliedAmount != null are candidates.
//   - Entire operation is wrapped in a DB transaction; rolled back on any error.
//   - Idempotent: re-running on already-reconciled rows is a no-op.
// ─────────────────────────────────────────────────────────────────────────────
app.MapPost("/api/admin/debug/users/{userId}/reconcile-fixed-costs",
    async (int userId, HttpContext httpContext, ApiDbContext dbContext, IConfiguration config,
           bool dryRun = true) =>
{
    // ── Auth gate (same as audit endpoint) ───────────────────────────────────
    var providedSecret = httpContext.Request.Headers["X-Debug-Secret"].FirstOrDefault();
    var expectedSecret = config["Debug:Secret"]
                         ?? System.Environment.GetEnvironmentVariable("DEBUG_SECRET");

    if (string.IsNullOrWhiteSpace(providedSecret) ||
        string.IsNullOrWhiteSpace(expectedSecret) ||
        !string.Equals(providedSecret, expectedSecret, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    // ── Load user ─────────────────────────────────────────────────────────────
    var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null) return Results.NotFound($"User {userId} not found.");

    // ── Load fixed costs ──────────────────────────────────────────────────────
    var fixedCosts = await dbContext.FixedCosts
        .Where(fc => fc.UserId == userId)
        .ToListAsync();

    // ── Load balance ──────────────────────────────────────────────────────────
    var balance = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == userId);

    // ── Candidate transactions ────────────────────────────────────────────────
    // Only debits that were charged to the budget and haven't been manually resolved.
    var candidates = await dbContext.Transactions
        .Where(t =>
            t.UserId == userId &&
            t.BudgetAppliedAmount != null &&          // was charged against budget
            t.SuggestedKind == TransactionSuggestedKind.Unknown && // debit (not a deposit)
            !t.LargeExpenseHandled &&                 // not an already-handled large expense
            !t.HoldReviewed &&                        // not a hold the user has reviewed
            t.UserDecision == TransactionUserDecision.Undecided) // user hasn't overridden it
        .OrderBy(t => t.Date)
        .ToListAsync();

    // ── Reconcile ─────────────────────────────────────────────────────────────
    var matchedRows = new List<object>();
    var skippedRows = new List<object>();
    decimal totalRestored = 0m;

    foreach (var tx in candidates)
    {
        string txMerchant = tx.MerchantName ?? tx.Name ?? "(unknown)";

        var (matchedFc, matchType) = FixedCostMatcher.TryMatch(
            fixedCosts, txMerchant, tx.Amount, tx.Date);

        if (matchedFc != null)
        {
            decimal restored = tx.BudgetAppliedAmount!.Value;
            totalRestored += restored;

            matchedRows.Add(new
            {
                transactionId = tx.Id,
                plaidTransactionId = tx.PlaidTransactionId,
                merchantName = txMerchant,
                amount = tx.Amount,
                date = tx.Date.ToString("yyyy-MM-dd"),
                matchedFixedCostId = matchedFc.Id,
                matchedFixedCostName = matchedFc.Name,
                matchType,
                budgetAppliedAmountRestored = restored,
            });

            if (!dryRun)
            {
                // Restore the balance
                if (balance != null)
                {
                    balance.BalanceAmount += restored;
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                // Clear the budget impact
                tx.BudgetAppliedAmount = null;
                tx.UpdatedAt = DateTime.UtcNow;

                // Enrich the fixed cost for faster future matching
                if (string.IsNullOrEmpty(matchedFc.PlaidMerchantName) &&
                    !string.IsNullOrEmpty(tx.MerchantName))
                {
                    matchedFc.PlaidMerchantName = tx.MerchantName;
                    matchedFc.PlaidAccountId = tx.AccountId;
                }
            }
        }
        else
        {
            // Calculate why it was skipped (for diagnostic output)
            decimal tol = Math.Max(FixedCostMatcher.AbsoluteTolerance,
                                   tx.Amount * FixedCostMatcher.RelativeTolerance);
            skippedRows.Add(new
            {
                transactionId = tx.Id,
                merchantName = txMerchant,
                amount = tx.Amount,
                date = tx.Date.ToString("yyyy-MM-dd"),
                skipReason = $"no-fixed-cost-match (tolerance=${tol:0.00})",
            });
        }
    }

    // ── Commit (only when not dry-run) ────────────────────────────────────────
    if (!dryRun && matchedRows.Count > 0)
    {
        await dbContext.SaveChangesAsync();
    }

    return Results.Ok(new
    {
        userId,
        dryRun,
        candidatesConsidered = candidates.Count,
        matchedCount = matchedRows.Count,
        totalAmountRestored = dryRun ? totalRestored : totalRestored, // same in both modes
        appliedToDb = !dryRun && matchedRows.Count > 0,
        matched = matchedRows,
        skipped = skippedRows,
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// GET /api/transactions/deposits/pending/summary
//
// Authenticated — returns { count, totalAmount } for undecided unexpected
// deposits belonging to the current user.
//
// "Unexpected deposit" = Windfall | InternalTransfer | Refund.
// Paycheck is intentionally excluded — it is expected income that was already
// planned during onboarding.  Showing paychecks here would be misleading.
//
// No amount-sign logic is used.  Transaction.Amount is always stored as a
// positive absolute value by TransactionService (Math.Abs of Plaid raw amount).
// ─────────────────────────────────────────────────────────────────────────────
app.MapGet("/api/transactions/deposits/pending/summary", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);
        if (user == null) return Results.NotFound("User not found.");

        // Same three unexpected-deposit kinds used by GET /deposits/pending.
        // Paycheck is deliberately absent — it is expected and already planned.
        var unexpectedDepositKinds = new[]
        {
            TransactionSuggestedKind.Windfall,
            TransactionSuggestedKind.InternalTransfer,
            TransactionSuggestedKind.Refund
        };

        var pendingUnexpectedDeposits = await dbContext.Transactions
            .Where(t => t.UserId == user.Id
                && unexpectedDepositKinds.Contains(t.SuggestedKind)
                && t.UserDecision == TransactionUserDecision.Undecided
                && !t.IsLargeExpenseCandidate)
            .ToListAsync();

        var count = pendingUnexpectedDeposits.Count;
        var totalAmount = pendingUnexpectedDeposits.Sum(t => t.Amount);

        return Results.Ok(new { count, totalAmount });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetUnexpectedDepositSummary")
.WithOpenApi();

// ─────────────────────────────────────────────────────────────────────────────
// GET /api/recurring/suggestions
//
// Returns likely recurring fixed costs detected from the user's stored
// transaction history over the last 6 months.
//
// This endpoint is completely independent of Plaid recurring streams.
// It does NOT call GET /api/plaid/recurring and does NOT require Plaid
// recurring data to exist.  GET /api/plaid/recurring is still available
// as a separate endpoint and is intentionally left unchanged.
//
// Auth:  Firebase bearer token (same pattern as all other endpoints).
// Query: last 6 months, non-pending transactions, all SuggestedKind values
//        (income exclusion is handled inside RecurringSuggestionsAnalyzer).
// Returns: List<RecurringSuggestionDto> — DTOs only, never EF entities.
// ─────────────────────────────────────────────────────────────────────────────
app.MapGet("/api/recurring/suggestions", async (ApiDbContext dbContext, HttpContext httpContext) =>
{
    try
    {
        string? idToken = httpContext.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Split(" ")
            .Last();

        if (string.IsNullOrEmpty(idToken)) return Results.Unauthorized();

        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
            .VerifyIdTokenAsync(idToken);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUuid == decodedToken.Uid);

        if (user == null) return Results.NotFound("User not found.");

        // Look back 6 months from today (UTC).
        // We load ALL non-pending transactions — no SuggestedKind filter here.
        // RecurringSuggestionsAnalyzer excludes income/deposits itself by
        // inspecting SuggestedKind, so pre-filtering would risk throwing away
        // valid recurring debit transactions before the analyzer sees them.
        DateTime cutoff = DateTime.UtcNow.Date.AddMonths(-6);

        var transactions = await dbContext.Transactions
            .Where(t => t.UserId == user.Id
                && !t.Pending
                && t.Date >= cutoff)
            .OrderBy(t => t.Date)
            .ToListAsync();

        var suggestions = RecurringSuggestionsAnalyzer.Analyze(transactions, cutoff);

        return Results.Ok(suggestions);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        return Results.Problem(e.Message);
    }
})
.WithName("GetRecurringSuggestions")
.WithOpenApi();

// ─────────────────────────────────────────────────────────────────────────────
// GET /api/admin/debug/users/{userId}/recurring-suggestions-audit
//
// Admin/debug-only endpoint that exposes full per-group diagnostic data from
// RecurringSuggestionsAnalyzer for a specific user.  Use this to understand
// exactly why expected recurring charges (Netflix, Max, Spotify, etc.) were
// included or rejected by the suggestions pipeline.
//
// Security: X-Debug-Secret header must match DEBUG_SECRET env var / config.
//           Returns 401 if header is missing, wrong, or no secret is configured.
//
// Does NOT call Plaid.
// Does NOT modify GET /api/plaid/recurring.
// Does NOT modify GET /api/recurring/suggestions.
// Does NOT create migrations.
// Returns only DTOs / anonymous objects — never EF navigation properties.
//
// Response structure:
//   {
//     userId, generatedAt, cutoffDate,
//     summary: { allTransactionsLoaded, pendingExcludedCount, outflowCount,
//                inflowOrZeroCount, groupCount, recurringSuggestionCount },
//     suggestions:  [ /* same as public endpoint */ ],
//     groups:       [ /* RecurringSuggestionAuditGroupDto per group */ ],
//     watchList:    { terms, matches }   // scans ALL loaded txs including pending
//   }
// ─────────────────────────────────────────────────────────────────────────────
app.MapGet("/api/admin/debug/users/{userId}/recurring-suggestions-audit",
    async (int userId, HttpContext httpContext, ApiDbContext dbContext, IConfiguration config) =>
    {
        // ── 1. Security gate (same pattern as other admin/debug endpoints) ─────
        var configuredSecret =
            config["Debug:Secret"] ??
            System.Environment.GetEnvironmentVariable("DEBUG_SECRET");

        if (string.IsNullOrWhiteSpace(configuredSecret))
            return Results.Unauthorized();

        var providedSecret = httpContext.Request.Headers["X-Debug-Secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedSecret) ||
            !string.Equals(providedSecret, configuredSecret, StringComparison.Ordinal))
            return Results.Unauthorized();

        // ── 2. Load user ───────────────────────────────────────────────────────
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Results.NotFound(new { error = $"User {userId} not found." });

        // ── 3. Load all transactions for this user from the last 6 months ──────
        // Same window as the public endpoint.
        // Load ALL (including pending) so the watch-list section can tell the caller
        // whether the transactions exist at all before worrying about grouping.
        DateTime cutoff = DateTime.UtcNow.Date.AddMonths(-6);

        var allTransactions = await dbContext.Transactions
            .Where(t => t.UserId == userId && t.Date >= cutoff)
            .OrderBy(t => t.Date)
            .ToListAsync();

        // Separate pending vs non-pending for summary counts
        var nonPending = allTransactions.Where(t => !t.Pending).ToList();
        int pendingExcludedCount = allTransactions.Count - nonPending.Count;
        int outflowCount = nonPending.Count(t => t.Amount > 0);
        int inflowOrZero = nonPending.Count(t => t.Amount <= 0);

        // ── 4. Run the same Analyze() used by the public endpoint ──────────────
        // (non-pending only, same cutoff)
        var suggestions = RecurringSuggestionsAnalyzer.Analyze(nonPending, cutoff);

        // ── 5. Run the debug analyzer (includes rejected groups + reasons) ─────
        var groups = RecurringSuggestionsAnalyzer.AnalyzeGroupsForDebug(nonPending, cutoff);

        // ── 6. Watch-list scan ────────────────────────────────────────────────
        // Searches ALL loaded transactions (including pending) for common
        // subscription terms so caller can verify whether the charges exist
        // before digging into grouping / exclusion logic.
        var watchTerms = new[]
        {
            "NETFLIX", "MAX", "HBO", "DISNEY", "HULU", "SPOTIFY",
            "APPLE", "PARAMOUNT", "PEACOCK", "YOUTUBE", "AMAZON PRIME",
        };

        var watchMatches = allTransactions
            .Where(t =>
            {
                var name = (t.MerchantName ?? t.Name ?? string.Empty).ToUpperInvariant();
                return watchTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(t => t.Date)
            .Select(t => new
            {
                id = t.Id,
                date = t.Date.ToString("yyyy-MM-dd"),
                name = t.Name,
                merchantName = t.MerchantName,
                amount = t.Amount,
                pending = t.Pending,
                suggestedKind = t.SuggestedKind.ToString(),
                normalizedName = RecurringSuggestionsAnalyzer.NormalizeName(
                    t.MerchantName ?? t.Name ?? string.Empty),
                withinCutoffWindow = t.Date >= cutoff,
                excludedBecausePending = t.Pending,
                excludedBecauseAmountNotPositive = t.Amount <= 0,
            })
            .ToList();

        // ── 7. Build and return response ─────────────────────────────────────
        string? earliestDate = allTransactions.Count > 0
            ? allTransactions.Min(t => t.Date).ToString("yyyy-MM-dd")
            : null;
        string? latestDate = allTransactions.Count > 0
            ? allTransactions.Max(t => t.Date).ToString("yyyy-MM-dd")
            : null;

        return Results.Ok(new
        {
            userId,
            generatedAt = DateTime.UtcNow.ToString("O"),
            cutoffDate = cutoff.ToString("yyyy-MM-dd"),
            summary = new
            {
                allTransactionsLoaded = allTransactions.Count,
                pendingExcludedCount,
                outflowCount,
                inflowOrZeroCount = inflowOrZero,
                earliestTransactionDate = earliestDate,
                latestTransactionDate = latestDate,
                groupCount = groups.Count,
                groupsIncluded = groups.Count(g => g.Included),
                groupsRejected = groups.Count(g => !g.Included),
                recurringSuggestionCount = suggestions.Count,
            },
            suggestions,
            groups,
            watchList = new
            {
                terms = watchTerms,
                matchCount = watchMatches.Count,
                matches = watchMatches,
            },
        });
    })
    .WithName("RecurringSuggestionsAudit")
    .WithOpenApi();

// --- RUN THE APP ---
app.Run();

// --- TYPE DECLARATIONS ---

public record MarkRecurringFromTransactionRequest(
    DateTime? FirstDueDate  // optional override; can be null for now
);

public record PaycheckDecisionRequest(string Decision, decimal? Amount = null);



public record RegisterDeviceRequest(string ExpoPushToken, string? Platform);

/// <summary>
/// Typed row used by the simple format branch of the transaction-balance-audit endpoint.
/// Avoids anonymous-type boxing issues when filtering/mapping after the main audit loop.
/// </summary>
internal record AuditSimpleRowData(
    int Id,
    DateTime Date,
    string? MerchantName,
    string Name,
    decimal Amount,
    bool Pending,
    bool IsBeforeUserRegistration,
    bool IsConsideredRecurring,
    decimal BalanceBefore,
    decimal Effect,
    decimal BalanceAfter,
    string Reason
);
