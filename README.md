# Codec
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat?ref=badge_shield)

Codec is a Discord-like chat application built with SvelteKit and ASP.NET Core Web API. It supports multiple authentication methods (Google Sign-In, email/password, GitHub OAuth, Discord OAuth, and SAML 2.0 SSO), all backed by API-issued JWTs with rotating refresh tokens.

> ⚠️ **Alpha status:** Codec is in active alpha development. APIs, data models, and UI may change without notice. Not recommended for production use. Found a bug? [Report it here](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml).

## Repository Structure
```
codec/
├── apps/
│   ├── admin/                         # SvelteKit admin panel (platform management)
│   ├── api/
│   │   ├── Codec.Api/                 # ASP.NET Core 10 Web API
│   │   ├── Codec.Api.Tests/           # xUnit unit tests (services, controllers)
│   │   ├── Codec.Api.IntegrationTests/ # Integration tests (Testcontainers + WebApplicationFactory)
│   │   └── Codec.ServiceDefaults/     # Shared OpenTelemetry, health checks, resilience
│   ├── aspire/
│   │   └── Codec.AppHost/            # .NET Aspire orchestrator (local dev)
│   ├── sfu/                          # mediasoup SFU for voice channels
│   └── web/                          # SvelteKit web front-end (includes Vitest specs)
├── docs/             # Project documentation
├── infra/            # Bicep IaC modules (Azure infrastructure)
├── .github/          # Copilot agent guidance, CI/CD workflows
└── scripts/          # Build and utility scripts
```

## Quick Start

