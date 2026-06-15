# Debugging Template

## Problem

- User-visible behavior:
- Sentry issue/link:
- Logs:
- User id(s):
- Plaid item/account/transaction ids, if relevant:
- Time window:
- Expected behavior:
- Actual behavior:

## Initial hypothesis

List likely causes, but treat them as unproven.

## Inspection requirements

Claude must inspect:

- Relevant endpoint(s)
- Relevant service(s)
- Data model(s)
- Existing tests
- Recent related changes, if visible
- Logs/Sentry evidence provided by the user

## Root-cause proof

Before changing code, provide:

- Exact file/function involved
- Why the bug happens
- Why competing explanations are less likely
- Minimal reproduction path using code, test, logs, or DB query

## Fix requirements

- Smallest safe change
- No unrelated refactors
- No cosmetic formatting
- Preserve existing behavior unless explicitly changing it
- Add or update a regression test where practical

## Validation requirements

Provide:

- Tests added/updated
- Commands run
- Output summary
- Any commands that failed and why
- Before/after evidence where possible

## Deployment/manual verification

State:

- What should be checked after deploy
- What logs/Sentry events should disappear or change
- What admin/debug endpoint or DB query can verify the fix
