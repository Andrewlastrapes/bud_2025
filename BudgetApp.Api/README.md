# bud_2025
Backend Deployment (BudgetApp API)

## Architecture Overview

- **Backend:** .NET 8 Web API
- **Containerization:** Docker
- **Image Registry:** AWS ECR
- **Hosting:** AWS ECS Fargate
- **Database:** Render Postgres
- **Config:** ECS Task Definition environment variables

**Flow:**

```
Code → Docker Image → ECR → ECS Fargate → Live API
                           ↓
                      Render Postgres
```

---

## 1. Prerequisites

- Docker Desktop running
- AWS CLI configured (`aws configure`)
- ECR repo exists: `<AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api`
- ECS cluster and Fargate service already created and pointing to ECR

---

## 2. Build & Push Docker Image

From repo root (or wherever your Dockerfile is):

```bash
docker build -t budgetapp-api:<TAG> .
docker tag budgetapp-api:<TAG> <AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:<TAG>
```

Authenticate with ECR (if needed):

```bash
aws ecr get-login-password --region us-east-2 \
| docker login --username AWS --password-stdin <AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com
```

Push image:

```bash
docker push <AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:<TAG>
```

---

## 3. Deploy to ECS Fargate

In AWS Console:

1. Go to **ECS → Task Definitions → budgetapp-api**
2. Click **Create new revision**
3. Update the image URI to: `<AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:<TAG>`
4. Verify port mapping: `8080:8080`
5. Click **Create**

Then:

1. Go to **ECS → Clusters → your-cluster → Services**
2. Select your Fargate service
3. Click **Update service**
4. Choose the new task definition revision
5. Enable **Force new deployment**
6. Click **Update**

⚠️ **Important:**
- ECS Fargate does not deploy from Git
- It only deploys what exists in ECR
- Pushing code alone does nothing

---

## 4. Environment Variables

Configured in the ECS Task Definition:

| Variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | Postgres connection string |
| `Plaid__ClientId` | Plaid client ID |
| `Plaid__Secret` | Plaid secret |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` |

`ConnectionStrings__DefaultConnection` maps to:

```csharp
builder.Configuration.GetConnectionString("DefaultConnection")
```

---

## 5. Database (Render Postgres)

Hosted on Render. Tables are NOT auto-created unless you do it explicitly.

**Option A (recommended)** — Run migrations on startup:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}
```

**Option B** — Run manually:

```bash
dotnet ef database update
```

---

## 6. Health Check

ECS expects:

- **Port:** `8080`
- **Health endpoint:** `/health`

If the health check fails, the task will be killed and replaced.

---

## 7. Debugging Deployment

Check logs in **ECS → Clusters → your-cluster → Tasks → Logs**.

Look for:

```
BOOT: DB Host=...
BOOT: DB Name=...
BOOT: DB CanConnect=true/false
```

Add this in `Program.cs` to debug:

```csharp
var csb = new NpgsqlConnectionStringBuilder(connectionString);
Console.WriteLine($"BOOT: DB Host={csb.Host}");
Console.WriteLine($"BOOT: DB Name={csb.Database}");
```

---

## 8. Common Issues

| Error | Cause |
|---|---|
| "My changes aren't deployed" | You didn't push a new Docker image |
| No tables in database | Migrations never ran |
| Health check failed | Wrong port, app not starting, or endpoint missing |
| Wrong database | Incorrect `ConnectionStrings__DefaultConnection` in task definition |
| Docker build fails | Docker daemon not running |

---

## 9. Deployment Checklist

Before every deploy:

- [ ] Docker Desktop running
- [ ] Image built with new versioned tag
- [ ] Image pushed to ECR
- [ ] New ECS task definition revision created with updated image tag
- [ ] ECS service updated with force new deployment
- [ ] Logs confirm new version running
- [ ] DB connection verified
- [ ] Health check passing