### Prerequisites
- **Node.js** 20+ and npm — [download](https://nodejs.org/)
- **.NET SDK** 10.x — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker** — for containers (PostgreSQL, Redis, Azurite)
- **Google Cloud Console** project with an OAuth 2.0 Client ID ([setup](https://console.cloud.google.com/apis/credentials) — add `http://localhost:5174` as an authorized JavaScript origin)
- **GIPHY API Key** *(optional)* — for the GIF picker ([get one](https://developers.giphy.com/dashboard/))

### Start with Aspire (recommended)

Aspire orchestrates all services with a single command and provides a dashboard with distributed tracing, logs, and metrics.

```bash
# Set your Google Client ID (persists in user secrets)
cd apps/aspire/Codec.AppHost
dotnet user-secrets set "Google:ClientId" "YOUR_GOOGLE_CLIENT_ID"

# Configure the web app
cd ../../../apps/web
cp .env.example .env
# Edit .env — set PUBLIC_GOOGLE_CLIENT_ID and PUBLIC_API_BASE_URL=http://localhost:5050

# Start everything
cd ../../apps/aspire/Codec.AppHost
dotnet run
```

- **Web:** http://localhost:5174
- **API:** http://localhost:5050
- **Aspire Dashboard:** https://localhost:17222

### Start without Aspire (alternative)

```bash
docker compose up -d postgres azurite    # Start PostgreSQL + Azurite
cd apps/api/Codec.Api
# Edit appsettings.Development.json — set Google:ClientId
dotnet run                                # API at http://localhost:5050

cd ../../apps/web
cp .env.example .env
# Edit .env — set PUBLIC_GOOGLE_CLIENT_ID and PUBLIC_API_BASE_URL
npm install && npm run dev                # Web at http://localhost:5174
```

For full setup details, see [Development Setup](docs/DEV_SETUP.md).

## Features

- **Servers & channels** — create servers, invite members, custom roles with granular permissions and channel overrides, organize conversations into channels with categories
- **Real-time messaging** — instant delivery via SignalR WebSockets with typing indicators, @mentions, message replies, editing, deletion, text formatting, and message search
- **Direct messages** — 1:1 private conversations with unread badges
- **Voice & video** — voice channels in servers (mediasoup SFU), 1:1 DM calls with ringing/accept/decline flow, video chat, and screen sharing
- **Rich media** — image uploads, file attachments, emoji reactions, custom server emojis, GIPHY GIF picker, link previews with Open Graph metadata, inline YouTube embeds, image proxying
- **User profiles** — nicknames, custom status messages, custom avatars with server-specific overrides, friends system with real-time notifications
- **Moderation** — user banning with message purge, content reporting, custom roles with 21 granular permissions, audit log with 90-day retention
- **Admin panel** — standalone admin site for platform-wide management: live dashboard, user/server management, moderation queue, system announcements, audit log
- **Integrations** — outgoing webhooks with HMAC-SHA256 signing, web push notifications
- **PWA** — installable on desktop and mobile with offline fallback, app shortcuts, and share target
- **Authentication** — Google Sign-In, email/password, GitHub OAuth, Discord OAuth, and SAML 2.0 SSO — all backed by API-issued JWTs with rotating refresh tokens
- **Account management** — account deletion with data anonymization, account linking across providers, email verification

For a full feature list, see [Features](docs/FEATURES.md). For API endpoints and system design, see [Architecture](docs/ARCHITECTURE.md).

## Testing

Codec has 1,746 automated tests across four test suites — see [Testing](docs/TESTING.md) for the full strategy.

```bash
# Web tests (Vitest)
cd apps/web && npm test

# Admin tests (Vitest)
cd apps/admin && npm test

# API unit + integration tests (requires Docker for integration tests)
dotnet test Codec.sln
```

| Suite | Tests | Coverage |
|-------|-------|----------|
| API Unit (xUnit) | 1,308 | 95% line (core services) |
| API Integration (Testcontainers) | 182 | 80% line (full pipeline) |
| Web (Vitest) | 181 | 85% statement (utilities + API client) |
| Admin (Vitest) | 75 | 98% statement (API client + services) |

## Self-Hosting

Codec is designed to be easy to self-host. The repository includes a production-ready `docker-compose.yml` that starts the API, web frontend, PostgreSQL, Redis, and a local blob storage emulator.

```bash
git clone https://github.com/jflavan/codec-chat.git
cd codec-chat
cp .env.example .env
# Edit .env — set GOOGLE_CLIENT_ID
docker compose up -d
# Open http://localhost:3000
```

For full details — custom domains, HTTPS with Caddy/nginx/Traefik, external PostgreSQL, storage options, and production hardening — see the [Self-Hosting Guide](docs/SELF_HOSTING.md).

## Bug Reports

Codec is in alpha — your feedback matters! Use the [Bug Report template](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml) to report any issues you find. The app also shows a notification on every login with a direct link to file a report.

## Documentation
- [Self-Hosting Guide](docs/SELF_HOSTING.md) - Deploy Codec on your own server with Docker Compose
- [Deployment (Azure)](docs/DEPLOYMENT.md) - Azure Container Apps deployment, rollback, and operations
- [Infrastructure](docs/INFRA.md) - Azure Bicep modules, resource configuration, and zero-downtime deploys
- [Development Setup](docs/DEV_SETUP.md) - Detailed development environment setup
- [Authentication](docs/AUTH.md) - Multi-provider auth: Google, email/password, GitHub, Discord, SAML 2.0
- [Architecture](docs/ARCHITECTURE.md) - System design and API endpoints
- [Features](docs/FEATURES.md) - Current and planned features
- [Admin Panel](docs/ADMIN.md) - Global admin panel for platform management
- [Roles & Permissions](docs/ROLES.md) - Custom roles, granular permissions, and channel overrides
- [Design](docs/DESIGN.md) - UI/UX design specification and theme
- [Friends](docs/FRIENDS.md) - Friends feature specification
- [Direct Messages](docs/DIRECT_MESSAGES.md) - Direct messages feature specification
- [Link Previews](docs/LINK_PREVIEWS.md) - Link previews feature specification
- [Nicknames](docs/NICKNAMES.md) - Nicknames feature specification
- [User Settings](docs/USER_SETTINGS.md) - User settings feature specification
- [Message Replies](docs/REPLIES.md) - Message replies feature specification
- [Voice Channels](docs/VOICE.md) - Voice channels feature specification, SFU architecture, and infrastructure
- [Data Layer](docs/DATA.md) - Database schema and migrations
- [Testing](docs/TESTING.md) - Testing strategy, test suites, coverage, and how to add tests

## Community & Project Policies
- [License](LICENSE) - MIT license
- [Contributing](CONTRIBUTING.md) - How to contribute and submit PRs
- [Code of Conduct](CODE_OF_CONDUCT.md) - Community behavior expectations
- [Security Policy](SECURITY.md) - How to report vulnerabilities responsibly

## Technology Stack
- **Frontend:** SvelteKit 2.x, Svelte 5 runes, TypeScript, Vite — modular domain-store architecture (auth, servers, channels, messages, DMs, friends, voice, UI) with context-based state management; installable PWA via `@vite-pwa/sveltekit` with offline fallback, runtime font caching, and OS integration (shortcuts, share target)
- **Admin:** Standalone SvelteKit app with Chart.js dashboards and SignalR real-time stats
- **Backend:** ASP.NET Core 10, Controller-based APIs
- **Caching:** Redis 8 distributed cache (message history) + SignalR backplane (multi-instance scale-out)
- **Real-time:** SignalR (WebSockets) for messaging and signaling; mediasoup-client + WebRTC for voice audio
- **Voice SFU:** Node.js + mediasoup v3 on a dedicated Azure VM (UDP media plane requires native sockets)
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** API-issued JWTs with rotating refresh tokens; Google Sign-In, email/password (bcrypt), GitHub OAuth, Discord OAuth, SAML 2.0 SSO
- **Observability:** OpenTelemetry (traces, metrics, logs) via `Codec.ServiceDefaults`; Azure Monitor / Application Insights in production; OTLP export to local Aspire dashboard in dev
- **Local Dev:** .NET Aspire AppHost for single-command orchestration (Postgres, Redis, Azurite, API, Web, Admin) with developer dashboard
- **Testing:** xUnit + FluentAssertions + Moq (API unit), Testcontainers + WebApplicationFactory (API integration), Vitest + jsdom (web and admin)
- **Infrastructure:** Azure Container Apps + dedicated VM (voice SFU), Bicep IaC, GitHub Actions CI/CD

## License & Quality Checks
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat?ref=badge_large)
