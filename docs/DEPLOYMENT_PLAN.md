# Codec Alpha Deployment Plan

## Overview

This document is the phased execution plan for deploying the Codec chat application to Azure (Central US region). Each phase is a self-contained unit of work that an agent can execute independently. Phases must be completed in order — each phase builds on the outputs of the previous one.

### Current State

| Concern | Current | Target |
|---------|---------|--------|
| Database | SQLite (file-based) | Azure Database for PostgreSQL Flexible Server |
| File storage | Local disk (`uploads/avatars`, `uploads/images`) | Azure Blob Storage |
| Web adapter | `@sveltejs/adapter-auto` | `@sveltejs/adapter-node` |
| Containers | None | Dockerfiles for API and Web |
| Container registry | None | Azure Container Registry (ACR) |
| Hosting | Local dev servers | Azure Container Apps (ACA) |
| IaC | None | Bicep modules under `infra/` |
| CI/CD | Build-only (`ci.yml`) | Full CI + CD + Infrastructure pipelines via GitHub Actions |
| Secrets | Hardcoded in config files | Azure Key Vault + GitHub Secrets |
| CORS | Hardcoded localhost | Environment-driven configuration |
| Health checks | Basic `/health` endpoint | Readiness + liveness probes for ACA |

### Target Architecture (MVP)

```
                     ┌─────────────────────────────────────────────────────┐
                     │                  Azure (Central US)                 │
                     │                                                     │
┌──────────┐        │  ┌──────────────────────────────────────────────┐   │
│  Users /  │  HTTPS │  │          Azure Container Apps Environment    │   │
│  Browser  │───────►│  │                                              │   │
└──────────┘        │  │  ┌────────────────┐  ┌────────────────────┐  │   │
                     │  │  │  Web App (ACA) │  │  API App (ACA)     │  │   │
                     │  │  │  SvelteKit     │──│  ASP.NET Core      │  │   │
                     │  │  │  Node.js       │  │  SignalR            │  │   │
                     │  │  │  adapter-node  │  │  WebSockets         │  │   │
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
                     └─────────────────────────────────────────────────────┘

                     ┌─────────────────────────────────────────────────────┐
                     │                  GitHub                             │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  GitHub Actions                              │   │
                     │  │                                              │   │
                     │  │  • CI pipeline (build + test on PR/push)     │   │
                     │  │  • CD pipeline (build images → deploy ACA)   │   │
                     │  │  • Infra pipeline (Bicep → provision Azure)  │   │
                     │  └──────────────────────────────────────────────┘   │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  GitHub Secrets                              │   │
                     │  │  OIDC federation → Azure (no static creds)   │   │
                     │  └──────────────────────────────────────────────┘   │
                     │                                                     │
                     └─────────────────────────────────────────────────────┘
```

### Azure Resource Summary

| Resource | SKU / Tier | Purpose |
|----------|-----------|---------|
| Resource Group | — | `rg-codec-alpha` in Central US |
| Azure Container Apps Environment | Consumption | Hosts API and Web containers |
| Azure Container App — API | 0.5 vCPU / 1 GiB, min 1 max 3 | ASP.NET Core API + SignalR |
| Azure Container App — Web | 0.25 vCPU / 0.5 GiB, min 1 max 3 | SvelteKit Node.js frontend |
| Azure Container Registry | Basic | Docker image storage |
| Azure Database for PostgreSQL | Flexible Server, Burstable B1ms | Relational data |
| Azure Blob Storage | Standard LRS, Hot tier | Avatar and image file storage |
| Azure Key Vault | Standard | Secrets management |
| Log Analytics Workspace | Pay-as-you-go | Container Apps logs and metrics |

---

## Phase 1: Migrate Database from SQLite to PostgreSQL

**Goal:** Replace the SQLite EF Core provider with Npgsql (PostgreSQL) and ensure the application works with PostgreSQL in local development.

### Tasks

#### 1.1 Update NuGet packages

- [ ] Remove `Microsoft.EntityFrameworkCore.Sqlite` from `Codec.Api.csproj`
- [ ] Add `Npgsql.EntityFrameworkCore.PostgreSQL` (latest stable for EF Core 9)
- [ ] Keep `Microsoft.EntityFrameworkCore.Design` for migration tooling

#### 1.2 Update `Program.cs` database configuration

