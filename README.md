# Codec

Codec is a Discord-like app built with SvelteKit and ASP.NET Core Web API. Authentication uses Google Sign-In; the web client obtains an ID token and the API validates it on each request.

## Repo layout
- apps/web: SvelteKit web front-end
- apps/api: ASP.NET Core Web API
- docs: Project documentation
- .github: Copilot agent guidance and workflows

## Prerequisites
- Node.js 20+ and npm
- .NET SDK 9.x

## Quick start
### API
1. Set Google Client ID in apps/api/appsettings.Development.json
   - The API will fail fast if Google:ClientId is missing.
   - SQLite database path is configured in ConnectionStrings:Default.
2. Run the API:
   - cd apps/api/Codec.Api
   - dotnet run

### Web
1. Create apps/web/.env with values based on apps/web/.env.example
2. Run the web app:
   - cd apps/web
   - npm install
   - npm run dev

## Google Sign-In setup (dev)
1. Create an OAuth 2.0 Client ID in Google Cloud Console.
2. Add authorized JavaScript origins (for dev):
   - http://localhost:5173
3. Copy the Client ID into:
   - apps/web/.env (PUBLIC_GOOGLE_CLIENT_ID)
   - apps/api/appsettings.Development.json (Google:ClientId)

## Documentation
Start with:
- docs/DEV_SETUP.md
- docs/AUTH.md
- docs/ARCHITECTURE.md
- docs/FEATURES.md
