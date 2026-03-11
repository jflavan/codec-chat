# Infrastructure

This document covers the Azure infrastructure provisioned by Bicep for the Codec chat application.

For deployment procedures, pipeline triggers, and rollback guides, see [DEPLOYMENT.md](DEPLOYMENT.md).

## Resource Overview

All resources are deployed to the `rg-codec-prod` resource group in `centralus` using the naming convention `{abbreviation}-codec-{env}`.

| Resource | Name | Type | SKU / Size |
|----------|------|------|------------|
| Log Analytics | `log-codec-prod` | Workspace | PerGB2018 (30-day retention) |
| Application Insights | `appi-codec-prod` | Insights component | Web (LogAnalytics ingestion) |
| Container Registry | `acrcodecprod` | ACR | Basic |
| Key Vault | `kv-codec-prod` | Secrets store | Standard (soft delete 7d) |
| PostgreSQL | `psql-codec-prod` | Flexible Server | Standard_B1ms, 32 GB, PG 16 |
| Storage Account | `stcodecprod` | Blob (LRS) | Standard_LRS, Hot tier |
| Container Apps Env | `cae-codec-prod` | Managed environment | Consumption (serverless) |
| API App | `ca-codec-prod-api` | Container App | 0.5 CPU, 1 GB, 1 replica |
| Web App | `ca-codec-prod-web` | Container App | 0.25 CPU, 0.5 GB, 1–2 replicas |
| Redis Cache | `redis-codec-prod` | Azure Cache for Redis | Basic C0 (conditional) |
| Voice VM | `vm-codec-prod-voice` | Virtual Machine | Standard_D2als_v7 (conditional) |

## Bicep Structure

```
infra/
├── main.bicep              # Orchestrator — wires all modules together
├── main.bicepparam         # Production parameter defaults
├── modules/
│   ├── log-analytics.bicep
│   ├── application-insights.bicep  # OpenTelemetry sink (traces, metrics, logs)
│   ├── container-registry.bicep
│   ├── key-vault.bicep
│   ├── key-vault-secret.bicep    # Reusable helper (called per secret)
│   ├── postgresql.bicep
│   ├── storage-account.bicep
│   ├── container-apps-env.bicep
│   ├── container-app-api.bicep
│   ├── container-app-web.bicep
│   ├── managed-certificate.bicep
│   ├── redis-cache.bicep
│   └── voice-vm.bicep
└── voice/
    └── docker-compose.yml  # SFU + coturn (deployed to voice VM by CI/CD)
```

### Module Dependency Graph

```
logAnalytics ──┬─────────► containerAppsEnv ──┬──► apiApp ──► apiCert
               └─► appInsights (conn string)──┘  └──► webApp ──► webCert
containerRegistry ──────────────────────────┤
keyVault ──┬──► postgresql (stores conn string)  │
           ├──► googleClientIdSecret             │
           ├──► globalAdminEmailSecret           │
           ├──► gitHubTokenSecret (optional)     │
           ├──► voiceTurnSecretKv (if voice)     │
           └──► voiceSfuInternalKeyKv (if voice) │
storageAccount ─────────────────────────────────┘
redisCache (if redisEnabled) ──► stores conn string in KV
voiceVm (if voiceVmEnabled) ────────────────────┘
```

> **Note:** The Application Insights connection string is passed to the API container app as a plain environment variable (not via Key Vault) because the ingestion key is write-only — it can only send telemetry, not read data.

## Key Vault Secrets

All sensitive values are stored in Key Vault and referenced at runtime via system-assigned managed identities. No secrets appear in environment variables or container app configuration.

| Secret | Source | Consumer |
|--------|--------|----------|
| `ConnectionStrings--Default` | PostgreSQL module | API app |
| `Google--ClientId` | GitHub Actions secret | API app |
| `GlobalAdmin--Email` | GitHub Actions secret | API app |
| `GitHub--Token` | GitHub Actions secret (optional) | API app |
| `Redis--ConnectionString` | Redis Cache module (if Redis) | API app |
| `Voice--TurnSecret` | GitHub Actions secret (if voice) | API app |
| `Voice--SfuInternalKey` | GitHub Actions secret (if voice) | API app |

## Container Apps

Both container apps use `activeRevisionsMode: Multiple` to support blue-green deployments managed by the CD pipeline.

### API App (`ca-codec-prod-api`)

