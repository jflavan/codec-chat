# Architecture

## Overview
Codec is a modern Discord-like chat application built as a monorepo. The architecture follows a clean separation between the client and server. All authentication methods (Google Sign-In, email/password, GitHub OAuth, Discord OAuth, SAML 2.0 SSO) produce backend-issued JWTs with rotating refresh tokens.

### Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 10 Web API (Controller-based APIs)
- **Real-time:** SignalR (WebSockets with automatic fallback); Redis backplane for multi-instance scale-out
- **Caching:** Redis 8 distributed cache (`IDistributedCache`) for message history with channel-level invalidation and link preview metadata with 1-hour TTL
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** Google Sign-In, email/password, GitHub OAuth, Discord OAuth, SAML 2.0 SSO (all produce JWTs)
- **Observability:** OpenTelemetry (traces, metrics, logs) via `Codec.ServiceDefaults`; Azure Monitor / Application Insights in production; OTLP export for local Aspire dashboard
- **Local Dev:** .NET Aspire AppHost for single-command orchestration (Postgres, Redis, Azurite, API, Web)
- **Deployment:** Containerized on Azure Container Apps (Docker multi-stage builds)

## System Components

### Web Client (SvelteKit)
- **Location:** `apps/web/`
- **Framework:** SvelteKit 2.x with Svelte 5 runes
- **Language:** TypeScript (ES2022 target)
- **Build Tool:** Vite 7
- **PWA:** `@vite-pwa/sveltekit` with Workbox `generateSW` strategy
- **Key Features:**
  - Server-side rendering (SSR) capable
  - Client-side Google Sign-In integration
  - Reactive state management with Svelte 5 runes (`$state`, `$derived`)
  - Type-safe API client
  - Modular component architecture with context-based dependency injection
  - Offline fallback page with branded "You're offline" experience
  - Runtime font caching (Google Fonts, CacheFirst strategy)
  - Share target, app shortcuts, and custom protocol handler (`web+codec://`)

#### Frontend Architecture

The web client follows a layered, modular architecture. Each layer has a single responsibility and communicates through well-defined interfaces.

```
src/
├── lib/
│   ├── types/              # Shared TypeScript type definitions
│   │   ├── models.ts       # Domain models (Server, Channel, Message, Member, etc.)
│   │   └── index.ts        # Barrel re-export
│   ├── api/
│   │   └── client.ts       # Typed HTTP client (ApiClient class with ApiError)
│   ├── auth/
│   │   ├── session.ts      # Token persistence & session management (localStorage)
│   │   ├── google.ts       # Google Identity Services SDK initialization
│   │   └── oauth.ts        # GitHub/Discord OAuth redirect helpers
│   ├── services/
│   │   ├── chat-hub.ts     # SignalR hub connection lifecycle (ChatHubService)
│   │   ├── giphy.ts        # GIPHY REST API client — getTrendingGifs() and searchGifs(); maps API response to GiphyGif shape
│   │   └── push-notifications.ts  # Web Push subscription management
│   ├── state/
│   │   ├── ui-store.svelte.ts       # UIStore: modals, theme, errors, navigation flags, presence
│   │   ├── auth-store.svelte.ts     # AuthStore: auth flows, tokens, user profile
│   │   ├── server-store.svelte.ts   # ServerStore: server list, settings, moderation, roles
│   │   ├── channel-store.svelte.ts  # ChannelStore: channels, selection, mentions
│   │   ├── message-store.svelte.ts  # MessageStore: messages, reactions, pinning, search
│   │   ├── dm-store.svelte.ts       # DmStore: DM conversations, messages
│   │   ├── friend-store.svelte.ts   # FriendStore: friends, requests
│   │   ├── voice-store.svelte.ts    # VoiceStore: voice/video, WebRTC, calls
│   │   ├── navigation.svelte.ts     # Cross-store navigation orchestration (goHome, selectServer)
│   │   ├── signalr.svelte.ts        # Cross-store SignalR event wiring
│   │   └── index.ts                 # Barrel re-exports for all stores
│   ├── styles/
│   │   ├── tokens.css      # CSS custom properties (CODEC CRT design tokens)
│   │   └── global.css      # Base styles, resets, font imports
│   ├── data/
│   │   └── emojis.ts       # Static emoji dataset (categories, names, keywords)
│   ├── utils/
│   │   ├── format.ts       # Date/time formatting helpers
│   │   ├── emoji-frequency.ts  # localStorage-backed emoji usage frequency tracker
│   │   └── theme.ts        # Theme registry, persistence (localStorage), and DOM application
│   ├── components/
│   │   ├── server-sidebar/
│   │   │   └── ServerSidebar.svelte      # Server icon rail (create/join-via-invite)
│   │   ├── channel-sidebar/
│   │   │   ├── ChannelSidebar.svelte     # Channel list & create form
│   │   │   ├── InvitePanel.svelte        # Invite code management (create/list/revoke)
│   │   │   └── UserPanel.svelte          # User avatar/name/role & sign-out
│   │   ├── chat/
│   │   │   ├── ChatArea.svelte           # Chat shell (header, feed, composer)
│   │   │   ├── Composer.svelte           # Message input with send button; hosts tabbed Emoji / GIFs picker
│   │   │   ├── EmojiPicker.svelte        # Full emoji picker (search, categories, custom emojis, frequent)
│   │   │   ├── GifPicker.svelte          # GIPHY GIF picker (trending & search, 2-column grid, GIPHY attribution)
│   │   │   ├── ImagePreview.svelte       # Full-screen image lightbox overlay
│   │   │   ├── LinkPreviewCard.svelte    # Open Graph link preview embed card
│   │   │   ├── LinkifiedText.svelte      # Auto-linked URLs in message body (+ custom emoji rendering)
│   │   │   ├── MessageFeed.svelte        # Scrollable message list with grouping
│   │   │   ├── MessageItem.svelte        # Single message (grouped/ungrouped)
│   │   │   ├── ReactionBar.svelte        # Reaction pills (emoji + count)
│   │   │   ├── FileCard.svelte           # File attachment card (icon, name, size, download)
│   │   │   ├── ReplyComposerBar.svelte   # "Replying to" bar above composer
│   │   │   ├── ReplyReference.svelte     # Inline reply context above message
│   │   │   └── TypingIndicator.svelte    # Animated typing dots
│   │   ├── friends/
│   │   │   ├── FriendsPanel.svelte       # Friends view with tab navigation
│   │   │   ├── FriendsList.svelte        # Confirmed friends list (click to DM)
│   │   │   ├── PendingRequests.svelte    # Incoming/outgoing friend requests
│   │   │   └── AddFriend.svelte          # User search & send request
│   │   ├── dm/
│   │   │   ├── HomeSidebar.svelte        # Home sidebar (Friends nav + DM list)
│   │   │   ├── DmList.svelte             # DM conversation entries
│   │   │   └── DmChatArea.svelte         # DM chat (header, feed, composer, call button)
│   │   ├── voice/
│   │   │   ├── VoiceControls.svelte      # Mute/deafen controls in voice channel
│   │   │   ├── UserActionSheet.svelte    # Per-user action sheet (desktop popup / mobile bottom sheet)
│   │   │   ├── IncomingCallOverlay.svelte # Full-screen incoming call modal with ring tone
│   │   │   ├── DmCallHeader.svelte       # Active call header (timer, mute, end)
│   │   │   ├── VideoGrid.svelte          # Responsive grid layout for video tiles
│   │   │   └── VideoTile.svelte          # Individual video tile (camera or screen share)
│   │   ├── settings/
│   │   │   ├── UserSettingsModal.svelte   # Full-screen modal overlay shell
│   │   │   ├── SettingsSidebar.svelte     # Category navigation sidebar
│   │   │   ├── BugReportModal.svelte     # In-app bug report submission dialog
│   │   │   ├── ProfileSettings.svelte     # Nickname + avatar + status management
│   │   │   ├── AccountSettings.svelte     # Read-only info + sign out
│   │   │   ├── AppearanceSettings.svelte  # Theme picker (4 presets with preview cards)
│   │   │   └── NotificationSettings.svelte # Push notification enable/disable
│   │   ├── server-settings/
│   │   │   ├── ServerSettingsModal.svelte  # Modal shell with sidebar + content area
│   │   │   ├── ServerSettingsSidebar.svelte # Category navigation (General, Channels, Roles, Invites, Emojis, Members, Bans, Webhooks, Audit Log)
│   │   │   ├── ServerSettings.svelte      # General tab — server name, description, icon, danger zone
│   │   │   ├── ServerChannels.svelte      # Channels tab — category and channel management with drag-and-drop reordering
│   │   │   ├── ServerRoles.svelte         # Roles tab — custom role CRUD with permission editor
│   │   │   ├── ServerInvites.svelte       # Invites tab — invite CRUD (create, list, revoke)
│   │   │   ├── ServerAuditLog.svelte      # Audit Log tab — paginated action history
│   │   │   ├── ServerEmojis.svelte        # Emojis tab — custom emoji upload, rename, delete (Owner/Admin)
│   │   │   ├── ServerMembers.svelte       # Members tab — member role management (promote/demote, Owner/Admin)
│   │   │   ├── ServerBans.svelte          # Bans tab — ban/unban members with reason
│   │   │   └── ServerWebhooks.svelte      # Webhooks tab — webhook CRUD with event type selection
│   │   ├── discord-import/
│   │   │   ├── DiscordImportWizard.svelte  # 4-step import wizard container
│   │   │   ├── WizardStepBotSetup.svelte   # Step 2: Discord bot creation instructions
│   │   │   ├── WizardStepConnect.svelte     # Step 3: Bot token + guild ID entry
│   │   │   ├── WizardStepDestination.svelte # Step 1: Choose target server
│   │   │   └── WizardStepProgress.svelte    # Step 4: Real-time import progress
│   │   ├── report/
│   │   │   └── ReportModal.svelte       # User/message/server report submission dialog
│   │   ├── announcements/
│   │   │   └── AnnouncementBanner.svelte # Dismissible site-wide announcement banner
│   │   ├── shared/
│   │   │   └── PresenceDot.svelte        # Online/idle/offline indicator dot
│   │   ├── ReloadPrompt.svelte            # PWA update toast (new version available)
│   │   └── members/
│   │       ├── MembersSidebar.svelte     # Members grouped by role (online-first sorting)
│   │       └── MemberItem.svelte         # Single member card (with presence dot)
│   └── index.ts            # Public barrel exports
└── routes/
    ├── +layout.svelte      # Root layout (global CSS, font preconnect)
    ├── +page.svelte        # Thin composition shell (~75 lines)
    └── auth/callback/
        ├── github/+page.svelte   # GitHub OAuth callback handler
        └── discord/+page.svelte  # Discord OAuth callback handler
```