- [ ] Replace `options.UseSqlite(connectionString)` with `options.UseNpgsql(connectionString)`
- [ ] Update connection string format in `appsettings.json` to PostgreSQL format:
  ```json
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=codec_dev;Username=codec;Password=codec_dev_password"
  }
  ```
- [ ] Create `appsettings.Development.json` with local PostgreSQL connection string if not already present

#### 1.3 Update `CodecDbContext` for PostgreSQL compatibility

- [ ] Remove the `DateTimeOffsetToStringConverter` workaround (PostgreSQL supports `timestamptz` natively)
- [ ] Review and update any SQLite-specific column type configurations
- [ ] Ensure `Guid` primary keys work with PostgreSQL (they map to `uuid` natively)

#### 1.4 Recreate EF Core migrations

- [ ] Delete all existing migration files under `Migrations/` (they target SQLite)
- [ ] Generate a fresh initial migration for PostgreSQL: `dotnet ef migrations add InitialPostgres`
- [ ] Verify the generated migration SQL targets PostgreSQL syntax

#### 1.5 Add Docker Compose for local PostgreSQL

- [ ] Create `docker-compose.dev.yml` at repository root with a PostgreSQL 16 service:
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

#### 1.6 Update `SeedData.cs`

- [ ] Verify seed data logic works with PostgreSQL (no SQLite-specific SQL)
- [ ] Ensure concurrent upsert patterns use PostgreSQL-compatible retry logic

#### 1.7 Update development documentation

- [ ] Update `docs/DEV_SETUP.md` with PostgreSQL prerequisites (Docker or local install)
- [ ] Update `docs/ARCHITECTURE.md` data layer section
- [ ] Update `docs/DATA.md` to reflect PostgreSQL

#### 1.8 Verify

- [ ] Run `dotnet build` — 0 errors
- [ ] Start PostgreSQL via `docker-compose -f docker-compose.dev.yml up -d`
- [ ] Run `dotnet ef database update` — migration applies cleanly
- [ ] Run the API — seed data creates successfully, endpoints work

---

## Phase 2: Migrate File Storage to Azure Blob Storage

**Goal:** Replace local disk file storage for avatars and images with Azure Blob Storage, while keeping local disk as a fallback for development.

### Tasks

#### 2.1 Add Azure Blob Storage NuGet package

- [ ] Add `Azure.Storage.Blobs` to `Codec.Api.csproj`
- [ ] Add `Azure.Identity` for Managed Identity authentication

#### 2.2 Create `BlobStorageService` implementations

- [ ] Create `IBlobStorageService` interface with `UploadAsync`, `DeleteAsync`, `GetUrlAsync` methods
- [ ] Create `AzureBlobStorageService` implementing the interface using `BlobServiceClient`
- [ ] Use Managed Identity authentication (`DefaultAzureCredential`) — no connection strings for blob access
- [ ] Create two containers: `avatars` and `images`
- [ ] Generate blob URLs using the public blob endpoint or SAS tokens

#### 2.3 Refactor `AvatarService` and `ImageUploadService`

- [ ] Update `IAvatarService` and `IImageUploadService` to support both local and cloud storage
- [ ] Use configuration to switch between local disk (development) and Azure Blob Storage (production)
- [ ] Add `Storage:Provider` config key: `"Local"` or `"AzureBlob"`
- [ ] Add `Storage:AzureBlobEndpoint` config key for the Blob Storage account URL

#### 2.4 Update `Program.cs` service registration

- [ ] Register blob storage services based on the `Storage:Provider` configuration
- [ ] Remove hardcoded `PhysicalFileProvider` static file middleware for production (blobs served directly from Azure)
- [ ] Keep static file middleware active when `Storage:Provider` is `"Local"` (development)

#### 2.5 Update configuration files

- [ ] Add storage configuration to `appsettings.json`:
  ```json
  "Storage": {
    "Provider": "Local"
  }
  ```
- [ ] Document production override:
  ```json
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlobEndpoint": "https://<account>.blob.core.windows.net"
  }
  ```

#### 2.6 Verify

- [ ] Run in development mode with `Storage:Provider=Local` — local disk path works as before
- [ ] Run `dotnet build` — 0 errors
- [ ] Ensure avatar upload, image upload, and URL resolution work in local dev

---