ASP.NET Core 10 Web API with SignalR.

- **Port:** 8080
- **Ingress:** External HTTP, CORS restricted to web app origin
- **Health probes:** Liveness (`/health/live`, 30s), Readiness (`/health/ready`, 10s, 5s timeout)
- **RBAC roles:**
  - AcrPull on Container Registry
  - Storage Blob Data Contributor on Storage Account
  - Key Vault Secrets User on Key Vault
- **Environment variables:** `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__Default` (secret ref), `Google__ClientId` (secret ref), `Api__BaseUrl`, `Cors__AllowedOrigins`, `Storage__Provider`, `Storage__AzureBlob__ServiceUri`, `GlobalAdmin__Email` (secret ref), `Redis__ConnectionString` (secret ref), `OTEL_SERVICE_NAME`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, plus optional voice and GitHub config

### Web App (`ca-codec-prod-web`)

SvelteKit with adapter-node.

- **Port:** 3000
- **Ingress:** External HTTP
- **Health probe:** Liveness (`/health`, 30s)
- **Scaling:** 1 min, 2 max replicas
- **RBAC roles:** AcrPull on Container Registry
- **Environment variables:** `NODE_ENV`, `PUBLIC_API_BASE_URL`, `PUBLIC_GOOGLE_CLIENT_ID`

## Custom Domains and TLS

Custom domains (`codec-chat.com`, `api.codec-chat.com`) use Azure-managed TLS certificates with automatic renewal.

### Two-Pass Certificate Deployment

Managed certificates can only be created after the hostname is registered on a container app, which creates a circular dependency. This is resolved with a `bindCertificates` parameter:

1. **Pass 1** (`bindCertificates=false`): Deploy apps with custom hostnames registered but no certificate binding
2. **Pass 2** (`bindCertificates=true`): Bind the provisioned managed certificates with SNI

Certificate resource IDs are computed deterministically in `main.bicep` to avoid implicit dependency cycles.

The infra pipeline optimizes this: if certificates already exist (checked via `az containerapp env certificate list`), it skips pass 1 and deploys in a single pass.

### DNS Configuration (Squarespace Domains)

| Record | Type | Value |
|--------|------|-------|
| `codec-chat.com` | A | Container Apps static IP |
| `api.codec-chat.com` | CNAME | Container Apps Environment FQDN |
| `asuid` | TXT | Domain verification token |
| `asuid.api` | TXT | Domain verification token |

## PostgreSQL

- **Version:** 16
- **SKU:** Standard_B1ms (Burstable)
- **Storage:** 32 GB
- **Backup:** 7-day retention, no geo-redundancy
- **Admin user:** `codecadmin`
- **Extensions:** `PG_TRGM` allow-listed via `azure.extensions` server parameter (required for trigram search indexes)
- **Firewall:** AllowAzureServices rule (0.0.0.0) permits access from Container Apps
- **Connection string:** Stored in Key Vault as `ConnectionStrings--Default`

## Storage Account

- **Redundancy:** Standard_LRS (locally redundant)
- **Access:** HTTPS only, TLS 1.2 minimum, shared key access disabled (RBAC only)
- **Containers:**
  - `avatars` — User/server profile images (public blob read)
  - `images` — Uploaded images and link preview media (public blob read)

## Voice Infrastructure (Conditional)

Deployed only when `voiceVmEnabled = true`. Both the SFU (mediasoup) and TURN server (coturn) require UDP port exposure that Azure Container Apps cannot provide, so they run on a dedicated VM.

### Voice VM (`vm-codec-prod-voice`)

- **Image:** Ubuntu 24.04 LTS
- **Size:** Standard_D2als_v7 (2 vCPU, 4 GB RAM)
- **Network:** VNet 10.1.0.0/24, static public IP with DNS label
- **Identity:** System-assigned (AcrPull role for pulling SFU image)
- **cloud-init:** Installs Docker and Docker Compose on first deployment

### Network Security Group Rules

| Priority | Name | Port(s) | Protocol | Source | Purpose |
|----------|------|---------|----------|--------|---------|
| 1000 | SSH | 22 | TCP | Operator CIDR or AzureCloud | Admin access |
| 1100 | TURN-UDP | 3478 | UDP | Any | STUN/TURN signaling |
| 1200 | TURN-TCP | 3478 | TCP | Any | TURN TCP fallback |
| 1300 | coturn-relay | 49152–49200 | UDP | Any | TURN relay media |
| 1400 | mediasoup-RTC | 40000–40100 | UDP | Any | WebRTC media |
| 1500 | SFU-API | 3001 | TCP | AzureCloud | Internal SFU HTTP API |

