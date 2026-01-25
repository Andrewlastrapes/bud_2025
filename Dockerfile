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

# Copy csproj first for better layer caching
COPY BudgetApp.Api/BudgetApp.Api.csproj BudgetApp.Api/
RUN dotnet restore BudgetApp.Api/BudgetApp.Api.csproj

# Copy the rest of the source
COPY . .

WORKDIR /src/BudgetApp.Api
RUN dotnet publish "BudgetApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =========
# FINAL IMAGE
# =========
FROM base AS final
WORKDIR /app

# If you keep firebase-service-account.json at repo root (gitignored),
# copy it into the container. Adjust path if you keep it elsewhere.
COPY ./BudgetApp.Api/firebase-service-account.json ./firebase-service-account.json

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BudgetApp.Api.dll"]
