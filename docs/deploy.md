# Deploy

This document defines how the backend is built and deployed to AWS ECS (Fargate) behind an ALB, how it is validated, and how it rolls back.

It also defines the environment variable inventory for both backend and mobile.

If your real infra differs from this doc, treat this doc as wrong and update it — do not “mentally patch” it.

## Deployment architecture

- Container registry: AWS ECR
- Runtime: AWS ECS on Fargate
- Ingress/load balancing: ALB → target group → ECS service
- Logs: CloudWatch Logs (ECS awslogs or equivalent log config)
- Database: Postgres (provider unspecified in snapshot)
- CI/CD: GitLab (pipeline YAML unspecified in snapshot)

## Health check contract

ALB target group health checks should use:
- Path: /health
- Method: GET
- Success codes: 200 (default ALB matcher)
- Interval/timeout: tuned to your startup time (confirm)
- Health check grace period in ECS: set to cover cold start + DB connect time

Backend /health behavior:
- Must not require authentication.
- Should respond quickly.
- Current implementation checks DB connectivity; that means DB outages will mark tasks unhealthy.

References (AWS):
- ALB target group health checks: https://docs.aws.amazon.com/elasticloadbalancing/latest/application/target-group-health-checks.html
- ECS + ALB integration: https://docs.aws.amazon.com/AmazonECS/latest/developerguide/alb.html

## Environment variables inventory

Important:
- ASP.NET Core maps environment variables to configuration keys using `__` as a cross-platform replacement for `:` (for example, Plaid__ClientId maps to Plaid:ClientId).
- Prefer storing secrets in AWS Secrets Manager / SSM Parameter Store or GitLab masked/protected variables, not in plaintext task definitions.

References:
- ASP.NET Core configuration mapping: https://learn.microsoft.com/aspnet/core/fundamentals/configuration/
- GitLab CI/CD variables: https://docs.gitlab.com/ci/variables/

### Backend env vars

| Variable | Required | Default | Used for | Notes |
|---|---:|---|---|---|
| ConnectionStrings__DefaultConnection | Yes | None | Postgres connection | Backend fails fast if missing. Treat as secret. |
| Plaid__ClientId | Yes | None | Plaid API auth | Treat as secret when paired with secret. |
| Plaid__Secret | Yes | None | Plaid API auth | Secret. Rotate on leak. |
| Plaid__WebhookUrl | Yes (if webhooks used) | None | Link token webhook field | Needed if you want Plaid to call your webhook. |
| Plaid__Products | No | "transactions" | Link token products list | Comma-separated list. |
| Plaid__CountryCodes | No | "US" | Link token country codes | Comma-separated list. |
| FIREBASE_SERVICE_ACCOUNT_JSON | Yes (prod) | None | Firebase Admin init | Alternative to file-based credential. Treat as secret. |
| ASPNETCORE_ENVIRONMENT | No (but important) | "Production" | Runtime env | Controls: Plaid env selection and migration strategy. |
| PORT | No | 8080 | Listen port | Code sets ASPNETCORE_URLS to 0.0.0.0:PORT if missing. |
| ASPNETCORE_URLS | No | computed | Kestrel bind | Often set in container/task def; code sets it if missing. |

Unspecified but recommended:
- LOG_LEVEL or ASPNETCORE_LOGGING__LOGLEVEL__DEFAULT (if you want consistent log verbosity)
- A feature-flag to pick Plaid environment explicitly (currently driven only by ASPNETCORE_ENVIRONMENT)

### Mobile env vars (Expo)

Expo CLI loads environment variables prefixed with `EXPO_PUBLIC_` from .env files for use in client code.

References:
- Environment variables in Expo: https://docs.expo.dev/guides/environment-variables/
- Environment variables in EAS: https://docs.expo.dev/eas/environment-variables/

Because the config file that defines API_BASE_URL is not included in snapshot, variable names below are the expected contract and must be verified against `./config/api` and `./firebaseConfig`.

