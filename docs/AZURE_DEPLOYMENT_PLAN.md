# Azure Deployment Plan

This document is the phased execution plan for deploying the Codec chat application to Azure (Central US region). Each phase is a self-contained unit of work that can be executed independently by an agent. Phases must be completed in order — each phase builds on the outputs of the previous one.

## Current State

| Component | Current (Dev) | Production Target |
|-----------|--------------|-------------------|
| Frontend | SvelteKit + Vite dev server (`adapter-auto`) | Containerized Node.js (`adapter-node`) on Azure Container Apps |
| Backend | ASP.NET Core 9 on localhost:5050 | Containerized .NET 9 on Azure Container Apps |
| Database | SQLite file (`codec.db`) | Azure Database for PostgreSQL Flexible Server |
| File Storage | Local filesystem (`uploads/avatars`, `uploads/images`) | Azure Blob Storage |
| Real-time | SignalR over WebSocket (single instance) | SignalR on single Container Apps instance (Redis backplane deferred) |
| Auth | Google ID token validation (JWT Bearer) | Same — add authorized origins for production domain |
| CI/CD | Build-only GitHub Actions workflow (`ci.yml`) | Full CI + CD + Infrastructure pipelines via GitHub Actions |
| IaC | None | Bicep modules under `infra/` |
| Secrets | `appsettings.json` / `.env` with empty values | Azure Key Vault + GitHub Secrets (OIDC federation) |
| CORS | Hardcoded `localhost:5174` | Environment-driven configuration |
| Health Checks | Basic `/health` endpoint | Liveness + readiness probes for Container Apps |
| Domain/TLS | localhost | `codec-chat.com` with managed TLS via Azure Container Apps |

### Target Architecture

```
                     ┌─────────────────────────────────────────────────────┐
                     │                Azure (Central US)                   │
                     │                                                     │
┌──────────┐        │  ┌──────────────────────────────────────────────┐   │
│  Users /  │  HTTPS │  │          Azure Container Apps Environment    │   │
│  Browser  │───────►│  │                                              │   │
└──────────┘        │  │  ┌────────────────┐  ┌────────────────────┐  │   │
                     │  │  │  Web App (ACA) │  │  API App (ACA)     │  │   │
                     │  │  │  SvelteKit     │──│  ASP.NET Core 9    │  │   │
                     │  │  │  Node.js 20    │  │  SignalR WebSocket │  │   │
                     │  │  │  adapter-node  │  │  EF Core 9         │  │   │
                     │  │  └────────────────┘  └────────┬───────────┘  │   │
                     │  │                                │              │   │
                     │  └────────────────────────────────┼──────────────┘   │
                     │                                   │                  │
                     │  ┌────────────────┐  ┌───────────┴──────────────┐   │
                     │  │  Azure Blob    │  │  Azure Database for      │   │
                     │  │  Storage       │  │  PostgreSQL               │   │
                     │  │  (avatars,     │  │  Flexible Server          │   │
                     │  │   images)      │  │  (Burstable B1ms)        │   │
                     │  └────────────────┘  └──────────────────────────┘   │
                     │                                                     │
                     │  ┌────────────────┐  ┌──────────────────────────┐   │
                     │  │  Azure         │  │  Azure Key Vault         │   │
                     │  │  Container     │  │  (secrets, connection     │   │
                     │  │  Registry      │  │   strings)               │   │
                     │  └────────────────┘  └──────────────────────────┘   │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  Log Analytics Workspace                     │   │
                     │  │  (container logs, metrics)                   │   │
                     │  └──────────────────────────────────────────────┘   │
                     └─────────────────────────────────────────────────────┘

                     ┌─────────────────────────────────────────────────────┐
                     │                  GitHub                             │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  GitHub Actions                              │   │
                     │  │  • CI pipeline  (build + test on PR/push)    │   │
                     │  │  • CD pipeline  (images → migrate → deploy)  │   │
                     │  │  • Infra pipeline (Bicep what-if → deploy)   │   │
                     │  └──────────────────────────────────────────────┘   │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  GitHub Secrets (OIDC federation → Azure)     │   │
                     │  │  No long-lived service principal secrets      │   │
                     │  └──────────────────────────────────────────────┘   │
                     └─────────────────────────────────────────────────────┘
```

### Azure Resource Summary