**State Management Pattern:**

State is split into domain-specific stores under `lib/state/` (e.g. `AuthStore`, `ServerStore`, `ChannelStore`). Each store is created once in `+page.svelte` via its `create*Store()` factory and injected into the component tree with `setContext()`. Child components retrieve only the stores they need via `get*Store()` helpers (e.g. `getAuthStore()`, `getServerStore()`). Cross-store orchestration lives in `navigation.svelte.ts` and `signalr.svelte.ts`.

| Store | Responsibility |
|-------|---------------|
| `UIStore` | Modals, theme, transient errors, navigation flags, presence, hub connection status |
| `AuthStore` | Auth flows (Google, email/password, OAuth), tokens, user profile, nickname |
| `ServerStore` | Server list, settings, moderation, roles, permissions, bans |
| `ChannelStore` | Channels, categories, selection, mention badges |
| `MessageStore` | Messages, reactions, pinning, search, replies, link previews |
| `DmStore` | DM conversations, DM messages, DM typing |
| `FriendStore` | Friends list, friend requests |
| `AnnouncementStore` | Announcements: active list, dismissed state (localStorage-persisted) |
| `VoiceStore` | Voice channels, DM calls, WebRTC, mute/deafen, video, screen sharing |

```
+page.svelte
  ├─ createUIStore()        → setContext()
  ├─ createAuthStore()      → setContext()
  ├─ createServerStore()    → setContext()
  ├─ createChannelStore()   → setContext()
  ├─ createMessageStore()   → setContext()
  ├─ createDmStore()        → setContext()
  ├─ createFriendStore()    → setContext()
  ├─ createVoiceStore()     → setContext()
  └─ wires cross-store callbacks, navigation, and SignalR orchestration
      ├── ServerSidebar      → getServerStore(), getUIStore()
      ├── ChannelSidebar     → getChannelStore(), getServerStore()
      │   └── UserPanel      → getAuthStore(), getUIStore()
      ├── HomeSidebar        → getDmStore(), getFriendStore()
      ├── FriendsPanel       → getFriendStore()
      ├── DmChatArea         → getDmStore()
      ├── ChatArea           → getMessageStore(), getChannelStore()
      │   ├── MessageFeed    → getMessageStore()
      │   ├── Composer       → getMessageStore()
      │   └── TypingIndicator → getMessageStore()
      ├── MembersSidebar     → getServerStore()
      ├── UserSettingsModal  → getAuthStore(), getUIStore()
      └── ImagePreview       → getUIStore()  (shown when lightboxImageUrl is set)
```

**Layer Responsibilities:**

| Layer | Purpose |
|-------|---------|
| `types/` | Shared TypeScript interfaces for domain models |
| `api/` | HTTP communication with the REST API; typed methods, `encodeURIComponent` on path params |
| `auth/` | Token lifecycle (persist, load, expire, clear) and Google SDK setup |
| `services/` | External service integrations (SignalR hub connection management, GIPHY REST API client) |
| `state/` | Domain-specific reactive stores; each store owns its slice of state, API calls, and SignalR handlers |
| `data/` | Static datasets (emoji categories, keywords) |
| `styles/` | Design tokens as CSS custom properties (`[data-theme]` palettes for theming); global base styles |
| `utils/` | Pure utility functions (formatting, frequency tracking, etc.) |
| `components/` | Presentational Svelte 5 components grouped by feature area |

### Admin Panel (SvelteKit)
- **Location:** `apps/admin/`
- **Framework:** SvelteKit 2.x with Svelte 5 runes
- **Purpose:** Standalone global admin interface for platform management
- **Key Features:**
  - Dashboard with live stats (users, servers, messages, reports) and live activity chart (messages/min, connections) via SignalR `AdminHub`
  - User management (search, disable/enable, force logout, reset password, promote/demote admin)
  - Server management (search, quarantine/unquarantine, delete, transfer ownership)
  - Moderation (report queue with status/type filters, full-text message search)
  - System tools (admin action audit log, system announcement CRUD, live connection count)
  - JWT-based auth with `GlobalAdmin` policy enforcement on all API calls

### API Server (ASP.NET Core)
- **Location:** `apps/api/Codec.Api/`
- **Framework:** ASP.NET Core 10 with Controller-based APIs
- **Language:** C# 14 (.NET 10)
- **Database:** PostgreSQL via Entity Framework Core (Npgsql)
- **Testing:** `Codec.Api.Tests/` (xUnit unit tests) + `Codec.Api.IntegrationTests/` (Testcontainers integration tests) — see [TESTING.md](TESTING.md)
- **Key Features:**
  - Stateless JWT validation
  - RESTful controller-based API design (`[ApiController]`)
  - SignalR hub for real-time messaging and typing indicators
  - Shared `UserService` for cross-cutting user resolution
  - `DiscordImportService`, `DiscordApiClient`, `DiscordPermissionMapper`, `DiscordRateLimitHandler`, `DiscordImportWorker`, `DiscordImportCancellationRegistry`, `DiscordMediaRehostService` for Discord server import (newest-first message pagination with `PendingReply` backfill, 4 parallel channels, global `TokenBucketRateLimiter` at 50 req/sec, live `ImportMessagesAvailable` channel events)
  - Automatic migrations (development)
  - CORS support for local development

### Authorization & Error Handling
- Controllers use `UserService` helper methods (`EnsureMemberAsync`, `EnsureAdminAsync`, `EnsureOwnerAsync`, `EnsureDmParticipantAsync`) for membership and role checks
- Authorization failures throw `ForbiddenException` (→ 403) or `NotFoundException` (→ 404)
- A global exception handler converts exceptions to RFC 7807 ProblemDetails JSON responses
- Request DTOs use `System.ComponentModel.DataAnnotations` (`[Required]`, `[StringLength]`, etc.) for structural validation; business logic validation remains inline in controllers

### Permission Resolution
- **Multi-role data model** — `ServerMemberRoles` join table links members to zero or more roles; replaces the legacy single `RoleId` column on `ServerMember`
- **`PermissionResolverService`** — central service that computes the effective permission set for a member in a given context:
  1. Collects all roles assigned to the member via `ServerMemberRoles`
  2. OR-merges the `Permissions` bitmask across all roles
  3. If the result includes `Administrator`, bypasses all further checks (server Owners are also always granted full access)
  4. For channel-scoped checks, loads `ChannelPermissionOverrides` for each role and applies them using a deny-wins model: deny bits are unioned across roles and subtracted from the allow set, then merged with the base permissions
- **`ChannelPermissionOverrides` table** — stores `(ChannelId, RoleId, Allow, Deny)` rows; `Deny` always wins over `Allow` when the same bit appears in both; absence of an override row means the channel inherits the role's base permissions unchanged

### Real-time Layer (SignalR)
- **Hub endpoint:** `/hubs/chat`
- **Transport:** WebSockets (with automatic fallback to Server-Sent Events / Long Polling)
- **Backplane:** Redis pub/sub (when configured) — enables SignalR events to broadcast across multiple API instances; channel prefix `codec` prevents key collisions
- **Authentication:** JWT passed via `access_token` query parameter for WebSocket connections
- **JSON serialization:** camelCase payload naming (configured via `AddJsonProtocol`) to match REST API conventions
- **Key Features:**
  - Channel-scoped groups — clients join/leave groups per channel
  - User-scoped groups — clients auto-join `user-{userId}` on connect for friend events
  - Server-scoped groups — clients auto-join `server-{serverId}` on connect for all joined servers (global admin joins all server groups); receives membership events (MemberJoined, MemberLeft)
  - DM channel groups — clients join `dm-{dmChannelId}` for DM-specific events
  - Real-time message broadcast on `POST /channels/{channelId}/messages`
  - DM message broadcast on `POST /dm/channels/{channelId}/messages`
  - Typing indicators (`UserTyping` / `UserStoppedTyping` events)
  - DM typing indicators (`DmTyping` / `DmStoppedTyping` events)
  - Friend-related event delivery (request received/accepted/declined/cancelled, friend removed)
  - `AccountDeleted` — sent to `user-{userId}` group when account is deleted; forces all client sessions to sign out
  - Automatic reconnect via `withAutomaticReconnect()` with explicit retry schedule
  - Graceful restart with exponential backoff (2 s → 30 s cap) when all automatic reconnect attempts are exhausted — no page reload
  - Visibility-aware recovery — immediate reconnect attempt when a backgrounded tab becomes active
  - Inactive tab tolerance — server KeepAliveInterval (30 s) and ClientTimeoutInterval (90 s) prevent false disconnects from throttled pings

