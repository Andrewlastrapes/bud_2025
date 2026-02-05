# =========
# BASE RUNTIME
# =========
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# App Runner expects 8080 by default
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# =========
# BUILD IMAGE
# =========
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1) Copy the csproj for layer caching
COPY BudgetApp.Api/BudgetApp.Api.csproj BudgetApp.Api/

# 2) Restore dependencies
RUN dotnet restore BudgetApp.Api/BudgetApp.Api.csproj

# 3) Copy the rest of the source
COPY . .

# 4) Publish the app
WORKDIR /src/BudgetApp.Api
RUN dotnet publish "BudgetApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =========
# FINAL IMAGE
# =========
FROM base AS final
WORKDIR /app

# 5) Copy the firebase service account JSON to where Program.cs expects it
#    Program.cs uses: GoogleCredential.FromFile("firebase-service-account.json")
COPY ./BudgetApp.Api/firebase-service-account.json ./firebase-service-account.json

# 6) Copy the published app from the build stage
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BudgetApp.Api.dll"]
