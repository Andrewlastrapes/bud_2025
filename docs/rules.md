
**/docs/rules.md**
```md
# Rules

These rules are written to support an AI-assisted, agentic workflow. The goal is repeatable, reviewable change with enforced validation — not speed at all costs.

If a rule conflicts with reality, update the docs first, then the code.

## Non-negotiables

- No secrets in repo, docs, screenshots, prompts, or logs.
- Anything unclear must be labeled as unknown and verified before deployment.
- Every change must include a validation plan and a rollback plan.
- Production changes require traceability:
  - what changed
  - why it changed
  - how it was validated
  - how it rolls back

## Backend coding standards (.NET minimal API)

### Keep Program.cs thin
- Route handlers should only do:
  - auth/user resolution
  - request validation
  - call a service
  - return a response
- Move business logic into services (TransactionService-style).
- Prefer introducing explicit modules:
  - Auth / UserContext
  - PlaidLinking
  - TransactionSync
  - Budgeting
  - Notifications

### Authentication and authorization consistency
- All protected routes must:
  - require Authorization: Bearer <Firebase ID token>
  - verify token server-side via Firebase Admin
  - map Firebase uid → Users row
- Do not duplicate token parsing/verification logic in every endpoint.
  - Create a shared helper/middleware that returns a typed UserContext.

### Input validation and error handling
- Validate request bodies explicitly. Fail with 400 for invalid input, 401 for missing/invalid auth, 404 for missing resources.
- For integrations (Plaid, Expo push), return safe error messages to clients but log full diagnostics server-side.
- Avoid swallowing errors in ways that hide broken behavior. If you must return 200 OK to a webhook provider to prevent retries, log loudly and include a metric/alert for failures.

### Persistence rules (EF Core / Postgres)
- Store timestamps in UTC.
- Ensure idempotency for external events:
  - Webhooks can be delivered multiple times.
  - Transaction sync can return overlapping results.
- Add unique constraints where needed (e.g., Transactions.paid_transaction_id).
- Prefer deterministic updates over “best effort” inserts.

### Plaid-specific rules
- Transactions Sync must be implemented according to Plaid’s incremental model:
  - Handle added, modified, and removed updates every time.
  - Treat `next_cursor` as the only canonical progress marker.
- Webhook processing must be idempotent:
  - A webhook should be safe to reprocess.
- Pending → posted transitions can be modeled by Plaid as:
  - removal of pending transaction + addition of posted transaction.
  - This means ignoring `removed` updates can leave stale pending rows.

### Notifications rules (Expo push)
- Treat Expo push tokens as sensitive identifiers (not “secrets”, but do not leak them).
- Centralize message formatting and send behavior in one service.
- Always store and query device tokens from DB; never hardcode tokens.

### Logging and observability
- Every request path that mutates state should emit:
  - userId (DB id, not email)
  - firebase uid (if safe)
  - plaid item id (not access_token)
  - counts: tx added/modified/removed
  - cursor movement old → new

## Frontend coding standards (Expo / React Native)

### Environment and config
- All deployable environment settings must be driven by EXPO_PUBLIC_* variables (or a documented config layer).
- Never hardcode production API URLs in screens/components.
- Firebase config must not be mixed with runtime environment selection.

### API client discipline
- All API requests must go through a centralized client (axios wrapper).
- The wrapper must:
  - attach the Firebase ID token
  - standardize error handling and retry behavior
  - log request failures with enough detail to debug environment issues

### Navigation and onboarding
- Treat onboarding as a state machine:
  - explicitly define onboarding states and transitions
  - avoid state hidden in ad-hoc local flags
- OnboardingComplete is set by backend; frontend should not guess.

### Push notifications
- Push token registration must be idempotent.
- Registration must not block login.
- Handle permissions and device-only behavior explicitly.

## Deployment and infrastructure rules (AWS ECS + ALB)

### Health checks
- ALB health check path must be `/health` and must return 200 quickly.
- `/health` must not require authentication.
- `/health` should validate DB connectivity only if you accept that DB outages will take tasks out of service.

### ECS deployments
- Use ECS deployment circuit breaker with rollback enabled where possible.
- Always deploy immutable, versioned image tags (never reuse “latest” for production).

### Secrets and configuration
- Store secrets in one place:
  - AWS Secrets Manager or SSM Parameter Store (recommended)
  - GitLab CI masked/protected variables (if you must)
- Do not store secrets in task definitions committed to git.
- Only non-sensitive config belongs in docs.

## CI/CD rules (GitLab)

Because pipeline YAML is currently unspecified, these describe the contract a future .gitlab-ci.yml must satisfy.

### Minimum required pipeline checks
Merge requests must pass:
- Backend:
  - dotnet restore
  - dotnet build
  - dotnet test
- Frontend:
  - npm ci
  - a lightweight lint/typecheck step (add if missing)
  - expo doctor (optional but recommended)
- Security:
  - secret scanning (GitLab or external)
  - dependency scanning (if available)

### Deployment jobs
- Deployment must be defined as explicit jobs (not implicit “push triggers deploy”).
- Deploy jobs must be restricted to protected branches/tags.
- Rollback must be possible by re-deploying a previous artifact (previous image tag or previous task definition revision).

## Security rules

### Secret handling
- Never commit:
  - Postgres connection strings with passwords
  - Plaid secrets
  - Firebase service account JSON
  - AWS credentials
- Rotate leaked credentials immediately:
  - database credentials
  - Plaid secrets
  - service account keys
  - AWS keys

### Transport security
- Mobile → API must be HTTPS in production.
- Do not rely on iOS ATS exceptions for production traffic.
- Authorization tokens must not be transmitted over HTTP.

### Principle of least privilege
- ECS task role must only allow what the container needs.
- CI runner credentials should be scoped to:
  - push to ECR
  - update ECS service/task definition
  - read secrets (if needed), but only for the target environment

## Official references used for these rules

- Plaid Transactions Sync + updates model:
  https://plaid.com/docs/api/products/transactions/
  https://plaid.com/docs/transactions/webhooks/
  https://plaid.com/docs/transactions/transactions-data/

- Firebase ID token verification:
  https://firebase.google.com/docs/auth/admin/verify-id-tokens
  https://firebase.google.com/docs/admin/setup

- Expo push notifications:
  https://docs.expo.dev/push-notifications/sending-notifications/
  https://docs.expo.dev/versions/latest/sdk/notifications/
  https://docs.expo.dev/app-signing/security/

- AWS ECS / ALB / ECR:
  https://docs.aws.amazon.com/AmazonECS/latest/developerguide/alb.html
  https://docs.aws.amazon.com/elasticloadbalancing/latest/application/target-group-health-checks.html
  https://docs.aws.amazon.com/AmazonECR/latest/userguide/docker-push-ecr-image.html
  https://docs.aws.amazon.com/AmazonECS/latest/developerguide/using_awslogs.html
  https://docs.aws.amazon.com/AmazonECS/latest/developerguide/deployment-circuit-breaker.html

- GitLab CI/CD variables and deployments:
  https://docs.gitlab.com/ci/variables/
  https://docs.gitlab.com/ci/environments/deployments/
  https://docs.gitlab.com/ci/pipelines/merge_request_pipelines/

- ASP.NET Core env var config mapping:
  https://learn.microsoft.com/aspnet/core/fundamentals/configuration/