| Resource | SKU / Tier | Purpose |
|----------|-----------|---------|
| Resource Group | — | `rg-codec-prod` in Central US |
| Container Apps Environment | Consumption | Hosts API and Web containers |
| Container App — API | 0.5 vCPU / 1 GiB, min 1 max 1 | ASP.NET Core API + SignalR |
| Container App — Web | 0.25 vCPU / 0.5 GiB, min 0 max 2 | SvelteKit Node.js frontend |
| Container Registry | Basic | Docker image storage |
| PostgreSQL Flexible Server | Burstable B1ms, 32 GB | Relational data |
| Storage Account | Standard LRS, Hot | Avatar and image blob storage |
| Key Vault | Standard | Secrets management |
| Log Analytics Workspace | Pay-as-you-go | Container Apps logs and metrics |

---

## Phase 1 — Migrate Database from SQLite to PostgreSQL

**Goal:** Replace the SQLite EF Core provider with Npgsql (PostgreSQL) and ensure the application works with PostgreSQL locally.

### 1.1 Update NuGet packages

- [x] Remove `Microsoft.EntityFrameworkCore.Sqlite` from `Codec.Api.csproj`
- [x] Add `Npgsql.EntityFrameworkCore.PostgreSQL` (latest stable for EF Core 9)
- [x] Keep `Microsoft.EntityFrameworkCore.Design` for migration tooling

### 1.2 Update `Program.cs` database configuration

- [x] Replace `options.UseSqlite(connectionString)` with `options.UseNpgsql(connectionString)`
- [x] Update connection string in `appsettings.json` to PostgreSQL format:
  ```json
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password"
  }
  ```
- [x] Create `appsettings.Development.json` with local PostgreSQL connection string

### 1.3 Update `CodecDbContext` for PostgreSQL compatibility

- [x] **Remove the `DateTimeOffsetToStringConverter` workaround** — the `OnModelCreating` method currently applies `DateTimeOffsetToStringConverter` to all `DateTimeOffset` properties because SQLite doesn't support native `timestamptz`. PostgreSQL handles `DateTimeOffset` natively via `timestamptz`, so this converter must be deleted entirely.
- [x] Review the `CK_LinkPreview_SingleParent` check constraint — PostgreSQL uses standard SQL quoting; verify column name quoting is compatible
- [x] Confirm `Guid` primary keys map to PostgreSQL `uuid` type (they do natively with Npgsql)

### 1.4 Recreate EF Core migrations

- [x] Delete **all** existing migration files under `Migrations/` (they target SQLite schema)
- [x] Generate a fresh initial migration for PostgreSQL: `dotnet ef migrations add InitialPostgres`
- [x] Verify the generated migration SQL uses PostgreSQL syntax (`uuid`, `timestamptz`, etc.)

### 1.5 Add Docker Compose for local PostgreSQL

- [x] Create `docker-compose.dev.yml` at repository root:
  ```yaml
  services:
    postgres:
      image: postgres:16-alpine
      environment:
        POSTGRES_DB: codec_dev
        POSTGRES_USER: codec
        POSTGRES_PASSWORD: codec_dev_password
      ports:
        - "5432:5432"
      volumes:
        - pgdata:/var/lib/postgresql/data
  volumes:
    pgdata:
  ```

### 1.6 Update seed data

- [x] Verify `SeedData.cs` logic works with PostgreSQL (no SQLite-specific SQL)
- [x] Verify `UserService.cs` retry logic is compatible with PostgreSQL exception types

### 1.7 Update documentation

- [x] Update `docs/DEV_SETUP.md` with PostgreSQL prerequisites (Docker or local install)
- [x] Update `docs/ARCHITECTURE.md` data layer section
- [x] Update `docs/DATA.md` to reflect PostgreSQL

### 1.8 Verify

- [x] `dotnet build` — 0 errors
- [x] Start PostgreSQL via `docker-compose -f docker-compose.dev.yml up -d`
- [x] `dotnet ef database update` — migration applies cleanly
- [x] Run the API — seed data creates, all endpoints work

---

## Phase 2 — Migrate File Storage to Azure Blob Storage

**Goal:** Abstract file storage behind an interface so avatars and images can be stored in Azure Blob Storage in production while keeping local disk for development.

### 2.1 Add NuGet packages

- [x] Add `Azure.Storage.Blobs` to `Codec.Api.csproj`
- [x] Add `Azure.Identity` for Managed Identity authentication

### 2.2 Create storage abstraction