## Phase 3: Prepare the SvelteKit Web App for Production

**Goal:** Switch the SvelteKit app from `adapter-auto` to `adapter-node` for containerized deployment, and externalize all configuration.

### Tasks

#### 3.1 Install `adapter-node`

- [ ] Run `npm install -D @sveltejs/adapter-node` in `apps/web/`
- [ ] Remove `@sveltejs/adapter-auto` from `devDependencies`

#### 3.2 Update `svelte.config.js`

- [ ] Replace the adapter import:
  ```js
  import adapter from '@sveltejs/adapter-node';
  ```
- [ ] Configure the adapter to output to `build/`:
  ```js
  adapter: adapter({ out: 'build' })
  ```

#### 3.3 Type environment variables in `app.d.ts`

- [ ] Declare public env vars in `App.Platform` or use `$env/static/public`:
  ```typescript
  declare global {
    namespace App {
      interface Platform {}
    }
  }
  ```
- [ ] Ensure `PUBLIC_API_BASE_URL` and `PUBLIC_GOOGLE_CLIENT_ID` are properly typed

#### 3.4 Add a health check endpoint

- [ ] Create `apps/web/src/routes/health/+server.ts` returning a 200 JSON response:
  ```typescript
  export function GET() {
    return new Response(JSON.stringify({ status: 'healthy' }), {
      headers: { 'content-type': 'application/json' }
    });
  }
  ```

#### 3.5 Review and update API base URL handling

- [ ] Ensure all API calls use `PUBLIC_API_BASE_URL` from environment (not hardcoded)
- [ ] Verify SignalR hub connection URL is derived from the API base URL

#### 3.6 Verify

- [ ] Run `npm run build` — outputs to `build/` directory
- [ ] Run `node build/index.js` — serves the app on port 3000 (adapter-node default)
- [ ] Verify `npm run check` passes with 0 errors

---

## Phase 4: Prepare the API for Production Hosting

**Goal:** Add production-readiness features to the ASP.NET Core API: environment-driven configuration, health checks, CORS flexibility, and logging.

### Tasks

#### 4.1 Enhance health checks

- [ ] Add `Microsoft.Extensions.Diagnostics.HealthChecks` usage
- [ ] Add a database health check (verify PostgreSQL connectivity)
- [ ] Map health check endpoints: `/health/live` (liveness) and `/health/ready` (readiness, includes DB check)

#### 4.2 Environment-driven CORS configuration

- [ ] Replace the `"dev"` CORS policy name with `"default"`
- [ ] Ensure `Cors:AllowedOrigins` array is read from configuration (already done, verify it works with production URLs)
- [ ] Apply the CORS policy to all environments (not just dev)

#### 4.3 Environment-driven configuration

- [ ] Verify `Google:ClientId` is read from configuration (already done)
- [ ] Add `Api:BaseUrl` configuration documentation for production (used for avatar/image URL generation)
- [ ] Ensure all hardcoded localhost references are replaced with configuration values

#### 4.4 Production database migration strategy

- [ ] Remove automatic `db.Database.Migrate()` call in production — migrations should be run as a pre-deployment step
- [ ] Add a migration section to the deployment pipeline (EF Core migration bundle or `dotnet ef database update` step)
- [ ] Keep auto-migration for development only (behind `IsDevelopment()` check — already done, verify)

#### 4.5 Add structured logging

- [ ] Add `Serilog` NuGet packages (`Serilog.AspNetCore`, `Serilog.Sinks.Console`)
- [ ] Configure Serilog in `Program.cs` with structured JSON console output (for Container Apps log streaming)
- [ ] Add request logging middleware

#### 4.6 Configure WebSocket support for Azure Container Apps

- [ ] Ensure SignalR is configured to handle WebSocket transport behind a reverse proxy
- [ ] Add `ForwardedHeaders` middleware to trust Azure Container Apps' proxy headers:
  ```csharp
  app.UseForwardedHeaders(new ForwardedHeadersOptions
  {
      ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
  });
  ```

#### 4.7 Verify

- [ ] Run `dotnet build` — 0 errors
- [ ] Test health endpoints return expected responses
- [ ] Confirm CORS headers are present in API responses with configured origins

---

## Phase 5: Containerize Both Applications

**Goal:** Create optimized multi-stage Dockerfiles for the API and Web apps, and verify they run correctly in containers.

