# Deployment Guide (Azure)

This document covers deploying Codec to **Azure Container Apps** using the infrastructure and CI/CD pipelines defined in this repository.

> **Looking for self-hosting?** If you want to run Codec on your own server without Azure, see [SELF_HOSTING.md](SELF_HOSTING.md) for a Docker Compose–based deployment guide.

## Architecture Overview

```
                     ┌─────────────────────────────────────────────────────┐
                     │                Azure (Central US)                   │
                     │                                                     │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │          Container Apps Environment           │   │
                     │  │                                              │   │
                     │  │  ┌────────────────┐  ┌────────────────────┐  │   │
                     │  │  │  Web App       │  │  API App           │  │   │
                     │  │  │  SvelteKit     │  │  ASP.NET Core 10   │  │   │
                     │  │  │  Node.js 20    │──│  SignalR WebSocket │  │   │
                     │  │  │  Port 3000     │  │  Port 8080         │  │   │
                     │  │  └────────────────┘  └────────┬───────────┘  │   │
                     │  └────────────────────────────────┼──────────────┘   │
                     │                                   │                  │
                     │  ┌────────────────┐  ┌───────────┴──────────────┐   │
                     │  │  Blob Storage  │  │  PostgreSQL Flexible     │   │
                     │  │  (avatars,     │  │  Server (B1ms, 32 GB)    │   │
                     │  │   images)      │  └──────────────────────────┘   │
                     │  └────────────────┘                                 │
                     │  ┌────────────────┐  ┌──────────────────────────┐   │
                     │  │  Container     │  │  Key Vault               │   │
                     │  │  Registry      │  │  (secrets)               │   │
                     │  └────────────────┘  └──────────────────────────┘   │
                     │  ┌──────────────────────────────────────────────┐   │
                     │  │  Log Analytics Workspace                     │   │
                     │  └──────────────────────────────────────────────┘   │
                     └─────────────────────────────────────────────────────┘
```

## Resource Naming Convention

All resources use the pattern `{abbreviation}-codec-{env}`:

| Resource | Name |
|----------|------|
| Resource Group | `rg-codec-prod` |
| Log Analytics | `log-codec-prod` |
| Container Registry | `acrcodecprod` |
| PostgreSQL Server | `psql-codec-prod` |
| Database | `codec` |
| Storage Account | `stcodecprod` |
| Key Vault | `kv-codec-prod` |
| Container Apps Env | `cae-codec-prod` |
| API Container App | `ca-codec-prod-api` |
| Web Container App | `ca-codec-prod-web` |

## Prerequisites

### Azure Setup

1. **Azure subscription** with Contributor access
2. **Resource group**: Create `rg-codec-prod` in `centralus`:
   ```bash
   az group create --name rg-codec-prod --location centralus
   ```
3. **Microsoft Entra ID App Registration** for GitHub Actions OIDC:
   - Create an App Registration in Microsoft Entra ID
   - Add Federated Identity Credentials for the `main` branch and `prod` environment
   - Grant the App Registration **Contributor** and **User Access Administrator** roles on `rg-codec-prod`

### GitHub Repository Secrets

Configure these secrets in the repository's Settings > Secrets and variables > Actions:

| Secret | Description |
|--------|-------------|
| `AZURE_CLIENT_ID` | Entra App Registration client ID |
| `AZURE_TENANT_ID` | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `GOOGLE_CLIENT_ID` | Google OAuth 2.0 client ID |
| `POSTGRESQL_ADMIN_PASSWORD` | Password for PostgreSQL admin user |
| `PUBLIC_API_BASE_URL` | API URL baked into SvelteKit build (`https://api.codec-chat.com`) |
| `PUBLIC_GOOGLE_CLIENT_ID` | Google client ID baked into SvelteKit build |
| `GLOBAL_ADMIN_EMAIL` | Email address of the platform-wide global admin |

### GitHub Environment

Create a `prod` environment in Settings > Environments with:
- **Required reviewers** for deployment approval
- **Deployment branches** restricted to `main`

## Pipelines

### Infrastructure Pipeline (`infra.yml`)

Provisions Azure resources via Bicep.

**Triggers:**
- Manual dispatch (`workflow_dispatch`) only

> **Note:** The `infra.yml` push trigger on `infra/**` was removed to prevent deployment conflicts with `cd.yml`. Infrastructure changes should be deployed manually or via the CD pipeline.

**Steps:**
1. Login to Azure via OIDC
2. Run `what-if` to preview changes
3. Deploy Bicep template to `rg-codec-prod`

**First-time provisioning:**
```bash
# Trigger manually from GitHub Actions UI
gh workflow run infra.yml
```