- [x] Create `IFileStorageService` interface with `UploadAsync`, `DeleteAsync`, `GetUrlAsync` methods
- [x] Implement `LocalFileStorageService` (wraps current behavior for local dev)
- [x] Implement `AzureBlobStorageService` using `BlobServiceClient` with `DefaultAzureCredential` (Managed Identity — no connection strings)
- [x] Use two blob containers: `avatars` and `images`

### 2.3 Refactor existing services

- [x] Update `AvatarService` to use `IFileStorageService` instead of direct disk I/O
- [x] Update `ImageUploadService` to use `IFileStorageService` instead of direct disk I/O
- [x] Select storage backend via configuration: `Storage:Provider` = `Local` | `AzureBlob`
- [x] Add `Storage:AzureBlobEndpoint` config for the blob account URL

### 2.4 Update `Program.cs` service registration

- [x] Register `IFileStorageService` based on `Storage:Provider` config
- [x] Keep `PhysicalFileProvider` static file middleware only when `Storage:Provider=Local`
- [x] In production, blobs are served directly from Azure Blob Storage URLs

### 2.5 Update configuration

- [x] Add to `appsettings.json`:
  ```json
  "Storage": {
    "Provider": "Local"
  }
  ```
- [x] Document production override: `"Provider": "AzureBlob"`, `"AzureBlobEndpoint": "https://<account>.blob.core.windows.net"`

### 2.6 Verify

- [x] `dotnet build` — 0 errors
- [x] Run in dev mode with `Storage:Provider=Local` — local disk path works as before
- [x] Avatar upload, image upload, and URL resolution work

---

## Phase 3 — Prepare SvelteKit Web App for Production

**Goal:** Switch from `adapter-auto` to `adapter-node`, externalize configuration, and add a health endpoint.

### 3.1 Install `adapter-node`

- [x] Run `npm install -D @sveltejs/adapter-node` in `apps/web/`
- [x] Remove `@sveltejs/adapter-auto` from `devDependencies`

### 3.2 Update `svelte.config.js`

- [x] Replace the adapter import:
  ```js
  import adapter from '@sveltejs/adapter-node';
  ```
- [x] Configure output directory:
  ```js
  adapter: adapter({ out: 'build' })
  ```

### 3.3 Add a health check endpoint

- [x] Create `apps/web/src/routes/health/+server.ts`:
  ```typescript
  export function GET() {
    return new Response(JSON.stringify({ status: 'healthy' }), {
      headers: { 'content-type': 'application/json' }
    });
  }
  ```

### 3.4 Type environment variables

- [x] Declare `PUBLIC_API_BASE_URL` and `PUBLIC_GOOGLE_CLIENT_ID` in `app.d.ts`
- [x] Verify all API calls and the SignalR hub connection URL derive from `PUBLIC_API_BASE_URL`

### 3.5 Verify

- [x] `npm run build` — outputs to `build/` directory
- [x] `node build/index.js` — serves the app on port 3000
- [x] `npm run check` — 0 errors

---

## Phase 4 — Prepare the API for Production Hosting

**Goal:** Add production-readiness features: enhanced health checks, environment-driven CORS, forwarded headers, and structured logging.

### 4.1 Enhance health checks

- [x] Add `AspNetCore.HealthChecks.NpgSql` NuGet package (or use built-in EF Core health check)
- [x] Map liveness probe: `/health/live` (always returns 200 — proves the process is running)
- [x] Map readiness probe: `/health/ready` (includes DB connectivity check)
- [x] Keep existing `/health` endpoint for backward compatibility

### 4.2 Environment-driven CORS

- [x] Rename the CORS policy from `"dev"` to `"default"` in `Program.cs`
- [x] `Cors:AllowedOrigins` is already read from config — verify it works with production URLs like `https://codec-chat.com`
- [x] Update `app.UseCors("dev")` → `app.UseCors("default")`

### 4.3 Add Forwarded Headers middleware

- [x] Azure Container Apps terminates TLS and proxies requests — the API must trust forwarded headers:
  ```csharp
  app.UseForwardedHeaders(new ForwardedHeadersOptions
  {
      ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
  });
  ```
- [x] Place this **before** `UseAuthentication()` so JWT issuer validation uses the correct scheme

### 4.4 Add structured logging (Serilog)

