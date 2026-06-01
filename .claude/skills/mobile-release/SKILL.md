# Mobile Release

Use this skill when preparing the Budget App mobile frontend for build/submission.

The user manually builds and submits the frontend. Do not claim to submit the app unless the user explicitly asks and provides the required environment/access.

## Goals

Prepare a safe mobile release checklist for the React Native / Expo frontend.

## Process

1. Inspect the current git status.
2. Identify frontend changes since the last release.
3. Confirm whether backend API contract changes are required.
4. Check environment/config values:
   - API base URL
   - Firebase config
   - Plaid redirect/deep link config
   - Expo app config
   - bundle identifier / package name
   - app version and build number
5. Run available frontend checks:
   - npm install / npm ci only if needed
   - npm run lint only if script exists
   - npm test only if script exists
   - npx expo-doctor if appropriate
6. Confirm app starts locally if requested.
7. Produce a manual submission checklist for the user.

## Required release checklist

Before submission, verify:

- App points to production API.
- Firebase project is production.
- Plaid environment is production.
- Sentry DSN is configured correctly if used.
- App version/build number increased.
- Push notification registration still works.
- Login/register flow works.
- Plaid Link flow works.
- Onboarding flow works.
- Home dashboard loads dynamic balance.
- Transactions screen loads.
- Fixed costs screen loads.
- Deposit review flow works.
- Large expense review flow works.
- No secrets are committed.
- Backend deploy is already live if frontend depends on new API behavior.

## Output format

Always return:

1. Files changed that affect release.
2. Build commands to run.
3. Manual QA checklist.
4. Submission notes.
5. Risks or blockers.

Do not invent package scripts. Inspect package.json first.
