# Pull Request Checklist

## Before Opening PR

- [ ] All tests pass locally: `dotnet test BudgetApp.Api/BudgetApp.Api.Tests`
- [ ] No cosmetic formatting changes
- [ ] No unrelated refactors
- [ ] Git diff reviewed (only relevant changes)

## PR Description Must Include

- **Problem**: What bug/feature is this addressing?
- **Solution**: What approach did you take?
- **Testing**: What tests were added/updated?
- **Proof**: Test output showing tests pass
- **Files Changed**: List of modified files with brief explanation

## Code Review Focus

- Correctness: Does it solve the problem?
- Safety: Could this break production?
- Tests: Are edge cases covered?
- Maintainability: Is it clear why this change was made?

## Deployment Checklist

- [ ] Database migrations reviewed (if any)
- [ ] Sentry alerts configured (if new error paths)
- [ ] Manual verification plan documented
- [ ] Rollback plan documented (if risky change)

## Post-Merge

- [ ] Monitor Sentry for new errors
- [ ] Verify logs show expected behavior
- [ ] Check admin/debug endpoints if applicable
