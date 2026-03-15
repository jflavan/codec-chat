# Codec
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat?ref=badge_shield)

Codec is a Discord-like chat application built with SvelteKit and ASP.NET Core Web API. Users authenticate via Google Sign-In; the web client obtains an ID token and the API validates it on each request.

**Live:** [https://codec-chat.com](https://codec-chat.com)

> ⚠️ **Alpha status:** Codec is in active alpha development. APIs, data models, and UI may change without notice. Not recommended for production use. Found a bug? [Report it here](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml).

## Repository Structure
```
codec/
├── apps/
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

- **Servers & channels** — create servers, invite members, manage roles (Owner/Admin/Member), organize conversations into channels
- **Real-time messaging** — instant delivery via SignalR WebSockets with typing indicators, @mentions, message replies, editing, deletion, and text formatting
- **Direct messages** — 1:1 private conversations with unread badges
- **Voice** — voice channels in servers (mediasoup SFU) and 1:1 DM calls with ringing/accept/decline flow
- **Rich media** — image uploads, emoji reactions, link previews with Open Graph metadata, inline YouTube embeds
- **User profiles** — nicknames, custom avatars with server-specific overrides, friends system with real-time notifications
- **PWA** — installable on desktop and mobile with offline fallback, app shortcuts, and share target
- **Google Sign-In** — stateless JWT authentication with automatic silent token refresh

For a full feature list, see [Features](docs/FEATURES.md). For API endpoints and system design, see [Architecture](docs/ARCHITECTURE.md).

## Testing

Codec has 448 automated tests across three test suites — see [Testing](docs/TESTING.md) for the full strategy.

```bash
# Web tests (Vitest)
cd apps/web && npm test

# API unit + integration tests (requires Docker for integration tests)
dotnet test Codec.sln
```

| Suite | Tests | Coverage |
|-------|-------|----------|
| Web (Vitest) | 134 | 98% line (unit-testable code) |
| API Unit (xUnit) | 205 | 95% line (core services) |
| API Integration (Testcontainers) | 109 | 72% line (full pipeline) |

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
- [Authentication](docs/AUTH.md) - How Google ID token validation works
- [Architecture](docs/ARCHITECTURE.md) - System design and API endpoints
- [Features](docs/FEATURES.md) - Current and planned features
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
- **Frontend:** SvelteKit 2.x, Svelte 5 runes, TypeScript, Vite — modular layered architecture with context-based state management; installable PWA via `@vite-pwa/sveltekit` with offline fallback, runtime font caching, and OS integration (shortcuts, share target)
- **Backend:** ASP.NET Core 10, Controller-based APIs
- **Caching:** Redis 8 distributed cache (message history) + SignalR backplane (multi-instance scale-out)
- **Real-time:** SignalR (WebSockets) for messaging and signaling; mediasoup-client + WebRTC for voice audio
- **Voice SFU:** Node.js + mediasoup v3 on a dedicated Azure VM (UDP media plane requires native sockets)
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** Google Identity Services (ID tokens)
- **Observability:** OpenTelemetry (traces, metrics, logs) via `Codec.ServiceDefaults`; Azure Monitor / Application Insights in production; OTLP export to local Aspire dashboard in dev
- **Local Dev:** .NET Aspire AppHost for single-command orchestration (Postgres, Redis, Azurite, API, Web) with developer dashboard
- **Testing:** xUnit + FluentAssertions + Moq (API unit), Testcontainers + WebApplicationFactory (API integration), Vitest + jsdom (web)
- **Infrastructure:** Azure Container Apps + dedicated VM (voice SFU), Bicep IaC, GitHub Actions CI/CD

## License & Quality Checks
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat?ref=badge_large)