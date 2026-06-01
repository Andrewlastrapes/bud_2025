# Skill: Implement Budget Feature

Use this skill when implementing any new feature or meaningful change to this budget app — backend, frontend, or both.

The goal is the smallest safe change that works correctly, with no regressions.

---

## Process

### Step 1 — Inspect existing code first

Before writing a single line, read the relevant existing code:

- If touching transactions: read `TransactionService.cs`, `Transaction.cs`, `ApiDbContext.cs`
- If touching budget calculations: read `DynamicBudgetEngine.cs`, `PaycheckSummaryService.cs`, `PendingReconciliationCalculator.cs`
- If touching fixed costs: read `FixedCost.cs`, `FixedCostMatcher.cs`, `FixedCostAdvancer.cs`
- If touching Plaid sync: read `TransactionService.cs`, `PlaidItems.cs`, `PlaidWebhookRequest.cs`
- If touching notifications: read `NotificationService.cs`, `UserDevice.cs`
- If touching the frontend: read the relevant screen file and `config/api.js`

Do not assume how something works. Read it.

### Step 2 — Identify backend impact

Answer these before writing any backend code:

- Does this require a new API endpoint, or modifying an existing one?
- Does this require a new model field or table? If yes, an EF migration is needed.
- Does this change the response shape of an existing endpoint?
- Does this affect the `DynamicBudgetEngine` calculation? If yes, existing unit tests must still pass.
- Does this touch Plaid sync logic? If yes, review idempotency.
- Is `UserId` filtering required on any new query? (Almost always yes.)

### Step 3 — Identify frontend impact

Answer these before writing any frontend code:

- Which screen(s) does this feature affect?
- Does any existing API call need to change (URL, request body, or response handling)?
- If the backend response shape changed, update the frontend consumer.
- Does this require a new navigation route or onboarding step?
- Does this affect notification registration or behavior?

### Step 4 — Make the smallest safe change

- Add only what is needed for this feature. Do not refactor unrelated code in the same change.
- If a migration is needed, follow the EF migration skill (`budget-ef-migration`).
- If an endpoint changes, update both backend and frontend in the same logical unit.
- Do not change the `DynamicBudgetEngine` calculation unless the feature explicitly requires it.

### Step 5 — Run relevant checks

After making changes:

**Backend changed:**

```
dotnet build BudgetApp.Api/BudgetApp.Api.csproj
dotnet test BudgetApp.Api/BudgetApp.Api.Tests
```

**Frontend changed:**
Check `frontend/BudgetApp.Mobile/package.json` for `lint` or `test` scripts. Run them only if they exist. Do not invent scripts.

**Migration added:**

```
dotnet ef database update --project BudgetApp.Api --startup-project BudgetApp.Api
```

Then verify the column/table exists in the local DB.

### Step 6 — Summarize what changed

Before finishing, list:

- Files modified (with one-line reason for each)
- Files created (migrations, new services, new screens)
- Any new API endpoint or changed endpoint (method + path)
- Any changed response/request shape
- Suggested commit message

---

## Do not

- Do not modify production DB directly.
- Do not change `next_cursor` handling or Plaid amount sign conventions without explicit instruction.
- Do not remove `UserId` filters.
- Do not add nullable fields to EF entities without a migration default.
- Do not skip the build/test step even if the change looks trivial.
