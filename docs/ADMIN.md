# Global Admin Panel

The admin panel is a standalone SvelteKit application for platform-wide management, built for a small ops team (2-5 people). It runs as a separate deployment from the main chat app and communicates with the same backend API via dedicated `/admin/*` endpoints.

## Quick Start

```bash
# Start the API (requires Postgres)
cd apps/api/Codec.Api && dotnet run

# Start the admin app
cd apps/admin
cp .env.example .env    # Edit with your API URL and Google Client ID
npm install
npm run dev             # http://localhost:5175
```

Sign in with an email/password or Google account that has `IsGlobalAdmin = true`. The global admin is configured via `GlobalAdmin:Email` in the API's `appsettings.json` (see [Configuration](#configuration)).

## Architecture

```
[Admin SvelteKit App]  ──HTTP──▶  [Codec.Api /admin/* routes]  ──▶  [Postgres]
  apps/admin (port 5175)            apps/api (port 5050)
  separate origin                   [GlobalAdmin] policy on all endpoints
```

- **Frontend:** SvelteKit 5 with Svelte 5 runes, Chart.js, `@microsoft/signalr`
- **Backend:** Admin endpoints live in the existing `Codec.Api` project under `Controllers/Admin/`
- **Auth:** Same JWT tokens as the main app; admin app has its own login page; `GET /me` verifies `isGlobalAdmin` after login
- **Real-time:** `/hubs/admin` SignalR hub for live dashboard stats (separate from `/hubs/chat`)

## Feature Areas

### Dashboard (`/`)
Platform health at a glance.

| Metric | Source | Update |
|--------|--------|--------|
| Total users, new (24h/7d/30d) | `GET /admin/stats` | On page load |
| Total servers, new (24h/7d/30d) | `GET /admin/stats` | On page load |
| Messages (24h/7d/30d) | `GET /admin/stats` | On page load |
| Open reports | `GET /admin/stats` | On page load + live |
| Active connections | SignalR `StatsUpdated` | Every 5 seconds |
| Messages/min | SignalR `StatsUpdated` | Every 5 seconds |

### Users (`/users`, `/users/[id]`)
Search, inspect, and act on user accounts.

**List:** Paginated table with search by display name or email. Columns: name, email, auth providers, created date, status, admin badge.

**Detail:** Profile info, linked auth providers, account status, server memberships, recent messages (last 50), admin action history, report history.

**Actions:**
| Action | Endpoint | Notes |
|--------|----------|-------|
| Disable account | `POST /admin/users/{id}/disable` | Requires reason; revokes refresh tokens |
| Enable account | `POST /admin/users/{id}/enable` | Clears disabled state |
| Force logout | `POST /admin/users/{id}/force-logout` | Revokes refresh tokens without disabling |
| Reset password | `POST /admin/users/{id}/reset-password` | Clears password hash |
| Promote/demote admin | `PUT /admin/users/{id}/global-admin` | Cannot demote self or last admin |

### Servers (`/servers`, `/servers/[id]`)
Oversight of all community servers.

**List:** Paginated with search by name. Columns: name, member count, created date, quarantine status.

**Detail:** Server info, owner (resolved via position-0 system role), members, channels, roles.

**Actions:**
| Action | Endpoint | Notes |
|--------|----------|-------|
| Quarantine | `POST /admin/servers/{id}/quarantine` | Requires reason; hides from discovery |
| Unquarantine | `POST /admin/servers/{id}/unquarantine` | Restores visibility |
| Transfer ownership | `PUT /admin/servers/{id}/transfer-ownership` | Target must be existing member |
| Delete server | `DELETE /admin/servers/{id}` | Requires reason + name confirmation |

### Moderation (`/moderation`, `/moderation/[id]`, `/moderation/search`)
Abuse reports and content search.

**Report queue:** Filterable by status (Open/Reviewing/Resolved/Dismissed) and type (User/Message/Server). Related reports badge links to filtered view.

**Report detail:** Reporter info, target context (with `TargetSnapshot` fallback if target was deleted), actions: assign, mark reviewing, resolve (with note), dismiss (with reason).

**Message search:** `ILIKE` search across all server messages, capped at 100 results. Minimum 2 characters.

### System (`/system`)
Admin audit log, announcements, and connection monitoring.

