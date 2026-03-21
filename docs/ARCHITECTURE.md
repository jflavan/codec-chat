# Architecture

## Overview
Codec is a modern Discord-like chat application built as a monorepo. The architecture follows a clean separation between the client and server, with Google Identity Services handling authentication through ID tokens.

### Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 10 Web API (Controller-based APIs)
- **Real-time:** SignalR (WebSockets with automatic fallback); Redis backplane for multi-instance scale-out
- **Caching:** Redis 8 distributed cache (`IDistributedCache`) for message history with channel-level invalidation and link preview metadata with 1-hour TTL
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** Google Identity Services (ID token validation)
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
│   │   └── google.ts       # Google Identity Services SDK initialization
│   ├── services/
│   │   └── chat-hub.ts     # SignalR hub connection lifecycle (ChatHubService)
│   ├── state/
│   │   └── app-state.svelte.ts  # Central reactive state (AppState class with $state/$derived)
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
│   │   │   ├── Composer.svelte           # Message input with send button
│   │   │   ├── EmojiPicker.svelte        # Full emoji picker (search, categories, custom emojis, frequent)
│   │   │   ├── ImagePreview.svelte       # Full-screen image lightbox overlay
│   │   │   ├── LinkPreviewCard.svelte    # Open Graph link preview embed card
│   │   │   ├── LinkifiedText.svelte      # Auto-linked URLs in message body (+ custom emoji rendering)
│   │   │   ├── MessageFeed.svelte        # Scrollable message list with grouping
│   │   │   ├── MessageItem.svelte        # Single message (grouped/ungrouped)
│   │   │   ├── ReactionBar.svelte        # Reaction pills (emoji + count)
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
│   │   │   └── DmCallHeader.svelte       # Active call header (timer, mute, end)
│   │   ├── settings/
│   │   │   ├── UserSettingsModal.svelte   # Full-screen modal overlay shell
│   │   │   ├── SettingsSidebar.svelte     # Category navigation sidebar
│   │   │   ├── BugReportModal.svelte     # In-app bug report submission dialog
│   │   │   ├── ProfileSettings.svelte     # Nickname + avatar management
│   │   │   ├── AccountSettings.svelte     # Read-only info + sign out
│   │   │   └── AppearanceSettings.svelte  # Theme picker (4 presets with preview cards)
│   │   ├── server-settings/
│   │   │   ├── ServerSettingsModal.svelte  # Modal shell with sidebar + content area
│   │   │   ├── ServerSettingsSidebar.svelte # Category navigation (General, Channels, Invites, Emojis, Members, Audit Log)
│   │   │   ├── ServerSettings.svelte      # General tab — server name, description, icon, danger zone
│   │   │   ├── ServerChannels.svelte      # Channels tab — category and channel management with drag-and-drop reordering
│   │   │   ├── ServerInvites.svelte       # Invites tab — invite CRUD (create, list, revoke)
│   │   │   ├── ServerAuditLog.svelte      # Audit Log tab — paginated action history
│   │   │   ├── ServerEmojis.svelte        # Emojis tab — custom emoji upload, rename, delete (Owner/Admin)
│   │   │   └── ServerMembers.svelte       # Members tab — member role management (promote/demote, Owner/Admin)
│   │   ├── shared/
│   │   │   └── PresenceDot.svelte        # Online/idle/offline indicator dot
│   │   ├── ReloadPrompt.svelte            # PWA update toast (new version available)
│   │   └── members/
│   │       ├── MembersSidebar.svelte     # Members grouped by role (online-first sorting)
│   │       └── MemberItem.svelte         # Single member card (with presence dot)
│   └── index.ts            # Public barrel exports
└── routes/
    ├── +layout.svelte      # Root layout (global CSS, font preconnect)
    └── +page.svelte        # Thin composition shell (~75 lines)
```

**State Management Pattern:**

The `AppState` class in `app-state.svelte.ts` uses Svelte 5 runes (`$state`, `$derived`) for fine-grained reactivity. It is created once in `+page.svelte` via `createAppState()` and injected into the component tree via Svelte's `setContext()`. Child components retrieve it with `getAppState()`.

```
+page.svelte
  └─ createAppState(apiBaseUrl, googleClientId)  → setContext(APP_STATE_KEY, state)
      ├── ServerSidebar      → getAppState()
      ├── ChannelSidebar     → getAppState()
      │   └── UserPanel      → getAppState()
      ├── HomeSidebar        → getAppState()  (shown when Home is active)
      │   └── DmList         → getAppState()
      ├── FriendsPanel       → getAppState()  (shown when Home active, no DM selected)
      │   ├── FriendsList    → getAppState()
      │   ├── PendingRequests → getAppState()
      │   └── AddFriend      → getAppState()
      ├── DmChatArea         → getAppState()  (shown when DM conversation selected)
      ├── ChatArea           → getAppState()
      │   ├── MessageFeed    → getAppState()
      │   ├── Composer       → getAppState()
      │   └── TypingIndicator → getAppState()
      ├── MembersSidebar     → getAppState()
      │   └── MemberItem     (receives props, no context needed)
      ├── UserSettingsModal  → getAppState()  (shown when settingsOpen)
      │   ├── SettingsSidebar  → getAppState()
      │   ├── ProfileSettings  → getAppState()
      │   └── AccountSettings  → getAppState()
      ├── BugReportModal     → getAppState()  (shown when bugReportOpen)
      └── ImagePreview       → getAppState()  (shown when lightboxImageUrl is set)