| Variable | Required | Used for | Notes |
|---|---:|---|---|
| EXPO_PUBLIC_API_URL | Yes | Backend base URL | Must be HTTPS in production. |
| EXPO_PUBLIC_FIREBASE_API_KEY | Yes | Firebase client config | Not a secret, but still avoid leaking other credentials. |
| EXPO_PUBLIC_FIREBASE_AUTH_DOMAIN | Yes | Firebase client config | Verify needed for your platform targets. |
| EXPO_PUBLIC_FIREBASE_PROJECT_ID | Yes | Firebase client config |  |
| EXPO_PUBLIC_FIREBASE_STORAGE_BUCKET | Maybe | Firebase client config | Only if used. |
| EXPO_PUBLIC_FIREBASE_MESSAGING_SENDER_ID | Yes | Firebase client config |  |
| EXPO_PUBLIC_FIREBASE_APP_ID | Yes | Firebase client config |  |
| EXPO_PUBLIC_FIREBASE_MEASUREMENT_ID | No | Analytics | Only if used. |

## Deployment checklist

This checklist assumes you are deploying the backend container.

### Pre-flight
- Confirm the target environment: staging vs production.
- Confirm API is reachable via HTTPS (recommended).
- Confirm DB connectivity and migrations strategy for this deploy.
- Confirm ALB target group health check is /health.
- Confirm secrets exist in the environment store, not in git.

### Build
- Run validation locally or in CI:
  - dotnet restore
  - dotnet build
  - dotnet test
- Build the container image:
  - docker build -t budgetapp-api:<tag> .

### Push
- Authenticate to ECR:
  - aws ecr get-login-password --region <region> | docker login --username AWS --password-stdin <acct>.dkr.ecr.<region>.amazonaws.com
- Tag and push:
  - docker tag budgetapp-api:<tag> <acct>.dkr.ecr.<region>.amazonaws.com/<repo>:<tag>
  - docker push <acct>.dkr.ecr.<region>.amazonaws.com/<repo>:<tag>

Reference:
- ECR push instructions: https://docs.aws.amazon.com/AmazonECR/latest/userguide/docker-push-ecr-image.html

### Deploy
Pick one strategy and document which you use.

Option A (recommended): New task definition revision
- Register a new ECS task definition revision that points to the new image tag.
- Update ECS service to use that new task definition revision.

Option B: Force new deployment (only if task def references a mutable image tag)
- Update ECS service with --force-new-deployment
- This is weaker because it relies on mutable tags.

ECS safety:
- Enable ECS deployment circuit breaker with rollback.
Reference:
- https://docs.aws.amazon.com/AmazonECS/latest/developerguide/deployment-circuit-breaker.html

### Validate (smoke tests)
Minimum smoke tests after deployment:
- GET https://<alb-host-or-domain>/health
  - Expect: 200 OK JSON including status=healthy
- Confirm tasks are healthy in target group:
  - AWS console or:
  - aws elbv2 describe-target-health --target-group-arn <arn>
- Confirm logs show expected “BOOT” lines and no repeated crash loops.

Optional but recommended:
- Do an authenticated call from a real device build:
  - GET /api/users/profile
- Trigger a Plaid sync in a controlled environment.

### Rollback
Rollback options:
- If using task definition revisions:
  - Update ECS service back to the previous task definition revision.
- If relying on image tags:
  - Deploy the previous known-good image tag by creating a new task definition revision pointing to it.

Notes:
- ECS circuit breaker can rollback automatically if enabled.
- Always keep a record of known-good tags and task definition revisions.

## Mermaid deployment timeline

```mermaid
timeline
  title Backend deploy to ECS (Fargate)
  section Validate
    "dotnet build/test": done
  section Build
    "docker build": done
  section Push
    "docker login to ECR": done
    "docker push image tag": done
  section Deploy
    "register new task definition revision": done
    "update ECS service": done
    "wait for steady state": done
  section Verify
    "ALB target group healthy": done
    "GET /health": done
    "CloudWatch logs sanity check": done
  section Rollback (if needed)
    "revert service to prior task def": done
