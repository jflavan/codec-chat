# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Codec is a Discord-like chat application — a monorepo with a SvelteKit frontend (`apps/web`) and an ASP.NET Core 10 Web API backend (`apps/api`). Authentication is Google Sign-In only (ID tokens validated by the API on every request). Real-time features run over SignalR WebSockets.

## Development Commands

### Start PostgreSQL (required before API)
```bash
docker compose up -d postgres azurite
# PostgreSQL 16 on localhost:5433, DB: codec_dev, user: codec, password: codec_dev_password
# (docker-compose.dev.yml is gitignored; use the top-level docker-compose.yml for local dev)
```

### API (`apps/api/Codec.Api`)
```bash
cd apps/api/Codec.Api
dotnet run          # API at http://localhost:5050; auto-migrates DB in development
dotnet build        # Build only
```

### Web (`apps/web`)
```bash
cd apps/web
npm install
npm run dev         # Dev server at http://localhost:5174
npm run build       # Production build
npm run check       # svelte-check + TypeScript + deprecated-events lint
```

### Database migrations (EF Core)
```bash
# Requires: dotnet tool install --global dotnet-ef
cd apps/api/Codec.Api
dotnet ef migrations add <MigrationName>  # Create new migration
dotnet ef database update                  # Apply manually
dotnet ef migrations script                # View SQL
```

## Architecture

### Repository Layout
```
apps/
  api/Codec.Api/     # ASP.NET Core 10 Web API
    Controllers/     # One controller per resource area
    Models/          # EF Core entities + request DTOs
    Data/            # CodecDbContext, SeedData, DesignTimeDbContextFactory
    Hubs/            # ChatHub (SignalR)
    Services/        # UserService, AvatarService, ImageUploadService, LinkPreviewService, file storage
    Migrations/      # EF Core code-first migrations
  web/src/
    lib/
      api/           # ApiClient class (typed HTTP client)
      auth/          # Token persistence (localStorage) + Google SDK init
      services/      # ChatHubService (SignalR lifecycle)
      state/         # AppState class — central $state/$derived reactive state
      types/         # Domain models (models.ts)
      styles/        # CSS design tokens (tokens.css) + global.css
      utils/         # Pure helpers (format.ts)
      components/    # Svelte 5 components grouped by feature area
    routes/
      +layout.svelte  # Root layout
      +page.svelte    # Thin shell (~75 lines); creates AppState, sets context
infra/               # Bicep IaC modules (Azure)
.github/             # Copilot agent/instruction files, CI/CD workflows
```

### Frontend State Pattern
`AppState` (`lib/state/app-state.svelte.ts`) is the single source of truth. It is created once in `+page.svelte` via `createAppState()` and injected into the component tree with `setContext()`. All child components retrieve it with `getAppState()`. Use Svelte 5 runes (`$state`, `$derived`) — never legacy stores.

### API Design
- Controller-based (`[ApiController]`), all endpoints require `[Authorize]` except `/health/*`
- Google ID token validated as JWT Bearer; SignalR reads token from `?access_token=` query string
- All JSON uses camelCase (configured via `AddJsonProtocol`)
- Rate limit: fixed window, 100 req/min
- Response compression: Brotli + Gzip on `application/json`
- File storage: `Local` (dev) or `AzureBlob` (prod), controlled by `Storage:Provider` config

### Real-time (SignalR)
Hub at `/hubs/chat`. Clients subscribe to:
- `channel-{channelId}` groups for message events
- `server-{serverId}` groups for membership/settings events
- `user-{userId}` groups for friend/DM/kick notifications
- `dm-{dmChannelId}` groups for DM events

### Authentication Flow
1. Browser gets Google ID token via Google Identity Services SDK
2. API requests use `Authorization: Bearer <token>`
3. API validates token against Google JWKS (no server-side sessions)
4. User identity mapped to internal `User` records via `GoogleSubject`

## Configuration

### Web (`.env` — copy from `.env.example`)
```
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=<your google oauth client id>
```

### API (`appsettings.Development.json`)
```json
{
  "Google": { "ClientId": "<your google oauth client id>" },
  "ConnectionStrings": { "Default": "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password" },
  "Cors": { "AllowedOrigins": ["http://localhost:5174"] },
  "Api": { "BaseUrl": "http://localhost:5050" },
  "GlobalAdmin": { "Email": "<optional global admin email>" }
}
```
`Google:ClientId` is required — the API throws on startup if missing.

## Code Conventions

### C# (API)
- C# 14, .NET 10, nullable reference types enabled, file-scoped namespaces
- PascalCase for public members/methods; camelCase for private fields
- `is null` / `is not null` (not `== null`)
- Use pattern matching and switch expressions where applicable

### TypeScript/Svelte (Web)
- Svelte 5 runes only (`$state`, `$derived`, `$effect`) — no legacy stores
- Prefer `$derived` over `$effect` for computed values
- Use `<script lang="ts">` with `$props()` for component props
- Components organized by feature area under `lib/components/`
- Avoid adding new dependencies without a clear reason

## Required Updates When Changing Code

- **Data model changes:** create a new EF Core migration and update `SeedData` if needed
- **Auth changes:** update `docs/AUTH.md`
- **User-visible behavior changes:** update `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, and `PLAN.md`
- **Public env vars:** keep `apps/web/.env.example` in sync