### Voice Services (Docker Compose)

Deployed to `/opt/voice/docker-compose.yml` on the VM by CI/CD, with secrets injected via `envsubst`.

- **SFU (mediasoup):** Custom image from ACR, port 3001 HTTP + ports 40000–40100 UDP. Authenticates internal API calls via `SFU_INTERNAL_KEY`.
- **coturn:** `coturn/coturn:4.6.2`, host networking, HMAC-SHA256 time-limited credentials. Realm: `codec-chat.com`.

## Zero-Downtime Infrastructure Deploys

The infra pipeline preserves running application containers through Bicep deployments, avoiding any production downtime.

### The Problem

Bicep container app modules default to a quickstart placeholder image (`mcr.microsoft.com/k8se/quickstart:latest`). Deploying Bicep without specifying the currently running image would reset containers to the placeholder, taking the site down.

### The Solution

Before deploying, the pipeline queries Azure for the currently running container images and passes them as Bicep parameters:

```yaml
- name: Resolve current container images
  run: |
    API_IMAGE=$(az containerapp show \
      --name ca-codec-prod-api \
      --resource-group rg-codec-prod \
      --query "properties.template.containers[0].image" -o tsv 2>/dev/null || echo "")
    if [ -z "$API_IMAGE" ]; then API_IMAGE="$FALLBACK"; fi
```

This ensures running revisions are never disrupted. On first-time deployments (no existing apps), it falls back to the quickstart placeholder gracefully.

### Race Prevention

Both `infra.yml` and `cd.yml` share the `deploy-prod` concurrency group with `cancel-in-progress: false`. When a commit includes `infra/` changes:

1. CI completes and triggers both pipelines
2. CD's `check-skip` job detects `infra/` changes and yields
3. Infra pipeline runs to completion
4. Infra triggers CD via `gh workflow run cd.yml`
5. Shared concurrency group prevents overlap

### Previous Approach (Superseded)

The original infra pipeline deactivated all active container app revisions before deploying. This solved secret/registry conflicts but caused production downtime during every infrastructure deployment.

## Bicep Parameters

The full parameter list for `main.bicep`:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `location` | string | `centralus` | Azure region |
| `environmentName` | string | `prod` | Environment name for resource naming |
| `googleClientId` | secure string | — | Google OAuth Client ID |
| `postgresqlAdminPassword` | secure string | — | PostgreSQL admin password |
| `globalAdminEmail` | secure string | — | Global admin user email |
| `apiContainerImage` | string | quickstart | API container image |
| `webContainerImage` | string | quickstart | Web container image |
| `webCustomDomain` | string | `''` | Custom domain for web app |
| `apiCustomDomain` | string | `''` | Custom domain for API |
| `bindCertificates` | bool | `false` | Bind managed TLS certificates |
| `redisEnabled` | bool | `true` | Deploy Azure Cache for Redis |
| `voiceVmEnabled` | bool | `false` | Deploy voice VM infrastructure |
| `voiceAdminSshPublicKey` | secure string | `''` | SSH public key for voice VM |
| `voiceSshAllowedSourcePrefix` | string | `''` | Source CIDR for SSH access |
| `gitHubToken` | secure string | `''` | GitHub PAT for bug reporting |
| `voiceTurnSecret` | secure string | `''` | TURN server shared secret |
| `voiceSfuInternalKey` | secure string | `''` | SFU internal API key |

## Outputs

| Output | Description |
|--------|-------------|
| `apiAppFqdn` | API Container App FQDN |
| `webAppFqdn` | Web Container App FQDN |
| `containerRegistryLoginServer` | ACR login server URL |
| `postgresqlFqdn` | PostgreSQL server FQDN |
| `storageBlobEndpoint` | Blob storage endpoint |
| `keyVaultUri` | Key Vault URI |
| `voiceVmPublicIp` | Voice VM public IP (if enabled) |
| `voiceVmFqdn` | Voice VM DNS name (if enabled) |
| `sfuApiUrl` | SFU HTTP API URL (if enabled) |
| `turnServerUrl` | TURN server URL (if enabled) |