### Tasks

#### 5.1 Create API Dockerfile

- [ ] Create `apps/api/Dockerfile` with multi-stage build:
  - **Build stage:** `mcr.microsoft.com/dotnet/sdk:9.0` — restore, build, publish
  - **Runtime stage:** `mcr.microsoft.com/dotnet/aspnet:9.0` — copy published output, set entrypoint
- [ ] Expose port 8080 (ASP.NET Core default in containers)
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` as default
- [ ] Add `.dockerignore` in `apps/api/` to exclude `bin/`, `obj/`, `*.db`

#### 5.2 Create Web Dockerfile

- [ ] Create `apps/web/Dockerfile` with multi-stage build:
  - **Build stage:** `node:20-alpine` — `npm ci`, `npm run build`
  - **Runtime stage:** `node:20-alpine` — copy `build/`, `package.json`, install prod dependencies, set entrypoint `node build/index.js`
- [ ] Expose port 3000 (adapter-node default)
- [ ] Add `.dockerignore` in `apps/web/` to exclude `node_modules/`, `.svelte-kit/`, `build/`

#### 5.3 Update Docker Compose for full-stack local development

- [ ] Extend `docker-compose.dev.yml` (or create `docker-compose.yml`) to include:
  - PostgreSQL (already exists)
  - API container (build from `apps/api/Dockerfile`)
  - Web container (build from `apps/web/Dockerfile`)
  - Azurite (Azure Storage emulator) for local blob storage testing
- [ ] Configure networking so containers can communicate
- [ ] Map environment variables for each service

#### 5.4 Verify

- [ ] Run `docker build -t codec-api ./apps/api` — builds successfully
- [ ] Run `docker build -t codec-web ./apps/web` — builds successfully
- [ ] Run `docker-compose up` — full stack starts, API connects to PostgreSQL, Web connects to API
- [ ] Test end-to-end: sign in, send a message, upload an image

---

## Phase 6: Infrastructure as Code (Bicep)

**Goal:** Define all Azure resources using Bicep modules. Place all IaC files under the `infra/` directory.

### Tasks

#### 6.1 Create Bicep project structure

- [ ] Create directory structure:
  ```
  infra/
  ├── main.bicep              # Orchestrator — deploys all modules
  ├── main.bicepparam         # Parameter file for alpha environment
  ├── modules/
  │   ├── container-registry.bicep
  │   ├── container-apps-environment.bicep
  │   ├── container-app-api.bicep
  │   ├── container-app-web.bicep
  │   ├── postgresql.bicep
  │   ├── storage-account.bicep
  │   ├── key-vault.bicep
  │   └── log-analytics.bicep
  ```

#### 6.2 Create `main.bicep` orchestrator

- [ ] Define parameters: `location`, `environmentName`, `googleClientId`
- [ ] Default `location` to `'centralus'`
- [ ] Use a naming convention: `{resourceAbbrev}-codec-{env}` (e.g., `rg-codec-alpha`, `acr-codec-alpha`)
- [ ] Wire module outputs as inputs to dependent modules

#### 6.3 Create Log Analytics module

- [ ] Resource: `Microsoft.OperationalInsights/workspaces`
- [ ] SKU: `PerGB2018`
- [ ] Retention: 30 days

#### 6.4 Create Container Registry module

- [ ] Resource: `Microsoft.ContainerRegistry/registries`
- [ ] SKU: `Basic`
- [ ] Admin user: disabled (use Managed Identity for pull)
- [ ] Do NOT enable anonymous pull access

#### 6.5 Create PostgreSQL Flexible Server module

- [ ] Resource: `Microsoft.DBforPostgreSQL/flexibleServers`
- [ ] SKU: `Standard_B1ms` (Burstable)
- [ ] PostgreSQL version: 16
- [ ] Storage: 32 GB
- [ ] Enable Microsoft Entra authentication
- [ ] Configure firewall to allow Azure services
- [ ] Store admin credentials in Key Vault
- [ ] Create the `codec` database

#### 6.6 Create Storage Account module

- [ ] Resource: `Microsoft.Storage/storageAccounts`
- [ ] SKU: `Standard_LRS`
- [ ] Kind: `StorageV2`
- [ ] Access tier: `Hot`
- [ ] Disable shared key access — use RBAC (Managed Identity)
- [ ] Create blob containers: `avatars`, `images`
- [ ] Enable public blob access for avatar/image serving

#### 6.7 Create Key Vault module

- [ ] Resource: `Microsoft.KeyVault/vaults`
- [ ] SKU: `standard`
- [ ] Enable RBAC authorization
- [ ] Do NOT disable purge protection
- [ ] Store secrets: PostgreSQL connection string, Google Client ID

#### 6.8 Create Container Apps Environment module

- [ ] Resource: `Microsoft.App/managedEnvironments`
- [ ] Link to Log Analytics workspace
- [ ] Internal networking: disabled (public-facing)

#### 6.9 Create Container App modules (API and Web)

- [ ] API Container App:
  - Image: `{acrName}.azurecr.io/codec-api:latest`
  - CPU: 0.5, Memory: 1.0Gi
  - Min replicas: 1, Max replicas: 3
  - Ingress: external, port 8080, transport HTTP
  - Enable WebSocket connections for SignalR
  - Liveness probe: `/health/live`
  - Readiness probe: `/health/ready`
  - Environment variables from Key Vault references
  - System-assigned Managed Identity
  - RBAC: `AcrPull` on ACR, `Storage Blob Data Contributor` on Storage, `Key Vault Secrets User` on Key Vault
- [ ] Web Container App:
  - Image: `{acrName}.azurecr.io/codec-web:latest`
  - CPU: 0.25, Memory: 0.5Gi
  - Min replicas: 1, Max replicas: 3
  - Ingress: external, port 3000, transport HTTP
  - Liveness probe: `/health`
  - Environment variables: `PUBLIC_API_BASE_URL` (API app FQDN), `PUBLIC_GOOGLE_CLIENT_ID`
  - System-assigned Managed Identity
  - RBAC: `AcrPull` on ACR

#### 6.10 Create parameter file

- [ ] Create `main.bicepparam` with alpha-specific values:
  ```bicep
  using './main.bicep'

  param location = 'centralus'
  param environmentName = 'alpha'
  ```

#### 6.11 Verify

- [ ] Run `az bicep build --file infra/main.bicep` — compiles with 0 errors
- [ ] Run `az deployment group what-if` (dry run) — review planned resource creation

---

## Phase 7: CI Pipeline (GitHub Actions)

**Goal:** Enhance the existing CI pipeline to build, test, lint, and produce Docker images as artifacts. Runs on every push to `main` and on pull requests.

### Tasks

#### 7.1 Enhance the existing `ci.yml` workflow

- [ ] Rename to `ci.yml` (keep the name)
- [ ] Add explicit `permissions: contents: read` at the workflow level
- [ ] Add `concurrency` group to cancel redundant PR runs:
  ```yaml
  concurrency:
    group: ci-${{ github.ref }}
    cancel-in-progress: true
  ```

#### 7.2 Add API test job

- [ ] Add `test-api` job that runs after `build-api`
- [ ] Start a PostgreSQL service container for integration tests
- [ ] Run `dotnet test` with test result output
- [ ] Upload test results as artifacts

#### 7.3 Add Web lint and check job

- [ ] Add `check-web` job
- [ ] Run `npm run check` (includes `svelte-check` and deprecated events lint)
- [ ] Upload any lint/check results as artifacts

#### 7.4 Add Docker image build jobs

- [ ] Add `docker-build-api` job (depends on `build-api` success)
  - Build the API Docker image using `apps/api/Dockerfile`
  - Tag with `${{ github.sha }}` and `latest`
  - Do NOT push to ACR (CI only validates the build)
- [ ] Add `docker-build-web` job (depends on `build-web` success)
  - Build the Web Docker image using `apps/web/Dockerfile`
  - Tag with `${{ github.sha }}` and `latest`
  - Do NOT push to ACR (CI only validates the build)

#### 7.5 Add caching

- [ ] Cache NuGet packages for API jobs:
  ```yaml
  - uses: actions/cache@v4
    with:
      path: ~/.nuget/packages
      key: ${{ runner.os }}-nuget-${{ hashFiles('**/Codec.Api.csproj') }}
  ```
- [ ] Cache npm packages (already present via `actions/setup-node`, verify it uses v4)

#### 7.6 Verify

- [ ] Push a test branch and confirm all CI jobs pass
- [ ] Confirm Docker images build successfully in CI
- [ ] Confirm caching reduces build times on subsequent runs

---

## Phase 8: Infrastructure Pipeline (GitHub Actions)

**Goal:** Create a GitHub Actions workflow that provisions and updates Azure infrastructure using the Bicep templates from Phase 6.

### Tasks

#### 8.1 Set up Azure OIDC authentication for GitHub Actions

- [ ] Create a Microsoft Entra ID App Registration for GitHub Actions
- [ ] Configure federated credentials for the `main` branch and for environment `alpha`
- [ ] Add GitHub Secrets:
  - `AZURE_CLIENT_ID` — App Registration client ID
  - `AZURE_TENANT_ID` — Entra tenant ID
  - `AZURE_SUBSCRIPTION_ID` — Azure subscription ID
- [ ] Document the OIDC setup in `docs/` or `CONTRIBUTING.md`

#### 8.2 Create `infra.yml` workflow

- [ ] Trigger: `workflow_dispatch` (manual) and push to `main` when files change in `infra/`
- [ ] Permissions: `id-token: write`, `contents: read`
- [ ] Job: `deploy-infra`
  - Checkout code
  - Login to Azure using OIDC (`azure/login@v2`)
  - Run `az deployment group what-if` for preview
  - Run `az deployment group create` to deploy Bicep
  - Output the deployed resource URLs and names

#### 8.3 Add environment protection

- [ ] Create GitHub Environment `alpha` with manual approval required
- [ ] Scope the deployment job to the `alpha` environment

#### 8.4 Verify

- [ ] Trigger the workflow manually
- [ ] Confirm `what-if` output shows expected resources
- [ ] Confirm deployment creates all resources successfully
- [ ] Confirm resources appear in Azure Portal under `rg-codec-alpha`

---

## Phase 9: CD Pipeline (GitHub Actions)

**Goal:** Create a GitHub Actions workflow that builds Docker images, pushes them to ACR, runs database migrations, and deploys to Azure Container Apps. Triggered on push to `main` after CI passes.

### Tasks

#### 9.1 Create `cd.yml` workflow

- [ ] Trigger: `workflow_run` on successful completion of `ci` workflow on `main`, plus `workflow_dispatch` for manual runs
- [ ] Permissions: `id-token: write`, `contents: read`, `packages: read`
- [ ] Concurrency: prevent parallel deployments
  ```yaml
  concurrency:
    group: cd-alpha
    cancel-in-progress: false
  ```

#### 9.2 Add `build-and-push` job

- [ ] Login to Azure via OIDC
- [ ] Login to ACR (`az acr login`)
- [ ] Build API Docker image, tag with `${{ github.sha }}` and `latest`
- [ ] Build Web Docker image, tag with `${{ github.sha }}` and `latest`
- [ ] Push both images to ACR
- [ ] Output image tags for the deploy job

#### 9.3 Add `migrate-database` job

- [ ] Depends on `build-and-push`
- [ ] Install .NET SDK and EF Core tools
- [ ] Generate an EF Core migration bundle:
  ```bash
  dotnet ef migrations bundle --project apps/api/Codec.Api --output efbundle --self-contained
  ```
- [ ] Run the bundle against the production PostgreSQL (connection string from Key Vault / GitHub Secret)
- [ ] Alternative: run `dotnet ef database update` with the production connection string

#### 9.4 Add `deploy` job

- [ ] Depends on `migrate-database`
- [ ] GitHub Environment: `alpha` (with manual approval)
- [ ] Login to Azure via OIDC
- [ ] Update API Container App revision:
  ```bash
  az containerapp update \
    --name ca-codec-api-alpha \
    --resource-group rg-codec-alpha \
    --image {acrName}.azurecr.io/codec-api:${{ github.sha }}
  ```
- [ ] Update Web Container App revision:
  ```bash
  az containerapp update \
    --name ca-codec-web-alpha \
    --resource-group rg-codec-alpha \
    --image {acrName}.azurecr.io/codec-web:${{ github.sha }}
  ```
- [ ] Wait for revisions to become active

#### 9.5 Add `smoke-test` job

- [ ] Depends on `deploy`
- [ ] Verify API health: `curl https://{api-fqdn}/health/ready`
- [ ] Verify Web health: `curl https://{web-fqdn}/health`
- [ ] Report pass/fail

