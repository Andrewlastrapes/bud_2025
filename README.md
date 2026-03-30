# Budget App

Personal finance app (Expo / React Native) backed by a .NET API with Plaid + Firebase Auth, using Postgres on Render.

## Architecture (high level)

Mobile App (Expo / React Native)
- Firebase Auth (client)
- Plaid Link (client)
- Expo Push Notifications (client)

⬇ Firebase ID token (Authorization: Bearer …)

Backend API (.NET / ASP.NET Minimal API) — hosted on AWS ECS Fargate
- Verifies Firebase ID tokens (Firebase Admin)
- Plaid API integration (Going.Plaid)
- Sends push notifications via Expo push service

⬇ EF Core (Npgsql)

PostgreSQL — hosted on Render
- Users
- Plaid Items (access tokens)
- Transactions
- Fixed costs
- Balances
- User devices (Expo push tokens)

---

## Tech Stack + Links

### Mobile
- Expo: https://expo.dev/ — app runtime/build tools
- EAS Build/Submit: https://docs.expo.dev/build/introduction/ — iOS builds + TestFlight submission
- React Native: https://reactnative.dev/
- React Navigation: https://reactnavigation.org/
- React Native Paper: https://callstack.github.io/react-native-paper/
- Axios: https://axios-http.com/
- Expo Notifications: https://docs.expo.dev/versions/latest/sdk/notifications/
- Firebase (client SDK): https://firebase.google.com/docs/web/setup
- Plaid Link (React Native SDK): https://plaid.com/docs/link/react-native/

### Backend
- .NET / ASP.NET Core: https://dotnet.microsoft.com/ + https://learn.microsoft.com/aspnet/core/
- Entity Framework Core: https://learn.microsoft.com/ef/core/
- Npgsql (Postgres provider for EF Core): https://www.npgsql.org/efcore/
- Firebase Admin SDK (token verification): https://firebase.google.com/docs/admin/setup
- Plaid API: https://plaid.com/docs/
- Going.Plaid (.NET library): https://github.com/viceroypenguin/Going.Plaid

### Infrastructure
- AWS ECS Fargate: https://aws.amazon.com/fargate/
- Render Postgres: https://render.com/docs/databases

---

## Auth Model

- Mobile app signs in with Firebase Auth.
- For API requests, the app sends a Firebase ID token in the `Authorization: Bearer <token>` header.
- The .NET API verifies the token using Firebase Admin and maps the Firebase UID to a `User` row in Postgres.

---

## Plaid Integration

Backend supports:
- Create Link token
- Exchange public token
- Categories
- Recurring streams
- Transaction sync & processing
- Plaid webhooks (`/api/plaid/webhook`) which trigger:
  - transaction sync
  - dynamic budget recalculation
  - optional push notifications to user devices

---

## Push Notifications

- Mobile registers Expo push token and sends it to backend.
- Backend stores device tokens and can send push notifications via Expo push API.

---

## Database / Schema Management

- Postgres is hosted on Render.
- EF Core is used as the ORM (DbContext + LINQ).
- **Schema changes are currently done via manual SQL** (no EF migrations workflow yet).

---

## Environment Variables

### Mobile (`EXPO_PUBLIC_*`)
Stored locally in `.env` / `.env.production` (not committed).
In EAS, set these as environment variables (Production/Preview/etc):

- `EXPO_PUBLIC_API_URL`
- `EXPO_PUBLIC_FIREBASE_API_KEY`
- `EXPO_PUBLIC_FIREBASE_AUTH_DOMAIN`
- `EXPO_PUBLIC_FIREBASE_PROJECT_ID`
- `EXPO_PUBLIC_FIREBASE_STORAGE_BUCKET`
- `EXPO_PUBLIC_FIREBASE_MESSAGING_SENDER_ID`
- `EXPO_PUBLIC_FIREBASE_APP_ID`
- `EXPO_PUBLIC_MEASUREMENT_ID` (if used)

### Backend
Configured via ECS Task Definition environment variables:

- `ConnectionStrings:DefaultConnection`
- `Plaid:ClientId`
- `Plaid:Secret`
- `Plaid:Products`
- `Plaid:CountryCodes`
- `Plaid:WebhookUrl`
- Firebase Admin service account (local file `firebase-service-account.json` in current setup)

---

## Common Commands

### iOS build (cloud)
```bash
npx eas-cli build -p ios --profile production


// Submit to TestFlight
npx eas-cli submit -p ios --profile production


Database = psql "postgresql://<DB_USER>:<DB_PASSWORD>@<DB_HOST>/<DB_NAME>?sslmode=require"

\dt
SELECT * FROM "Users" LIMIT 5;
SELECT * FROM "Balances" LIMIT 5;
SELECT * FROM "PlaidItems" LIMIT 5;



Backend deploy:

1. Authenticate with ECR and push image

```bash
aws sts get-caller-identity
aws ecr get-login-password --region us-east-2 | docker login --username AWS --password-stdin <AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com

docker buildx build \
  --platform linux/amd64 \
  -t <AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:<TAG> \
  --push \
  .
```

2. Create New Task Definition Revision

   - Go to **ECS → Task Definitions → budgetapp-api**
   - Click **Create new revision**
   - Update the image to: `<AWS_ACCOUNT_ID>.dkr.ecr.us-east-2.amazonaws.com/budgetapp-api:<TAG>`
   - Verify: `ASPNETCORE_URLS = http://0.0.0.0:8080`, port mapping `8080:8080`
   - Click **Create**

3. Update ECS Fargate Service

   - Go to **ECS → Clusters → your-cluster → Services**
   - Select your Fargate service → **Update service**
   - Choose the new task definition revision
   - Enable **Force new deployment**
   - Click **Update**

4. Verify Deployment

   - Check **Tasks → Logs** for startup output
   - Confirm target health in **EC2 → Target Groups → budgetapp-api-tg → Targets** (should show Healthy)
   - Health check: `curl -i http://<ALB_ENDPOINT>/health`
