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
        ClientName = "Dynamic Budget App",
        Language = Language.English,
        CountryCodes = countryCodes,
        User = user,
        Products = products,
        Webhook = config["Plaid:WebhookUrl"],
        RedirectUri = "https://plaid-redirect.dynamicbudgetapp.com"
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

                return Results.Problem("Unexpected error occurred.");
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
            NextDueDate = requestBody.NextDueDate
        };

        await dbContext.FixedCosts.AddAsync(newCost);
        await dbContext.SaveChangesAsync();

        return Results.Ok(newCost);
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

        return Results.Ok(new
        {
            inflow_streams = response.InflowStreams,
            outflow_streams = response.OutflowStreams
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
    BaseBudgetHttpRequest request) =>
{
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

        // Filter fixed costs in this period (exclude Savings category — debt/savings not applied yet)
        var fixedBillsThisPeriod = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id
                && fc.NextDueDate.HasValue
                && fc.NextDueDate.Value.Date >= today
                && fc.NextDueDate.Value.Date <= nextPaycheck
                && fc.Category != "Savings")
            .ToListAsync();

        decimal totalFixedBills = fixedBillsThisPeriod.Sum(fc => fc.Amount);

        // Calculate base budget: paycheck - fixedCosts only (NO debt, NO savings)
        var baseResult = budgetEngine.CalculateBaseBudget(new BaseBudgetRequest
        {
            PaycheckAmount   = request.PaycheckAmount,
            Today            = today,
            NextPaycheckDate = nextPaycheck,
            TotalFixedBills  = totalFixedBills
        });

        return Results.Ok(new
        {
            paycheckAmount      = baseResult.PaycheckAmount,
            fixedCostsRemaining = baseResult.FixedCostsRemaining,
            baseRemaining       = baseResult.BaseRemaining
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
    FinalizeBudgetRequest request) =>
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

        // Step 2 — Filter fixed costs that occur BEFORE the next paycheck
        // (savings are excluded from the date filter — always included per paycheck)
        var fixedBillsThisPeriod = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id
                && fc.NextDueDate.HasValue
                && fc.NextDueDate.Value.Date >= today
                && fc.NextDueDate.Value.Date <= nextPaycheck
                && fc.Category != "Savings")
            .ToListAsync();

        var savingsThisPeriod = await dbContext.FixedCosts
            .Where(fc => fc.UserId == user.Id && fc.Category == "Savings")
            .ToListAsync();

        decimal totalFixedBills = fixedBillsThisPeriod.Sum(fc => fc.Amount);
        decimal savingsContribution = savingsThisPeriod.Sum(fc => fc.Amount);
        decimal debtPerPaycheck = request.DebtPerPaycheck ?? 0m;

        // Step 3 — Delegate to the pure budget engine (NO proration)
        // Formula: remainingToSpend = paycheckAmount - fixedBills - savings - debt
        var calcRequest = new BudgetCalculationRequest
        {
            PaycheckAmount      = request.PaycheckAmount,
            Today               = today,
            NextPaycheckDate    = nextPaycheck,
            TotalFixedBills     = totalFixedBills,
            SavingsContribution = savingsContribution,
            DebtPerPaycheck     = debtPerPaycheck
        };

        var result = budgetEngine.CalculateDynamicBudget(calcRequest);

        // Persist the remaining-to-spend as the user's balance
        var balanceRecord = await dbContext.Balances.FirstOrDefaultAsync(b => b.UserId == user.Id);
        if (balanceRecord == null)
        {
            balanceRecord = new Balance
            {
                UserId        = user.Id,
                BalanceAmount = result.RemainingToSpend,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            };
            await dbContext.Balances.AddAsync(balanceRecord);
        }
        else
        {
            balanceRecord.BalanceAmount = result.RemainingToSpend;
            balanceRecord.UpdatedAt     = DateTime.UtcNow;
        }

        // Persist user settings
        user.OnboardingComplete      = true;
        user.PayDay1                 = request.PayDay1;
        user.PayDay2                 = request.PayDay2;
        user.ExpectedPaycheckAmount  = request.PaycheckAmount;
        user.DebtPerPaycheck         = request.DebtPerPaycheck;

        await dbContext.SaveChangesAsync();

        // Return structured response — no proration fields
        return Results.Ok(new
        {
            message                = "Setup complete.",
            paycheckAmount         = result.PaycheckAmount,
            fixedCostsRemaining    = result.FixedCostsRemaining,
            debtPerPaycheck        = result.DebtPerPaycheck,
            savingsContribution    = result.SavingsContribution,
            remainingToSpend       = result.RemainingToSpend,
            // Legacy alias kept so older clients still work
            dynamicSpendableAmount = result.DynamicSpendableAmount,
            dynamicBalance         = result.RemainingToSpend.ToString("0.00"),
            explanation            = result.Explanation
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
app.MapPost("/api/plaid/webhook", async (
    ITransactionService transactionService,
    ApiDbContext dbContext,
    PlaidWebhookRequest requestBody) =>
    
{

    SentrySdk.CaptureMessage(
        $"Plaid webhook received: type={requestBody.WebhookType}, code={requestBody.WebhookCode}, itemId={requestBody.ItemId}"
    );
    // 🔍 Log EVERYTHING so we stop guessing
    Console.WriteLine($"📡 Webhook received:");
    Console.WriteLine($"   Type: {requestBody.WebhookType}");
    Console.WriteLine($"   Code: {requestBody.WebhookCode}");
    Console.WriteLine($"   ItemId: {requestBody.ItemId}");

    SentrySdk.CaptureMessage("webhook endpoint hit");


    SentrySdk.AddBreadcrumb(
        $"Webhook received: type={requestBody.WebhookType}, code={requestBody.WebhookCode}, itemId={requestBody.ItemId}",
        level: BreadcrumbLevel.Info
    );


    // ✅ Only care about TRANSACTIONS webhooks — ignore everything else
    if (requestBody.WebhookType != "TRANSACTIONS")
    {
        Console.WriteLine("⏭ Ignoring non-transaction webhook");
        return Results.Ok(new { message = "Ignored non-transaction webhook." });
    }

    try
    {
        Console.WriteLine("➡️ Running transaction sync...");
        SentrySdk.AddBreadcrumb("Starting transaction sync");
        SentrySdk.CaptureMessage("STarting transaction sync");

        bool sendNotifications =
        requestBody.WebhookCode != "INITIAL_UPDATE" &&
        requestBody.WebhookCode != "HISTORICAL_UPDATE";

        var syncResponse = await transactionService.SyncAndProcessTransactions(requestBody.ItemId, sendNotifications);

        var addedCount = syncResponse.Added?.Count ?? 0;

        Console.WriteLine($"✅ Sync complete. Added: {addedCount}");

        SentrySdk.AddBreadcrumb(
            $"Webhook sync complete: added={addedCount}",
            level: BreadcrumbLevel.Info
        );

        return Results.Ok(new
        {
            message = "Webhook processed successfully",
            added = addedCount
        });
        }
    catch (Exception e)
        {
        Console.WriteLine("💥 WEBHOOK PROCESSING FAILED");
        Console.WriteLine(e.ToString());
        SentrySdk.CaptureMessage("Webhook processing failed");


        SentrySdk.CaptureException(e, scope =>
        {
            scope.SetTag("webhook.itemId", requestBody.ItemId ?? "unknown");
            scope.SetTag("webhook.type", requestBody.WebhookType ?? "unknown");
            scope.SetTag("webhook.code", requestBody.WebhookCode ?? "unknown");
        });

        // Still return 200 so Plaid doesn't retry forever
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
        decimal totalDebt = 0m;

        foreach (var item in items)
        {
            var req = new AccountsGetRequest
            {
                AccessToken = item.AccessToken
            };

            var resp = await plaidClient.AccountsGetAsync(req);

            foreach (var acct in resp.Accounts)
            {
                // Only care about credit accounts for "debt"
                if (acct.Type != AccountType.Credit) continue;

                var bal = acct.Balances.Current ?? 0m;
                if (bal <= 0) continue; // no debt on this card

                accounts.Add(new DebtSnapshotAccountDto
                {
                    InstitutionName = item.InstitutionName ?? "Unknown institution",
                    AccountName = acct.Name ?? acct.OfficialName ?? "Credit account",
                    Mask = acct.Mask,
                    CurrentBalance = bal
                });

                totalDebt += bal;
            }
        }

        var result = new DebtSnapshotResponse
        {
            TotalDebt = totalDebt,
            Accounts = accounts
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



// --- RUN THE APP ---
app.Run();

// --- TYPE DECLARATIONS ---

public record MarkRecurringFromTransactionRequest(
    DateTime? FirstDueDate  // optional override; can be null for now
);



public record RegisterDeviceRequest(string ExpoPushToken, string? Platform);