#### 9.6 Add rollback documentation

- [ ] Document how to rollback by redeploying the previous image tag:
  ```bash
  az containerapp update --name ca-codec-api-alpha --image {acrName}.azurecr.io/codec-api:{previous-sha}
  ```
- [ ] Document how to rollback database migrations (if applicable)

#### 9.7 Verify

- [ ] Trigger a deployment via push to `main`
- [ ] Confirm images are pushed to ACR with correct tags
- [ ] Confirm database migration runs successfully
- [ ] Confirm Container Apps update to new revisions
- [ ] Confirm smoke tests pass
- [ ] Access the live application via the Web Container App FQDN

---

## Phase 10: Post-Deployment Hardening and Documentation

**Goal:** Apply production security hardening, configure monitoring, and finalize documentation.

### Tasks

#### 10.1 Security hardening

- [ ] Enable HTTPS-only on both Container Apps (redirect HTTP to HTTPS)
- [ ] Configure Content Security Policy (CSP) headers in the API
- [ ] Add rate limiting middleware to the API (`Microsoft.AspNetCore.RateLimiting`)
- [ ] Verify Azure Key Vault is the sole source of secrets in production
- [ ] Verify Managed Identity is used for all Azure service-to-service communication (no connection strings)
- [ ] Review CORS origins to only allow the Web Container App FQDN

