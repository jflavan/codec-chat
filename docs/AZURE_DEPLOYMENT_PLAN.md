# Azure Deployment Plan

This document outlines the plan to prepare Codec for beta testing on Microsoft Azure. It covers infrastructure-as-code, CI/CD pipelines, application changes, and operational concerns.

## Current State

| Component | Current (Dev) | Production Target |
|-----------|--------------|-------------------|
| Frontend | SvelteKit + Vite dev server | Containerized Node.js (adapter-node) on Azure Container Apps |
| Backend | ASP.NET Core 9 on localhost:5050 | Containerized .NET 9 on Azure Container Apps |
| Database | SQLite file | Azure Database for PostgreSQL Flexible Server |
| File Storage | Local filesystem (`uploads/`) | Azure Blob Storage container |
| Real-time | SignalR over WebSocket (single instance) | SignalR on single Container Apps instance (Redis backplane deferred to post-beta) |
| Auth | Google ID token validation | Same (add authorized origins for production domain) |
| CI/CD | Build-only GitHub Actions workflow | Full build → test → deploy pipeline |
| IaC | None | Terraform |
| Secrets | `appsettings.Development.json` / `.env` | Azure Key Vault |
| Domain/TLS | localhost | `codec-chat.com` with managed TLS via Azure Container Apps |

---

## Phase 1 — Application Changes (Pre-Cloud)

These changes make the app cloud-ready without requiring any cloud resources yet. They can be developed and tested locally.

### 1.1 Dockerize Both Services

**API Dockerfile** (`apps/api/Dockerfile`)
- Multi-stage build: `mcr.microsoft.com/dotnet/sdk:9.0` → `mcr.microsoft.com/dotnet/aspnet:9.0`
- `dotnet publish -c Release` to produce a self-contained image
- Expose port 8080 (Azure Container Apps default ingress port)
- Use `ASPNETCORE_URLS=http://+:8080`
- Non-root user for security

**Web Dockerfile** (`apps/web/Dockerfile`)
- Multi-stage build: `node:20-alpine` (build) → `node:20-alpine` (run)
- `npm ci && npm run build` to produce the SvelteKit output
- Run with `node build/index.js` (adapter-node output)
- Expose port 3000
- Non-root user for security

**Docker Compose** (`docker-compose.yml`) — for local integration testing of the containerized stack.

### 1.2 Switch SvelteKit to adapter-node

- Replace `@sveltejs/adapter-auto` with `@sveltejs/adapter-node` in `package.json`
- Update `svelte.config.js` to use the new adapter
- Configure `PORT` environment variable for runtime port binding
- Verify `npm run build && node build` works locally

### 1.3 Migrate Database from SQLite to PostgreSQL

- Add `Npgsql.EntityFrameworkCore.PostgreSQL` NuGet package
- Make the database provider configurable via environment variable or connection string format detection
- Keep SQLite as the default for local development (`Data Source=codec-dev.db`)
- Use PostgreSQL when `ConnectionStrings:Default` contains a PostgreSQL connection string (`Host=...`)
- Create a new EF Core migration targeting PostgreSQL schema
- Test locally with a PostgreSQL Docker container
- Update `Program.cs` database provider selection logic

### 1.4 Migrate File Storage to Azure Blob Storage

- Create `IFileStorageService` interface abstracting file upload/download/URL operations
- Implement `LocalFileStorageService` (current behavior — for local dev)
- Implement `AzureBlobStorageService` using `Azure.Storage.Blobs` NuGet package
- Refactor `AvatarService` and `ImageUploadService` to use `IFileStorageService`
- Storage backend selected via configuration (`Storage:Provider` = `Local` | `AzureBlob`)
- Azure Blob configuration: storage account name, container name, optional CDN prefix for public URLs
- Public container access or SAS tokens for serving uploaded files

### 1.5 Add SignalR Redis Backplane (Deferred — Post-Beta)

> **Decision:** For beta, the API will run as a single Container Apps instance (`max-replicas=1`), so in-memory SignalR is sufficient. Redis backplane will be added when we need to scale beyond one instance.