- [x] Add NuGet packages: `Serilog.AspNetCore`, `Serilog.Sinks.Console`
- [x] Configure Serilog in `Program.cs` with structured JSON console output:
  ```csharp
  builder.Host.UseSerilog((ctx, config) => config
      .ReadFrom.Configuration(ctx.Configuration)
      .WriteTo.Console(new RenderedCompactJsonFormatter()));
  ```
- [x] JSON output is auto-ingested by Azure Monitor via Container Apps Log Analytics integration
- [x] Add request logging middleware: `app.UseSerilogRequestLogging()`

### 4.5 Production database migration strategy

- [x] The current `db.Database.Migrate()` call is guarded by `IsDevelopment()` — verify this remains in place
- [x] For production, migrations will run as a **separate CD pipeline job** (EF Core migration bundle), not on app startup
- [x] This avoids race conditions if multiple API replicas start simultaneously in the future

### 4.6 Externalize all configuration

Audit `Program.cs` and `appsettings.json` for anything that must change per environment:

| Setting | Source |
|---------|--------|
| `ConnectionStrings:Default` | Key Vault → Container Apps secret |
| `Google:ClientId` | Key Vault → Container Apps secret |
| `Api:BaseUrl` | Container Apps env var |
| `Cors:AllowedOrigins` | Container Apps env var (JSON array) |
| `Storage:Provider` | Container Apps env var (`AzureBlob`) |
| `Storage:AzureBlobEndpoint` | Container Apps env var |
| `PUBLIC_API_BASE_URL` | Build-time env var for SvelteKit |
| `PUBLIC_GOOGLE_CLIENT_ID` | Build-time env var for SvelteKit |

### 4.7 Verify

- [x] `dotnet build` — 0 errors
- [x] Health endpoints return expected JSON responses
- [x] CORS headers present in API responses with configured origins
- [x] Serilog outputs structured JSON to console

---

## Phase 5 — Containerize Both Applications

**Goal:** Create optimized multi-stage Dockerfiles for the API and Web apps.

### 5.1 Create API Dockerfile (`apps/api/Dockerfile`)

- [x] Multi-stage build:
  - **Build stage:** `mcr.microsoft.com/dotnet/sdk:9.0` — restore, build, publish
  - **Runtime stage:** `mcr.microsoft.com/dotnet/aspnet:9.0` — copy published output, set entrypoint
- [x] Expose port 8080 (`ASPNETCORE_URLS=http://+:8080`)
- [x] Set `ASPNETCORE_ENVIRONMENT=Production` as default
- [x] Run as non-root user for security
- [x] Add `.dockerignore` excluding `bin/`, `obj/`, `*.db`

### 5.2 Create Web Dockerfile (`apps/web/Dockerfile`)

- [x] Multi-stage build:
  - **Build stage:** `node:20-alpine` — `npm ci && npm run build`
  - **Runtime stage:** `node:20-alpine` — copy `build/`, `package.json`, install prod deps, `node build/index.js`
- [x] Expose port 3000 (adapter-node default)
- [x] Run as non-root user
- [x] Add `.dockerignore` excluding `node_modules/`, `.svelte-kit/`, `build/`

### 5.3 Create `docker-compose.yml` for full integration testing

- [x] Include: PostgreSQL 16, API container, Web container, Azurite (Azure Storage emulator)
- [x] Configure inter-container networking
- [x] Map environment variables for each service

### 5.4 Verify

- [x] `docker build -t codec-api ./apps/api` — builds successfully
- [x] `docker build -t codec-web ./apps/web` — builds successfully
- [ ] `docker-compose up` — full stack starts, API connects to PostgreSQL, Web connects to API
- [ ] End-to-end test: sign in, send a message, upload an image

---

## Phase 6 — Infrastructure as Code (Bicep)

**Goal:** Define all Azure resources using Bicep modules under `infra/`.

### 6.1 Project structure

```
infra/
├── main.bicep                    # Orchestrator — deploys all modules
├── main.bicepparam               # Parameter file (prod values)
├── modules/
│   ├── log-analytics.bicep       # Log Analytics workspace
│   ├── container-registry.bicep  # Azure Container Registry
│   ├── postgresql.bicep          # PostgreSQL Flexible Server
│   ├── storage-account.bicep     # Blob storage for uploads
│   ├── key-vault.bicep           # Azure Key Vault
│   ├── container-apps-env.bicep  # Container Apps Environment
│   ├── container-app-api.bicep   # API Container App
│   └── container-app-web.bicep   # Web Container App
```

