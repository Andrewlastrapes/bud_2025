# Skill: Code Review Standards

Use this checklist to determine whether a change is done. A change is not done until every relevant item passes.

---

## Done checklist

### Auth and data isolation

- [ ] Every query on user-owned data filters by `UserId`
- [ ] `UserId` comes from the verified Firebase token, not from the request body
- [ ] No endpoint accidentally exposes another user's data

### Secret hygiene

- [ ] No secrets in code, logs, comments, or migration SQL
- [ ] No Plaid `access_token` in logs or responses
- [ ] `appsettings.Production.json` contains no real credentials

### EF migration (if a migration was added)

- [ ] `Up()` is non-destructive (nullable column or has a default)
- [ ] `Down()` correctly reverses `Up()`, or has an explicit comment explaining why it cannot
- [ ] No raw `DROP TABLE`, `TRUNCATE`, or unguarded `DELETE FROM` in the migration
- [ ] Local `dotnet ef database update` was run and verified

### API correctness

- [ ] New endpoints return `400` for invalid input, `401` for missing/invalid auth, `404` for missing resources
- [ ] Changed endpoints still satisfy all existing frontend callers
- [ ] Plaid amount sign convention is preserved (positive = spending, negative = income)
- [ ] Webhook handlers are idempotent

### Build and tests

- [ ] `dotnet build` passes with no errors
- [ ] `dotnet test BudgetApp.Api/BudgetApp.Api.Tests` passes
- [ ] No existing test was silently broken or deleted to make the build pass

### Frontend / backend contract

- [ ] If response shape changed, all frontend screens that consume it are updated
- [ ] If a new required request field was added, all frontend callers send it
- [ ] Dates are UTC ISO strings, amounts are numeric and match Plaid's sign convention

### General

- [ ] Change is the smallest safe unit — no unrelated refactoring mixed in
- [ ] Any new `TODO` or `FIXME` is tracked (not left as "I'll fix it later")
- [ ] Change has a clear, descriptive commit message