```

**Layer Responsibilities:**

| Layer | Purpose |
|-------|---------|
| `types/` | Shared TypeScript interfaces for domain models |
| `api/` | HTTP communication with the REST API; typed methods, `encodeURIComponent` on path params |
| `auth/` | Token lifecycle (persist, load, expire, clear) and Google SDK setup |
| `services/` | External service integrations (SignalR hub connection management) |
| `state/` | Central reactive application state; orchestrates API calls, auth, and hub events |
| `data/` | Static datasets (emoji categories, keywords) |
| `styles/` | Design tokens as CSS custom properties (`[data-theme]` palettes for theming); global base styles |
| `utils/` | Pure utility functions (formatting, frequency tracking, etc.) |
| `components/` | Presentational Svelte 5 components grouped by feature area |

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
  - Automatic migrations (development)
  - CORS support for local development

### Authorization & Error Handling
- Controllers use `UserService` helper methods (`EnsureMemberAsync`, `EnsureAdminAsync`, `EnsureOwnerAsync`, `EnsureDmParticipantAsync`) for membership and role checks
- Authorization failures throw `ForbiddenException` (→ 403) or `NotFoundException` (→ 404)
- A global exception handler converts exceptions to RFC 7807 ProblemDetails JSON responses
- Request DTOs use `System.ComponentModel.DataAnnotations` (`[Required]`, `[StringLength]`, etc.) for structural validation; business logic validation remains inline in controllers

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
  - Automatic reconnect via `withAutomaticReconnect()`
  - Auto-refresh fallback — page reloads if reconnection fails within 5 seconds or WebSocket closes with an error

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
       │  4. ID Token (JWT)    │                        │
       │<──────────────────────┤                        │
       │                       │                        │
       │  5. API Call + Bearer Token                    │
       ├───────────────────────────────────────────────>│
       │                       │                        │
       │                       │  6. Validate Token     │
       │                       │<───────────────────────┤
       │                       │                        │
       │                       │  7. Token Valid        │
       │                       ├───────────────────────>│
       │                       │                        │
       │  8. Response (JSON)                            │
       │<───────────────────────────────────────────────┤
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
4. Web client receives a JWT ID token
5. Client sends API requests with `Authorization: Bearer <token>` header
6. API validates the token against Google's JWKS endpoint
7. API extracts user claims (subject, email, name, picture)
8. API returns requested data or performs operations

**Key Points:**
- Stateless authentication (no server-side sessions)
- Tokens are short-lived (typically 1 hour)
- API does not issue its own tokens
- User identity mapped to internal User records via Google subject
- Web client persists token in `localStorage` for session continuity (up to 1 week)
- Automatic silent token refresh via Google One Tap (`auto_select: true`)
- SignalR WebSocket connections authenticate via `access_token` query parameter (standard pattern for WebSocket auth since `Authorization` headers aren't supported)

**reCAPTCHA v3 Bot Protection:**
- `POST /auth/register` and `POST /auth/login` are decorated with `[ValidateRecaptcha]` action filter
- Frontend loads Google reCAPTCHA Enterprise script and calls `grecaptcha.enterprise.execute()` to obtain a token on each auth request
- `RecaptchaService` sends the token to Google's reCAPTCHA Enterprise Assessment API and validates the score against a configurable threshold (default 0.5)
- Fail-closed: if the Google API is unreachable or returns an error, the request is rejected (403)
- Disabled by default in local development (`Recaptcha:Enabled = false` in `appsettings.json`); enabled in production when `Recaptcha:SiteKey` is configured (derived in Bicep)
- Google Sign-In flow is unaffected (does not go through `AuthController`)

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

#### Image Uploads
- `POST /uploads/images` - Upload an image file (multipart/form-data; JPEG, PNG, WebP, GIF; 10 MB max; returns `{ imageUrl }`)

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
| `AcceptCall` | `callId: string` | Accept an incoming call; sets up SFU transports; returns transport options for both parties |
| `SetupCallTransports` | `callId: string, sendTransportId: string, recvTransportId: string` | Finalize transport setup for the call initiator after callee accepts |
| `DeclineCall` | `callId: string` | Decline an incoming call; ends the call with `Declined` reason |
| `EndCall` | `callId: string` | End an active or ringing call; cleans up SFU state and VoiceState records |

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
         │                                        │         │
         ├──── Message ──────── Channel           │    AuditLogEntry
         │        │
         ├──── Reaction ────────┘
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
         └──── PresenceState (transient; one per connection)

Message ───────┐
               ├──── LinkPreview
DirectMessage ─┘
```

### Core Entities

#### User
- Internal representation of authenticated users
- Linked to Google identity via `GoogleSubject`
- Fields: Id, GoogleSubject, DisplayName, Nickname, Email, AvatarUrl, CustomAvatarPath, IsGlobalAdmin
- Effective display name: `Nickname ?? DisplayName`
- `IsGlobalAdmin` grants platform-wide privileges (full access to all servers — read, post, react, manage channels/invites, delete any server/channel/message, kick any member)

#### Server
- Top-level organizational unit (like Discord servers)
- Contains channels, members, categories, and custom emojis
- Fields: Id, Name, Description (nullable, max 256 chars)

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
- Connection status awareness — composer disables with "Codec connecting..." when SignalR disconnects, preventing failed sends; auto-refreshes on persistent failure

### Future Improvements
- Database indexing strategy
- CDN for static assets
- Connection pooling
- Query optimization with compiled queries
