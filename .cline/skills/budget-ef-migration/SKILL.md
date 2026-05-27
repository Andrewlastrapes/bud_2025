Use the `budget-ef-migration` skill.

Context:
We are in the repo root:

/Users/andrewlastrapes/Desktop/Projects/andrew/bud_2025

Confirmed project paths:

- Backend project: BudgetApp.Api/BudgetApp.Api.csproj
- Test project: BudgetApp.Api/BudgetApp.Api.Tests/BudgetApp.Api.Tests.csproj
- Migrations folder: BudgetApp.Api/Migrations

Confirmed production migration behavior:

- BudgetApp.Api/Program.cs calls db.Database.Migrate() in non-development.
- So do not run migrations against production.
- Create/apply locally, commit migration files, and production will apply on backend startup after deploy.

Task:
Create and apply the EF migration for the new User property:

[Column("debt_review_required")]
public bool DebtReviewRequired { get; set; } = false;

Migration name:
AddDebtReviewRequiredToUsers

Please do the following:

1. Confirm the User.cs model includes the DebtReviewRequired property.
2. If it is missing, add it to BudgetApp.Api/Data/User.cs with the proper Column attribute.
3. Run the EF migration add command from the repo root:
   dotnet ef migrations add AddDebtReviewRequiredToUsers --project BudgetApp.Api --startup-project BudgetApp.Api
4. Apply it locally:
   dotnet ef database update --project BudgetApp.Api --startup-project BudgetApp.Api
5. Verify the local database column exists:
   psql -d budget -c "
   SELECT column_name, data_type, column_default, is_nullable
   FROM information_schema.columns
   WHERE table_name = 'Users'
   AND column_name = 'debt_review_required';
   "
6. Run:
   dotnet build BudgetApp.Api
   dotnet test BudgetApp.Api/BudgetApp.Api.Tests
7. Show me:
   - exact files changed
   - migration files created
   - local DB verification output
   - build/test output
   - suggested commit message

Do not connect to or modify production manually.