### 6.2 Create `main.bicep` orchestrator

- [x] Define parameters: `location`, `environmentName`, `googleClientId`
- [x] Default `location` to `'centralus'`
- [x] Naming convention: `{resourceAbbrev}-codec-{env}` (e.g., `rg-codec-prod`, `acr-codec-prod`)
- [x] Wire module outputs as inputs to dependent modules
- [x] `targetScope = 'resourceGroup'`

### 6.3 Log Analytics module

- [x] Resource: `Microsoft.OperationalInsights/workspaces`
- [x] SKU: `PerGB2018`
- [x] Retention: 30 days

### 6.4 Container Registry module

- [x] Resource: `Microsoft.ContainerRegistry/registries`
- [x] SKU: `Basic`
- [x] Admin user: **disabled** (use Managed Identity for AcrPull)

### 6.5 PostgreSQL Flexible Server module

- [x] Resource: `Microsoft.DBforPostgreSQL/flexibleServers`
- [x] SKU: `Standard_B1ms` (Burstable)
- [x] PostgreSQL version: 16
- [x] Storage: 32 GB
- [x] Configure firewall to allow Azure services
- [x] Store admin password in Key Vault
- [x] Create the `codec` database
- [x] Automated backups: 7-day retention (default)
- [ ] Consider enabling PgBouncer (built into Flexible Server) for connection pooling

### 6.6 Storage Account module

- [x] Resource: `Microsoft.Storage/storageAccounts`
- [x] SKU: `Standard_LRS`, Kind: `StorageV2`, Access tier: `Hot`
- [x] Disable shared key access — use RBAC via Managed Identity
- [x] Create blob containers: `avatars`, `images`
- [x] Enable public blob access for avatar/image serving

### 6.7 Key Vault module

- [x] Resource: `Microsoft.KeyVault/vaults`
- [x] SKU: `standard`
- [x] Enable RBAC authorization (not legacy access policies)
- [x] Secrets: PostgreSQL connection string, Google Client ID

### 6.8 Container Apps Environment module

- [x] Resource: `Microsoft.App/managedEnvironments`
- [x] Link to Log Analytics workspace
- [x] Workload profile: Consumption

### 6.9 Container App modules (API and Web)

**API Container App:**
- [x] Image: `{acrName}.azurecr.io/codec-api:latest`
- [x] CPU: 0.5, Memory: 1.0Gi
- [x] Min replicas: 1, Max replicas: 1 (single instance for SignalR — no Redis backplane needed)
- [x] Ingress: external, port 8080, transport HTTP
- [x] **Enable WebSocket connections** for SignalR
- [x] Liveness probe: `GET /health/live` (HTTP, period 30s)
- [x] Readiness probe: `GET /health/ready` (HTTP, period 10s)
- [x] Environment variables and secrets from Key Vault references
- [x] System-assigned Managed Identity with RBAC:
  - `AcrPull` on Container Registry
  - `Storage Blob Data Contributor` on Storage Account
  - `Key Vault Secrets User` on Key Vault

**Web Container App:**
- [x] Image: `{acrName}.azurecr.io/codec-web:latest`
- [x] CPU: 0.25, Memory: 0.5Gi
- [x] Min replicas: 0, Max replicas: 2 (scale to zero when idle)
- [x] Ingress: external, port 3000, transport HTTP
- [x] Liveness probe: `GET /health` (HTTP, period 30s)
- [x] Environment variables: `PUBLIC_API_BASE_URL`, `PUBLIC_GOOGLE_CLIENT_ID`
- [x] System-assigned Managed Identity with RBAC: `AcrPull` on Container Registry

### 6.10 Create parameter file

- [x] Create `main.bicepparam`:
  ```bicep
  using './main.bicep'

  param location = 'centralus'
  param environmentName = 'prod'
  ```

### 6.11 Verify

- [x] `az bicep build --file infra/main.bicep` — compiles with 0 errors
- [ ] `az deployment group what-if` — review planned resource creation

---

## Phase 7 — CI Pipeline (GitHub Actions)

**Goal:** Enhance the existing CI pipeline to build, test, lint, and validate Docker images. Runs on every push to `main` and on pull requests.

### 7.1 Enhance `ci.yml` workflow

- [x] Add `permissions: contents: read` at workflow level
- [x] Add concurrency group to cancel redundant PR runs:
  ```yaml
  concurrency:
    group: ci-${{ github.ref }}
    cancel-in-progress: true
  ```

