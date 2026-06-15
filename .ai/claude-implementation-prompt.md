# Claude Implementation Prompt

## Context

You are working in a .NET 8 Budget App with React Native mobile frontend.

## Key Areas

- Plaid transaction sync
- Recurring transaction detection
- Fixed cost onboarding
- Paycheck summaries
- Sentry/user-specific debugging

## Implementation Rules

1. **No cosmetic formatting changes** - preserve existing style
2. **No unrelated refactors** - stay focused on the task
3. **No production behavior changes** unless a test proves a bug
4. **Prefer deterministic tests** over model-graded evals
5. **Provide proof** for every claim: files changed, diffs, test output

## Before Changing Code

- Read relevant services, models, and existing tests
- Understand the current behavior
- Identify the minimal change needed

## After Changing Code

- Run tests: `dotnet test BudgetApp.Api/BudgetApp.Api.Tests`
- Provide exact output
- If tests fail, explain why and what command failed
- Never say "should work" without proof

## Test Requirements

- Pure static analyzers (like RecurringSuggestionsAnalyzer) should be tested without EF, Plaid, Firebase, Sentry, or network calls
- Use simple xUnit facts with clear arrange/act/assert
- Cover edge cases and rejection paths
- Prove the fix with a regression test
