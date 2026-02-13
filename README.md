# Codec

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Codec is a Discord-like chat application built with SvelteKit and ASP.NET Core Web API. Users authenticate via Google Sign-In; the web client obtains an ID token and the API validates it on each request.

> ⚠️ **Alpha status:** Codec is in active alpha development. APIs, data models, and UI may change without notice. Not recommended for production use.

## Repository Structure
```
codec/
├── apps/
│   ├── api/          # ASP.NET Core 9 Web API
│   │   └── Codec.Api/
│   └── web/          # SvelteKit web front-end
├── docs/             # Project documentation
├── .github/          # Copilot agent guidance and CI workflows
└── scripts/          # Build and utility scripts
```

## Prerequisites
- **Node.js** 20+ and npm
- **.NET SDK** 9.x
- **Google Cloud Console** project with OAuth 2.0 credentials

## Quick Start

### 1. Configure Google Sign-In
1. Create an OAuth 2.0 Client ID in [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
2. Add authorized JavaScript origins:
   - `http://localhost:5174` (for development)
3. Copy your Client ID for the next steps

### 2. Start the API
```bash
cd apps/api/Codec.Api
# Edit appsettings.Development.json and set Google:ClientId
dotnet run
```
The API runs at `http://localhost:5050` by default.

**Note:** The API will fail fast if `Google:ClientId` is missing. SQLite database migrations run automatically in development.

> **macOS users:** Port 5000 is reserved by AirPlay Receiver. The API uses port 5050 to avoid this conflict.

### 3. Start the Web App
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
- ✅ Kick members from servers (Owner/Admin only, with real-time overlay notification and fade-out)
- ✅ Server invites — Owner/Admin can generate invite codes; any user can join via code
- ✅ Channel browsing within servers
- ✅ Real-time message posting and viewing via SignalR (WebSockets)
- ✅ Typing indicators (“X is typing…”)
- ✅ User profile display
- ✅ Custom avatar upload with server-specific overrides and fallback chain
- ✅ Member lists for servers
- ✅ Friends system (send/accept/decline/cancel requests, friends list, remove friend)
- ✅ User search for adding friends (by name or email)
- ✅ Real-time friend request notifications via SignalR
- ✅ Friends panel accessible from Home icon with notification badge
- ✅ Direct messages — 1-on-1 private conversations with real-time delivery, typing indicators, and unread badges
- ✅ Emoji reactions on messages (toggle, reaction pills with counts, real-time sync)
- ✅ **Nicknames** — user-chosen display name that overrides Google name across all surfaces (messages, member lists, friends, typing indicators)
- ✅ **User Settings** — full-screen modal with profile management (nickname editing, avatar upload/remove) and account info

## Documentation
- [Development Setup](docs/DEV_SETUP.md) - Detailed development environment setup
- [Authentication](docs/AUTH.md) - How Google ID token validation works
- [Architecture](docs/ARCHITECTURE.md) - System design and API endpoints
- [Features](docs/FEATURES.md) - Current and planned features
- [Design](docs/DESIGN.md) - UI/UX design specification and theme
- [Friends](docs/FRIENDS.md) - Friends feature specification
- [Direct Messages](docs/DIRECT_MESSAGES.md) - Direct messages feature specification
- [Nicknames](docs/NICKNAMES.md) - Nicknames feature specification
- [User Settings](docs/USER_SETTINGS.md) - User settings feature specification
- [Data Layer](docs/DATA.md) - Database schema and migrations

## Community & Project Policies
- [License](LICENSE) - MIT license
- [Contributing](CONTRIBUTING.md) - How to contribute and submit PRs
- [Code of Conduct](CODE_OF_CONDUCT.md) - Community behavior expectations
- [Security Policy](SECURITY.md) - How to report vulnerabilities responsibly

## Technology Stack
- **Frontend:** SvelteKit 2.x, Svelte 5 runes, TypeScript, Vite — modular layered architecture with context-based state management
- **Backend:** ASP.NET Core 9, Controller-based APIs
- **Real-time:** SignalR (WebSockets)
- **Database:** SQLite with Entity Framework Core
- **Authentication:** Google Identity Services (ID tokens)
