# Broot.Redirect

A URL redirect management system for handling domain migrations and legacy URLs. Built with .NET 8 and Angular 19, backed by Azure Table Storage.

Manages redirect rules with multiple matching strategies (wildcard, partial, domain, regex), quality scoring, query parameter handling, and search-and-replace transformations. Includes an admin panel for rule management, analytics, bulk import/export, and a public-facing info page that shows users where their old URL now points.

## Project Structure

```
Broot.Redirect.API/            # ASP.NET Core REST API
Broot.Redirect.Core/           # Domain models and service interfaces
Broot.Redirect.Infrastructure/ # Azure Table Storage persistence, caching
Broot.Redirect.Client/         # Angular 19 frontend
Broot.Redirect.Tests/          # xUnit tests
```

## Local Development

### Prerequisites

- .NET 8 SDK
- Node.js 22
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (Azure Storage emulator) or Docker

### Option 1: Docker Compose (recommended)

```bash
docker-compose up
```

This starts both the app on `http://localhost:8080` and an Azurite container for storage. No other setup needed.

### Option 2: Run Individually

Start Azurite (if not using Docker):

```bash
azurite --tableHost 127.0.0.1 --tablePort 10002
```

Start the API:

```bash
cd Broot.Redirect.API
dotnet run
```

The API starts on `https://localhost:7233`.

Start the Angular dev server:

```bash
cd Broot.Redirect.Client
npm install
npm start
```

The frontend runs on `http://localhost:4200` and proxies `/api` requests to the backend.

### Default Login

The default admin password is `Password1`. Change it via the `SmartRedirect__AdminPassword` environment variable.

## Running Tests

```bash
dotnet test
```

Tests use xUnit with NSubstitute for mocking and FluentAssertions. Coverage reports can be generated locally with AltCover (configured in the test project).

## Environment Variables

Configuration follows ASP.NET Core conventions. Override any `appsettings.json` value using double-underscore notation (e.g., `SmartRedirect__AdminPassword`).

### Core Settings (`SmartRedirect__*`)

| Variable | Description | Default |
|---|---|---|
| `SmartRedirect__AdminPassword` | Admin panel password (compared via SHA256) | `Password1` |
| `SmartRedirect__SessionTimeoutDays` | Admin session idle timeout in days | `7` |
| `SmartRedirect__TrackingRetentionDays` | Days to retain tracking data before cleanup | `30` |

### URL Matching (`SmartRedirect__*`)

| Variable | Description | Default |
|---|---|---|
| `SmartRedirect__CaseSensitivePath` | Case-sensitive path matching | `false` |
| `SmartRedirect__CaseSensitiveQuery` | Case-sensitive query parameter matching | `false` |
| `SmartRedirect__TrailingSlashPolicy` | `ignore`, `require`, or `strip` | `ignore` |
| `SmartRedirect__RegexMatchTimeoutSeconds` | Timeout for regex pattern matching | `1` |

### Match Scoring (`SmartRedirect__*`)

| Variable | Description | Default |
|---|---|---|
| `SmartRedirect__WeightPathSegment` | Score weight for path segment matches | `10` |
| `SmartRedirect__WeightQueryPair` | Score weight for query pair matches | `5` |
| `SmartRedirect__PenaltyWildcard` | Score penalty for wildcard matches | `1` |
| `SmartRedirect__BonusExactMatch` | Score bonus for exact matches | `50` |

### Rate Limiting (`SmartRedirect__*`)

| Variable | Description | Default |
|---|---|---|
| `SmartRedirect__RateLimitGlobalMax` | Max requests per window (general) | `300` |
| `SmartRedirect__RateLimitTrackingMax` | Max requests per window (tracking endpoints) | `300` |
| `SmartRedirect__RateLimitAdminMax` | Max requests per window (admin endpoints) | `60` |
| `SmartRedirect__RateLimitWindowSeconds` | Rate limit window duration in seconds | `60` |

### Brute Force Protection (`SmartRedirect__*`)

| Variable | Description | Default |
|---|---|---|
| `SmartRedirect__LoginMaxAttempts` | Failed login attempts before blocking | `5` |
| `SmartRedirect__LoginBlockDurationMinutes` | Block duration after max attempts exceeded | `1440` (24h) |

### Azure Table Storage (`AzureTableStorage__*`)

| Variable | Description | Default |
|---|---|---|
| `AzureTableStorage__ConnectionString` | Azure Table Storage connection string | Azurite local dev account |
| `AzureTableStorage__TableName` | Table name for redirect rules | `RedirectRules` |

### Telemetry

| Variable | Description | Default |
|---|---|---|
| `APPLICATIONINSIGHTS__CONNECTIONSTRING` | Application Insights connection string (optional) | None (telemetry disabled) |

### ASP.NET Core

| Variable | Description | Default |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging`, or `Production` | `Development` |
| `ASPNETCORE_HTTP_PORTS` | HTTP port binding | `8080` |

## Deploying to Azure

### Azure Container Apps (recommended)

1. **Create resources:**

   ```bash
   az group create --name broot-redirect-rg --location westeurope

   az storage account create \
     --name brootredirectstore \
     --resource-group broot-redirect-rg \
     --sku Standard_LRS \
     --kind StorageV2

   az containerapp env create \
     --name broot-redirect-env \
     --resource-group broot-redirect-rg \
     --location westeurope
   ```

2. **Get the storage connection string:**

   ```bash
   az storage account show-connection-string \
     --name brootredirectstore \
     --resource-group broot-redirect-rg \
     --query connectionString -o tsv
   ```

3. **Deploy the container:**

   The CI pipeline pushes images to `ghcr.io`. Deploy the latest image:

   ```bash
   az containerapp create \
     --name broot-redirect \
     --resource-group broot-redirect-rg \
     --environment broot-redirect-env \
     --image ghcr.io/<your-repo>/broot.redirect:latest \
     --target-port 8080 \
     --ingress external \
     --env-vars \
       ASPNETCORE_ENVIRONMENT=Production \
       SmartRedirect__AdminPassword=<your-password> \
       AzureTableStorage__ConnectionString=<connection-string>
   ```

### Azure App Service

1. **Create the App Service:**

   ```bash
   az appservice plan create \
     --name broot-redirect-plan \
     --resource-group broot-redirect-rg \
     --sku B1 \
     --is-linux

   az webapp create \
     --name broot-redirect \
     --resource-group broot-redirect-rg \
     --plan broot-redirect-plan \
     --container-image-name ghcr.io/<your-repo>/broot.redirect:latest
   ```

2. **Configure environment variables:**

   ```bash
   az webapp config appsettings set \
     --name broot-redirect \
     --resource-group broot-redirect-rg \
     --settings \
       ASPNETCORE_ENVIRONMENT=Production \
       SmartRedirect__AdminPassword=<your-password> \
       AzureTableStorage__ConnectionString=<connection-string> \
       WEBSITES_PORT=8080
   ```

### Production Checklist

- Set `SmartRedirect__AdminPassword` to a strong password
- Set `ASPNETCORE_ENVIRONMENT=Production` (disables Swagger, enforces secure cookies)
- Point `AzureTableStorage__ConnectionString` to a real Azure Storage account
- Optionally set `APPLICATIONINSIGHTS__CONNECTIONSTRING` for monitoring