When needed:
- Add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` NuGet package
- Conditionally enable Redis backplane when `SignalR:RedisConnectionString` is configured
- Use Azure Cache for Redis in production
- No change for local dev (single-instance in-memory backplane is fine)

### 1.6 Externalize All Configuration

Audit `Program.cs`, `appsettings.json`, and the web `.env` for anything that must change per environment:

| Setting | Source |
|---------|--------|
| `ConnectionStrings:Default` | Key Vault → env var |
| `Google:ClientId` | Key Vault → env var |
| `Api:BaseUrl` | Environment variable |
| `Cors:AllowedOrigins` | Environment variable (JSON array) |
| `Storage:Provider` | Environment variable |
| `Storage:AzureBlobConnectionString` | Key Vault → env var |
| `Storage:AzureBlobContainer` | Environment variable |
| `SignalR:RedisConnectionString` | Key Vault → env var (deferred — post-beta) |
| `PUBLIC_API_BASE_URL` | Build-time env var for SvelteKit |
| `PUBLIC_GOOGLE_CLIENT_ID` | Build-time env var for SvelteKit |

---

## Phase 2 — Infrastructure as Code (Terraform)

All Azure resources managed via Terraform in a new `infra/` directory.

### 2.1 Project Structure

```
infra/
├── main.tf                  # Provider config, backend
├── variables.tf             # Input variables
├── outputs.tf               # Useful outputs (URLs, IPs)
├── versions.tf              # Terraform and provider version constraints
├── environments/
│   ├── beta.tfvars          # Beta environment variable values
│   └── prod.tfvars          # Future production values
├── modules/
│   ├── networking/          # VNet, subnets, private endpoints
│   ├── database/            # Azure Database for PostgreSQL Flexible Server
│   ├── storage/             # Azure Storage accounts (uploads, Terraform state)
│   ├── redis/               # Azure Cache for Redis (deferred — post-beta)
│   ├── keyvault/            # Azure Key Vault secrets
│   ├── container-registry/  # Azure Container Registry
│   ├── container-apps/      # Azure Container Apps environment + apps (api, web)
│   ├── identity/            # Managed Identities, RBAC role assignments, OIDC
│   └── dns/                 # Azure DNS zone (optional)
```

### 2.2 Azure Resources

#### Foundational
| Resource | Purpose |
|----------|---------|
| **Azure Subscription + Resource Group** | Existing Azure subscription; new resource group `rg-codec-beta` |
| **Terraform State Storage Account** | Azure Storage account + blob container for remote Terraform state with versioning |
| **Managed Identities** | User-assigned managed identities for Container Apps, least-privilege RBAC |
| **VNet + Subnets** | Private networking between Container Apps and PostgreSQL |
| **Azure Container Registry (ACR)** | Docker image registry (`codecregistry.azurecr.io`) |

#### Data
| Resource | Purpose |
|----------|---------|
| **Azure Database for PostgreSQL Flexible Server** | Primary database, private access via VNet integration, automated backups |
| **Azure Storage Account (uploads)** | Blob container for user-uploaded avatars and images, public read access on container |

#### Compute
| Resource | Purpose |
|----------|---------|
| **Container Apps — API** | ASP.NET Core backend, min 1 / max 1 replica (beta, single instance for SignalR), VNet integrated, env vars from Key Vault |
| **Container Apps — Web** | SvelteKit frontend, min 0 / max 2 replicas (beta), calls API internally |
| **Container Apps Environment** | Shared environment for both apps with Log Analytics workspace |

#### Security
| Resource | Purpose |
|----------|---------|
| **Azure Key Vault** | DB connection string, Google Client ID, storage connection string |
| **RBAC Role Assignments** | Managed Identity → Key Vault Secrets User, Storage Blob Data Contributor, AcrPull |
| **Federated Identity Credentials** | OIDC trust for GitHub Actions (no long-lived service principal secrets) |

#### Networking & DNS
| Resource | Purpose |
|----------|---------|
| **Container Apps custom domains** | Map `codec-chat.com` / `api.codec-chat.com` to Container Apps |
| **Azure Front Door (future)** | WAF / DDoS protection / CDN for production |

### 2.3 Terraform Configuration Details

**Remote Backend:**
```hcl
terraform {
  backend "azurerm" {
    resource_group_name  = "rg-codec-tfstate"
    storage_account_name = "codectfstate"
    container_name       = "tfstate"
    key                  = "beta.terraform.tfstate"
  }
}
```

**Provider:**
```hcl
provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}
```

**Key Variables:**
- `subscription_id` — Azure subscription ID
- `location` — `eastus` (default)
- `environment` — `beta` | `prod`
- `domain` — `codec-chat.com`
- `google_client_id` — Google OAuth client ID

---

## Phase 3 — CI/CD Pipelines (GitHub Actions)

Expand the existing `ci.yml` and add deployment workflows.

### 3.1 Workflow Overview

```
                    push to main
                         │
                    ┌────┴────┐
                    │  CI     │  (existing, expanded)
                    │  Build  │
                    │  Test   │
                    │  Lint   │
                    └────┬────┘
                         │ on success
                    ┌────┴────┐
                    │  Build  │
                    │  Docker │
                    │  Images │
                    └────┬────┘
                         │ push to ACR
                    ┌────┴────┐
                    │  Deploy │
                    │  Beta   │
                    │(Container│
                    │  Apps)  │
                    └────┬────┘
                         │ post-deploy
                    ┌────┴────┐
                    │  Smoke  │
                    │  Tests  │
                    └─────────┘