### Data Layer
- **ORM:** Entity Framework Core 10
- **Database:** PostgreSQL (local via Docker Compose, production via Azure Database for PostgreSQL)
- **Migrations:** Code-first with automatic application
- **Seeding:** Development data seeded on first run

## Authentication Flow

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│   Browser   │         │  Google IDP  │         │   Codec API │
└──────┬──────┘         └──────┬───────┘         └──────┬──────┘
       │                       │                        │
       │  1. Sign In Button    │                        │
       ├──────────────────────>│                        │
       │                       │                        │
       │  2. Google Auth UI    │                        │
       │<──────────────────────┤                        │
       │                       │                        │
       │  3. Consent & Login   │                        │
       ├──────────────────────>│                        │
       │                       │                        │
       │  4. Google ID Token   │                        │
       │<──────────────────────┤                        │
       │                       │                        │
       │  5. POST /auth/google { credential }           │
       ├───────────────────────────────────────────────>│
       │                       │                        │
       │                       │  6. Validate Google    │
       │                       │     ID Token (JWKS)    │
       │                       │<───────────────────────┤
       │                       │                        │
       │  7. { accessToken, refreshToken, user }        │
       │<───────────────────────────────────────────────┤
       │                       │                        │
       │  8. API calls + Bearer <accessToken>           │
       ├───────────────────────────────────────────────>│
       │                       │                        │
       │  9. SignalR connect (/hubs/chat?access_token)  │
       ├───────────────────────────────────────────────>│
       │                       │                        │
       │  10. WebSocket established                     │
       │<───────────────────────────────────────────────┤
       │                       │                        │
