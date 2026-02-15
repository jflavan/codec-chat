# Self-Hosting Guide

This guide explains how to deploy Codec on your own infrastructure using Docker Compose. No cloud provider account is required — just a machine with Docker installed.

## Prerequisites

- **Docker** 24+ and **Docker Compose** v2+
- A **Google Cloud Console** project with OAuth 2.0 credentials ([setup instructions](#google-oauth-setup))
- A server or VM with at least **1 vCPU** and **1 GB RAM**
- (Optional) A domain name and reverse proxy for HTTPS

## Quick Start

1. Clone the repository:
   ```bash
   git clone https://github.com/jflavan/codec-chat.git
   cd codec-chat
   ```

2. Create a `.env` file in the repository root:
   ```bash
   cp .env.example .env
   ```

3. Edit `.env` with your configuration:
   ```env
   GOOGLE_CLIENT_ID=your-google-client-id.apps.googleusercontent.com
   ```

4. Start all services:
   ```bash
   docker compose up -d
   ```

5. Open `http://localhost:3000` in your browser.

That's it. The `docker-compose.yml` starts four services:
- **PostgreSQL 16** — database on port `5433` (internal `5432`)
- **Azurite** — local blob storage emulator on port `10000`
- **API** — ASP.NET Core backend on port `5050`
- **Web** — SvelteKit frontend on port `3000`

## Google OAuth Setup

Codec uses Google Sign-In for authentication. You need a Google OAuth 2.0 Client ID:

1. Go to [Google Cloud Console → Credentials](https://console.cloud.google.com/apis/credentials)
2. Create a new project (or select an existing one)
3. Navigate to **APIs & Services** → **Credentials**
4. Click **Create Credentials** → **OAuth 2.0 Client ID**
5. Configure the OAuth consent screen if prompted
6. Select **Web application** as the application type
7. Under **Authorized JavaScript origins**, add:
   - `http://localhost:3000` (for local access)
   - `https://your-domain.com` (if using a custom domain)
8. Save and copy the **Client ID**

> **Important:** Add every origin where users will access Codec. If you host it at `https://chat.example.com`, that origin must be listed.

## Environment Variables

### `.env` file (Docker Compose root)

The `docker-compose.yml` reads these variables from a `.env` file in the project root:

| Variable | Required | Description |
|----------|----------|-------------|
| `GOOGLE_CLIENT_ID` | Yes | Your Google OAuth 2.0 Client ID |

### API Container

These are configured in `docker-compose.yml` and generally don't need changes for basic self-hosting:

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Set to `Production` for production use |
| `ConnectionStrings__Default` | (set in compose) | PostgreSQL connection string |
| `Google__ClientId` | From `GOOGLE_CLIENT_ID` | Google OAuth Client ID |
| `Api__BaseUrl` | `http://localhost:5050` | Public URL of the API (used for avatar/image URLs) |
| `Cors__AllowedOrigins__0` | `http://localhost:3000` | Allowed CORS origin (must match web app URL) |
| `Storage__Provider` | `AzureBlob` | File storage backend: `Local` or `AzureBlob` |
| `Storage__AzureBlob__ServiceUri` | (Azurite URL) | Blob storage endpoint (Azurite in dev, Azure in prod) |

### Web Container

| Variable | Default | Description |
|----------|---------|-------------|
| `PUBLIC_API_BASE_URL` | `http://localhost:5050` | URL where the browser can reach the API |
| `PUBLIC_GOOGLE_CLIENT_ID` | From `GOOGLE_CLIENT_ID` | Google OAuth Client ID |

> **Note:** `PUBLIC_API_BASE_URL` is baked into the SvelteKit build at image build time (via Docker build args) and also set as a runtime environment variable. The build-time value is used for server-side rendering; the runtime value is available to the Node.js process.

## Storage Options

Codec supports two file storage backends for avatars and uploaded images:

### Option 1: Azurite (Default in docker-compose.yml)

The default `docker-compose.yml` uses [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite), a local Azure Blob Storage emulator. This works out of the box with no configuration.

```yaml
# Already configured in docker-compose.yml
Storage__Provider: "AzureBlob"
Storage__AzureBlob__ServiceUri: "http://azurite:10000/devstoreaccount1"
```

### Option 2: Local Disk Storage

To store files on the host filesystem instead:

1. Update the API environment in `docker-compose.yml`:
   ```yaml
   api:
     environment:
       Storage__Provider: "Local"
     volumes:
       - ./uploads:/app/uploads
   ```
2. Remove the `azurite` service from `docker-compose.yml` if no longer needed.

Files are stored under `/app/uploads/` inside the container. The volume mount persists them on the host.

### Option 3: Azure Blob Storage (Production)

To use a real Azure Storage Account:

```yaml
api:
  environment:
    Storage__Provider: "AzureBlob"
    Storage__AzureBlob__ServiceUri: "https://yourstorageaccount.blob.core.windows.net"
```

The API uses `DefaultAzureCredential` for authentication — configure managed identity, a service principal, or connection-string-based auth as appropriate.

## Custom Domain with Reverse Proxy

For production use, place a reverse proxy in front of Codec to handle HTTPS, custom domains, and WebSocket upgrades.

### Caddy (Recommended — automatic HTTPS)

Create a `Caddyfile`:

```caddyfile
chat.example.com {
    reverse_proxy web:3000
}

api.chat.example.com {
    reverse_proxy api:8080
}
```

Add Caddy to `docker-compose.yml`:

```yaml
services:
  caddy:
    image: caddy:2-alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy_data:/data
    depends_on:
      - web
      - api

volumes:
  caddy_data:
```

Then update the environment variables:

```yaml
api:
  environment:
    Api__BaseUrl: "https://api.chat.example.com"
    Cors__AllowedOrigins__0: "https://chat.example.com"

web:
  build:
    args:
      PUBLIC_API_BASE_URL: "https://api.chat.example.com"
  environment:
    PUBLIC_API_BASE_URL: "https://api.chat.example.com"
```

And add your domain to Google OAuth **Authorized JavaScript origins** in Google Cloud Console.

### nginx

Example `nginx.conf`:

```nginx
server {
    listen 443 ssl;
    server_name chat.example.com;

    ssl_certificate     /etc/ssl/certs/chat.example.com.pem;
    ssl_certificate_key /etc/ssl/private/chat.example.com.key;

    location / {
        proxy_pass http://web:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

server {
    listen 443 ssl;
    server_name api.chat.example.com;

    ssl_certificate     /etc/ssl/certs/api.chat.example.com.pem;
    ssl_certificate_key /etc/ssl/private/api.chat.example.com.key;

    location / {
        proxy_pass http://api:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket support (required for SignalR)
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

> **Important:** The API uses SignalR (WebSockets) for real-time messaging. Your reverse proxy **must** support WebSocket upgrades on the API endpoint. The nginx config above includes the required `Upgrade` and `Connection` headers.

### Traefik

If using Traefik, add labels to the `api` and `web` services in `docker-compose.yml`:

```yaml
api:
  labels:
    - "traefik.enable=true"
    - "traefik.http.routers.api.rule=Host(`api.chat.example.com`)"
    - "traefik.http.routers.api.tls.certresolver=letsencrypt"
    - "traefik.http.services.api.loadbalancer.server.port=8080"

web:
  labels:
    - "traefik.enable=true"
    - "traefik.http.routers.web.rule=Host(`chat.example.com`)"
    - "traefik.http.routers.web.tls.certresolver=letsencrypt"
    - "traefik.http.services.web.loadbalancer.server.port=3000"
```

## Database

### Default (Docker Compose PostgreSQL)

The `docker-compose.yml` includes a PostgreSQL 16 container. Data is persisted in a Docker named volume (`pgdata`).

The API automatically applies database migrations on startup in Development mode. In Production mode, you should run migrations explicitly:

```bash
# Apply migrations via the API container
docker compose exec api dotnet Codec.Api.dll --migrate
```

Or run migrations using EF Core tools from the host:

```bash
dotnet tool install --global dotnet-ef
cd apps/api/Codec.Api
dotnet ef database update --connection "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password"
```

### External PostgreSQL

To use your own PostgreSQL instance (e.g., a managed database service):

1. Remove the `postgres` service from `docker-compose.yml`
2. Update the API's connection string:
   ```yaml
   api:
     environment:
       ConnectionStrings__Default: "Host=your-db-host;Port=5432;Database=codec;Username=codecuser;Password=your-secure-password"
   ```

> **Supported version:** PostgreSQL 14 or later.

### Backups

For the Docker Compose PostgreSQL container, back up the database with `pg_dump`:

```bash
docker compose exec postgres pg_dump -U codec codec_dev > backup_$(date +%Y%m%d).sql
```

Restore from a backup:

```bash
docker compose exec -T postgres psql -U codec codec_dev < backup_20260101.sql
```

## Seed Data

On first startup, the API automatically creates:
- A default **"Codec HQ"** server with `general`, `announcements`, and `build-log` channels
- Sample users and messages (in Development mode only)

Every new user who signs in is automatically joined to "Codec HQ" as a Member.

## Health Checks

| Endpoint | Service | Description |
|----------|---------|-------------|
| `http://localhost:5050/health/live` | API | Liveness — process is running |
| `http://localhost:5050/health/ready` | API | Readiness — database connectivity OK |
| `http://localhost:3000/health` | Web | Liveness — Node.js process running |

## Updating

To update to the latest version:

```bash
git pull
docker compose build
docker compose up -d
```

The API will apply any new database migrations automatically on startup (Development mode). For production, run migrations before updating containers:

```bash
docker compose build
docker compose run --rm api dotnet Codec.Api.dll --migrate
docker compose up -d
```

## Production Hardening Checklist

Before exposing Codec to the internet, review these items:

- [ ] **HTTPS:** Use a reverse proxy with TLS certificates (see [Custom Domain](#custom-domain-with-reverse-proxy))
- [ ] **Database password:** Change the default PostgreSQL password in `docker-compose.yml`
- [ ] **ASPNETCORE_ENVIRONMENT:** Set to `Production` (disables seed data, enables stricter security settings)
- [ ] **CORS origins:** Update `Cors__AllowedOrigins__0` to match your production domain
- [ ] **API base URL:** Update `Api__BaseUrl` and `PUBLIC_API_BASE_URL` to your production URLs
- [ ] **Google OAuth origins:** Add your production domain to Authorized JavaScript Origins in Google Cloud Console
- [ ] **Firewall:** Only expose ports 80/443 (via reverse proxy). Do not expose ports 5050, 5433, or 10000 directly
- [ ] **Backups:** Set up regular database backups (see [Backups](#backups))
- [ ] **Resource limits:** Add `deploy.resources.limits` to Docker Compose services for memory/CPU control
- [ ] **Logs:** Monitor API and web container logs for errors (`docker compose logs -f`)

## Troubleshooting

### API fails to start — "Google:ClientId is required"
Set `GOOGLE_CLIENT_ID` in your `.env` file. The API will not start without a valid Google OAuth Client ID.

### Google Sign-In fails — "Unauthorized JavaScript origin"
Add the URL where you access Codec (e.g., `http://localhost:3000` or `https://chat.example.com`) as an Authorized JavaScript Origin in your Google Cloud Console project.

### CORS errors in the browser
Ensure `Cors__AllowedOrigins__0` on the API matches the exact URL of the web app (protocol + host + port).

### WebSocket/SignalR connection fails
If using a reverse proxy, ensure it supports WebSocket upgrades. See the [nginx configuration example](#nginx) for required headers.

### Database connection refused
Ensure the PostgreSQL container is running and healthy:
```bash
docker compose ps
docker compose logs postgres
```

### Images/avatars not loading
Check the `Storage__Provider` setting. If using `Local` storage, ensure the `/app/uploads` volume is mounted. If using Azurite, ensure the Azurite container is running.

### Reset everything
To wipe the database and start fresh:
```bash
docker compose down -v
docker compose up -d
```

> **Warning:** This deletes all data (messages, users, servers, uploaded files).

## Architecture Reference

For a detailed breakdown of the system architecture, API endpoints, SignalR events, and data model, see [ARCHITECTURE.md](ARCHITECTURE.md).

## Comparison with Azure Deployment

The [DEPLOYMENT.md](DEPLOYMENT.md) guide covers deploying Codec to Azure Container Apps with Bicep IaC. The self-hosting approach documented here uses the same Docker images and the same `docker-compose.yml`, but runs everything on your own infrastructure. The key differences:

| Aspect | Self-Hosted (this guide) | Azure (DEPLOYMENT.md) |
|--------|--------------------------|----------------------|
| Infrastructure | Your own server/VM | Azure Container Apps |
| Database | Docker Compose PostgreSQL or external | Azure Database for PostgreSQL Flexible Server |
| File storage | Azurite or local disk | Azure Blob Storage |
| HTTPS/TLS | Reverse proxy (Caddy, nginx, Traefik) | Azure-managed certificates |
| CI/CD | Manual `docker compose build && up` | GitHub Actions → ACR → Container Apps |
| Secrets | `.env` file | Azure Key Vault |
| Cost | Only your server costs | ~$37–58/month (Azure resources) |
