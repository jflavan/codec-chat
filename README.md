# Codec

Codec is a Discord-like chat application built with SvelteKit and ASP.NET Core Web API. Users authenticate via Google Sign-In; the web client obtains an ID token and the API validates it on each request.

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
- ✅ Server creation and discovery
- ✅ Server joining and membership
- ✅ Server membership and roles (Owner, Admin, Member)
- ✅ Channel creation (Owner/Admin only)
- ✅ Channel browsing within servers
- ✅ Real-time message posting and viewing via SignalR (WebSockets)
- ✅ Typing indicators (“X is typing…”)
- ✅ User profile display
- ✅ Member lists for servers

## Documentation
- [Development Setup](docs/DEV_SETUP.md) - Detailed development environment setup
- [Authentication](docs/AUTH.md) - How Google ID token validation works
- [Architecture](docs/ARCHITECTURE.md) - System design and API endpoints
- [Features](docs/FEATURES.md) - Current and planned features
- [Data Layer](docs/DATA.md) - Database schema and migrations

## Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 9, Controller-based APIs
- **Real-time:** SignalR (WebSockets)
- **Database:** SQLite with Entity Framework Core
- **Authentication:** Google Identity Services (ID tokens)
