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
│   ├── api/          # ASP.NET Core 10 Web API
│   │   └── Codec.Api/
│   ├── sfu/          # mediasoup SFU for voice channels
│   └── web/          # SvelteKit web front-end
├── docs/             # Project documentation
├── infra/            # Bicep IaC modules (Azure infrastructure)
├── .github/          # Copilot agent guidance, CI/CD workflows
└── scripts/          # Build and utility scripts
```

## Quick Start

### 1. Install Prerequisites
- **Node.js** 20+ and npm — [download](https://nodejs.org/)
- **.NET SDK** 10.x — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker** — for local PostgreSQL via Docker Compose
- **Google Cloud Console** project with OAuth 2.0 credentials

### 2. Configure Google Sign-In
1. Create an OAuth 2.0 Client ID in [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
2. Add authorized JavaScript origins:
   - `http://localhost:5174` (for development)
3. Copy your Client ID for the next steps

### 3. Start PostgreSQL
```bash
docker compose -f docker-compose.dev.yml up -d
```
This starts PostgreSQL 16 on `localhost:5433` with database `codec_dev`, user `codec`, password `codec_dev_password`.

### 4. Start the API
```bash
cd apps/api/Codec.Api
# Edit appsettings.Development.json - set Google:ClientId
dotnet run
```
The API runs at `http://localhost:5050` by default.

**Note:** The API will fail fast if `Google:ClientId` is missing. PostgreSQL database migrations run automatically in development.

> **macOS users:** Port 5000 is reserved by AirPlay Receiver. The API uses port 5050 to avoid this conflict.

### 5. Start the Web App
```bash
cd apps/web
cp .env.example .env
# Edit .env and set PUBLIC_GOOGLE_CLIENT_ID and PUBLIC_API_BASE_URL (http://localhost:5050)
npm install
npm run dev
```
The web app runs at `http://localhost:5174` by default.

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
- **Infrastructure:** Azure Container Apps + dedicated VM (voice SFU), Bicep IaC, GitHub Actions CI/CD

## License & Quality Checks
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2Fjflavan%2Fcodec-chat?ref=badge_large)