### 7.2 Add API test job

- [ ] Add `test-api` job (depends on `build-api`) — deferred, no test project exists yet
- [ ] Start a PostgreSQL service container for integration tests
- [ ] Run `dotnet test` with test result output
- [ ] Upload test results as artifacts

### 7.3 Add Web lint/check job

- [x] Add `check-web` job
- [x] Run `npm run check` (includes `svelte-check`)
- [x] Run the deprecated events check script (included in `npm run check` via `lint:events`)

### 7.4 Add Docker build validation jobs

- [x] `docker-build-api` (depends on `build-api`): build API image, tag with `${{ github.sha }}`, do **not** push
- [x] `docker-build-web` (depends on `build-web`): build Web image, tag with `${{ github.sha }}`, do **not** push

### 7.5 Add caching

- [x] Cache NuGet packages: `~/.nuget/packages` keyed on `**/Codec.Api.csproj` hash
- [x] Verify npm caching via `actions/setup-node@v4` (already configured via `cache: npm`)

### 7.6 Verify

- [ ] Push a test branch — all CI jobs pass
- [ ] Docker images build successfully in CI
- [ ] Caching reduces build times on subsequent runs

> Note: Verification requires pushing to GitHub and running the workflow.

---

## Phase 8 — Infrastructure Pipeline (GitHub Actions)

**Goal:** Create a GitHub Actions workflow that provisions and updates Azure infrastructure using Bicep.

### 8.1 Set up Azure OIDC authentication

- [ ] Create a Microsoft Entra ID App Registration for GitHub Actions
- [ ] Configure **Federated Identity Credentials** for the `main` branch and `prod` environment
- [ ] Add GitHub repository secrets:
  - `AZURE_CLIENT_ID` — App Registration client ID
  - `AZURE_TENANT_ID` — Entra tenant ID
  - `AZURE_SUBSCRIPTION_ID` — Azure subscription ID
- [ ] Document the OIDC setup process

> Note: These are manual Azure portal / CLI steps that must be performed before running the workflow.

### 8.2 Create `infra.yml` workflow

- [x] Trigger: `workflow_dispatch` (manual) and push to `main` when `infra/**` files change
- [x] Permissions: `id-token: write`, `contents: read`
- [x] Job: `deploy-infra`
  - Checkout code
  - Login to Azure using OIDC (`azure/login@v2`)
  - Run `az deployment group what-if` to preview changes
  - Run `az deployment group create --resource-group rg-codec-prod --template-file infra/main.bicep --parameters infra/main.bicepparam`
  - Output deployed resource URLs/names

### 8.3 Add environment protection

- [x] Create GitHub Environment `prod` with manual approval required for `infra.yml`
- [x] Scope deployment job to the `prod` environment

> Note: The `prod` environment with approval rules must be configured manually in GitHub repository settings.

### 8.4 Verify

- [ ] Trigger workflow manually
- [ ] `what-if` output shows expected resources
- [ ] Deployment creates all resources successfully
- [ ] Resources appear in Azure Portal under `rg-codec-prod`

---

## Phase 9 — CD Pipeline (GitHub Actions)

**Goal:** Build Docker images, push to ACR, run database migrations, and deploy to Container Apps. Triggered on push to `main` after CI passes.

### 9.1 Create `cd.yml` workflow

- [x] Trigger: `workflow_run` on successful CI on `main`, plus `workflow_dispatch`
- [x] Permissions: `id-token: write`, `contents: read`
- [x] Concurrency: prevent parallel deployments
  ```yaml
  concurrency:
    group: cd-prod
    cancel-in-progress: false
  ```

### 9.2 `build-and-push` job

- [x] Login to Azure via OIDC
- [x] Login to ACR (`az acr login`)
- [x] Build API Docker image, tag with `${{ github.sha }}` and `latest`, push to ACR
- [x] Build Web Docker image (pass `PUBLIC_API_BASE_URL`, `PUBLIC_GOOGLE_CLIENT_ID` as build args), tag, push to ACR
- [x] Output image tags for deploy job

### 9.3 `migrate-database` job

- [x] Depends on `build-and-push`
- [x] Install .NET SDK and EF Core tools
- [x] Generate an EF Core migration bundle:
  ```bash
  dotnet ef migrations bundle --project apps/api/Codec.Api --output efbundle --self-contained
  ```