- **Admin action log** — every admin write action is recorded automatically. Paginated, filterable by action type.
- **Announcements** — create/edit/delete platform-wide announcements (title, markdown body, optional expiry). Active announcements are available via `GET /announcements/active` (public, no auth).
- **Connections** — live count of active SignalR connections.

## Authorization

All `/admin/*` endpoints are protected by the `GlobalAdmin` authorization policy.

```csharp
// Registered in Program.cs
options.AddPolicy("GlobalAdmin", policy =>
    policy.Requirements.Add(new GlobalAdminRequirement()));
```

The `GlobalAdminHandler` resolves the user from the database and checks `user.IsGlobalAdmin`. Users with `IsDisabled = true` are blocked from all auth flows.

**Safety constraints:**
- Cannot demote yourself from global admin
- Cannot demote the last remaining global admin
- All destructive actions require a non-empty `reason` field
- Every admin write creates an `AdminAction` audit log entry

**Rate limiting:**
- `admin-writes` — 30 requests/minute for mutating admin endpoints
- `reports` — 5 reports/hour per user for report submissions

## Data Models

### Report
User-submitted abuse report. Filed via `POST /reports` (rate limited, any authenticated user).

| Field | Type | Notes |
|-------|------|-------|
| ReportType | enum | User, Message, Server |
| TargetId | string | Polymorphic ID (validated at application level) |
| TargetSnapshot | JSON | Captures target metadata at report time |
| Status | enum | Open, Reviewing, Resolved, Dismissed |
| Resolution | string | Admin's resolution note |

### AdminAction
Immutable audit trail for all admin operations.

| Field | Type | Notes |
|-------|------|-------|
| ActionType | enum | 16 action types (UserDisabled, ServerDeleted, etc.) |
| TargetType | string | "User", "Server", "Report", "Announcement" |
| Reason | string | Required for destructive actions |
| Details | JSON | Additional context (e.g., new owner ID) |

### SystemAnnouncement
Platform-wide announcements displayed in the main app.

| Field | Type | Notes |
|-------|------|-------|
| Title | string | Max 200 chars |
| Body | string | Max 5000 chars, markdown |
| IsActive | bool | Can be deactivated without deleting |
| ExpiresAt | DateTimeOffset? | Optional auto-expiry |

### User additions
- `IsDisabled` (bool) — blocks login/refresh; set by admin
- `DisabledReason` (string) — admin-provided reason
- `DisabledAt` (DateTimeOffset) — when disabled

### Server additions
- `CreatedAt` (DateTimeOffset) — server creation timestamp
- `IsQuarantined` (bool) — hides from discovery/invites
- `QuarantinedReason` (string) — admin-provided reason
- `QuarantinedAt` (DateTimeOffset) — when quarantined

## API Endpoints

### Pagination Contract
All paginated endpoints use offset-based pagination:

**Query params:** `page` (1-indexed), `pageSize` (max 100), `search`, `sortBy`, `sortDir`

**Response:**
```json
{ "items": [...], "totalCount": 1234, "page": 1, "pageSize": 25, "totalPages": 50 }
```

### Admin Endpoints (require `GlobalAdmin` policy)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/admin/stats` | Dashboard metrics |
| GET | `/admin/users` | Paginated user list |
| GET | `/admin/users/{id}` | User detail |
| POST | `/admin/users/{id}/disable` | Disable user |
| POST | `/admin/users/{id}/enable` | Enable user |
| POST | `/admin/users/{id}/force-logout` | Revoke tokens |
| POST | `/admin/users/{id}/reset-password` | Clear password |
| PUT | `/admin/users/{id}/global-admin` | Set admin status |
| GET | `/admin/servers` | Paginated server list |
| GET | `/admin/servers/{id}` | Server detail |
| POST | `/admin/servers/{id}/quarantine` | Quarantine server |
| POST | `/admin/servers/{id}/unquarantine` | Unquarantine |
| DELETE | `/admin/servers/{id}` | Delete server |
| PUT | `/admin/servers/{id}/transfer-ownership` | Transfer owner |
| GET | `/admin/reports` | Report queue |
| GET | `/admin/reports/{id}` | Report detail |
| PUT | `/admin/reports/{id}` | Update report |
| GET | `/admin/messages/search` | Message search |
| GET | `/admin/actions` | Admin action log |
| GET | `/admin/connections` | Connection count |
| GET | `/admin/announcements` | List announcements |
| POST | `/admin/announcements` | Create announcement |
| PUT | `/admin/announcements/{id}` | Update announcement |
| DELETE | `/admin/announcements/{id}` | Delete announcement |