### CI Pipeline (`ci.yml`)

Runs on every push and pull request.

**Jobs:**
- `build-api` — Restore and build .NET project
- `build-web` — Install npm deps and build SvelteKit
- `check-web` — Run `svelte-check` and lint
- `docker-build-api` — Validate API Dockerfile builds
- `docker-build-web` — Validate Web Dockerfile builds

### CD Pipeline (`cd.yml`)

Deploys to production after CI passes on `main` using a blue-green deployment strategy.

**Jobs:**
1. `build-and-push` — Build Docker images and push to ACR
2. `migrate-database` — Run EF Core migration bundle against production PostgreSQL
3. `deploy` — Blue-green deployment to Container Apps (requires `prod` environment approval):
   - Ensure multiple revision mode and ACR registry configuration
   - Deploy new staging revisions (with unique suffix per commit + attempt)
   - Wait for revisions to reach a running state (`Running`, `Degraded`, or `RunningAtMaxScale`) with a healthy health state (`Healthy` or `None`)
   - Verify staging revision health via Azure CLI before switching traffic
   - Switch 100% traffic to the new revisions
   - Deactivate old revisions
   - On failure: rollback by deactivating the failed staging revisions
4. `smoke-test` — Verify health endpoints respond with 200

## Deploying

### Standard Deployment (Push to Main)

1. Merge a PR to `main`
2. CI pipeline runs automatically
3. On CI success, CD pipeline triggers
4. Images are built and pushed to ACR, then database migrations run
5. Review and approve the deployment in the `prod` environment
6. New staging revisions are deployed and verified healthy before traffic is switched
7. Smoke tests verify the deployment

### Manual Deployment

```bash
# Trigger CD pipeline manually
gh workflow run cd.yml
```

### Infrastructure Updates

Edit files under `infra/` and push to `main`. Then trigger the `infra.yml` workflow manually:

```bash
gh workflow run infra.yml
```

> **Note:** Infrastructure changes are managed separately via the `infra.yml` workflow. The CD pipeline (`cd.yml`) only handles application deployment (image updates and revision management), not infrastructure provisioning.

### Custom Domain

Codec is served at `codec-chat.com` (web) and `api.codec-chat.com` (API) with Azure-managed TLS certificates. The CD pipeline handles custom domain binding automatically via a two-phase Bicep deployment:

1. **Phase 1** (`bindCertificates=false`): Deploys container apps with custom hostnames registered but no certificate binding
2. **Phase 2** (`bindCertificates=true`): Binds the provisioned managed certificates to the custom domains with SNI

This two-phase approach is required because Azure Container Apps managed certificates can only be created after the hostname is registered on the container app.

DNS records (managed via Squarespace Domains):
- `codec-chat.com` — A record pointing to Container Apps static IP
- `api.codec-chat.com` — CNAME record pointing to Container Apps Environment FQDN
- `asuid` / `asuid.api` — TXT records for domain verification

## Rollback

### Application Rollback

Redeploy the previous image by its commit SHA:

```bash
# Find the previous working commit SHA
git log --oneline -5

# Redeploy API
az containerapp update \
  --name ca-codec-prod-api \
  --resource-group rg-codec-prod \
  --image acrcodecprod.azurecr.io/codec-api:<previous-sha>

# Redeploy Web
az containerapp update \
  --name ca-codec-prod-web \
  --resource-group rg-codec-prod \
  --image acrcodecprod.azurecr.io/codec-web:<previous-sha>
```

Alternatively, revert the commit and push to `main` to trigger a normal deployment.

### Database Migration Rollback

EF Core migration bundles support rolling back by specifying a target migration:

```bash
# List applied migrations
dotnet ef migrations list --project apps/api/Codec.Api

# Generate a bundle that targets a specific earlier migration
dotnet ef migrations bundle \
  --project apps/api/Codec.Api \
  --output efbundle \
  --self-contained

# Run the bundle targeting the previous migration
./efbundle --connection "<connection-string>" <TargetMigrationName>
```

> **Warning:** Rolling back migrations that dropped columns or tables may cause data loss. Always review the migration's `Down()` method before rolling back.

### Database Point-in-Time Recovery

Azure Database for PostgreSQL Flexible Server includes automated backups with 7-day retention. To restore:

1. Go to the Azure Portal > `psql-codec-prod` > **Backup and restore**
2. Select a restore point (any time within the last 7 days)
3. Restore to a new server, verify data, then swap connection strings

## Manual Database Migrations

If you need to run migrations outside the CD pipeline:

```bash
# Install EF Core tools
dotnet tool install --global dotnet-ef

# Build the migration bundle
cd apps/api
dotnet ef migrations bundle \
  --project Codec.Api \
  --output ../../efbundle \
  --self-contained

# Get the connection string from Key Vault
CONNECTION_STRING=$(az keyvault secret show \
  --vault-name kv-codec-prod \
  --name ConnectionStrings--Default \
  --query value -o tsv)

# Run the bundle
cd ../..
./efbundle --connection "$CONNECTION_STRING"
```

## Environment Variables and Secrets

### API Container App

| Variable | Source | Description |
|----------|--------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Env var | `Production` |
| `ConnectionStrings__Default` | Key Vault secret ref | PostgreSQL connection string |
| `Google__ClientId` | Key Vault secret ref | Google OAuth client ID |
| `Api__BaseUrl` | Env var | Public API URL (`https://api.codec-chat.com`) |
| `Cors__AllowedOrigins` | Env var | Allowed CORS origins JSON array |
| `Storage__Provider` | Env var | `AzureBlob` |
| `Storage__AzureBlob__ServiceUri` | Env var | Blob storage endpoint URL |
| `GlobalAdmin__Email` | Key Vault secret ref | Email of the global admin user |

### Web Container App

| Variable | Source | Description |
|----------|--------|-------------|
| `NODE_ENV` | Env var | `production` |
| `PUBLIC_API_BASE_URL` | Build arg | API URL baked into the SvelteKit build |
| `PUBLIC_GOOGLE_CLIENT_ID` | Build arg | Google client ID baked into the build |

### Key Vault Secrets

| Secret Name | Description |
|-------------|-------------|
| `ConnectionStrings--Default` | PostgreSQL connection string |
| `Google--ClientId` | Google OAuth 2.0 client ID |
| `GlobalAdmin--Email` | Email address of the platform-wide global admin |

## Monitoring

### Logs

Container Apps logs stream to Log Analytics. Query logs in the Azure Portal:

```kusto
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "ca-codec-prod-api"
| order by TimeGenerated desc
| take 100
```

The API outputs structured JSON logs via Serilog, which Log Analytics can parse for filtering and alerting.

### Health Endpoints

| Endpoint | Container App | Purpose |
|----------|---------------|---------|
| `/health/live` | API | Liveness probe — process is running |
| `/health/ready` | API | Readiness probe — DB connectivity OK |
| `/health` | Web | Liveness probe — Node.js process running |

### Recommended Alerts

Configure in Azure Monitor:

- API container restart count > 0 in 5 minutes
- HTTP 5xx error rate > 5% in 5 minutes
- PostgreSQL CPU utilization > 80% sustained
- Container App replica scaling events

## Troubleshooting

### Container App won't start

```bash
# Check container logs
az containerapp logs show \
  --name ca-codec-prod-api \
  --resource-group rg-codec-prod \
  --type console \
  --follow

# Check revision status
az containerapp revision list \
  --name ca-codec-prod-api \
  --resource-group rg-codec-prod \
  -o table
```

### Database connection failures

```bash
# Verify the connection string in Key Vault
az keyvault secret show \
  --vault-name kv-codec-prod \
  --name ConnectionStrings--Default \
  --query value -o tsv

# Test connectivity from your machine (requires firewall rule)
psql "host=psql-codec-prod.postgres.database.azure.com dbname=codec user=codecadmin"
```

### Image pull failures

```bash
# Verify ACR has the image
az acr repository show-tags \
  --name acrcodecprod \
  --repository codec-api \
  -o table

# Verify the Container App's managed identity has AcrPull role
az role assignment list \
  --scope /subscriptions/<sub-id>/resourceGroups/rg-codec-prod/providers/Microsoft.ContainerRegistry/registries/acrcodecprod \
  -o table
```

### WebSocket / SignalR issues

Container Apps supports WebSocket connections natively. If SignalR fails:

1. Verify the API container app has `transport: http` ingress (not `http2`)
2. Check CORS origins include the web app domain
3. Verify the `access_token` query parameter reaches the API through the proxy

## Cost Estimate

| Resource | Spec | Est. Monthly Cost |
|----------|------|-------------------|
| Container Apps — API | 0.5 vCPU, 1 GiB, min 1 / max 1 | ~$15–25 |
| Container Apps — Web | 0.25 vCPU, 0.5 GiB, min 0 / max 2 | ~$5–10 |
| PostgreSQL Flexible | Burstable B1ms, 32 GB | ~$12–18 |
| Storage Account | < 1 GB | ~$0.02 |
| Container Registry | Basic, < 5 GB | ~$5 |
| Key Vault | < 10 secrets | ~$0.03 |
| Log Analytics | Free tier (5 GB/month) | ~$0 |
| **Total** | | **~$37–58/month** |