- [x] Run the bundle against production PostgreSQL (connection string from Key Vault / GitHub Secret)
- [x] This is **safer than `Migrate()` on app startup** — avoids race conditions with multiple replicas and ensures migration failures block deployment

### 9.4 `deploy` job

- [x] Depends on `migrate-database`
- [x] GitHub Environment: `prod` (with manual approval)
- [x] Login to Azure via OIDC
- [x] Update API Container App:
  ```bash
  az containerapp update \\
    --name ca-codec-api-prod \\
    --resource-group rg-codec-prod \\
    --image {acrName}.azurecr.io/codec-api:${{ github.sha }}
  ```
- [x] Update Web Container App:
  ```bash
  az containerapp update \\
    --name ca-codec-web-prod \\
    --resource-group rg-codec-prod \\
    --image {acrName}.azurecr.io/codec-web:${{ github.sha }}
  ```
- [x] Wait for revisions to become active

### 9.5 `smoke-test` job

- [x] Depends on `deploy`
- [x] `curl https://{api-fqdn}/health/ready` — expect 200
- [x] `curl https://{web-fqdn}/health` — expect 200
- [x] Report pass/fail

### 9.6 Rollback documentation

- [x] Document rollback by redeploying previous image:
  ```bash
  az containerapp update --name ca-codec-api-prod --image {acrName}.azurecr.io/codec-api:{previous-sha}
  ```
- [x] Document database migration rollback process

### 9.7 Verify

- [ ] Trigger deployment via push to `main`
- [ ] Images pushed to ACR with correct tags
- [ ] Database migration runs successfully
- [ ] Container Apps update to new revisions
- [ ] Smoke tests pass
- [ ] Application accessible via Web Container App FQDN

---

## Phase 10 — Production Hardening, Domain & Documentation

**Goal:** Apply security hardening, configure the production domain, set up monitoring, and finalize documentation.

### 10.1 Domain & DNS

- [ ] Configure `codec-chat.com` DNS (registered via Squarespace Domains)
- [ ] Point DNS to Azure Container Apps via CNAME or transfer DNS to Azure DNS
- [ ] Map `codec-chat.com` → Web Container App (custom domain binding)
- [ ] Map `api.codec-chat.com` → API Container App (custom domain binding)
- [ ] Azure-managed TLS certificates via Container Apps managed certificates
- [ ] Update Google OAuth console: add `https://codec-chat.com` and `https://api.codec-chat.com` as authorized JavaScript origins

### 10.2 Security hardening

- [ ] Enable HTTPS-only on both Container Apps (redirect HTTP → HTTPS)
- [x] Configure Content Security Policy (CSP) headers in SvelteKit response hooks
- [x] Add rate limiting middleware to the API (`Microsoft.AspNetCore.RateLimiting`)
- [ ] Verify Key Vault is the sole source of secrets in production
- [ ] Verify Managed Identity is used for all Azure service-to-service auth (no connection strings for blob access)
- [ ] Review CORS origins — only allow the production domain(s)
- [ ] Integrate Trivy or Microsoft Defender for Containers in CI for image scanning
- [x] Verify non-root user in both Dockerfiles

### 10.3 Monitoring & alerting

- [ ] Verify Container Apps logs stream to Log Analytics
- [ ] Configure Azure Monitor alerts:
  - API container restarts
  - 5xx error rate > threshold
  - Database CPU > 80%
  - Replica scaling events
- [ ] Verify Serilog structured JSON logs appear in Log Analytics
- [ ] Create basic Azure Monitor dashboard: request latency, error rate, replica count, CPU/memory

### 10.4 Database operations

- [ ] Verify automated backups are enabled (7-day retention, included with Flexible Server)
- [ ] Verify Npgsql connection pooling settings
- [ ] Document point-in-time recovery procedure

### 10.5 Documentation

- [x] Update `docs/ARCHITECTURE.md` with production deployment diagram
- [x] Update `docs/DEV_SETUP.md` with PostgreSQL + Docker instructions
- [x] Create `docs/DEPLOYMENT.md` covering:
  - How to provision infrastructure (trigger `infra.yml`)
  - How to deploy the application (push to `main` or trigger `cd.yml`)
  - How to rollback a deployment
  - How to run database migrations manually
  - Environment variables and secrets reference
  - Troubleshooting guide
- [x] Update `README.md` with deployment status badges and live URL

### 10.6 Final verification