```

### 3.2 Workflow Files

#### `.github/workflows/ci.yml` (expand existing)

Add to existing build jobs:
- **Linting job**: `dotnet format --verify-no-changes`, `npm run check`
- **Test jobs**: Unit tests for API (`dotnet test`), frontend type-checking
- **Security scanning**: `dependency-review-action` on PRs, CodeQL analysis
- **Cache optimization**: NuGet package cache, npm cache

#### `.github/workflows/deploy-beta.yml` (new)

Triggers: on push to `main` after CI passes, or manual `workflow_dispatch`.

Jobs:
1. **authenticate** — OIDC token exchange with Azure via Federated Identity Credentials (no service principal secrets)
2. **build-push-api** — Build API Docker image, tag with `${{ github.sha }}`, push to ACR
3. **build-push-web** — Build Web Docker image with build-time env vars, tag, push to ACR
4. **deploy-api** — `az containerapp update --name codec-api --image ... --resource-group rg-codec-beta`
5. **deploy-web** — `az containerapp update --name codec-web --image ... --resource-group rg-codec-beta`
6. **smoke-test** — Hit `/health` endpoint, verify 200 response

```yaml
permissions:
  contents: read
  id-token: write  # Required for OIDC

jobs:
  deploy-api:
    runs-on: ubuntu-latest
    environment: beta
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      # ... docker build, push to ACR, deploy to Container Apps
```

#### `.github/workflows/terraform.yml` (new)

Triggers: changes to `infra/**` on PR (plan only) or push to main (apply).

Jobs:
1. **terraform-plan** — On PR: `terraform plan`, post plan output as PR comment
2. **terraform-apply** — On push to main: `terraform apply -auto-approve` (with environment protection rules)

### 3.3 GitHub Configuration

| Setting | Location |
|---------|----------|
| **Environment: beta** | GitHub repo → Settings → Environments (with protection rules) |
| **Secrets** | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |
| **Variables** | `AZURE_LOCATION`, `ACR_LOGIN_SERVER`, `RESOURCE_GROUP` |

---

## Phase 4 — Security & Observability

### 4.1 Security Hardening

- [ ] **HTTPS enforcement** — Azure Container Apps provides TLS termination automatically
- [ ] **CORS configuration** — Update `AllowedOrigins` to production domain(s) only
- [ ] **Content Security Policy** — Add CSP headers in SvelteKit response hooks
- [ ] **Rate limiting** — Container Apps concurrency limits + consider Azure Front Door WAF rules
- [ ] **Secret rotation** — Document rotation process for DB password in Key Vault
- [ ] **Google OAuth** — Add production domain to authorized JavaScript origins
- [ ] **Container scanning** — Integrate Trivy or Microsoft Defender for Containers in CI pipeline
- [ ] **Non-root containers** — Both Dockerfiles run as non-root users

### 4.2 Observability

- [ ] **Structured logging** — Configure ASP.NET Core and SvelteKit to output JSON logs (auto-ingested by Azure Monitor via Log Analytics)
- [ ] **Health checks** — `/health` endpoint already exists; configure Container Apps health probes (liveness/readiness)
- [ ] **Azure Monitor dashboards** — Request latency, error rate, replica count, CPU/memory
- [ ] **Alerting** — Azure Monitor alerts for error rate spikes, high latency, scaling events
- [ ] **Application Insights** — Add OpenTelemetry instrumentation to the API for distributed tracing (future)

### 4.3 Database Operations

- [ ] **Automated backups** — Azure Database for PostgreSQL automated backups enabled via Terraform (default 7-day retention)
- [ ] **Connection pooling** — Use built-in Npgsql pooling; consider PgBouncer (built into Flexible Server)
- [ ] **Migration strategy** — EF Core migrations run as a Container Apps startup task (`db.Database.Migrate()` on app startup, guarded by environment config). This is acceptable for beta with a single API instance; for production with multiple instances, switch to a separate migration job to avoid race conditions.
- [ ] **Point-in-time recovery** — Enable for Azure Database for PostgreSQL (included with Flexible Server)

---

## Phase 5 — Domain & DNS

- [ ] Configure `codec-chat.com` DNS (registered via Squarespace Domains)
- [ ] Point DNS to Azure Container Apps via Squarespace DNS management or transfer DNS to Azure DNS
- [ ] Map `codec-chat.com` → Container Apps web service (custom domain binding)
- [ ] Map `api.codec-chat.com` → Container Apps API service (custom domain binding)
- [ ] Azure-managed TLS certificates via Container Apps managed certificates
- [ ] Update Google OAuth console with `https://codec-chat.com` and `https://api.codec-chat.com` as authorized origins

---

## Implementation Order

This is the recommended order of execution. Each phase builds on the previous one.

### Batch 1 — App Changes (no cloud dependency)
1. Switch SvelteKit to `adapter-node`
2. Create Dockerfiles for API and Web
3. Create `docker-compose.yml` for local integration testing
4. Add PostgreSQL support (dual-provider: SQLite for dev, PostgreSQL for prod)
5. Create `IFileStorageService` abstraction + Azure Blob Storage implementation
6. Externalize all configuration into environment variables

### Batch 2 — Azure Subscription Setup & IaC Foundation
7. Create resource group and register required resource providers on Azure subscription
8. Create Terraform state storage account (manual one-time setup)
9. Set up Federated Identity Credentials for GitHub Actions OIDC (App Registration + Federated Credential)
10. Write Terraform modules: networking, identity (managed identities + RBAC), Container Registry, Key Vault
11. `terraform apply` for foundational resources

### Batch 3 — Data & Compute IaC
12. Write Terraform modules: Azure Database for PostgreSQL, Storage Account (blob container)
13. Write Terraform modules: Container Apps Environment + Container Apps (API with `max-replicas=1`, Web)
14. `terraform apply` to deploy full infrastructure
15. Run initial database migration (triggered on first Container Apps API startup)

### Batch 4 — CI/CD Pipelines
16. Expand `ci.yml` with linting, testing, caching, security scanning
17. Create `deploy-beta.yml` workflow (build images → push to ACR → deploy to Container Apps)
18. Create `terraform.yml` workflow (plan on PR → apply on merge)
19. Configure GitHub Environments with protection rules

### Batch 5 — Production Readiness
20. Configure `codec-chat.com` domain DNS and Container Apps custom domain bindings
21. Update Google OAuth authorized origins for `codec-chat.com`
22. Add Azure Monitor dashboards and alerting
23. Set up structured logging with Log Analytics
24. Write operational runbook (deployment, rollback, secret rotation, incident response)

---

## Cost Estimate (Beta)

Rough monthly cost estimate for a low-traffic beta environment:

| Resource | Spec | Est. Monthly Cost |
|----------|------|-------------------|
| Container Apps — API | 1 vCPU, 512 MB, min 1 / max 1 replica | ~$15–25 |
| Container Apps — Web | 1 vCPU, 256 MB, min 0 replicas | ~$5–10 |
| Azure Database for PostgreSQL Flexible Server | Burstable B1ms, 32 GB storage | ~$12–18 |
| Azure Storage Account (Blob) | < 1 GB | ~$0.02 |
| Azure Container Registry (Basic) | < 5 GB | ~$5 |
| Azure Key Vault | < 10 secrets | ~$0.03 |
| Log Analytics Workspace | Included free tier (5 GB/month ingestion) | ~$0 |
| **Total** | | **~$37–58/month** |

> **Note:** Container Apps' "min replicas = 0" for the web frontend means zero cost when idle. The API is set to min 1 / max 1 to maintain WebSocket connections and avoid the need for a Redis backplane. Azure Cache for Redis will be added when scaling beyond a single API instance is needed (~$40/month additional for Basic C0).

---

## Decisions Made

| # | Decision | Answer |
|---|----------|--------|
| 1 | **Domain name** | `codec-chat.com` |
| 2 | **Azure subscription** | Use existing Azure subscription |
| 3 | **Azure region** | `eastus` |
| 4 | **Database migration strategy** | EF Core migrations run as a Container Apps startup task (`db.Database.Migrate()`) |
| 5 | **Redis vs. single-instance** | Single API instance (`max-replicas=1`) for beta; Redis backplane deferred to post-beta |
| 6 | **CDN for uploads** | Direct Azure Blob Storage serving — no Azure CDN for beta |
| 7 | **Terraform state backend** | Azure Storage backend (`codectfstate` storage account) |
