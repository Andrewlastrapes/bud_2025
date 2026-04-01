# Current state

Snapshot date: 2026-03-30 (America/New_York)

This file documents what is known to work, what is fragile, and what is unknown. It is an operational document, not a marketing doc.

## What is working (observed in code)

Backend:
- Firebase ID token verification is implemented across many endpoints.
- User bootstrap:
  - POST /api/users/register
  - GET  /api/users/profile
- Plaid Link bootstrap:
  - POST /api/plaid/create_link_token
  - POST /api/plaid/exchange_public_token
- Transactions ingestion exists via Plaid Transactions Sync wrapper:
  - POST /api/transactions/sync
- Plaid webhook receiver exists:
  - POST /api/plaid/webhook
- Fixed cost CRUD exists:
  - GET/POST/DELETE /api/fixed-costs
- Device token registration exists:
  - POST /api/notifications/register-device
- Health endpoint exists:
  - GET /health (includes DB connectivity check)
- EF Core migrations exist and are applied at startup when not Development.

Mobile:
- Firebase Auth client is wired into app root and API calls.
- Fetches /api/users/profile and uses onboardingComplete to route.
- Registers Expo push token and posts to /api/notifications/register-device.
- Implements notification response handler to call backend actions (e.g., mark recurring).

Database:
- Migrations exist for core tables:
  - Users, PlaidItems, Transactions, Balances, FixedCosts, UserDevices

## What is fragile or likely incorrect

High-risk correctness issues:
- Transactions Sync processing appears to handle only `added` updates and ignore `modified` and `removed`.
  - Plaid explicitly returns added/modified/removed updates.
  - Pending→posted transitions can appear as removed pending + added posted.
  - Ignoring removed/modified can produce duplicates or stale pending rows.

Security and transport:
- iOS ATS exception exists for an ALB DNS name allowing insecure HTTP loads.
  - This is not acceptable for production if Firebase ID tokens are in transit.
  - Production should enforce HTTPS end-to-end.

Webhook robustness:
- Webhook receiver returns 200 OK even when processing fails (to avoid retries).
  - This prevents upstream retries, but can silently drop updates unless logs/alerts exist.
  - If you keep this behavior, you must have monitoring on failure logs/metrics.

Architecture / maintainability:
- Program.cs contains a very large amount of routing + business logic, increasing change risk.
- Auth parsing and Firebase verification are repeated across endpoints (risk of inconsistent behavior).
- Notification send logic exists in more than one place (service + inline code paths).

Infra/config drift risk:
- GitLab pipeline is unspecified in repo snapshot; deploy could be manual or partially automated.
- ECS task definition and ALB config are not present in repo snapshot, so behavior may differ from docs unless confirmed.

## What is unknown (not in the snapshot)

These must be found and documented before claiming end-to-end repeatability:
- Dockerfile and entrypoint details (ports, user, ASPNETCORE_URLS, healthcheck)
- ECS task definition JSON and service settings (CPU/memory, logging, desired count, health check grace period)
- ALB configuration (listener rules, HTTPS termination, target group matcher)
- GitLab CI pipeline YAML (.gitlab-ci.yml), runner executor, and deploy strategy
- Where Postgres is hosted (RDS? Render? other?), TLS requirements, and network topology (public vs private)
- Mobile config module for API_BASE_URL and how EXPO_PUBLIC_API_URL is injected
- Plaid webhook verification (Plaid supports verifying webhooks; current approach is unspecified)
- Alerting/monitoring setup (CloudWatch alarms, dashboards, log filters)

## Current technical debt

- Minimal API monolith (Program.cs is too broad)
- Missing explicit handling of Plaid modified/removed updates
- Hard-to-reason-about event flows (manual sync + webhook sync)
- Weak automated test coverage (unit tests exist, but integration/contract tests are missing)
- Environment/config sources not centralized and not enforced by tooling

## Practical priorities

Priority order is biased toward avoiding production incidents:

- Make production transport correct (HTTPS end-to-end, remove ATS exceptions).
- Fix Plaid transactions ingestion to handle modified/removed updates and cursor correctness.
- Centralize auth/user context extraction to eliminate repeated logic.
- Create a deployable, validated pipeline contract:
  - define required CI validation and deploy jobs
  - document rollback and smoke tests
- Move business logic out of Program.cs into services/modules.

## Recommended next steps to reduce uncertainty

- Locate and commit (or document externally) the ECS task definition and ALB target group settings.
- Add a /docs/infra.md or IaC folder if you want automation to be reliable.
- Add a minimal .gitlab-ci.yml that at least runs backend build/test on merge requests.
- Add a simple smoke test script that hits /health and one authenticated endpoint.