- [ ] Full end-to-end test on the live deployment:
  - Sign in with Google
  - Create a server and channels
  - Send messages (text, images, replies)
  - Test emoji reactions
  - Test friend requests and DMs
  - Verify SignalR real-time features (typing indicators, live message delivery)
- [ ] Verify WebSocket connections work through Azure Container Apps
- [ ] Confirm avatar and image uploads persist in Azure Blob Storage

---

## SignalR Scaling Strategy

> **Decision:** For the MVP, the API runs as a single Container Apps instance (`max-replicas=1`), so in-memory SignalR is sufficient. Redis backplane will be added when scaling beyond one instance is needed.

When needed (post-MVP):
- Add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` NuGet package
- Conditionally enable: `builder.Services.AddSignalR().AddStackExchangeRedis(connectionString)`
- Use Azure Cache for Redis (~$40/month for Basic C0)
- Increase API `max-replicas` from 1 to 3+

---

## Phase Summary

| Phase | Description | Depends On | Key Outputs |
|-------|-------------|-----------|-------------|
| 1 | Migrate SQLite → PostgreSQL | — | Npgsql provider, fresh migration, `docker-compose.dev.yml` |
| 2 | File storage → Azure Blob | Phase 1 | `IFileStorageService`, config-driven storage |
| 3 | Prepare SvelteKit for production | — | `adapter-node`, health endpoint, typed env vars |
| 4 | Prepare API for production | Phase 1 | Health probes, Serilog, CORS, forwarded headers |
| 5 | Containerize both apps | Phases 3, 4 | Dockerfiles, `docker-compose.yml` |
| 6 | Infrastructure as Code (Bicep) | — | `infra/` directory with all Bicep modules |
| 7 | CI pipeline | Phase 5 | Enhanced `ci.yml` with Docker builds and tests |
| 8 | Infrastructure pipeline | Phase 6 | `infra.yml`, OIDC auth, Azure resources provisioned |
| 9 | CD pipeline | Phases 7, 8 | `cd.yml`, automated deployment to Container Apps |
| 10 | Hardening, domain & docs | Phase 9 | Security, domain, monitoring, operational docs |

> **Parallelism:** Phases 1–2 and Phases 3–4 can be worked on in parallel (different apps). Phase 6 can also start in parallel with Phases 1–5 (purely IaC authoring).

---

## Cost Estimate (MVP)

Rough monthly cost for a low-traffic production environment:

| Resource | Spec | Est. Monthly Cost |
|----------|------|-------------------|
| Container Apps — API | 0.5 vCPU, 1 GiB, min 1 / max 1 | ~$15–25 |
| Container Apps — Web | 0.25 vCPU, 0.5 GiB, min 0 / max 2 | ~$5–10 |
| PostgreSQL Flexible Server | Burstable B1ms, 32 GB storage | ~$12–18 |
| Storage Account (Blob) | < 1 GB | ~$0.02 |
| Container Registry (Basic) | < 5 GB | ~$5 |
| Key Vault | < 10 secrets | ~$0.03 |
| Log Analytics Workspace | Free tier (5 GB/month) | ~$0 |
| **Total** | | **~$37–58/month** |

> **Notes:**
> - Web frontend at `min-replicas=0` means zero cost when idle.
> - API at `min-replicas=1` / `max-replicas=1` to maintain WebSocket connections without Redis.
> - Adding Azure Cache for Redis (future) would add ~$40/month for Basic C0.

---

## Decisions

| # | Decision | Answer |
|---|----------|--------|
| 1 | **Domain name** | `codec-chat.com` |
| 2 | **Azure subscription** | Use existing Azure subscription |
| 3 | **Azure region** | `centralus` |
| 4 | **IaC tool** | Bicep (Azure-native, no state management needed) |
| 5 | **Database migration strategy** | EF Core migration bundle run as a separate CD pipeline job (not on app startup) |
| 6 | **Redis vs. single-instance** | Single API instance (`max-replicas=1`) for MVP; Redis backplane deferred |
| 7 | **CDN for uploads** | Direct Azure Blob Storage serving — no CDN for MVP |
| 8 | **Structured logging** | Serilog with JSON console output → Log Analytics |
| 9 | **Auth for Azure services** | Managed Identity + RBAC (no connection strings for blob/ACR) |
| 10 | **GitHub → Azure auth** | OIDC federated credentials (no long-lived secrets) |
