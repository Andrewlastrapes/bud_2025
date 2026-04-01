# Context

This document is the source of truth for how the BudgetApp system is structured and how data moves through it.

Scope includes:
- Mobile (React Native / Expo)
- Backend (.NET minimal API)
- Postgres (EF Core)
- Plaid integration
- Firebase Auth (ID token verification on backend)
- Expo push notifications
- AWS ECS (Fargate) + ALB + ECR + CloudWatch
- GitLab CI/CD (pipeline definition currently unspecified in repo)

If something is not explicitly documented here, treat it as unknown and confirm before changing production.

## Architecture overview

Text diagram (high-level):

[ Mobile App (Expo) ]
  - Firebase Auth (client)
  - Plaid Link (client)
  - expo-notifications (client)
        |
        | HTTPS requests with Authorization: Bearer <Firebase ID token>
        v
[ AWS ALB ]
        |
        v
[ ECS Service (Fargate) ]

 - .NET minimal API container
  - EF Core (Npgsql)
  - Going.Plaid
  - Firebase Admin SDK
        |
        v
[ Postgres ]
  - Users
  - PlaidItems (access token + cursor)
  - Transactions
  - Balances
  - FixedCosts
  - UserDevices (Expo push tokens)

External dependencies:
- Plaid API (transactions sync + webhooks)
- Firebase Auth (token verification)
- Expo push service (server-side notifications)
- CloudWatch Logs (container logs)

## Mermaid architecture diagram

```mermaid
flowchart LR
  subgraph Mobile["Mobile (Expo / React Native)"]
    UI["App UI + Navigation"]
    FBClient["Firebase Auth SDK (client)"]
    Link["Plaid Link SDK"]
    ExpoNotif["expo-notifications"]
  end

  subgraph AWS["AWS"]
    ALB["Application Load Balancer"]
    ECS["ECS Service (Fargate)"]
    API[".NET container (Minimal API)"]
    CW["CloudWatch Logs"]
    # Context

This document is the source of truth for how the BudgetApp system is structured and how data moves through it.

Scope includes:
- Mobile (React Native / Expo)
- Backend (.NET minimal API)
- Postgres (EF Core)
- Plaid integration
- Firebase Auth (ID token verification on backend)
- Expo push notifications
- AWS ECS (Fargate) + ALB + ECR + CloudWatch
- GitLab CI/CD (pipeline definition currently unspecified in repo)

If something is not explicitly documented here, treat it as unknown and confirm before changing production.

## Architecture overview

Text diagram (high-level):

[ Mobile App (Expo) ]
  - Firebase Auth (client)
  - Plaid Link (client)
  - expo-notifications (client)
        |
        | HTTPS requests with Authorization: Bearer <Firebase ID token>
        v
[ AWS ALB ]
        |
        v
[ ECS Service (Fargate) ]
  - .NET minimal API container
  - EF Core (Npgsql)
  - Going.Plaid
  - Firebase Admin SDK
        |
        v
[ Postgres ]
  - Users
  - PlaidItems (access token + cursor)
  - Transactions
  - Balances
  - FixedCosts
  - UserDevices (Expo push tokens)

External dependencies:
- Plaid API (transactions sync + webhooks)
- Firebase Auth (token verification)
- Expo push service (server-side notifications)
- CloudWatch Logs (container logs)

## Mermaid architecture diagram

```mermaid
flowchart LR
  subgraph Mobile["Mobile (Expo / React Native)"]
    UI["App UI + Navigation"]
    FBClient["Firebase Auth SDK (client)"]
    Link["Plaid Link SDK"]
    ExpoNotif["expo-notifications"]
  end

  subgraph AWS["AWS"]
    ALB["Application Load Balancer"]
    ECS["ECS Service (Fargate)"]
    API[".NET container (Minimal API)"]
    CW["CloudWatch Logs"]
    ECR["ECR (container images)"]
  end

  subgraph Data["Data"]
    PG[("Postgres (EF Core)")]
  end

  Plaid["Plaid API"]
  Firebase["Firebase Admin / Auth"]
  ExpoPush["Expo Push Service"]

  UI --> FBClient
  UI --> Link
  UI --> ExpoNotif

  FBClient -->|sign-in| Firebase

  UI -->|HTTPS + Bearer ID token| ALB
  ALB --> ECS
  ECS --> API

  API --> PG
  API --> Plaid
  API --> Firebase
  API -->|send pushes| ExpoPush

  API --> CW
  ECR --> ECS