# bud_2025
Backend Deployment (BudgetApp API)
Architecture Overview

Backend: .NET 8 Web API

Containerization: Docker

Image Registry: AWS ECR

Hosting: AWS App Runner

Database: Render Postgres

Config: App Runner environment variables

Flow:

Code → Docker Image → ECR → App Runner → Live API
                           ↓
                      Render Postgres
1. Prerequisites

Docker Desktop running

AWS CLI configured (aws configure)

ECR repo exists:

531608153868.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api

App Runner service already created and pointing to ECR

2. Build & Push Docker Image

From repo root (or wherever your Dockerfile is):

docker build -t budgetapp-api:2026-03-23-1 .
docker tag budgetapp-api:2026-03-23-1 531608153868.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:2026-03-23-1
Authenticate with ECR (if needed)
aws ecr get-login-password --region us-east-2 \
| docker login --username AWS --password-stdin 531608153868.dkr.ecr.us-east-2.amazonaws.com
Push image
docker push 531608153868.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:2026-03-23-1
3. Deploy to App Runner

In AWS Console:

Go to App Runner

Select your service

Click Edit & Deploy

Update image tag → 2026-03-23-1

Deploy

⚠️ Important:

App Runner does not deploy from Git

It only deploys what exists in ECR

Pushing code alone does nothing

4. Environment Variables

Configured in App Runner:

Required
ConnectionStrings__DefaultConnection=postgres connection string

This maps to:

builder.Configuration.GetConnectionString("DefaultConnection")
5. Database (Render Postgres)

Hosted on Render

Access via:

render psql <database-id>
Important

Tables are NOT auto-created unless you do it explicitly

You must either:

Option A (recommended)

Run migrations on startup:

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}
Option B

Run manually:

dotnet ef database update
6. Health Check

App Runner expects:

Port: 8080

Health endpoint: /health (or / or /swagger depending on config)

If this fails:

deployment fails

service never becomes live

7. Debugging Deployment
Check logs in App Runner

Look for:

BOOT: DB Host=...
BOOT: DB Name=...
BOOT: DB CanConnect=true/false

Add this in Program.cs to debug:

var csb = new NpgsqlConnectionStringBuilder(connectionString);
Console.WriteLine($"BOOT: DB Host={csb.Host}");
Console.WriteLine($"BOOT: DB Name={csb.Database}");
8. Common Issues
❌ "My changes aren’t deployed"

You didn’t push a new Docker image.

❌ No tables in database

You never ran migrations.

❌ Health check failed

wrong port

app not starting

endpoint missing

❌ Wrong database

incorrect ConnectionStrings__DefaultConnection

not verified via logs

❌ Docker build fails

Docker daemon not running.

9. Recommended Improvements

Stop using latest tag → always use versioned tags

Add migration step on startup

Add /health endpoint explicitly

Log DB connection info on startup

Use CI/CD to automate build → push → deploy

10. Deployment Checklist

Before every deploy:

 Docker Desktop running

 Image built with new tag

 Image pushed to ECR

 App Runner updated to new tag

 Logs confirm new version running

 DB connection verified

 Health check passing

Blunt Reality

Right now your biggest issues have been:

Thinking Git push deploys backend (it doesn’t)

Database schema never created

Environment confusion between local vs prod

Frontend calling localhost in production

Fix those and the system stabilizes fast.