```

### Authentication Details
1. User clicks "Sign in with Google" in the web client
2. Google Identity Services displays authentication UI
3. User consents and authenticates with Google
4. Web client receives a Google ID token
5. Client sends the Google ID token to `POST /auth/google`
6. API validates the Google ID token against Google's JWKS endpoint
7. API issues a backend JWT access token (1-hour) and rotating refresh token (7-day)
8. Client uses the backend JWT for all subsequent API requests

**Key Points:**
- All auth methods (Google, email/password, GitHub, Discord, SAML) produce identical backend-issued JWTs (`iss` = `codec-api`)
- Tokens are short-lived (1 hour); refresh tokens provide 7-day session persistence
- User identity mapped to internal User records via Google subject (at token exchange time)
- Web client persists backend tokens in `localStorage` for session continuity
- Token refresh uses `POST /auth/refresh` for all auth types (no Google One Tap silent re-auth)
- Google One Tap `auto_select` only enabled for returning Google users (prevents FedCM console errors)
- SignalR WebSocket connections authenticate via `access_token` query parameter (standard pattern for WebSocket auth since `Authorization` headers aren't supported)

**reCAPTCHA v3 Bot Protection:**
- `POST /auth/register` and `POST /auth/login` are decorated with `[ValidateRecaptcha]` action filter
- Frontend loads Google reCAPTCHA Enterprise script and calls `grecaptcha.enterprise.execute()` to obtain a token on each auth request
- `RecaptchaService` sends the token to Google's reCAPTCHA Enterprise Assessment API and validates the score against a configurable threshold (default 0.5)
- Fail-closed: if the Google API is unreachable or returns an error, the request is rejected (403)
- Disabled by default in local development (`Recaptcha:Enabled = false` in `appsettings.json`); enabled in production when `Recaptcha:SiteKey` is configured (derived in Bicep)
- Google Sign-In flow uses `POST /auth/google` (separate from login/register, not reCAPTCHA-protected)

**Account Lockout:**
- After 5 consecutive failed login attempts, the account is locked for 15 minutes
- Failed attempt counter resets on successful login or when the lockout period expires
- Lockout applies to both the `/auth/login` and `/auth/link-google` endpoints

**Server-Side Logout:**
- `POST /auth/logout` accepts a refresh token and immediately revokes it
- Always returns 204 to avoid leaking token validity
- Frontend calls this before clearing local auth state

**Refresh Token Security:**
- Optimistic concurrency via PostgreSQL `xmin` column prevents concurrent token rotation
- Background cleanup service (`RefreshTokenCleanupService`) runs every 6 hours to purge expired tokens and tokens revoked more than 24 hours ago

**Email Verification:**
- Email/password registrations require email verification before app access
- On registration, a 24-hour verification token is generated and emailed to the user
- Tokens are SHA-256 hashed in the database (same security pattern as refresh tokens)
- `[RequireEmailVerified]` action filter gates all data-loading controllers, returning 403 for unverified users
- Google Sign-In users are auto-verified (bypassed by the filter via issuer check)
- `ConsoleEmailSender` logs verification links in development; `AzureEmailSender` sends via Azure Communication Services in production

## API Endpoints

### Public Endpoints
- `GET /` - API info (development only)
- `GET /health` - Health check

### Authenticated Endpoints

#### User Profile
- `GET /me` - Get current user profile (includes `nickname` and `effectiveDisplayName`)
- `PUT /me/nickname` - Set or update nickname (1–32 chars, trimmed; returns effective display name)
- `DELETE /me/nickname` - Remove nickname, revert to Google display name
- `POST /me/avatar` - Upload a custom global avatar (multipart/form-data, 10 MB max; JPG, JPEG, PNG, WebP, GIF)
- `DELETE /me/avatar` - Remove custom avatar, revert to Google profile picture
- `DELETE /me` - Permanently delete authenticated user's account (requires password or Google re-auth + typed "DELETE" confirmation; blocked if user owns servers)

#### Server Management
- `GET /servers` - List servers user is a member of (global admin sees all servers; `role` is `null` for non-member servers; includes `description`)
- `POST /servers` - Create a new server (authenticated user becomes Owner)
- `PATCH /servers/{serverId}` - Update server name and/or description (requires Owner, Admin, or Global Admin role; broadcasts `ServerNameChanged` and/or `ServerDescriptionChanged` via SignalR)
- `GET /servers/{serverId}/members` - List server members (requires membership or Global Admin)
- `GET /servers/{serverId}/channels` - List channels in a server (requires membership or Global Admin; includes `description`, `categoryId`, `position`)
- `POST /servers/{serverId}/channels` - Create a channel in a server (requires Owner, Admin, or Global Admin role)
- `PATCH /servers/{serverId}/channels/{channelId}` - Update channel name and/or description (requires Owner, Admin, or Global Admin role; broadcasts `ChannelNameChanged` and/or `ChannelDescriptionChanged` via SignalR)
- `POST /servers/{serverId}/avatar` - Upload a server-specific avatar (multipart/form-data, overrides global avatar in this server)
- `DELETE /servers/{serverId}/avatar` - Remove server-specific avatar, fall back to global avatar
- `PATCH /servers/{serverId}/members/{userId}/role` - Change a member's role (requires Owner, Admin, or Global Admin; Owner can promote/demote freely; Admin can promote Members but not demote other Admins; broadcasts `MemberRoleChanged` via SignalR)
- `DELETE /servers/{serverId}/members/{userId}` - Kick a member from the server (requires Owner, Admin, or Global Admin role; broadcasts `KickedFromServer` via SignalR)
- `DELETE /servers/{serverId}` - Delete a server and all associated data (requires Owner or Global Admin; cascade-deletes channels, messages, reactions, link previews, members, invites; broadcasts `ServerDeleted` via SignalR)
- `DELETE /servers/{serverId}/channels/{channelId}` - Delete a channel and all associated data (requires Owner, Admin, or Global Admin; cascade-deletes messages, reactions, link previews; broadcasts `ChannelDeleted` via SignalR)

#### Channel Categories
- `GET /servers/{serverId}/categories` - List all categories for a server ordered by position (requires membership or Global Admin)
- `POST /servers/{serverId}/categories` - Create a new category (requires Owner, Admin, or Global Admin; broadcasts `CategoryCreated` via SignalR)
- `PATCH /servers/{serverId}/categories/{categoryId}` - Rename a category (requires Owner, Admin, or Global Admin; broadcasts `CategoryRenamed` via SignalR)
- `DELETE /servers/{serverId}/categories/{categoryId}` - Delete a category; channels become uncategorized (requires Owner, Admin, or Global Admin; broadcasts `CategoryDeleted` via SignalR)
- `PUT /servers/{serverId}/channel-order` - Bulk update channel positions and category assignments (requires Owner, Admin, or Global Admin; broadcasts `ChannelOrderChanged` via SignalR)
- `PUT /servers/{serverId}/category-order` - Bulk update category positions (requires Owner, Admin, or Global Admin; broadcasts `CategoryOrderChanged` via SignalR)

#### Audit Log
- `GET /servers/{serverId}/audit-log?page={n}&pageSize={n}` - Paginated audit log (newest first; requires Owner, Admin, or Global Admin; returns `{ totalCount, entries }`)

#### Notification Preferences
- `GET /servers/{serverId}/notification-preferences` - Get current user's mute settings for the server and its channels
- `PUT /servers/{serverId}/mute` - Toggle server-level mute for the current user (`{ isMuted: bool }`)
- `PUT /servers/{serverId}/channels/{channelId}/mute` - Toggle channel-level mute for the current user (`{ isMuted: bool }`)

#### Custom Emojis
- `GET /servers/{serverId}/emojis` - List all custom emojis for a server (requires membership; returns name, imageUrl, contentType, isAnimated, uploadedByUserId, createdAt)
- `POST /servers/{serverId}/emojis` - Upload a new custom emoji (requires Owner/Admin; multipart/form-data with `name` and `file`; PNG, JPEG, WebP, GIF; 256 KB max; 50 emojis per server; broadcasts `CustomEmojiAdded` via SignalR)
- `PATCH /servers/{serverId}/emojis/{emojiId}` - Rename a custom emoji (requires Owner/Admin; broadcasts `CustomEmojiUpdated` via SignalR)
- `DELETE /servers/{serverId}/emojis/{emojiId}` - Delete a custom emoji and its stored file (requires Owner/Admin; broadcasts `CustomEmojiDeleted` via SignalR)

#### Server Invites
- `POST /servers/{serverId}/invites` - Create an invite code (requires Owner, Admin, or Global Admin role; generates 8-char alphanumeric code)
- `GET /servers/{serverId}/invites` - List active invites (requires Owner, Admin, or Global Admin role; filters expired invites)
- `DELETE /servers/{serverId}/invites/{inviteId}` - Revoke an invite code (requires Owner, Admin, or Global Admin role)
- `POST /invites/{code}` - Join a server via invite code (any authenticated user; validates expiry and max uses)

#### Messaging
- `GET /channels/{channelId}/messages?before={timestamp}&limit={n}` - Get messages in a channel with cursor-based pagination (requires membership or Global Admin; `before` DateTimeOffset cursor and `limit` 1–200 default 100; returns `{ hasMore, messages }` with `imageUrl`, `replyContext`)
- `POST /channels/{channelId}/messages` - Post a message to a channel (requires membership or Global Admin; accepts optional `imageUrl`, `replyToMessageId`; broadcasts via SignalR)
- `DELETE /channels/{channelId}/messages/{messageId}` - Delete a channel message (author or Global Admin; cascade-deletes reactions and link previews; broadcasts `MessageDeleted` via SignalR)
- `DELETE /channels/{channelId}/messages` - Purge all messages in a channel (Global Admin only; cascade-deletes reactions and link previews; broadcasts `ChannelPurged` via SignalR)
- `POST /channels/{channelId}/messages/{messageId}/reactions` - Toggle an emoji reaction on a message (requires membership or Global Admin; broadcasts via SignalR)
- `GET /channels/{channelId}/messages?around={messageId}` - Get messages around a target message (returns `{ hasMoreBefore, hasMoreAfter, messages }` centered on the target; used by jump-to-message)
- `GET /servers/{serverId}/search?q=...` - Search messages across server channels (requires membership or Global Admin; filters: `channelId`, `authorId`, `before`, `after`, `has`; paginated results with channel names, reactions, reply context)
- `GET /channels/{channelId}/pins` - List pinned messages in a channel (requires membership or Global Admin; ordered by most recently pinned; includes reactions and link previews)
- `POST /channels/{channelId}/pins/{messageId}` - Pin a message (requires Owner, Admin, or Global Admin; 50-pin limit per channel; creates PinNotification system message; broadcasts `MessagePinned` and `ReceiveMessage` via SignalR; audit logged)
- `DELETE /channels/{channelId}/pins/{messageId}` - Unpin a message (requires Owner, Admin, or Global Admin; broadcasts `MessageUnpinned` via SignalR; audit logged)

#### Friends
- `GET /friends` - List confirmed friends (returns the other user + friendship date)
- `DELETE /friends/{friendshipId}` - Remove a confirmed friend (broadcasts `FriendRemoved` via SignalR)
- `POST /friends/requests` - Send a friend request (broadcasts `FriendRequestReceived` via SignalR)
- `GET /friends/requests?direction=received|sent` - List pending friend requests
- `PUT /friends/requests/{requestId}` - Accept or decline a friend request (broadcasts `FriendRequestAccepted` or `FriendRequestDeclined` via SignalR)
- `DELETE /friends/requests/{requestId}` - Cancel a sent friend request (broadcasts `FriendRequestCancelled` via SignalR)

#### User Search
- `GET /users/search?q=...` - Search users by display name, nickname, or email (returns up to 20 results with relationship status and `effectiveDisplayName`)

#### Direct Messages
- `POST /dm/channels` - Start or resume a DM conversation with a friend (returns existing or creates new)
- `GET /dm/channels` - List open DM conversations (sorted by most recent message, `IsOpen = true` only)
- `GET /dm/channels/{channelId}/messages` - Get messages in a DM conversation (paginated via `before`/`limit`; returns `{ hasMore, messages }` with `imageUrl`, `replyContext`)
- `POST /dm/channels/{channelId}/messages` - Send a direct message (accepts optional `imageUrl`, `replyToDirectMessageId`; broadcasts `ReceiveDm` via SignalR; reopens closed conversations)
- `DELETE /dm/channels/{channelId}/messages/{messageId}` - Delete a direct message (author only; cascade-deletes link previews; broadcasts `DmMessageDeleted` via SignalR)
- `DELETE /dm/channels/{channelId}` - Close a DM conversation (sets `IsOpen = false` for current user; messages preserved)
- `GET /dm/channels/{channelId}/messages?around={messageId}` - Get DM messages around a target message (returns `{ hasMoreBefore, hasMoreAfter, messages }` centered on the target; used by jump-to-message)
- `GET /dm/search?q=...` - Search messages across DM conversations (filters: `channelId`, `authorId`, `before`, `after`, `has`; paginated results with DM channel display names)

#### Presence
- `GET /servers/{serverId}/presence` - Get online/idle presence for all members of a server (returns `[{ userId, status }]`, excludes offline; requires membership or Global Admin)
- `GET /dm/presence` - Get online/idle presence for the current user's DM contacts (returns `[{ userId, status }]`, excludes offline)

#### Voice Calls
- `GET /voice/active-call` - Get the current user's active or ringing call (returns caller/recipient display info, call status, and timestamps; used on page load/reconnect to restore call state)

#### Bug Reports
- `POST /issues` - Submit a bug report (requires auth; proxied to GitHub Issues API with `user-report` label; returns `{ issueUrl }`; 501 if GitHub token not configured; 502 on upstream failure)

#### Bans
- `POST /servers/{serverId}/bans/{targetUserId}` - Ban a member from the server (requires Owner, Admin, or custom role with BanMembers permission; optional `deleteMessageDays` to purge recent messages; broadcasts `BannedFromServer` and `MemberBanned` via SignalR; audit logged)
- `DELETE /servers/{serverId}/bans/{targetUserId}` - Unban a member (requires Owner, Admin, or custom role with BanMembers permission)
- `GET /servers/{serverId}/bans` - List banned members (requires Owner, Admin, or Global Admin; returns user info, reason, banned-by, and timestamp)

#### Custom Roles
- `GET /servers/{serverId}/roles` - List all roles for a server ordered by position (requires membership)
- `POST /servers/{serverId}/roles` - Create a custom role (requires Owner, Admin, or ManageRoles permission; returns new role with default permissions)
- `PATCH /servers/{serverId}/roles/{roleId}` - Update role name, color, permissions, hoisted/mentionable flags (requires ManageRoles permission; cannot edit roles above your own position)
- `DELETE /servers/{serverId}/roles/{roleId}` - Delete a custom role (requires ManageRoles permission; system roles cannot be deleted)
- `PUT /servers/{serverId}/roles/order` - Bulk update role positions (requires ManageRoles permission)
- `PUT /servers/{serverId}/members/{userId}/roles` - Replace all roles for a member (requires ManageRoles permission)
- `POST /servers/{serverId}/members/{userId}/roles/{roleId}` - Add a single role to a member (requires ManageRoles permission)
- `DELETE /servers/{serverId}/members/{userId}/roles/{roleId}` - Remove a single role from a member (requires ManageRoles permission)
- `GET /channels/{channelId}/overrides/{roleId}` - Get per-channel permission overrides for a role (requires ManageChannels permission)
- `PUT /channels/{channelId}/overrides/{roleId}` - Set per-channel allow/deny bitmasks for a role (requires ManageChannels permission; broadcasts `ChannelPermissionsChanged` via SignalR)
- `DELETE /channels/{channelId}/overrides/{roleId}` - Remove per-channel overrides for a role (requires ManageChannels permission; broadcasts `ChannelPermissionsChanged` via SignalR)

#### Webhooks
- `GET /servers/{serverId}/webhooks` - List webhooks for a server (requires Owner, Admin, or ManageServer permission)
- `POST /servers/{serverId}/webhooks` - Create a webhook (requires ManageServer permission; body includes name, url, optional secret, and event types)
- `PATCH /servers/{serverId}/webhooks/{webhookId}` - Update a webhook configuration
- `DELETE /servers/{serverId}/webhooks/{webhookId}` - Delete a webhook and its delivery logs

#### Discord Import
- `POST /servers/{serverId}/discord-import` - Start a Discord server import (requires ManageServer permission; validates bot token and guild ID; returns import ID)
- `GET /servers/{serverId}/discord-import` - Get latest import status for a server (requires ManageServer permission; returns status, counts, timestamps)
- `POST /servers/{serverId}/discord-import/resync` - Re-sync messages since last completed import (requires ManageServer permission)
- `DELETE /servers/{serverId}/discord-import` - Cancel an in-progress import (requires ManageServer permission)
- `GET /servers/{serverId}/discord-import/mappings` - List Discord-to-Codec user mappings (requires ManageServer permission)
- `POST /servers/{serverId}/discord-import/claim` - Claim a Discord identity and link imported messages to Codec account (requires server membership)

After the text import completes, a media re-hosting phase downloads imported emoji and message image attachments from Discord's CDN and re-uploads them to Codec's own storage (Local or AzureBlob). This ensures media remains accessible after Discord CDN URLs expire. Images over 10MB are resized (max 4096px) and compressed. Emojis over 512KB are compressed. Non-image files are skipped.

#### Push Notifications
- `GET /push-subscriptions/vapid-key` - Get VAPID public key for Web Push API (public, no auth)
- `POST /push-subscriptions` - Register or re-activate a push subscription (endpoint, p256dh, auth keys)
- `DELETE /push-subscriptions` - Unsubscribe from push notifications

#### SAML SSO
- `GET /auth/saml/providers` - List enabled SAML IdPs (public)
- `GET /auth/saml/login/{idpId}` - SP-initiated SSO redirect to IdP
- `POST /auth/saml/acs` - Assertion Consumer Service callback
- `GET /auth/saml/metadata` - SP metadata XML
- Admin CRUD for IdP configuration (global admin only)

#### OAuth
- `POST /auth/oauth/github` - GitHub OAuth callback (exchange authorization code)
- `POST /auth/oauth/discord` - Discord OAuth callback (exchange authorization code)
- `GET /auth/oauth/config` - Get enabled OAuth providers and client IDs (public)

#### Image Uploads
- `POST /uploads/images` - Upload an image file (multipart/form-data; JPEG, PNG, WebP, GIF; 10 MB max; returns `{ imageUrl }`)
- `POST /uploads/files` - Upload a file attachment (multipart/form-data; 25 MB max; returns `{ fileUrl, fileName, fileSize, fileMimeType }`)

#### Image Proxy
- `GET /images/proxy?url={encodedUrl}` - Proxy an external image URL through the API (SSRF protection, content-type validation, 10 MB max; returns proxied image bytes with original content type)

#### User Status
- `PUT /me/status` - Set status message and emoji (`{ statusText, statusEmoji }`; max 128 chars text, 8 chars emoji; broadcasts `UserStatusChanged` via SignalR)
- `DELETE /me/status` - Clear status message and emoji

#### API Documentation
- `GET /scalar/v1` - Swagger/OpenAPI documentation UI (Scalar)

#### Global Admin Panel
- `GET /admin/stats` - Aggregate platform stats (user/server/message counts by time window, open reports, live presence and messages/min)
- `GET /admin/users?page={n}&pageSize={n}&search={q}` - Paginated user list with search (by display name or email)
- `GET /admin/users/{id}` - User detail (profile, auth providers, server memberships, recent messages, report history, admin action log)
- `POST /admin/users/{id}/disable` - Disable a user account (revokes refresh tokens; blocks all auth flows)
- `POST /admin/users/{id}/enable` - Re-enable a disabled user account
- `POST /admin/users/{id}/force-logout` - Revoke all refresh tokens for a user
- `POST /admin/users/{id}/reset-password` - Remove password credential (sets `PasswordHash` to null)
- `PUT /admin/users/{id}/global-admin` - Promote or demote a user to/from global admin
- `GET /admin/servers?page={n}&pageSize={n}&search={q}` - Paginated server list with search
- `GET /admin/servers/{id}` - Server detail (members, channels, roles, owner)
- `POST /admin/servers/{id}/quarantine` - Quarantine a server (hides from discovery)
- `POST /admin/servers/{id}/unquarantine` - Remove quarantine from a server
- `DELETE /admin/servers/{id}` - Delete a server (broadcasts `ServerDeleted` via SignalR)
- `PUT /admin/servers/{id}/transfer-ownership` - Transfer server ownership to another member
- `GET /admin/reports?page={n}&status={s}&type={t}` - Paginated report list with status/type filters
- `GET /admin/reports/{id}` - Report detail with related report count
- `PUT /admin/reports/{id}` - Update report (assign, resolve, dismiss)
- `GET /admin/messages/search?q={q}` - Full-text message search across all servers
- `GET /admin/actions?page={n}&pageSize={n}` - Paginated admin action audit log
- `GET /admin/connections` - Live connection count
- `GET /admin/announcements` - List system announcements
- `POST /admin/announcements` - Create a system announcement
- `PUT /admin/announcements/{id}` - Update a system announcement
- `DELETE /admin/announcements/{id}` - Delete a system announcement
- `POST /reports` - Submit a user report (any authenticated user)
- `GET /announcements` - Get active system announcements (authenticated)

#### Admin Authorization
All `/admin/*` endpoints require the `GlobalAdmin` policy. The `GlobalAdminHandler` checks both `User.IsGlobalAdmin` and `!User.IsDisabled` on every request via a database lookup. Mutating admin endpoints use a separate `admin-writes` rate limit policy.

#### Admin Real-time (`/hubs/admin`)
- `AdminHub` SignalR hub at `/hubs/admin` for live admin dashboard stats
- `AdminMetricsService` background service broadcasts stats (active users, connections, messages/min, open reports) every 5 seconds via the `StatsUpdated` event

### SignalR Hub (`/hubs/chat`)

The SignalR hub provides real-time communication. Clients connect with their JWT token via query string.

**Connection URL:** `{API_BASE_URL}/hubs/chat?access_token={JWT}`

#### Client → Server Methods
| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinChannel` | `channelId: string` | Join a channel group to receive real-time events |
| `LeaveChannel` | `channelId: string` | Leave a channel group |
| `JoinServer` | `serverId: string` | Join a server group to receive membership events (called after joining via invite) |
| `LeaveServer` | `serverId: string` | Leave a server group (called after being kicked) |
| `Heartbeat` | `isActive: boolean` | Send presence heartbeat (every 30s); `isActive` true if user had input since last heartbeat |
| `StartTyping` | `channelId: string, displayName: string` | Broadcast typing indicator to channel |
| `StopTyping` | `channelId: string, displayName: string` | Clear typing indicator |
| `JoinDmChannel` | `dmChannelId: string` | Join a DM channel group for real-time events |
| `LeaveDmChannel` | `dmChannelId: string` | Leave a DM channel group |
| `StartDmTyping` | `dmChannelId: string, displayName: string` | Broadcast typing indicator to DM partner |
| `StopDmTyping` | `dmChannelId: string, displayName: string` | Clear typing indicator |
| `StartCall` | `dmChannelId: string` | Initiate a DM voice call; creates VoiceCall record; sends `IncomingCall` to recipient |
| `AcceptCall` | `callId: string` | Accept an incoming call; generates LiveKit token; returns room name and token for both parties |
| `SetupCallTransports` | `callId: string` | Generate LiveKit token for the call initiator after callee accepts; returns room name and token |
| `DeclineCall` | `callId: string` | Decline an incoming call; ends the call with `Declined` reason |
| `EndCall` | `callId: string` | End an active or ringing call; removes VoiceState records |

#### Server → Client Events
| Event | Payload | Description |
|-------|---------|-------------|
| `UserPresenceChanged` | `{ userId, status }` | User's presence status changed (online/idle/offline); sent to all server groups the user belongs to and friend user groups |
| `ReceiveMessage` | `{ id, authorName, authorUserId, body, createdAt, channelId, reactions, imageUrl, linkPreviews, replyContext }` | New message posted to current channel |
| `UserTyping` | `channelId: string, displayName: string` | Another user started typing |
| `UserStoppedTyping` | `channelId: string, displayName: string` | Another user stopped typing |
| `ReactionUpdated` | `{ messageId, channelId, reactions: [{ emoji, count, userIds }] }` | Reaction toggled on a message |
| `MessageDeleted` | `{ messageId, channelId }` | A channel message was deleted by its author or a Global Admin (sent to channel group) |
| `ChannelPurged` | `{ channelId }` | All messages in a channel were purged by a Global Admin (sent to channel group; clients clear message list) |
| `ServerDeleted` | `{ serverId }` | A server was deleted by its Owner or a Global Admin (sent to server group; clients navigate away and remove from server list) |
| `ChannelDeleted` | `{ serverId, channelId }` | A channel was deleted by an Owner, Admin, or Global Admin (sent to server group; clients remove from channel list and navigate if active) |
| `FriendRequestReceived` | `{ requestId, requester: { id, displayName, avatarUrl }, createdAt }` | Friend request received (sent to recipient's user group) |
| `FriendRequestAccepted` | `{ friendshipId, user: { id, displayName, avatarUrl }, since }` | Friend request accepted (sent to requester's user group) |
| `FriendRequestDeclined` | `{ requestId }` | Friend request declined (sent to requester's user group) |
| `FriendRequestCancelled` | `{ requestId }` | Friend request cancelled (sent to recipient's user group) |
| `FriendRemoved` | `{ friendshipId, userId }` | Friend removed (sent to the other participant's user group) |
| `ReceiveDm` | `{ id, dmChannelId, authorUserId, authorName, body, createdAt, imageUrl, linkPreviews, replyContext }` | New DM received (sent to DM channel group + recipient user group) |
| `DmMessageDeleted` | `{ messageId, dmChannelId }` | A DM was deleted by its author (sent to DM channel group + other participant's user group) |
| `DmTyping` | `{ dmChannelId, displayName }` | DM partner started typing |
| `DmStoppedTyping` | `{ dmChannelId, displayName }` | DM partner stopped typing |
| `DmConversationOpened` | `{ dmChannelId, participant: { id, displayName, avatarUrl } }` | A new DM conversation was opened (recipient's user group) |
| `KickedFromServer` | `{ serverId, serverName }` | User was kicked from a server (sent to kicked user's user group; displayed as transient overlay banner with 5s fade-out) |
| `MemberJoined` | `{ serverId }` | A new member joined the server (sent to server group; triggers member list refresh) |
| `MemberLeft` | `{ serverId }` | A member left or was kicked from the server (sent to server group; triggers member list refresh) |
| `MemberRoleChanged` | `{ serverId, userId, newRole }` | A member's role was changed (sent to server group; triggers member list refresh; updates caller's own role if affected) |
| `LinkPreviewsReady` | `{ messageId, channelId?, dmChannelId?, linkPreviews: [...] }` | Link preview metadata fetched — frontend patches the message's `linkPreviews` array |
| `CustomEmojiAdded` | `{ serverId, emoji: { id, name, imageUrl, contentType, isAnimated, uploadedByUserId, createdAt } }` | A custom emoji was uploaded to a server (sent to server group) |
| `CustomEmojiUpdated` | `{ serverId, emojiId, name }` | A custom emoji was renamed (sent to server group) |
| `CustomEmojiDeleted` | `{ serverId, emojiId }` | A custom emoji was deleted (sent to server group) |
| `ServerDescriptionChanged` | `{ serverId, description }` | Server description was updated (sent to server group) |
| `ChannelDescriptionChanged` | `{ serverId, channelId, description }` | Channel description/topic was updated (sent to server group) |
| `MessagePinned` | `{ messageId, channelId, pinnedBy, pinnedAt }` | A message was pinned in a channel (sent to channel group) |
| `MessageUnpinned` | `{ messageId, channelId, unpinnedBy }` | A message was unpinned from a channel (sent to channel group) |
| `CategoryCreated` | `{ serverId, category: { id, name, position } }` | A new channel category was created (sent to server group) |
| `CategoryRenamed` | `{ serverId, categoryId, name }` | A category was renamed (sent to server group) |
| `CategoryDeleted` | `{ serverId, categoryId }` | A category was deleted; affected channels become uncategorized (sent to server group) |
| `ChannelOrderChanged` | `{ serverId, channels: [{ channelId, position, categoryId }] }` | Channel positions and/or category assignments were bulk-updated (sent to server group) |
| `CategoryOrderChanged` | `{ serverId, categories: [{ categoryId, position }] }` | Category positions were bulk-updated (sent to server group) |
| `IncomingCall` | `{ callId, dmChannelId, callerUserId, callerDisplayName, callerAvatarUrl }` | Incoming voice call from a DM partner (sent to recipient's user group) |
| `CallAccepted` | `{ callId, sendTransportOptions, recvTransportOptions }` | Call was accepted; includes SFU transport options for the caller to connect |
| `CallDeclined` | `{ callId }` | Call was declined by the recipient (sent to caller's user group) |
| `CallEnded` | `{ callId }` | Call was ended by either party (sent to both participants' user groups) |
| `CallMissed` | `{ callId }` | Call timed out without being answered (sent to caller's user group) |
| `BannedFromServer` | `{ serverId, serverName, reason }` | User was banned from a server (sent to banned user's user group) |
| `MemberBanned` | `{ serverId, userId }` | A member was banned (sent to server group; triggers member list refresh) |
| `ImportProgress` | `{ stage, completed, total, percentComplete }` | Discord import progress update for each stage (sent to server group) |
| `ImportCompleted` | `{ importedChannels, importedMessages, importedMembers }` | Discord import milestone fired before media re-hosting begins (sent to server group) |
| `ImportFailed` | `{ errorMessage }` | Discord import failed with error details (sent to server group) |
| `ImportMessagesAvailable` | `{ channelId, count }` | New batch of imported messages available in a channel (sent to `channel-{channelId}` group); frontend reloads messages on receipt |
| `ImportRehostCompleted` | `{ importedChannels, importedMessages, importedMembers }` | Discord media re-hosting finished (sent to server group); frontend updates import status |

### Request/Response Format
All endpoints use JSON for request bodies and responses.

**Example Request (Create Server):**
```http
POST /servers HTTP/1.1
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "name": "My New Server"
}
```

**Example Response (Create Server):**
```http
HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "d3b07384-d113-4ec6-a3e6-9f6d2a3c5b1e",
  "name": "My New Server",
  "role": "Owner"
}
```

**Example Request (Create Channel):**
```http
POST /servers/d3b07384-d113-4ec6-a3e6-9f6d2a3c5b1e/channels HTTP/1.1
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "name": "design"
}
```

**Example Response (Create Channel):**
```http
HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "design",
  "serverId": "d3b07384-d113-4ec6-a3e6-9f6d2a3c5b1e"
}
```

**Example Request (Post Message):**
```http
POST /channels/550e8400-e29b-41d4-a716-446655440000/messages HTTP/1.1
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "body": "Hello, world!"
}
```

**Example Response:**
```http
HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "authorName": "John Doe",
  "authorUserId": "123e4567-e89b-12d3-a456-426614174000",
  "body": "Hello, world!",
  "createdAt": "2026-02-11T17:30:00Z",
  "channelId": "550e8400-e29b-41d4-a716-446655440000",
  "reactions": []
}
```

**Example Request (Toggle Reaction):**
```http
POST /channels/550e8400-e29b-41d4-a716-446655440000/messages/7c9e6679-7425-40de-944b-e07fc1f90ae7/reactions HTTP/1.1
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "emoji": "👍"
}
```

**Example Response (Toggle Reaction):**
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "action": "added",
  "reactions": [
    { "emoji": "👍", "count": 1, "userIds": ["123e4567-e89b-12d3-a456-426614174000"] }
  ]
}
```

## Data Model

### Entity Relationships
```
User ────┬──── ServerMember ──── Server ──── ChannelCategory
         │                         │              │
         │                    CustomEmoji     Channel ──────┐
         │                         │                  │         │
         │                    ServerRoleEntity    │    AuditLogEntry
         │                         │                  │
         ├──── Message ──────── Channel           │    BannedMember
         │        │                                    │
         ├──── Reaction ────────┘                 Webhook ──── WebhookDeliveryLog
         │        └──── DirectMessage
         │
         ├──── Friendship ──── User
         │    (Requester)     (Recipient)
         │
         ├──── DmChannelMember ──── DmChannel
         │                             │
         │                        DirectMessage
         │
         ├──── VoiceCall ──── DmChannel
         │    (Caller/Recipient)
         │
         ├──── VoiceState ──── Channel (nullable)
         │                     DmChannel (nullable)
         │
         ├──── ChannelNotificationOverride ──── Channel
         │
         ├──── PushSubscription (Web Push API subscription)
         │
         ├──── SamlIdentityProvider (SAML IdP link)
         │
         └──── PresenceState (transient; one per connection)

Message ───────┐
               ├──── LinkPreview
DirectMessage ─┘
```

### Core Entities

#### User
- Internal representation of authenticated users
- Linked to identity providers via `GoogleSubject`, `GitHubSubject`, `DiscordSubject`, or `SamlNameId`
- Fields: Id, GoogleSubject, GitHubSubject, DiscordSubject, SamlNameId, SamlIdentityProviderId, DisplayName, Nickname, Email, AvatarUrl, CustomAvatarPath, IsGlobalAdmin, IsDisabled, DisabledReason, DisabledAt, StatusText (nullable, max 128), StatusEmoji (nullable, max 8)
- Effective display name: `Nickname ?? DisplayName`
- `IsGlobalAdmin` grants platform-wide privileges (full access to all servers — read, post, react, manage channels/invites, delete any server/channel/message, kick any member)
- `IsDisabled` blocks all auth flows and admin access; set by global admin via admin panel

#### Server
- Top-level organizational unit (like Discord servers)
- Contains channels, members, categories, and custom emojis
- Fields: Id, Name, Description (nullable, max 256 chars), IsQuarantined, QuarantinedReason, QuarantinedAt

#### ServerMember
- Join table linking users to servers
- Tracks role, join date, and notification preferences
- Fields: ServerId, UserId, Role (Owner/Admin/Member), JoinedAt, CustomAvatarPath, IsMuted

#### Channel
- Text communication channel within a server
- Fields: Id, Name, ServerId, Description (nullable, max 256 chars), CategoryId (nullable FK → ChannelCategory), Position

#### ChannelCategory
- Ordered group of channels within a server
- Fields: Id, ServerId, Name, Position
- Cascade delete with Server; deleting a category sets `CategoryId` to null on its channels

#### AuditLogEntry
- Record of an administrative action performed in a server
- Fields: Id, ServerId, ActorUserId (nullable FK → User), Action (enum, 21 values), TargetType (nullable string), TargetId (nullable string), Details (nullable string), CreatedAt
- Entries older than 90 days are purged automatically by `AuditLogCleanupService` (background service)
- Action types: ServerCreated, ServerRenamed, ServerDescriptionChanged, ServerDeleted, ChannelCreated, ChannelRenamed, ChannelDescriptionChanged, ChannelDeleted, ChannelPurged, MemberJoined, MemberLeft, MemberKicked, MemberRoleChanged, InviteCreated, InviteRevoked, EmojiUploaded, EmojiRenamed, EmojiDeleted, MessageDeleted, CategoryCreated, CategoryDeleted

#### ChannelNotificationOverride
- Per-user, per-channel mute preference
- Composite primary key: (UserId, ChannelId)
- Fields: UserId (FK → User), ChannelId (FK → Channel), IsMuted
- Created on first mute; deleted when user leaves the server

#### Message
- Individual chat message in a channel
- Fields: Id, ChannelId, AuthorUserId, AuthorName, Body, ImageUrl (nullable), ReplyToMessageId (nullable, self-referencing FK), CreatedAt
- Has many `Reaction` entries
- Has many `LinkPreview` entries (max 5, fetched asynchronously after message is posted)
- Self-referencing FK with ON DELETE SET NULL (orphaned replies show "Original message was deleted")

#### Reaction
- Emoji reaction on a message or direct message by a user
- Fields: Id, MessageId (nullable), DirectMessageId (nullable), UserId, Emoji, CreatedAt
- Check constraint: exactly one of MessageId or DirectMessageId must be non-null
- Unique constraint on (MessageId, UserId, Emoji) for channel message reactions
- Unique constraint on (DirectMessageId, UserId, Emoji) for DM reactions

#### CustomEmoji
- Custom emoji image scoped to a server
- Fields: Id, ServerId, Name (max 32 chars, alphanumeric/underscore), ImageUrl, ContentType, IsAnimated, UploadedByUserId, CreatedAt
- Unique constraint on (ServerId, Name)
- Cascade delete with Server; restrict delete on User (emoji preserved if uploader is removed)
- Max 50 emojis per server (enforced at API level)
- Supported formats: PNG, JPEG, WebP, GIF (256 KB max)
- Content-addressed storage with SHA-256 hash in filename

#### Friendship
- Relationship between two users (friend request or confirmed friendship)
- Fields: Id, RequesterId, RecipientId, Status (Pending/Accepted/Declined), CreatedAt, UpdatedAt
- Unique constraint on (RequesterId, RecipientId) — one friendship record per user pair
- Bidirectional lookup: both sent and received requests checked to prevent duplicates

#### ServerInvite
- Invite code for joining a server without using the discover flow
- Fields: Id, ServerId, Code (unique 8-char alphanumeric), CreatedByUserId, ExpiresAt (nullable), MaxUses (nullable), UseCount, CreatedAt
- Unique index on Code for fast lookup
- Relationships: belongs to Server, created by User

#### DmChannel
- A private 1-on-1 conversation channel (not attached to any server)
- Fields: Id, CreatedAt
- Exactly two members per channel (enforced at API level)

#### DmChannelMember
- Join table linking users to DM channels
- Fields: DmChannelId, UserId, IsOpen, JoinedAt
- Composite primary key: (DmChannelId, UserId)
- `IsOpen` controls whether the conversation appears in the user's sidebar

#### DirectMessage
- Individual message within a DM conversation
- Fields: Id, DmChannelId, AuthorUserId, AuthorName, Body, ImageUrl (nullable), ReplyToDirectMessageId (nullable, self-referencing FK), MessageType (Regular=0, VoiceCallEvent=1), CreatedAt
- Follows the same shape as the server `Message` entity
- Has many `LinkPreview` entries (max 5, fetched asynchronously after message is posted)
- Self-referencing FK with ON DELETE SET NULL (orphaned replies show "Original message was deleted")
- `MessageType = VoiceCallEvent` messages are system messages for call events (body: "missed" or "call:{seconds}")

#### VoiceCall
- Tracks the lifecycle of a 1:1 DM voice call
- Fields: Id, DmChannelId, CallerUserId, RecipientUserId, Status (Ringing=0, Active=1, Ended=2), EndReason (Answered, Declined, Missed, Timeout, Disconnected), StartedAt, AnsweredAt, EndedAt
- One active call per DM channel at a time (enforced at API level)
- `VoiceCallTimeoutService` monitors ringing calls — ends them after 30 seconds with `Timeout` reason and creates a "missed" system message

#### PresenceState
- Transient entity tracking a user's real-time presence per SignalR connection
- Fields: Id, UserId (FK → User), Status (Online=0, Idle=1, Offline=2), ConnectionId, LastHeartbeatAt, LastActiveAt, ConnectedAt
- Created on SignalR connect, deleted on disconnect; all rows purged on server startup
- Multiple rows per user (one per browser tab/connection); aggregate status = best across connections
- In-memory `PresenceTracker` singleton handles heartbeat timestamps; DB only updated on status transitions

#### LinkPreview
- URL metadata extracted from a message body (Open Graph + HTML meta fallbacks)
- Fields: Id, MessageId (nullable FK), DirectMessageId (nullable FK), Url, Title, Description, ImageUrl, SiteName, CanonicalUrl, FetchedAt, Status
- Check constraint: exactly one of MessageId or DirectMessageId must be non-null
- Fetched asynchronously by `LinkPreviewService` after message posting; delivered to clients via `LinkPreviewsReady` SignalR event
- Metadata cached in Redis by URL hash (`linkpreview:{SHA256}`) with 1-hour TTL; failed fetches cached too to avoid re-hitting broken URLs
- SSRF protection: private IP blocking, DNS rebinding prevention via `SocketsHttpHandler.ConnectCallback`, redirect limiting

#### BannedMember
- Record of a user banned from a server
- Composite primary key: (ServerId, UserId)
- Fields: ServerId (FK → Server), UserId (FK → User), BannedByUserId (FK → User), Reason (nullable string), BannedAt
- Banned users are prevented from re-joining via invite codes

#### ServerRoleEntity
- Custom role with granular permissions for a server
- Fields: Id, ServerId (FK → Server), Name, Color (nullable hex string), Position (hierarchy ordering), Permissions (Permission bitmask), IsSystemRole, IsHoisted, IsMentionable, CreatedAt
- System roles (Owner, Admin, Member, @everyone) are created automatically and cannot be deleted
- Lower position = higher rank in the hierarchy

#### Permission (Flags Enum)
- 21 granular permission flags stored as a bitmask (bigint):
  - General: ViewChannels, ManageChannels, ManageServer, ManageRoles, ManageEmojis, ViewAuditLog, CreateInvites, ManageInvites
  - Membership: KickMembers, BanMembers
  - Messages: SendMessages, EmbedLinks, AttachFiles, AddReactions, MentionEveryone, ManageMessages, PinMessages
  - Voice: Connect, Speak, MuteMembers, DeafenMembers
  - Special: Administrator (bypasses all permission checks)

#### Webhook
- Outgoing webhook configuration scoped to a server
- Fields: Id, ServerId (FK → Server), Name, Url, Secret (nullable, HMAC-SHA256 signing), EventTypes (comma-separated), IsActive, CreatedByUserId (FK → User), CreatedAt
- Event types: MessageCreated, MessageUpdated, MessageDeleted, MemberJoined, MemberLeft, MemberRoleChanged, ChannelCreated, ChannelUpdated, ChannelDeleted
- Background dispatch with retry (exponential backoff: 5s, 30s, 5m); delivery logged via `WebhookDeliveryLog`

#### WebhookDeliveryLog
- Record of each webhook delivery attempt
- Fields: Id, WebhookId (FK → Webhook), EventType, Payload (JSON), StatusCode (nullable), ErrorMessage (nullable), Success, Attempt, CreatedAt
- One row per attempt; up to 3 attempts per event

#### PushSubscription
- Web Push API subscription for browser push notifications
- Fields: Id, UserId (FK → User), Endpoint (push service URL), P256dh (client public key), Auth (shared secret), IsActive, CreatedAt
- Notifications sent for: DMs, @mentions, friend requests
- Auto-deactivated when push service returns 410 Gone

#### SamlIdentityProvider
- SAML 2.0 identity provider configuration
- Fields: Id, EntityId, DisplayName, SingleSignOnUrl, CertificatePem (X.509 cert for signature verification), IsEnabled, AllowJitProvisioning, CreatedAt, UpdatedAt
- User entity extended with `SamlNameId` and `SamlIdentityProviderId` for SAML user matching

#### Report
- User-submitted report for moderation review
- Fields: Id, ReportType (User/Message/Server), TargetId, ReporterId (FK → User), Reason, Status (Open/Reviewing/Resolved/Dismissed), AssignedToUserId (nullable FK → User), Resolution (nullable), ResolvedByUserId (nullable FK → User), ResolvedAt, TargetSnapshot, CreatedAt

#### AdminAction
- Immutable audit log entry for admin operations
- Fields: Id, ActorUserId (FK → User), ActionType (enum, 16 values), TargetType, TargetId, Reason (nullable), Details (nullable), CreatedAt
- Action types: UserDisabled, UserEnabled, UserGlobalBanned, UserForcedLogout, UserPasswordReset, UserPromotedAdmin, UserDemotedAdmin, ServerQuarantined, ServerUnquarantined, ServerDeleted, ServerOwnershipTransferred, ReportResolved, ReportDismissed, AnnouncementCreated, AnnouncementDeleted, MessagesPurged

#### SystemAnnouncement
- Platform-wide announcement displayed to all users
- Fields: Id, Title, Body, IsActive, ExpiresAt (nullable), CreatedByUserId (FK → User), CreatedAt

#### DiscordImport
- Record of a Discord server import operation
- Fields: Id, ServerId (FK → Server), DiscordGuildId, EncryptedBotToken (nullable, cleared after import), Status (Pending/InProgress/Completed/Failed/Cancelled/RehostingMedia), StartedAt, CompletedAt, ErrorMessage, ImportedChannels, ImportedMessages, ImportedMembers, LastSyncedAt, InitiatedByUserId (FK → User), CreatedAt
- Bot token encrypted at rest via ASP.NET Core Data Protection

#### DiscordUserMapping
- Maps a Discord user to a Codec user within an imported server
- Fields: Id, ServerId (FK → Server), DiscordUserId, DiscordUsername, DiscordAvatarUrl (nullable), CodecUserId (nullable FK → User), ClaimedAt (nullable), CreatedAt
- `CodecUserId` is set when a Codec user claims the Discord identity

#### DiscordEntityMapping
- Maps a Discord entity ID to a Codec entity ID for cross-referencing during import
- Fields: Id, DiscordImportId (FK → DiscordImport), ServerId (FK → Server), DiscordEntityId, EntityType (Role/Category/Channel/Message/Emoji/PinnedMessage/PendingReply), CodecEntityId
- Used during re-sync for incremental message import (`after` pagination from newest imported Discord ID per channel) and to resolve reply chains
- `PendingReply` entries track replies to not-yet-imported messages (due to newest-first pagination) and are resolved in a final backfill pass per channel

## Configuration

### Web Client (`.env`)
```env
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=your_google_client_id
```

### API Server (`appsettings.Development.json`)
```json
{
  "Google": {
    "ClientId": "your_google_client_id"
  },
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5174"]
  },
  "GlobalAdmin": {
    "Email": "your_global_admin_email@example.com"
  }
}
```

> **Note:** `GlobalAdmin:Email` designates a user as the platform-wide admin at startup. In production, this is stored in Azure Key Vault as the `GlobalAdmin--Email` secret (provisioned via Bicep infrastructure) and injected as the `GlobalAdmin__Email` environment variable.

## Security Considerations

### Current Implementation
- ✅ JWT signature validation
- ✅ Audience validation (client ID)
- ✅ Issuer validation (Google)
- ✅ Token expiration checking
- ✅ CORS restrictions (environment-driven)
- ✅ User identity isolation (membership checks)
- ✅ Controller-level `[Authorize]` attribute enforcement
- ✅ Global admin role with configurable email (Key Vault in production)
- ✅ Rate limiting (fixed window, 100 req/min)
- ✅ Structured request logging (Serilog)
- ✅ Content Security Policy (CSP) headers
- ✅ Security headers (X-Content-Type-Options, X-Frame-Options, Referrer-Policy)
- ✅ Forwarded headers for reverse proxy (Azure Container Apps)
- ✅ SSRF protection on link preview fetching (private IP blocking, DNS rebinding prevention)
- ✅ Secrets management via Azure Key Vault (production)
- ✅ Managed Identity for all Azure service-to-service auth (no connection strings for blob/ACR)
- ✅ Account lockout after 5 failed login attempts (15-minute window)
- ✅ Server-side refresh token revocation on logout
- ✅ Optimistic concurrency on refresh token rotation (PostgreSQL `xmin`)
- ✅ Background cleanup of expired/revoked refresh tokens

### Production Requirements
- 🔒 HTTPS enforcement (via Azure Container Apps)
- 🔒 Database encryption at rest (Azure-managed)
- 🔒 Container image vulnerability scanning

## Deployment Architecture

### Development
```
┌──────────────────┐
│   Developer      │
│   Machine        │
│                  │
│  ┌────────────┐  │
│  │  Vite Dev  │  │ :5174
│  └────────────┘  │
│                  │
│  ┌────────────┐  │
│  │ ASP.NET    │  │ :5050
│  │ API        │  │
│  └────────────┘  │
│                  │
│  ┌────────────┐  │
│  │ PostgreSQL │  │
│  │  (Docker)  │  │ :5433
│  └────────────┘  │
└──────────────────┘
```

### Production (Azure)
```
                     ┌─────────────────────────────────────────────────────┐
                     │                Azure (Central US)                   │
                     │                                                     │
┌──────────┐  HTTPS  │  ┌──────────────────────────────────────────────┐   │
│  Users /  │───────►│  │          Container Apps Environment           │   │
│  Browser  │        │  │                                              │   │
└──────────┘        │  │  ┌────────────────┐  ┌────────────────────┐  │   │
                     │  │  │  Web App       │  │  API App           │  │   │
                     │  │  │  SvelteKit     │──│  ASP.NET Core 10   │  │   │
                     │  │  │  Node.js 20    │  │  SignalR WebSocket │  │   │
                     │  │  │  Port 3000     │  │  Port 8080         │  │   │
                     │  │  └────────────────┘  └────────┬───────────┘  │   │
                     │  └────────────────────────────────┼──────────────┘   │
                     │                                   │                  │
                     │  ┌────────────────┐  ┌───────────┴──────────────┐   │
                     │  │  Azure Blob    │  │  PostgreSQL Flexible     │   │
                     │  │  Storage       │  │  Server (B1ms, 32 GB)    │   │
                     │  │  (avatars,     │  └──────────────────────────┘   │
                     │  │   images)      │                                 │
                     │  └────────────────┘  ┌──────────────────────────┐   │
                     │                      │  Key Vault (secrets)     │   │
                     │  ┌────────────────┐  └──────────────────────────┘   │
                     │  │  Container     │                                 │
                     │  │  Registry      │  ┌──────────────────────────┐   │
                     │  └────────────────┘  │  Log Analytics Workspace │   │
                     │                      └──────────────────────────┘   │
                     └─────────────────────────────────────────────────────┘

                     ┌─────────────────────────────────────────────────────┐
                     │                  GitHub Actions                     │
                     │                                                     │
                     │  CI ──► Infra (if infra/ changed) ──► CD            │
                     │    └──► CD (if no infra changes)                    │
                     │                                                     │
                     │  CI: build, check, test (web + API unit +           │
                     │      API integration), Docker image validation      │
                     │  Infra: zero-downtime Bicep deploy (preserves       │
                     │         running container images), then triggers CD  │
                     │  CD: build, push, migrate, blue-green deploy, smoke │
                     │  Shared concurrency group prevents pipeline races    │
                     │  OIDC federated credentials (no long-lived secrets) │
                     └─────────────────────────────────────────────────────┘
```

See [DEPLOYMENT.md](DEPLOYMENT.md) for full deployment instructions, rollback procedures, and troubleshooting.

## Performance Considerations

### Current Optimizations
- Async/await throughout API
- Efficient EF Core queries (AsNoTracking)
- Redis distributed cache for message history — paginated history pages cached with 5-minute TTL via `MessageCacheService`; channel-level invalidation on all mutations (send, edit, delete, purge, reactions); skips caching the "latest" page to avoid write amplification; graceful degradation when Redis is unavailable
- Redis distributed cache for link preview metadata — `LinkPreviewService` caches fetched metadata by URL SHA-256 hash with 1-hour TTL; failed fetches cached as sentinels to prevent redundant requests; same graceful degradation pattern
- SignalR Redis backplane — enables horizontal scaling across multiple API instances via Redis pub/sub
- Response compression (Brotli + Gzip, `CompressionLevel.Fastest`) for `application/json` payloads
- Optimized user profile writes — `UserService.GetOrCreateUserAsync` skips `SaveChangesAsync` when Google profile fields are unchanged
- Cached mention parsing — regex results cached per message batch via `ToDictionary` to eliminate redundant execution
- Vite build optimization
- Tree-shaking and code splitting
- PWA with Workbox service worker — precaches static assets (HTML, JS, CSS, images) for faster repeat visits and offline-capable shell; runtime caching for Google Fonts; offline fallback page when network is unavailable
- SignalR for real-time message delivery and typing indicators (eliminates polling)
- Channel-scoped SignalR groups (targeted broadcasts, not global fan-out)
- Connection status awareness — composer disables with "Codec connecting..." when SignalR disconnects, preventing failed sends; graceful reconnection with exponential backoff on persistent failure (no page reload)

### Future Improvements
- Database indexing strategy
- CDN for static assets
- Connection pooling
- Query optimization with compiled queries
