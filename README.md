# Codec

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Codec is a Discord-like chat application built with SvelteKit and ASP.NET Core Web API. Users authenticate via Google Sign-In; the web client obtains an ID token and the API validates it on each request.

**Live:** [https://codec-chat.com](https://codec-chat.com)

> ⚠️ **Alpha status:** Codec is in active alpha development. APIs, data models, and UI may change without notice. Not recommended for production use. Found a bug? [Report it here](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml).

## Repository Structure
```
codec/
├── apps/
│   ├── api/          # ASP.NET Core 10 Web API
│   │   └── Codec.Api/
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
- ✅ Google Sign-In authentication with persistent sessions (1-week duration)
- ✅ Automatic silent token refresh via Google One Tap
- ✅ **Default server ("Codec HQ")** — created automatically on startup; every new user is auto-joined as a Member on first sign-in
- ✅ Server creation and membership
- ✅ Server membership and roles (Owner, Admin, Member)
- ✅ Channel creation (Owner/Admin only)
- ✅ Kick members from servers (Owner/Admin/Global Admin, with real-time overlay notification and fade-out)
- ✅ Server invites — Owner/Admin can generate invite codes; any user can join via code
- ✅ **Server Settings** — gear icon in channel sidebar opens server settings modal for Owners/Admins; edit server name, rename channels, delete channels, delete server, view member count; real-time sync via SignalR
- ✅ Channel browsing within servers
- ✅ Real-time message posting and viewing via SignalR (WebSockets)
- ✅ Typing indicators (“X is typing…”)
- ✅ User profile display
- ✅ Custom avatar upload with server-specific overrides and fallback chain
- ✅ Member lists for servers with real-time updates via SignalR (automatic refresh on member join/leave)
- ✅ Friends system (send/accept/decline/cancel requests, friends list, remove friend)
- ✅ User search for adding friends (by name or email)
- ✅ Real-time friend request notifications via SignalR
- ✅ Friends panel accessible from Home icon with notification badge
- ✅ Direct messages — 1-on-1 private conversations with real-time delivery, typing indicators, and unread badges
- ✅ Emoji reactions on messages (toggle, reaction pills with counts, real-time sync)
- ✅ **Image uploads** — post images (PNG, JPEG, WebP, GIF) via file picker, clipboard paste, or drag-and-drop in both server channels and DMs
- ✅ **Image lightbox** — click any inline image to view it full-size in an overlay with keyboard dismiss and open-original link
- ✅ **Nicknames** — user-chosen display name that overrides Google name across all surfaces (messages, member lists, friends, typing indicators)
- ✅ **User Settings** — full-screen modal with profile management (nickname editing, avatar upload/remove) and account info
- ✅ **Link previews** — automatic URL detection in messages, Open Graph metadata fetching with SSRF protection, clickable embed cards with title/description/thumbnail, real-time delivery via SignalR; YouTube links render as click-to-play inline video players via `svelte-youtube-embed`
- ✅ **@mentions** — autocomplete member picker in composer, @here to notify everyone, mention badge counts on server icons and channel names, badge clearing on navigation, mentioned message highlighting
- ✅ **Message replies** — inline reply to any message in channels or DMs, reply context displayed above message body, click to scroll to original with highlight animation, Escape to cancel, graceful handling of deleted parent messages
- ✅ **Global Admin** — configurable global admin role with full access to all servers (see all servers, read/post/react in any channel, manage channels and invites, delete any server/channel/message, kick any member) regardless of membership; configured via `GlobalAdmin:Email` application setting (Key Vault secret provisioned via Bicep in production) and seeded at startup
- ✅ **Message deletion** — authors can delete their own messages in channels and DMs; global admin can delete any channel message; cascade-deletes reactions and link previews; real-time removal via SignalR; replies to deleted messages handled gracefully
- ✅ **Message editing** — authors can edit their own messages in channels and DMs; inline edit mode with Enter to save and Escape to cancel; "(edited)" label on modified messages; real-time sync via SignalR
- ✅ **Text formatting** — bold (`*text*` or `**text**`) and italic (`_text_`) in messages, with live preview in composer input
- ✅ **Progressive message loading** — initially loads last 100 messages per channel and DM; older messages load seamlessly as the user scrolls up; cursor-based pagination with `hasMore` flag and scroll position preservation
- ✅ **Connection status awareness** — composer shows "Codec connecting..." with animated ellipsis when the SignalR connection is lost; automatically restores full input when reconnected; auto-refreshes the page if the WebSocket cannot reconnect within 5 seconds
- ✅ **Response compression** — API responses compressed with Brotli and Gzip for faster load times
- ✅ **Loading screen** — branded full-screen splash with animated progress bar, CRT scanlines, and glowing logo during initial data bootstrap; fades out smoothly once servers, channels, and messages are loaded
- ✅ **Alpha notification** — on every login, a modal notifies users of the app’s alpha status and links to the GitHub bug report template for easy issue reporting

## Self-Hosting

Codec is designed to be easy to self-host. The repository includes a production-ready `docker-compose.yml` that starts the API, web frontend, PostgreSQL, and a local blob storage emulator.

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
- [Azure Deployment Plan](docs/AZURE_DEPLOYMENT_PLAN.md) - Phased deployment plan (all 10 phases complete)
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
- [Data Layer](docs/DATA.md) - Database schema and migrations

## Community & Project Policies
- [License](LICENSE) - MIT license
- [Contributing](CONTRIBUTING.md) - How to contribute and submit PRs
- [Code of Conduct](CODE_OF_CONDUCT.md) - Community behavior expectations
- [Security Policy](SECURITY.md) - How to report vulnerabilities responsibly

## Technology Stack
- **Frontend:** SvelteKit 2.x, Svelte 5 runes, TypeScript, Vite — modular layered architecture with context-based state management
- **Backend:** ASP.NET Core 10, Controller-based APIs
- **Real-time:** SignalR (WebSockets)
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** Google Identity Services (ID tokens)
- **Infrastructure:** Azure Container Apps, Bicep IaC, GitHub Actions CI/CD