#### 10.2 Monitoring and alerts

- [ ] Verify Container Apps logs stream to Log Analytics
- [ ] Configure basic Azure Monitor alerts:
  - API container restarts
  - API 5xx error rate > threshold
  - Database CPU > 80%
- [ ] Verify Serilog structured logs appear in Log Analytics

#### 10.3 Update documentation

- [ ] Update `docs/ARCHITECTURE.md` with production deployment architecture diagram
- [ ] Update `docs/DEV_SETUP.md` with PostgreSQL and Docker instructions
- [ ] Create `docs/DEPLOYMENT.md` with:
  - How to provision infrastructure (trigger `infra.yml`)
  - How to deploy the application (trigger `cd.yml` or push to `main`)
  - How to rollback a deployment
  - How to run database migrations manually
  - Environment variables and secrets reference
  - Troubleshooting common issues
- [ ] Update `README.md` with deployment status badges and live URL

#### 10.4 Update `PLAN.md`

- [ ] Mark deployment milestones as complete
- [ ] Update current status section

#### 10.5 Final verification

- [ ] Full end-to-end test on the live alpha deployment:
  - Sign in with Google
  - Create a server
  - Create a channel
  - Send messages (text, images, replies)
  - Test emoji reactions
  - Test friend requests and DMs
  - Test SignalR real-time features (typing indicators, live message delivery)
