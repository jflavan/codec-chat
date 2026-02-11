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
- Controller-based API architecture (refactored from Minimal APIs)
- Shared UserService for user resolution and membership checks
- Read-only API endpoints for servers/channels/messages
- Authenticated message posting endpoint
- Web UI wired to API data
- User identity mapping stored from Google subject
- UI loading/error states added
- Server membership and roles with join flow
- Server member listing in API and UI
- Server creation endpoint and UI
- Channel creation endpoint and UI (Owner/Admin only)
- Discord-inspired dark theme with three-column layout
- Design spec documented in `docs/DESIGN.md`

## Task breakdown: Server & Channel Creation

### API – Server creation (`POST /servers`)
- [x] Add `CreateServerRequest` record (Name, required, max 100 chars)
- [x] Add endpoint: validate input → create Server → create default "general" channel → add user as Owner → return new server
- [x] Return 201 Created with server payload

### API – Channel creation (`POST /servers/{serverId}/channels`)
- [x] Add `CreateChannelRequest` record (Name, required, max 100 chars)
- [x] Add endpoint: validate input → verify membership → enforce Owner/Admin role → create Channel → return 201
- [x] Return 403 for non-admin members, 404 for missing server

### Web – Server creation UI
- [x] Add "Create Server" button in server sidebar
- [x] Inline form with name input and submit
- [x] On success, reload server list and select the new server

### Web – Channel creation UI
- [x] Add "Add Channel" button in channels sidebar (visible to Owner/Admin)
- [x] Inline form with name input and submit
- [x] On success, reload channel list and select the new channel

### Documentation
- [x] Update ARCHITECTURE.md with new endpoints
- [x] Update FEATURES.md to mark server/channel creation as implemented
- [x] Update README.md features list

## Task breakdown: Front-End Design Refinement

### Design specification
- [x] Create `docs/DESIGN.md` with Discord-inspired design spec (layout, colors, typography, components, responsive, accessibility)

### UI implementation
- [x] Three-column layout: server icon rail (72px) + channel sidebar (240px) + flexible chat area
- [x] Fourth column: members sidebar (240px), hidden on smaller screens
- [x] Dark color scheme with CSS custom properties (blurple accent #5865f2, dark backgrounds)
- [x] Discord-style server icons (circular, hover morph, active pill indicator)
- [x] Channel list with `#` hash icon prefix and active/hover states
- [x] Message feed with avatar, author, timestamp, grouped consecutive messages
- [x] Composer with inline send button and focus glow
- [x] User panel pinned to bottom of channel sidebar
- [x] Members sidebar grouped by role (Owner, Admin, Member)
- [x] Responsive breakpoints (≤899px single-column, 900-1199px three-column, ≥1200px four-column)
- [x] Accessibility: focus-visible outlines, prefers-reduced-motion, semantic HTML, ARIA labels

### Documentation
- [x] Update PLAN.md with design refinement milestone
- [x] Update FEATURES.md to reflect design implementation

## Next steps
- Introduce role-based authorization rules for additional operations
- Add richer validation and error surfaces in UI
- Server settings and configuration
- Channel editing/deletion
- Light mode theme toggle
- Mobile slide-out navigation for server/channel sidebars