### User-Facing Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/reports` | Submit report (5/hour rate limit) |
| GET | `/announcements/active` | Active announcements (no auth) |

## SignalR: Admin Hub

Hub at `/hubs/admin`, protected by `GlobalAdmin` policy. Token passed via `?access_token=` query string.

**Events (server to client):**
- `StatsUpdated` (every 5s) — `{ activeUsers, activeConnections, messagesPerMinute, openReports }`

**Implementation:**
- `AdminHub.cs` — minimal hub, no groups (all admins see everything)
- `AdminMetricsService.cs` — `BackgroundService` that aggregates stats and broadcasts via `IHubContext<AdminHub>`
- `MetricsCounterService.cs` — singleton in-memory counter incremented by `ChatHub` on message send; reset each broadcast cycle

## Frontend Structure

```
apps/admin/src/
  lib/
    api/client.ts              # Typed HTTP client for all /admin/* endpoints
    auth/auth.ts               # Token storage, admin verification
    state/admin-state.svelte.ts # Reactive state (Svelte 5 $state/$derived)
    services/admin-hub.ts      # SignalR connection to /hubs/admin
    types/models.ts            # TypeScript interfaces
    styles/global.css          # CSS variables and base styles
    components/
      layout/Sidebar.svelte    # Fixed sidebar with nav + report badge
      dashboard/StatCard.svelte      # Metric display card
      dashboard/ActivityChart.svelte # Real-time line chart (Chart.js)
      shared/
        DataTable.svelte       # Reusable sortable table
        Pagination.svelte      # Page controls
        ConfirmDialog.svelte   # Modal with optional text confirmation
  routes/
    +layout.svelte             # Auth guard + sidebar
    +page.svelte               # Dashboard
    login/+page.svelte         # Login form
    users/+page.svelte         # User list
    users/[id]/+page.svelte    # User detail
    servers/+page.svelte       # Server list
    servers/[id]/+page.svelte  # Server detail
    moderation/+page.svelte    # Report queue
    moderation/[id]/+page.svelte # Report detail
    moderation/search/+page.svelte # Message search
    system/+page.svelte        # Admin log + announcements + connections
```

## Configuration

### API (`appsettings.json`)
```json
{
  "GlobalAdmin": {
    "Email": "admin@example.com"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5174", "http://localhost:5175"]
  }
}
```

The `GlobalAdmin:Email` user is promoted to global admin on API startup via `SeedData.EnsureGlobalAdminAsync`. Additional admins can be promoted via the admin panel.

### Admin App (`apps/admin/.env`)
```
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=<your google oauth client id>
```

## Development

```bash
# Run admin app in dev mode
cd apps/admin && npm run dev

# Type-check
cd apps/admin && npm run check

# Build for production
cd apps/admin && npm run build
```

The admin app runs on port 5175 by default (configurable via `PORT` env var).

## Main App Integration

These features connect the admin panel's backend to the user-facing chat app:

- **Report submission UI** — `ReportModal` component triggered via message action bar (report button), member right-click context menu, and server context menu. Submits to `POST /reports` with rate-limit feedback (429 → "5 reports per hour").
- **Announcement banner** — `AnnouncementBanner` component fetches `GET /announcements/active` on sign-in. Displays the most recent active announcement above the app shell. Dismiss state persisted per-announcement in `localStorage` (`dismissed-announcements` key).
- **Quarantine enforcement** — `JoinViaInvite` (`POST /invites/{code}`) checks `server.IsQuarantined` and returns 403 ("This server is currently unavailable.") before the ban check.
- **Live activity chart** — `ActivityChart` component on the admin dashboard renders a Chart.js dual-axis line chart (messages/min + active connections) from the SignalR `StatsUpdated` feed with a 60-point rolling window (~5 minutes).

## Not Yet Implemented

These items are designed in the spec but deferred:

- Aspire AppHost integration for the admin app
- CI/CD pipeline for the admin app
- Global ban / message purge bulk actions
- Read replica for heavy admin queries