- [ ] Verify WebSocket connections work through Azure Container Apps
- [ ] Confirm avatar and image uploads persist in Azure Blob Storage

---

## Phase Summary

| Phase | Description | Depends On | Key Outputs |
|-------|-------------|-----------|-------------|
| 1 | Migrate SQLite → PostgreSQL | — | `Npgsql` provider, new migrations, `docker-compose.dev.yml` |
| 2 | Migrate file storage → Azure Blob | Phase 1 | `BlobStorageService`, config-driven storage |
| 3 | Prepare SvelteKit for production | — | `adapter-node`, health endpoint, typed env vars |
| 4 | Prepare API for production | Phase 1 | Health probes, structured logging, CORS, forwarded headers |
| 5 | Containerize both apps | Phases 3, 4 | `Dockerfile` for API and Web, full `docker-compose.yml` |
| 6 | Infrastructure as Code (Bicep) | — | `infra/` directory with all Bicep modules |
| 7 | CI pipeline | Phases 5 | Enhanced `ci.yml` with Docker builds and tests |
| 8 | Infrastructure pipeline | Phase 6 | `infra.yml` workflow, OIDC auth, Azure resources provisioned |
| 9 | CD pipeline | Phases 7, 8 | `cd.yml` workflow, automated deployment to ACA |
| 10 | Hardening and documentation | Phase 9 | Security headers, monitoring, final docs |

> **Note:** Phases 1–2 and Phases 3–4 can be worked on in parallel since they target different applications. Phase 6 can also be started in parallel with Phases 1–5 since it's purely IaC authoring.
