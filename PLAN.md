# Codec Plan

## Purpose
Create a Discord-like app called Codec with a SvelteKit web front-end and an ASP.NET Core Web API backend. Authentication uses Google Sign-In (ID tokens validated by the API).

## Milestones
1. Baseline scaffolding
   - Monorepo layout: apps/web, apps/api, docs, .github
   - Initial README and docs
   - Copilot agent guidance files
2. Backend skeleton
   - .NET 9 Web API project
   - Health endpoint
   - Google ID token validation
3. Frontend skeleton
   - SvelteKit app
   - Google Sign-In button
   - Authenticated call to /me
4. Dev workflow + CI
   - Local run instructions
   - Basic build workflow for web and API
5. Initial app shell
   - Server list, channel list, and message panel UI
   - Placeholder data and layout only
6. Data layer decision
   - Select database and persistence strategy
   - Document schema direction and migration approach

## Decisions
- Web: SvelteKit
- API: .NET 9
- Auth: Frontend obtains Google ID token; API validates per request
- Layout: apps/web + apps/api + docs + .github
- Package manager: npm
- Data: SQLite + EF Core

## Current status
- Scaffolding complete (docs, .github guidance)
- .NET API skeleton with Google token validation
- SvelteKit web shell with Google Sign-In UI
- CI workflow added
- SQLite data layer decided and documented
- Initial EF Core migration created and applied
- Read-only API endpoints for servers/channels/messages
- Authenticated message posting endpoint
- Web UI wired to API data
- User identity mapping stored from Google subject
- UI loading/error states added
- Server membership and roles with join flow
- Server member listing in API and UI

## Next steps
- Verify local runs for API and web
- Introduce role-based authorization rules
- Add richer validation and error surfaces in UI
