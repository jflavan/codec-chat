# Codec Plan

## Purpose
Create a Discord-like app called Codec with a SvelteKit web front-end and an ASP.NET Core Web API backend. Authentication supports both email/password registration and Google Sign-In, with equivalent security for both methods. New users choose a nickname during sign-up.

## Milestones
1. Baseline scaffolding
   - Monorepo layout: apps/web, apps/api, docs, .github
   - Initial README and docs
   - Copilot agent guidance files
2. Backend skeleton
   - .NET 10 Web API project
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
- API: .NET 10
- Auth: Two equivalent sign-in methods — (1) email/password with bcrypt hashing and API-issued JWTs, (2) Google Sign-In with token exchange (`POST /auth/google` exchanges Google ID tokens for backend-issued JWTs with rotating refresh tokens). Both flows require a nickname on first sign-up. Returning users sign in directly with either method. All auth methods produce identical `codec-api`-issued tokens. Email/password registrations require email verification before app access (24-hour token, SHA-256 hashed; `[RequireEmailVerified]` filter on data endpoints; Google users auto-verified).
- Layout: apps/web + apps/api + docs + .github
- Package manager: npm
- Data: PostgreSQL + EF Core (Npgsql); Azure Database for PostgreSQL Flexible Server in production
- Hosting: Azure Container Apps (Consumption tier), Docker multi-stage builds
- IaC: Bicep modules under `infra/`
- CI/CD: GitHub Actions (CI, CD, Infrastructure pipelines)
- Auth (Azure): OIDC federated credentials (no long-lived secrets)
- Secrets: Azure Key Vault + GitHub Secrets
- Global admin: Configurable via `GlobalAdmin:Email` setting (Key Vault in production, appsettings in development); has full access to all servers — can see all servers, read/post/react in any channel, manage channels and invites, rename servers, delete any server/channel/message, and kick any member
- File storage: Azure Blob Storage (production), local disk (development)
- Logging: Serilog with structured JSON → Log Analytics
- Observability: OpenTelemetry (traces, metrics, logs) via Codec.ServiceDefaults; Azure Monitor / Application Insights in production
- Local dev: .NET Aspire AppHost for single-command orchestration
- Testing (API): xUnit + FluentAssertions + Moq for unit tests; WebApplicationFactory + Testcontainers (PostgreSQL, Redis) for integration tests; coverlet for coverage
- Testing (Web): Vitest + jsdom for unit tests; @vitest/coverage-v8 for coverage
- Test auth: FakeAuthHandler bypasses Google JWT validation in integration tests (base64-encoded claims as Bearer token)

## Current status
- **All features implemented** — see [FEATURES.md](docs/FEATURES.md) for full list
- **Server settings enhancements** — server/channel descriptions, channel categories with drag-and-drop ordering, invite management tab, audit log (21 action types, 90-day retention), notification mute preferences (per-server and per-channel via right-click context menus)
- **Global admin role** — configurable global admin with full access to all servers (see all servers, read/post/react in any channel, manage channels/invites, rename servers, delete any server/channel/message, kick any member); seeded from `GlobalAdmin:Email` config; Key Vault integration for production
- **Global admin panel** — standalone SvelteKit app (`apps/admin/`) with dashboard (live stats via SignalR, live activity chart), user management (disable/enable, force logout, reset password, promote/demote), server management (quarantine, delete, transfer ownership), moderation (report queue, message search), system tools (audit log, announcements, connection count); all endpoints guarded by `GlobalAdmin` authorization policy with `IsDisabled` check; `AdminMetricsService` background service broadcasts stats every 5 seconds
- Real-time member list updates via SignalR server-scoped groups
- Alpha notification banner with in-app bug report modal shown on every login
- GitHub Issues bug report template for alpha testers (`.github/ISSUE_TEMPLATE/bug-report.yml`)
- In-app bug report submission via `POST /issues` endpoint (proxied to GitHub Issues API with server-side PAT; `BugReportModal` accessible from alpha banner and settings sidebar)
- **Deployed to Azure** via Container Apps (Central US)
- Database migrated from SQLite to PostgreSQL (Azure Database for PostgreSQL Flexible Server)
- File storage migrated to Azure Blob Storage (avatars + images containers)
- SvelteKit switched to `adapter-node` for containerized deployment
- API hardened: health probes, Serilog structured logging, CORS, forwarded headers, rate limiting
- Response compression enabled (Brotli + Gzip) for faster API responses
- User profile writes optimized — skips `SaveChangesAsync` when Google profile fields unchanged
- Mention parsing cached per message batch to eliminate redundant regex execution
- **Redis distributed cache** — message history pages cached with 5-minute TTL via `MessageCacheService`; channel-level invalidation on all mutations; link preview metadata cached with 1-hour TTL by URL hash in `LinkPreviewService`; SignalR Redis backplane for multi-instance scale-out; graceful degradation when Redis is unavailable
- DM messages endpoint returns paginated `{ hasMore, messages }` response (matching channel pagination)
- Connection status awareness — composer disables with "Codec connecting..." when SignalR disconnects; restores on reconnect; graceful reconnection with exponential backoff replaces page reload on persistent failure
- SignalR reconnection lifecycle tracked via `isHubConnected` reactive state; inactive tab tolerance via extended server-side KeepAlive/ClientTimeout intervals
- Both apps containerized with optimized multi-stage Dockerfiles
- Infrastructure as Code via Bicep modules under `infra/`
- CI pipeline: build, lint, test (web unit, API unit, API integration), Docker image validation on every push/PR
- CD pipeline: build → push to ACR → EF Core migration bundle → blue-green deploy (staging revisions → health verification → traffic switch) → smoke tests
- Infrastructure pipeline: Bicep what-if → deploy on push to `infra/` or manual dispatch
- OIDC federated credentials for GitHub Actions → Azure (no long-lived secrets)
- **Auth security hardening** — account lockout (5 failed attempts → 15-minute lock), server-side logout with refresh token revocation, optimistic concurrency on token rotation (PostgreSQL `xmin`), background refresh token cleanup service (every 6 hours)
- **reCAPTCHA v3 bot protection** — Google reCAPTCHA Enterprise on login/register endpoints; `[ValidateRecaptcha]` action filter with `RecaptchaService`; frontend token acquisition via `grecaptcha.enterprise.execute()`; fail-closed; configurable score threshold; Bicep/Key Vault integration for secrets; disabled in local dev and integration tests
- Content Security Policy with SvelteKit nonce-based inline script support
- Progressive Web App (PWA) — installable via `@vite-pwa/sveltekit`, Workbox service worker with precached assets, user-prompted update toast, branded icons from favicon.ico
- **Custom server emojis** — CRUD endpoints for custom emoji management (upload, list, rename, delete); 256 KB max, 50 per server; content-addressed storage; real-time SignalR sync
- **Server admin role management** — PATCH endpoint to promote/demote members; Owner can promote Members to Admin and demote Admins to Member; Admins can promote Members but not demote other Admins; Members tab in server settings with promote/demote UI; Owner/Admin role badges in member sidebar; real-time `MemberRoleChanged` SignalR event
- **DM reactions** — Reaction entity extended with nullable DirectMessageId (mutual exclusivity constraint with MessageId) to support emoji reactions on direct messages
- **.NET Aspire AppHost** — single-command local dev orchestration (Postgres, Redis, Azurite, API, Web) with dashboard at `https://localhost:17222`
- **OpenTelemetry observability** — `Codec.ServiceDefaults` shared project with distributed traces, metrics, and structured logs; Azure Monitor / Application Insights in production; OTLP export for local Aspire dashboard; Application Insights Bicep module wired to API container app
- **Testing** — 1,542 automated tests (1,188 API unit, 177 API integration, 177 web); API core services at 95%+ coverage; web unit-testable code at 98%+ coverage; combined API coverage at 80%+; integration tests use Testcontainers for disposable Postgres/Redis; see [TESTING.md](docs/TESTING.md)
- All health checks passing (API `/health/ready` 200, Web `/health` 200)
- Custom domain (`codec-chat.com`) with managed TLS certificates via two-phase Bicep deployment (HTTP validation)
- `PUBLIC_API_BASE_URL` GitHub Secret set to `https://api.codec-chat.com`
- **Message pinning** — `PinnedMessage` entity with unique `(ChannelId, MessageId)` index; pin/unpin/list endpoints in `ChannelsController` (Owner/Admin/GlobalAdmin, 50-pin limit); `PinNotification` system messages; `MessagePinned`/`MessageUnpinned` SignalR events; audit logging (`MessagePinned`/`MessageUnpinned` actions); frontend pin button in action bar, pin indicator on messages, slide-in pinned messages panel with unpin controls, reactive pin state in `MessageStore`
- **Custom roles and granular permissions** — `ServerRoleEntity` with 21 `Permission` flags (bitmask); role hierarchy with position ordering; system roles (Owner, Admin, Member, @everyone) + custom roles; full CRUD via `RolesController`; role management UI with permission editor; role badges with custom colors; `IsMentionable` and `IsHoisted` options
- **User banning** — `BannedMember` entity with reason and actor tracking; ban/unban/list endpoints; ban check on invite join; optional message purge on ban; real-time `BannedFromServer` and `MemberBanned` SignalR events; ban management tab in server settings
- **Video chat and screen sharing** — Voice Phase 5 complete; webcam video and screen sharing via LiveKit video/screen tracks; `IsVideoEnabled` and `IsScreenSharing` state per participant; `VideoTile` and `VideoGrid` components; `getDisplayMedia()` for screen capture
- **Outgoing webhooks** — `Webhook` entity with per-server config (name, URL, secret, event types); `WebhookDeliveryLog` for delivery tracking; background dispatch with exponential backoff retry (5s, 30s, 5m); HMAC-SHA256 payload signing; 9 event types
- **Web push notifications** — `PushSubscription` entity with VAPID keys; subscribe/unsubscribe endpoints; notifications for DMs, @mentions, and friend requests; auto-deactivation on 410 Gone
- **SAML 2.0 SSO** — `SamlIdentityProvider` entity; SP-initiated login with HTTP-Redirect binding; XML signature verification; JIT user provisioning; admin CRUD for IdP management; metadata import
- **GitHub and Discord OAuth** — authorization code exchange; user profile fetching; `GitHubSubject`/`DiscordSubject` on User entity; account linking to existing accounts; `GET /auth/oauth/config` for provider discovery
- **Status messages** — per-user `StatusText` (128 char) and `StatusEmoji` (8 char) fields; displayed in member lists and profiles
- **File attachments** — `FileUploadService` for document uploads (25 MB max); `FileName`, `FileSize`, `FileMimeType`, `FileUrl` fields on Message/DirectMessage; `FileCard` component; composer file picker and drag-and-drop
- **Image proxy** — `GET /images/proxy?url=` endpoint for external image proxying with SSRF protection, content-type validation, and 10 MB limit
- **Swagger/OpenAPI** — API documentation with Scalar UI at `/scalar/v1`
- **Azure Monitor alerts** — container restart, 5xx error rate, and DB CPU alerts via Bicep modules
- **Trivy container scanning** — Docker image vulnerability scanning in CI and CD pipelines (advisory mode)
- **VAPID key rotation** — push notification keys rotated to Azure Key Vault secrets
- **Client-side reporting** — `ReportModal` for users/messages/servers with rate-limit feedback; context menu on members, action bar button on messages
- **Client-side announcements** — dismissible `AnnouncementBanner` with `localStorage`-persisted dismiss state; fetched on sign-in via `GET /announcements/active`
- **Quarantine enforcement** — quarantined servers reject invite joins with HTTP 403
- **Admin live activity chart** — Chart.js dual-axis line chart on admin dashboard showing rolling messages/min and active connections from SignalR feed
- **Security hardening** — SAML XML signature validation strengthened; OAuth redirect URI validation; webhook URL validation; input length validation on all request DTOs; `[param:]` target on record primary constructor validation attributes
- **Discord server import** — full Discord server migration via bot token; imports roles, categories, channels, permission overrides, emojis, members, and full message history with attachments and replies; 4-step import wizard in server settings; background worker with `Channel<T>` queue and parallel channel imports (4 concurrent); Discord API rate limit handling; identity claiming for Discord members to link imported messages; re-sync for pulling new messages after initial import; real-time progress via SignalR (`ImportProgress`, `ImportCompleted`, `ImportFailed`); bot token encrypted at rest via Data Protection; emojis and image attachments re-hosted to Codec storage after text import via `DiscordMediaRehostService` (SkiaSharp) with a `RehostingMedia` status phase
- **Multi-role support** — `ServerMemberRoles` join table replaces single `RoleId` on `ServerMember`; members can hold multiple roles simultaneously; permissions OR-merged across all roles; new add/remove/replace endpoints in `RolesController`; `PermissionResolverService` handles OR-merge and channel-level resolution; server Owners and Administrators bypass all checks
- **Per-channel permission overrides** — `ChannelPermissionOverrides` table with `(ChannelId, RoleId, Allow, Deny)` rows; deny-wins model; `GET/PUT/DELETE /channels/{id}/overrides/{roleId}` endpoints in `ChannelsController`; `ChannelPermissions` Svelte component for three-state (allow/neutral/deny) override editing in server settings; `ChannelPermissionsChanged` SignalR event refreshes channel list on change
- **Account deletion** — users can permanently delete their own accounts via `DELETE /me`; requires password re-authentication (or Google re-auth) and typed "DELETE" confirmation; server ownership must be transferred first; messages anonymized (AuthorUserId nulled, shown as "Deleted User"); friendships, reactions, and memberships cascade-deleted; `AccountDeleted` SignalR event forces all sessions to sign out; EF migration makes 6 FK columns nullable (CustomEmoji, Webhook, ServerInvite, SystemAnnouncement, Report, BannedMember) with SetNull behavior; `DeleteAccountModal` confirmation UI in Account Settings
- **Invite landing page** — `/invite/[code]` route replaces 404 for invite links; authenticated users see server preview (name, icon, member count) via `GET /invites/{code}` and explicitly accept; unauthenticated users see a generic landing page, sign in, and are redirected back to the invite page to preview and accept; all endpoints remain authenticated
- **Date separators and smart timestamps** — horizontal date dividers between messages from different calendar days in both channel and DM feeds ("Today", "Yesterday", or full date); context-aware message timestamps (time only for today, "Yesterday at …" for yesterday, full date for older); message grouping resets at day boundaries; `formatMessageTimestamp`, `formatDateSeparator`, and `isDifferentDay` helpers in `format.ts`

## Task breakdown: Session Persistence

### Web – Persist login across reloads
- [x] Store Google ID token in `localStorage` on sign-in
- [x] Restore token from `localStorage` on page load (if not expired)
- [x] Enforce 1-week maximum session duration
- [x] Client-side JWT expiration check (with 60-second buffer)
- [x] Enable Google One Tap `auto_select` for silent token refresh
- [x] Call `google.accounts.id.prompt()` to trigger silent re-auth when stored token is expired
- [x] `clearSession()` helper to wipe stored credentials on session expiry

### Documentation
- [x] Update PLAN.md with session persistence task breakdown
- [x] Update FEATURES.md to mark session persistence as implemented
- [x] Update README.md features list
- [x] Update AUTH.md with session persistence details
- [x] Update ARCHITECTURE.md authentication flow

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
- [x] Dark color scheme with CSS custom properties (CODEC CRT phosphor-green palette)
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

## Task breakdown: Front-End Architecture Refactoring

### Module extraction
- [x] Create `$lib/types/models.ts` — shared TypeScript interfaces for domain models
- [x] Create `$lib/utils/format.ts` — pure utility functions (date/time formatting)
- [x] Create `$lib/api/client.ts` — typed `ApiClient` class with `ApiError`
- [x] Create `$lib/auth/session.ts` — token persistence, expiration checking, session management
- [x] Create `$lib/auth/google.ts` — Google Identity Services SDK initialization wrapper
- [x] Create `$lib/services/chat-hub.ts` — `ChatHubService` for SignalR hub connection lifecycle
- [x] Create `$lib/state/app-state.svelte.ts` — central `AppState` class with `$state`/`$derived` runes and context-based DI _(later decomposed into 8 domain stores: UIStore, AuthStore, ServerStore, ChannelStore, MessageStore, DmStore, FriendStore, VoiceStore)_

### Styles extraction
- [x] Create `$lib/styles/tokens.css` — CSS custom properties (CODEC CRT design tokens)
- [x] Create `$lib/styles/global.css` — base styles, resets, font imports
- [x] Import `global.css` in `+layout.svelte` with font preconnect links

### Component extraction
- [x] Create `ServerSidebar.svelte` — server icon rail with create/join-via-invite
- [x] Create `ChannelSidebar.svelte` — channel list with create form
- [x] Create `UserPanel.svelte` — user avatar/name/role, sign-out button, Google sign-in button
- [x] Create `ChatArea.svelte` — chat shell (header, error banner, message feed, typing indicator, composer)
- [x] Create `MessageFeed.svelte` — scrollable message list with grouping logic
- [x] Create `MessageItem.svelte` — single message (grouped/ungrouped variants)
- [x] Create `Composer.svelte` — message input with send button
- [x] Create `TypingIndicator.svelte` — animated typing dots with user names
- [x] Create `MembersSidebar.svelte` — members grouped by role using `$derived`
- [x] Create `MemberItem.svelte` — single member card with avatar

### Page rewrite and verification
- [x] Rewrite `+page.svelte` as thin composition shell (~75 lines)
- [x] Update `$lib/index.ts` barrel exports
- [x] Verify `npm run build` succeeds (0 errors)
- [x] Verify `svelte-check` passes (0 errors, 0 warnings)

### Documentation
- [x] Update ARCHITECTURE.md with frontend architecture details
- [x] Update web/README.md with architecture overview
- [x] Update FEATURES.md with frontend architecture features
- [x] Update AUTH.md with modular code references and sign-out status
- [x] Update DESIGN.md implementation notes
- [x] Update PLAN.md with refactoring milestone

## Task breakdown: Emoji Reactions

### API – Reaction model & database
- [x] Create `Reaction` entity (Id, MessageId, UserId, Emoji, CreatedAt)
- [x] Create `ToggleReactionRequest` DTO record
- [x] Add `Reactions` DbSet to `CodecDbContext`
- [x] Configure Reaction→Message and Reaction→User relationships
- [x] Add unique index on (MessageId, UserId, Emoji)
- [x] Create and apply EF Core migration (`AddReactions`)

### API – Toggle endpoint & SignalR broadcast
- [x] Add `POST /channels/{channelId}/messages/{messageId}/reactions` endpoint
- [x] Validate server membership before allowing reaction toggle
- [x] Toggle logic: add if not present, remove if already exists
- [x] Return grouped reaction summary (emoji, count, userIds)
- [x] Broadcast `ReactionUpdated` event to channel group via SignalR
- [x] Include reactions in `GetMessages` response
- [x] Include empty reactions array in `PostMessage` broadcast payload

### Web – Types, API client & SignalR
- [x] Add `Reaction` type to `models.ts` (emoji, count, userIds)
- [x] Add `reactions: Reaction[]` to `Message` type
- [x] Add `toggleReaction()` method to `ApiClient`
- [x] Add `ReactionUpdate` type and `onReactionUpdated` callback to `ChatHubService`
- [x] Register `ReactionUpdated` handler in hub `start()` method

### Web – State & UI components
- [x] Add `toggleReaction(messageId, emoji)` action to `MessageStore`
- [x] Wire `onReactionUpdated` SignalR callback in `startSignalR()`
- [x] Create `ReactionBar.svelte` — reaction pills (emoji + count, active highlight)
- [x] Add floating action bar to `MessageItem.svelte` (react button at top-right on hover)
- [x] Add emoji picker popover (8 quick emojis, opens below button)
- [x] Integrate `ReactionBar` into `MessageItem` when reactions exist

### Documentation
- [x] Update ARCHITECTURE.md (file tree, endpoints, SignalR events, data model)
- [x] Update FEATURES.md (move reactions from Planned to Implemented)
- [x] Update DATA.md (schema diagram, entity definition, indexes, DbContext)
- [x] Update DESIGN.md (reaction UI components specification)
- [x] Update PLAN.md (current status, task breakdown)
- [x] Update README.md (features list)
- [x] Update apps/web/README.md (file tree)

## Task breakdown: Friends (see [docs/FRIENDS.md](docs/FRIENDS.md))

### API
- [x] Create `Friendship` entity and `FriendshipStatus` enum in `Models/`
- [x] Add `Friendships` DbSet to `CodecDbContext`
- [x] Configure entity relationships, unique constraint, and indexes in `OnModelCreating`
- [x] Create and apply EF Core migration (`AddFriendships`)
- [x] Create `FriendsController` with all endpoints
- [x] Add user search endpoint (`GET /users/search?q=...`) for the Add Friend flow
- [x] Add user-scoped SignalR group support to `ChatHub` (join `user-{userId}` group on connect)
- [x] Broadcast friend-related events via SignalR

### Web
- [x] Add `Friendship`, `FriendRequest`, and `FriendshipStatus` types to `models.ts`
- [x] Add friend-related API methods to `ApiClient`
- [x] Add friend-related SignalR event handlers to `ChatHubService`
- [x] Add friends state management to `FriendStore`
- [x] Create `FriendsPanel.svelte` component with tab navigation
- [x] Create `FriendsList.svelte` (All Friends tab)
- [x] Create `PendingRequests.svelte` (Pending tab)
- [x] Create `AddFriend.svelte` (Add Friend tab with search)
- [x] Wire Home icon in `ServerSidebar` to display the Friends panel

### Documentation
- [x] Update `ARCHITECTURE.md` with new endpoints and SignalR events
- [x] Update `DATA.md` with Friendship entity and schema diagram
- [x] Update `FEATURES.md` to track Friends feature progress
- [x] Update `DESIGN.md` with Friends panel UI specification
- [x] Update `PLAN.md` with Friends task breakdown

## Task breakdown: Direct Messages (see [docs/DIRECT_MESSAGES.md](docs/DIRECT_MESSAGES.md))

### API
- [x] Create `DmChannel`, `DmChannelMember`, and `DirectMessage` entities in `Models/`
- [x] Add DbSets to `CodecDbContext` and configure relationships, keys, and indexes
- [x] Create and apply EF Core migration (`AddDirectMessages`)
- [x] Create `DmController` with all endpoints (create/resume, list, send, close)
- [x] Add friendship validation — verify accepted friendship before allowing DM creation
- [x] Add DM-specific SignalR hub methods (`JoinDmChannel`, `LeaveDmChannel`, `StartDmTyping`, `StopDmTyping`)
- [x] Broadcast `ReceiveDm`, `DmTyping`, `DmStoppedTyping`, and `DmConversationOpened` events via SignalR
- [x] Re-open closed conversations when new messages are sent

### Web
- [x] Add `DmChannel`, `DmConversation`, and `DirectMessage` types to `models.ts`
- [x] Add DM-related API methods to `ApiClient`
- [x] Add DM-related SignalR event handlers to `ChatHubService`
- [x] Add DM state management to `DmStore` (conversations list, active conversation, messages)
- [x] Create `DmList.svelte` component (conversation sidebar entries)
- [x] Create `DmChatArea.svelte` wrapper (adapts `ChatArea` components for DM context)
- [x] Create `HomeSidebar.svelte` — sidebar with Friends nav + DM conversations list
- [x] Wire Home icon navigation to show DM list + Friends panel
- [x] Wire friend click in `FriendsList.svelte` to open/create DM conversation
- [x] DM-specific Composer with "Message @{displayName}" placeholder

### Documentation
- [x] Update `ARCHITECTURE.md` with DM endpoints, SignalR events, and data model
- [x] Update `DATA.md` with DM entities and schema diagram
- [x] Update `FEATURES.md` to track Direct Messages feature progress
- [x] Update `DESIGN.md` with DM UI specification
- [x] Update `PLAN.md` with DM task breakdown

### API — Data model & migration
- [x] Add `ImageUrl` nullable property to `Message` and `DirectMessage` entities
- [x] Update `CreateMessageRequest` DTO to accept optional `ImageUrl`
- [x] Create and apply EF Core migration (`AddImageUrlToMessages`)

### API — Image upload service & endpoint
- [x] Create `IImageUploadService` interface and `ImageUploadService` implementation
- [x] Validate file type (JPEG, PNG, WebP, GIF) and size (10 MB max)
- [x] Store images with SHA-256 content-hash filenames under `uploads/images/{userId}/`
- [x] Create `ImageUploadsController` with `POST /uploads/images` endpoint
- [x] Configure static file serving for uploaded images in `Program.cs`

### API — Message posting integration
- [x] Update `ChannelsController.PostMessage` to accept and persist `ImageUrl`
- [x] Update `DmController.SendMessage` to accept and persist `ImageUrl`
- [x] Allow messages with image-only (no body text required)
- [x] Include `ImageUrl` in message query responses and SignalR broadcast payloads

### Web — Types, API client & state
- [x] Add `imageUrl` field to `Message` and `DirectMessage` types in `models.ts`
- [x] Add `uploadImage()` method to `ApiClient`
- [x] Update `sendMessage()` and `sendDm()` API methods to accept optional `imageUrl`
- [x] Add image attachment state and methods to `MessageStore` (`attachImage`, `clearPendingImage`, etc.)
- [x] Add client-side file type and size validation with `ALLOWED_IMAGE_TYPES` and `MAX_IMAGE_SIZE_BYTES`

### Web — UI components
- [x] Update `Composer.svelte` with attach button (`+`), hidden file input, and clipboard paste handler
- [x] Add image preview with remove button above composer input
- [x] Update `MessageItem.svelte` to display inline images (clickable, lazy-loaded)
- [x] Update `DmChatArea.svelte` with attach, paste, preview, and image display for DMs
- [x] Add drag-and-drop image support to `ChatArea.svelte` with visual drop overlay
- [x] Add drag-and-drop image support to `DmChatArea.svelte` with visual drop overlay

### Verification
- [x] Backend builds successfully (`dotnet build`)
- [x] Frontend type-checks with zero errors (`svelte-check`)
- [x] UI renders correctly with attach button visible in composer

## Task breakdown: Message Replies (see [docs/REPLIES.md](docs/REPLIES.md))

### API — Data model & migration
- [x] Add `ReplyToMessageId` nullable FK to `Message` entity (self-referencing, ON DELETE SET NULL)
- [x] Add `ReplyToDirectMessageId` nullable FK to `DirectMessage` entity (self-referencing, ON DELETE SET NULL)
- [x] Update `CreateMessageRequest` DTO to accept optional reply IDs
- [x] Create and apply EF Core migration (`AddMessageReplies`)

### API — Reply context in retrieval
- [x] Update `ChannelsController.GetMessages` to batch-load parent messages and include `ReplyContext` DTO
- [x] Update `DmController.GetMessages` to batch-load parent DMs and include `ReplyContext` DTO
- [x] `ReplyContextDto` includes messageId, authorName, authorAvatarUrl, authorUserId, bodyPreview (max 100 chars), isDeleted

### API — Reply support in posting
- [x] Validate reply target in `ChannelsController.PostMessage` (existence + same-channel check, 400 on failure)
- [x] Validate reply target in `DmController.SendMessage` (existence + same-DM-channel check, 400 on failure)
- [x] Include `replyContext` in SignalR broadcast payloads for both channel and DM messages

### Web — Types, API client & state
- [x] Add `ReplyContext` type to `models.ts` (messageId, authorName, authorAvatarUrl, authorUserId, bodyPreview, isDeleted)
- [x] Add `replyContext` field to `Message` and `DirectMessage` types
- [x] Update `sendMessage()` and `sendDm()` API methods to accept optional reply ID parameters
- [x] Add `replyingTo` reactive state to `MessageStore` with `startReply()` and `cancelReply()` methods
- [x] Clear reply state on channel/DM switch and sign-out
- [x] Wire `replyContext` into SignalR message callbacks

### Web — UI components
- [x] Create `ReplyReference.svelte` — compact clickable bar above message body (avatar, author, preview, deleted state)
- [x] Create `ReplyComposerBar.svelte` — "Replying to {author}" banner above composer with cancel button
- [x] Update `MessageItem.svelte` — reply button in floating action bar, `ReplyReference` display for replies
- [x] Update `MessageFeed.svelte` — `scrollToMessage()` with highlight animation, `data-message-id` attributes
- [x] Update `Composer.svelte` — integrate `ReplyComposerBar`, Escape key cancels reply
- [x] Update `DmChatArea.svelte` — full reply support (reply button, `ReplyReference`, `ReplyComposerBar`, scroll-to-message, Escape key)

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors)
- [x] Frontend type-checks with zero errors (`svelte-check`)

## Task breakdown: Image Preview Lightbox

### Web — State
- [x] Add `lightboxImageUrl` reactive state to `UIStore`
- [x] Add `openImagePreview(url)` and `closeImagePreview()` methods to `UIStore`
- [x] Clear lightbox state on sign-out

### Web — UI components
- [x] Create `ImagePreview.svelte` — full-screen `<dialog>` lightbox with backdrop, toolbar (open-original, close), Escape to close
- [x] Update `MessageItem.svelte` — replace `<a>` tag with `<button>` that opens lightbox (both grouped and ungrouped)
- [x] Update `DmChatArea.svelte` — replace `<a>` tags with `<button>` that opens lightbox (both grouped and ungrouped)
- [x] Mount `ImagePreview` in `+page.svelte` (app-level, renders above all content)
- [x] Add hover opacity transition on image thumbnails for visual feedback

### Verification
- [x] Frontend type-checks with zero errors (`svelte-check`)

## Task breakdown: Text Formatting (Bold & Italic)

### Web — Parsing & rendering
- [x] Add `FORMAT_REGEX` to `LinkifiedText.svelte` for `**bold**`, `*bold*`, and `_italic_` markers
- [x] Two-pass parsing: first extract links/mentions, then parse text segments for formatting
- [x] Render bold as `<strong>` and italic as `<em>` with scoped styles
- [x] Formatting applies to both channel messages and DMs via shared `LinkifiedText` component

### Web — Composer live preview
- [x] Create `ComposerOverlay.svelte` — transparent overlay that mirrors composer text with formatting applied
- [x] Overlay technique: input text rendered transparent with visible caret, formatted overlay positioned behind
- [x] Integrate overlay into `Composer.svelte` (channel composer)
- [x] Integrate overlay into `DmChatArea.svelte` (DM composer)
- [x] Scroll sync between input and overlay for long messages

### Verification
- [x] Frontend builds successfully (`npm run build`, 0 errors)

## Task breakdown: Loading Screen

### Web — State
- [x] Add `isInitialLoading = $state(true)` flag to `UIStore`
- [x] Make `handleCredential()` async — await `loadMe()`, `loadServers()`, and `startSignalR()` in parallel via `Promise.all`, then set `isInitialLoading = false`
- [x] Set `isInitialLoading = false` in `init()` when no stored session exists (sign-in UI path)
- [x] Reset `isInitialLoading = true` on `signOut()` for next login cycle
- [x] Set `isInitialLoading = false` on env-var error early returns in `onMount`

### Web — UI component
- [x] Create `LoadingScreen.svelte` — full-screen branded splash with CRT phosphor-green theme
- [x] Animated `[CODEC]` logo with glow keyframes
- [x] Sliding progress bar with accent color
- [x] "Initializing..." status text with animated dots
- [x] CRT scanline overlay (repeating gradient)
- [x] `transition:fade` for smooth exit
- [x] Respects `prefers-reduced-motion` media query

### Web — Page integration
- [x] Show `<LoadingScreen />` when `app.isSignedIn && app.isInitialLoading`
- [x] Gate app shell, settings modal, and image preview behind `{#if !app.isInitialLoading}`

### Verification
- [x] Frontend builds successfully (`npm run build`, 0 errors)

## Task breakdown: Real-Time Member List Updates

### API — SignalR server groups
- [x] Add `server-{serverId}` group concept to `ChatHub`
- [x] Auto-join all server groups on connect (`OnConnectedAsync` queries `ServerMembers`)
- [x] Add `JoinServer(serverId)` hub method for joining a server group after invite join
- [x] Add `LeaveServer(serverId)` hub method for leaving a server group after kick
- [x] Broadcast `MemberJoined` event from `ServersController.JoinViaInvite`
- [x] Broadcast `MemberLeft` event from `ServersController.KickMember`

### Web — SignalR events & state
- [x] Add `MemberJoinedEvent` and `MemberLeftEvent` types to `chat-hub.ts`
- [x] Add `onMemberJoined` and `onMemberLeft` callbacks to `SignalRCallbacks`
- [x] Register `MemberJoined` and `MemberLeft` handlers in `ChatHubService.start()`
- [x] Add `joinServer(serverId)` and `leaveServer(serverId)` methods to `ChatHubService`
- [x] Wire `onMemberJoined` callback in SignalR orchestration to reload member list
- [x] Wire `onMemberLeft` callback in SignalR orchestration to reload member list
- [x] Call `hub.joinServer()` after `joinViaInvite` succeeds
- [x] Call `hub.leaveServer()` in `onKickedFromServer` handler

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors)
- [x] Frontend type-checks with zero errors (`svelte-check`)

## Task breakdown: Alpha Notification & Bug Reporting

### GitHub — Issue template
- [x] Create `.github/ISSUE_TEMPLATE/bug-report.yml` structured bug report template
- [x] Include fields: description, steps to reproduce, expected/actual behavior, screenshots, browser, device type
- [x] Auto-label issues with `bug` and `alpha-tester` labels

### Web — Alpha notification banner
- [x] Add `showAlphaNotification` flag to `UIStore` (set `true` at end of auth flow)
- [x] Add `dismissAlphaNotification()` method to `UIStore`
- [x] Create `AlphaNotification.svelte` modal overlay component
- [x] Display ALPHA badge, welcome message, and bug reporting guidance
- [x] Link directly to GitHub bug report template (`/issues/new?template=bug-report.yml`)
- [x] Dismissable via "Got it" button or Escape key
- [x] Styled with existing CRT phosphor-green design tokens
- [x] Mount in `+page.svelte` alongside other overlays

### Documentation
- [x] Update PLAN.md with alpha notification task breakdown
- [x] Update FEATURES.md with alpha notification feature
- [x] Update README.md with alpha notification and bug reporting details

## Task breakdown: Message Deletion

### API — Channel message deletion
- [x] Add `DELETE /channels/{channelId}/messages/{messageId}` endpoint to `ChannelsController`
- [x] Verify server membership before allowing deletion
- [x] Verify message ownership — only the author can delete their own message (403 otherwise)
- [x] Cascade-delete associated reactions and link previews via EF Core relationship configuration
- [x] Replies referencing deleted message have `ReplyToMessageId` set to `null` automatically (ON DELETE SET NULL)
- [x] Broadcast `MessageDeleted { messageId, channelId }` via SignalR to channel group

### API — DM message deletion
- [x] Add `DELETE /dm/channels/{channelId}/messages/{messageId}` endpoint to `DmController`
- [x] Verify DM channel membership before allowing deletion
- [x] Verify message ownership — only the author can delete their own message (403 otherwise)
- [x] Cascade-delete associated link previews via EF Core relationship configuration
- [x] Replies referencing deleted DM have `ReplyToDirectMessageId` set to `null` automatically (ON DELETE SET NULL)
- [x] Broadcast `DmMessageDeleted { messageId, dmChannelId }` via SignalR to DM channel group + other participant's user group

### Web — Types, API client & SignalR
- [x] Add `MessageDeletedEvent` and `DmMessageDeletedEvent` types to `chat-hub.ts`
- [x] Add `onMessageDeleted` and `onDmMessageDeleted` callbacks to `SignalRCallbacks`
- [x] Register `MessageDeleted` and `DmMessageDeleted` handlers in `ChatHubService.start()`
- [x] Add `deleteMessage()` and `deleteDmMessage()` methods to `ApiClient`

### Web — State & UI
- [x] Add `deleteMessage(messageId)` action to `MessageStore` — calls API, falls back to local removal if SignalR disconnected
- [x] Add `deleteDmMessage(messageId)` action to `DmStore` — calls API, falls back to local removal if SignalR disconnected
- [x] Wire `onMessageDeleted` SignalR callback in `startSignalR()` to filter from `messages` array
- [x] Wire `onDmMessageDeleted` SignalR callback in `startSignalR()` to filter from `dmMessages` array
- [x] Add delete button (trash icon) to `MessageItem.svelte` floating action bar — visible only on own messages, red hover state
- [x] Add delete button (trash icon) to `DmChatArea.svelte` action bar — visible only on own messages, red hover state

### Documentation
- [x] Update `ARCHITECTURE.md` with DELETE endpoints and new SignalR events
- [x] Update `FEATURES.md` to mark message deletion as implemented for channels and DMs
- [x] Update `DIRECT_MESSAGES.md` to mark message deletion as implemented (was deferred)
- [x] Update `README.md` features list
- [x] Update `PLAN.md` with message deletion task breakdown

## Task breakdown: Message Editing

### API — Data model & migration
- [x] Add `EditedAt` nullable `DateTimeOffset` property to `Message` and `DirectMessage` entities
- [x] Create `EditMessageRequest` DTO record (`Body` string)
- [x] Create and apply EF Core migration (`AddEditedAt`)

### API — Edit endpoints
- [x] Add `PUT /channels/{channelId}/messages/{messageId}` endpoint to `ChannelsController`
- [x] Verify server membership and message ownership before allowing edit (403 otherwise)
- [x] Update message body and set `EditedAt` timestamp
- [x] Broadcast `MessageEdited { messageId, channelId, body, editedAt }` via SignalR to channel group
- [x] Include `EditedAt` in all GET/POST message projections
- [x] Add `PUT /dm/channels/{channelId}/messages/{messageId}` endpoint to `DmController`
- [x] Verify DM channel membership and message ownership before allowing edit (403 otherwise)
- [x] Broadcast `DmMessageEdited { messageId, dmChannelId, body, editedAt }` via SignalR to DM group + other participant's user group
- [x] Include `EditedAt` in all DM GET/POST message projections

### Web — Types, API client & SignalR
- [x] Add `editedAt` optional field to `Message` and `DirectMessage` types in `models.ts`
- [x] Add `editMessage()` and `editDmMessage()` methods to `ApiClient`
- [x] Add `MessageEditedEvent` and `DmMessageEditedEvent` types to `chat-hub.ts`
- [x] Add `onMessageEdited` and `onDmMessageEdited` callbacks to `SignalRCallbacks`
- [x] Register `MessageEdited` and `DmMessageEdited` handlers in `ChatHubService.start()`

### Web — State & UI
- [x] Add `editMessage(messageId, newBody)` action to `MessageStore` — calls API, falls back to local update if SignalR disconnected
- [x] Add `editDmMessage(messageId, newBody)` action to `DmStore` — calls API, falls back to local update if SignalR disconnected
- [x] Wire `onMessageEdited` SignalR callback to update `messages` array (body + editedAt)
- [x] Wire `onDmMessageEdited` SignalR callback to update `dmMessages` array (body + editedAt)
- [x] Add edit button (pencil icon) to `MessageItem.svelte` floating action bar — visible only on own messages
- [x] Add inline edit mode to `MessageItem.svelte` — textarea replaces message body, Enter to save, Escape to cancel
- [x] Add "(edited)" label next to timestamp on edited messages in `MessageItem.svelte`
- [x] Add edit button, inline edit mode, and "(edited)" label to `DmChatArea.svelte` for DM messages

### Documentation
- [x] Update `FEATURES.md` to mark message editing as implemented for channels and DMs
- [x] Update `README.md` features list
- [x] Update `PLAN.md` with message editing task breakdown

## Task breakdown: Progressive Message Loading

### API — Cursor-based pagination
- [x] Add `before` (DateTimeOffset) and `limit` (int) query parameters to `GET /channels/{channelId}/messages`
- [x] Clamp `limit` to 1–200 range with default of 100
- [x] Filter messages by `CreatedAt < before` when cursor is provided
- [x] Fetch `limit + 1` rows to determine `hasMore` flag
- [x] Return `{ hasMore, messages }` response instead of flat message array

### Web — Types & API client
- [x] Add `PaginatedMessages` type to `models.ts` (`{ hasMore: boolean; messages: Message[] }`)
- [x] Export `PaginatedMessages` from barrel index
- [x] Update `getMessages()` in `ApiClient` to accept optional `{ before?, limit? }` options and return `PaginatedMessages`

### Web — State management
- [x] Add `hasMoreMessages` and `isLoadingOlderMessages` reactive state fields to `MessageStore`
- [x] Update `loadMessages()` to use paginated response and set `hasMoreMessages`
- [x] Add `loadOlderMessages()` method — uses oldest message timestamp as cursor, prepends results
- [x] Reset `hasMoreMessages` on sign-out, goHome, kicked, and channel deselection

### Web — Scroll behavior
- [x] Add `TOP_THRESHOLD` constant (200px) for scroll-near-top detection in `MessageFeed.svelte`
- [x] Detect scroll near top in `handleScroll()` and trigger older message loading
- [x] Implement `loadOlderAndPreserveScroll()` — captures `scrollHeight`, loads messages, preserves scroll position via `tick()`
- [x] Sync `previousMessageCount` after prepending to prevent false unread badge
- [x] Guard scroll restoration with `isAutoScrolling` flag to prevent re-trigger
- [x] Add "Loading older messages…" indicator at top of feed

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors)
- [x] Frontend type-checks with zero errors (`svelte-check`)

## Task breakdown: YouTube Video Embeds

### Web — YouTube URL detection utility
- [x] Create `$lib/utils/youtube.ts` with `extractYouTubeVideoId(url)` — regex-based parser supporting `/watch?v=`, `/embed/`, `/shorts/`, `/live/`, and `youtu.be/` URL formats
- [x] Add `youTubeEmbedUrl(videoId)` helper — removed; embed handled by `svelte-youtube-embed` package

### Web — YouTubeEmbed component
- [x] Create `YouTubeEmbed.svelte` — wraps `svelte-youtube-embed` package for click-to-play YouTube embeds
- [x] Package fetches video title via YouTube oEmbed API and displays it on the thumbnail
- [x] Click-to-play pattern: shows thumbnail with play button, loads iframe only on user interaction
- [x] Privacy: uses `youtube-nocookie.com` domain for iframe embeds
- [x] Security: CSP `frame-src` allows `youtube-nocookie.com`, `connect-src` allows `youtube.com` for oEmbed
- [x] Client-side YouTube URL detection from message body (independent of backend link preview system)

### Web — Integration into LinkPreviewCard
- [x] Update `LinkPreviewCard.svelte` to detect YouTube URLs via `extractYouTubeVideoId()`
- [x] YouTube links render as `YouTubeEmbed` instead of the standard link preview card
- [x] Non-YouTube links continue to render as standard link preview cards (no behavior change)
- [x] Works in both server channels and DMs automatically (shared `LinkPreviewCard` component)

## Task breakdown: Performance Optimizations

### API — Response compression
- [x] Add Brotli and Gzip response compression middleware to `Program.cs`
- [x] Configure `CompressionLevel.Fastest` for minimal latency impact
- [x] Include `application/json` in compressible MIME types
- [x] Place `UseResponseCompression()` before `UseCors()` in middleware pipeline

### API — Skip unnecessary database writes
- [x] Update `UserService.GetOrCreateUserAsync` to compare incoming Google profile fields (DisplayName, Email, AvatarUrl) against stored values
- [x] Skip `SaveChangesAsync` when no fields have actually changed — eliminates redundant writes on every authenticated request

### API — Cached mention parsing
- [x] Cache mention regex parsing results in `ChannelsController.GetMessages` using `ToDictionary` lookup
- [x] Eliminates double regex execution per message (was parsing once for query, once for projection)

### API — DM pagination `hasMore` flag
- [x] Apply `limit + 1` fetch pattern to `DmController.GetMessages` (matching existing channel pagination)
- [x] Return `{ hasMore, messages }` response shape instead of flat `DirectMessage[]` array
- [x] Add `PaginatedDmMessages` type to frontend (`models.ts` + barrel export)
- [x] Update `ApiClient.getDmMessages` return type to `Promise<PaginatedDmMessages>`
- [x] Update `DmStore.loadDmMessages` to destructure paginated response

## Task breakdown: Redis Cache & SignalR Backplane

### Infrastructure — Redis container
- [x] Add `redis:8-alpine` to `docker-compose.yml` with healthcheck
- [x] Update API `depends_on` to include Redis

### API — NuGet packages and configuration
- [x] Add `Microsoft.Extensions.Caching.StackExchangeRedis` and `Microsoft.AspNetCore.SignalR.StackExchangeRedis`
- [x] Add `AspNetCore.HealthChecks.Redis` for health endpoint
- [x] Add `Redis:ConnectionString` to `appsettings.json`

### API — Redis registration in Program.cs
- [x] Register `IDistributedCache` via `AddStackExchangeRedisCache` (conditional on config)
- [x] Register `IConnectionMultiplexer` singleton with `abortConnect=false` for graceful degradation
- [x] Add SignalR Redis backplane via `AddStackExchangeRedis` with `codec` channel prefix
- [x] Add Redis health check to readiness probe

### API — MessageCacheService
- [x] Create `Services/MessageCacheService.cs` with `GetMessagesAsync`, `SetMessagesAsync`, `InvalidateChannelAsync`
- [x] Channel-level invalidation via Redis tracking sets (bulk key deletion)
- [x] 5-minute TTL with graceful degradation (try/catch on all Redis operations)
- [x] Register as singleton (handles null dependencies when Redis is unavailable)

### API — Cache integration in ChannelsController
- [x] Cache-first read in `GetMessages` for paginated history pages (`before` cursor only)
- [x] Skip caching "latest" page (no `before`) to avoid write amplification
- [x] Cache write with pre-serialized JSON for zero double-serialization
- [x] Awaited invalidation on PostMessage, DeleteMessage, EditMessage, PurgeChannel, ToggleReaction
- [x] Invalidation in link preview background task after `SaveChangesAsync`
- [x] Invalidation in `ServersController.DeleteChannel` for orphaned cache cleanup

## Task breakdown: Connection Status Awareness

### Web — SignalR reconnection callbacks
- [x] Add `onReconnecting`, `onReconnected`, and `onClose` callbacks to `SignalRCallbacks` type in `chat-hub.ts`
- [x] Wire `connection.onreconnecting()`, `connection.onreconnected()`, and `connection.onclose()` handlers in `ChatHubService.start()`

### Web — Hub connection state
- [x] Add `isHubConnected = $state(false)` reactive field to `UIStore`
- [x] Set `isHubConnected` to `true` after successful `hub.start()` and on `onReconnected`
- [x] Set `isHubConnected` to `false` on `signOut()`, `onReconnecting`, and `onClose`
- [x] Graceful reconnection — hub rebuilds connection with exponential backoff (2 s → 30 s cap) when automatic reconnect attempts are exhausted; visibility change triggers immediate retry when tab becomes active
- [x] Inactive tab tolerance — server KeepAliveInterval (30 s) and ClientTimeoutInterval (90 s) prevent false disconnects from throttled WebSocket pings

### Web — Composer disconnected state
- [x] Update `Composer.svelte` — when `!app.isHubConnected`, show "Codec connecting..." with animated CSS ellipsis instead of composer input
- [x] Update `DmChatArea.svelte` — same disconnected state treatment for DM composer
- [x] Add `.composer-disconnected`, `.connecting-message`, and `.animated-ellipsis` CSS styles

## Task breakdown: Miscellaneous Fixes

- [x] Add `<link rel="icon" href="%sveltekit.assets%/favicon.ico" />` to `app.html`
- [x] Change YouTube embed border-left color from `var(--accent)` to `var(--danger)` in `YouTubeEmbed.svelte`

## Task breakdown: Global Admin

### API — Data model & migration
- [x] Add `IsGlobalAdmin` boolean property to `User` entity (default `false`)
- [x] Create and apply EF Core migration (`AddGlobalAdminRole`)

### API — Server deletion endpoint
- [x] Add `DELETE /servers/{serverId}` endpoint to `ServersController`
- [x] Require Owner or global admin role (return 403 otherwise)
- [x] Cascade-delete all related data (messages, reactions, link previews, channels, members, invites)
- [x] Broadcast `ServerDeleted { serverId }` via SignalR to server group

### API — Channel deletion endpoint
- [x] Add `DELETE /servers/{serverId}/channels/{channelId}` endpoint to `ServersController`
- [x] Allow server Owner, Admin, or global admin (return 403 otherwise)
- [x] Cascade-delete all channel messages, reactions, and link previews
- [x] Broadcast `ChannelDeleted { serverId, channelId }` via SignalR to server group

### API — Message deletion bypass
- [x] Update `DELETE /channels/{channelId}/messages/{messageId}` to allow global admin to delete any message (bypass author-only check)

### API — Kick bypass
- [x] Update `DELETE /servers/{serverId}/members/{userId}` to allow global admin to kick any non-Owner member (bypass membership/role check)

### API — User profile
- [x] Include `isGlobalAdmin` in `GET /me` response

### API — Seed data & configuration
- [x] Add `EnsureGlobalAdminAsync(db, email)` method to `SeedData`
- [x] Read `GlobalAdmin:Email` from configuration in `Program.cs`
- [x] Add `GlobalAdmin:Email` to `appsettings.json` (empty default) and `appsettings.Development.json`

### Web — Types & API client
- [x] Add `isGlobalAdmin` optional boolean to `UserProfile` type
- [x] Add `deleteServer(serverId)` and `deleteChannel(serverId, channelId)` methods to `ApiClient`

### Web — State management
- [x] Add `isGlobalAdmin` derived state to `AuthStore`
- [x] Add `canDeleteServer` and `canDeleteChannel` derived properties
- [x] Add `canKickMembers` derived property (includes global admin)
- [x] Add `deleteServer(serverId)` and `deleteChannel(serverId, channelId)` actions
- [x] Add `onServerDeleted` and `onChannelDeleted` SignalR handlers

### Web — UI
- [x] Update `MessageItem.svelte` — delete button visible for own messages or global admin; edit restricted to own messages only
- [x] Update `ServerSettings.svelte` — add danger zone with delete server button (Owner or global admin) and delete channel buttons (Owner/Admin/global admin) with confirmation dialogs

### SignalR
- [x] Add `ServerDeletedEvent` and `ChannelDeletedEvent` types to `chat-hub.ts`
- [x] Register `ServerDeleted` and `ChannelDeleted` handlers in `ChatHubService.start()`

### Infrastructure & CI/CD
- [x] Add `GlobalAdmin--Email` Key Vault secret provisioned via Bicep (`key-vault-secret` module in `main.bicep`)
- [x] Add `GlobalAdmin__Email` environment variable to API container app (unconditional in `container-app-api.bicep`)
- [x] Add `globalAdminEmail` parameter to `infra.yml` Bicep deployment commands
- [x] Add `GLOBAL_ADMIN_EMAIL` GitHub Actions repository secret

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors)
- [x] Frontend type-checks with zero errors (`svelte-check`, 0 errors)

## Task breakdown: Global Admin — Full Server Access

### API — Server access bypasses
- [x] Update `GET /servers` to return all servers for global admin (with `role = null` for non-member servers)
- [x] Update `GET /servers/{serverId}/channels` to bypass membership check for global admin
- [x] Update `GET /servers/{serverId}/members` to bypass membership check for global admin
- [x] Update `PATCH /servers/{serverId}` to bypass Owner/Admin role check for global admin
- [x] Update `POST /servers/{serverId}/channels` to bypass Owner/Admin role check for global admin
- [x] Update `PATCH /servers/{serverId}/channels/{channelId}` to bypass Owner/Admin role check for global admin
- [x] Update `POST /servers/{serverId}/invites` to bypass Owner/Admin role check for global admin
- [x] Update `GET /servers/{serverId}/invites` to bypass Owner/Admin role check for global admin
- [x] Update `DELETE /servers/{serverId}/invites/{inviteId}` to bypass Owner/Admin role check for global admin

### API — Channel messaging bypasses
- [x] Update `GET /channels/{channelId}/messages` to bypass membership check for global admin
- [x] Update `POST /channels/{channelId}/messages` to bypass membership check for global admin
- [x] Update `DELETE /channels/{channelId}/messages/{messageId}` membership bypass (already had author bypass)
- [x] Update `PUT /channels/{channelId}/messages/{messageId}` to bypass membership check for global admin
- [x] Update `POST /channels/{channelId}/messages/{messageId}/reactions` to bypass membership check for global admin

### API — SignalR
- [x] Update `ChatHub.OnConnectedAsync` to join all server groups for global admin (not just member servers)

### Web — Frontend
- [x] Update `MemberServer.role` type from `string` to `string | null` to accommodate non-member servers
- [x] Update `canManageChannels` derived state to include `isGlobalAdmin`
- [x] Update `canManageInvites` derived state to include `isGlobalAdmin`

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors, 0 warnings)
- [x] Frontend type-checks with zero errors (`svelte-check`)

### Documentation
- [x] Update FEATURES.md with expanded global admin capabilities
- [x] Update ARCHITECTURE.md endpoint descriptions (membership/role requirements)
- [x] Update SERVER_SETTINGS.md permissions descriptions
- [x] Update PLAN.md with task breakdown
- [x] Update README.md global admin description

## Task breakdown: Progressive Web App (PWA)

### Web — Plugin & configuration
- [x] Install `@vite-pwa/sveltekit` and `workbox-window` dev dependencies
- [x] Configure `SvelteKitPWA` plugin in `vite.config.ts` with `registerType: 'prompt'`
- [x] Set web app manifest: name, short_name, theme_color, background_color, display: standalone
- [x] Configure Workbox `generateSW` strategy with `globPatterns` for client assets (HTML, JS, CSS, images)
- [x] Disable SvelteKit built-in service worker registration in `svelte.config.js`
- [x] Add `worker-src: ['self']` to CSP directives in `svelte.config.js`

### Web — Icons
- [x] Generate `pwa-192x192.png` from existing `favicon.ico` (256x256 frame)
- [x] Generate `pwa-512x512.png` from existing `favicon.ico` (256x256 frame)
- [x] Generate `apple-touch-icon.png` (180x180) from existing `favicon.ico`
- [x] Add `<link rel="apple-touch-icon">` to `app.html`
- [x] Configure icon entries in manifest (192x192 any, 512x512 any, 512x512 maskable)

### Web — Type declarations & manifest injection
- [x] Add `/// <reference types="vite-plugin-pwa/svelte" />` and `vite-plugin-pwa/info` to `app.d.ts`
- [x] Import `pwaInfo` in `+layout.svelte` and inject web manifest link via `{@html}`

### Web — Update prompt UI
- [x] Create `ReloadPrompt.svelte` component with CRT phosphor-green styling
- [x] User-prompted update flow: toast with "Reload" and "Close" buttons
- [x] Periodic service worker update check (hourly via `setInterval`)
- [x] Mount `ReloadPrompt` in `+layout.svelte`

### Web — PWA enhancements (PWABuilder compliance)
- [x] Add `offline.html` fallback page with branded "You're offline" experience and retry button
- [x] Configure `navigateFallback` and `navigateFallbackDenylist` in Workbox config
- [x] Add desktop (1280x720) and mobile (390x1024) screenshots to manifest
- [x] Add `display_override` stack (`standalone` → `minimal-ui`)
- [x] Add manifest fields: `id`, `lang`, `dir`, `orientation`, `categories`
- [x] Add app shortcut: "Direct Messages" for quick OS launcher access
- [x] Add `share_target` for receiving shared links/text from OS share sheet
- [x] Add `protocol_handlers` for `web+codec://` custom protocol
- [x] Add `launch_handler` (`navigate-existing`) and `edge_side_panel` support
- [x] Add runtime caching for Google Fonts (CacheFirst, 1-year TTL)
- [x] Add Google Fonts origins to CSP `connect-src` in `hooks.server.ts`
- [x] Set `prefer_related_applications: false` and `handle_links: 'preferred'`

### Verification
- [x] Frontend builds successfully (`npm run build`, PWA v1.2.0, precache 31 entries)
- [x] PWABuilder action items addressed (manifest, service worker, app capabilities)

## Task breakdown: Voice Channels — Phase 1 (see [docs/VOICE.md](docs/VOICE.md))

### Data model & migration
- [x] Add `ChannelType` enum (`Text = 0`, `Voice = 1`) to `Models/`
- [x] Add `ChannelType` property to `Channel` entity (default `Text`)
- [x] Create `VoiceState` entity (`Id`, `UserId`, `ChannelId`, `ParticipantId`, `ConnectionId`, `IsMuted`, `JoinedAt`)
- [x] Add unique index on `VoiceStates.UserId` to prevent multi-channel joins
- [x] Add `VoiceStates` DbSet to `CodecDbContext`
- [x] Create and apply EF Core migration (`AddVoiceChannels`)

### SFU service (`apps/sfu/`)
- [x] Create Node.js/Express SFU with mediasoup v3
- [x] Implement `POST /rooms/:id/participants` — create or join room, return `routerRtpCapabilities`
- [x] Implement `POST /rooms/:id/transports` — create WebRTC transport per participant (send + recv)
- [x] Implement `POST /rooms/:id/transports/:tid/connect` — finalize DTLS handshake
- [x] Implement `POST /rooms/:id/transports/:tid/produce` — create audio Producer
- [x] Implement `POST /rooms/:id/consumers` — create Consumer for a remote Producer
- [x] Implement `DELETE /rooms/:id/participants/:pid` — clean up participant transports
- [x] Write `Dockerfile` for the SFU (multi-stage build with native mediasoup dependencies)

### Security hardening (SFU)
- [x] Add `X-Internal-Key` shared secret middleware on all `/rooms/*` routes
- [x] Timing-safe comparison via `crypto.timingSafeEqual`
- [x] Refuse to start in `production` if `SFU_INTERNAL_KEY` is unset
- [x] Rate limiting: 120 req/min per IP via `express-rate-limit`
- [x] JSON body size cap: `express.json({ limit: '32kb' })`
- [x] Transport IDOR fix: require `participantId` in `/connect` and `/produce` bodies; validate ownership
- [x] Producer room validation: verify `producerId` exists in room before `canConsume`

### API — SignalR hub methods
- [x] Implement `JoinVoiceChannel(channelId)` — create `VoiceState`, create SFU room/transports, return capabilities + member list
- [x] Implement `LeaveVoiceChannel()` — remove `VoiceState`, delete SFU participant, broadcast `UserLeftVoice`
- [x] Implement `ConnectTransport(transportId, dtlsParameters)` — proxy to SFU with participant ownership
- [x] Implement `Produce(transportId, rtpParameters)` — proxy to SFU with participant ownership; broadcast `NewProducer`
- [x] Implement `Consume(producerId, recvTransportId, rtpCapabilities)` — proxy to SFU
- [x] Implement `SetMuted(muted)` — update `VoiceState.IsMuted`, broadcast `VoiceMuteChanged`
- [x] Broadcast `UserJoinedVoice` to channel group on join
- [x] Add `OnDisconnectedAsync` try-catch with fallback delete by `ConnectionId` for reliability
- [x] Catch `DbUpdateException` on concurrent join (unique index violation) and surface as `HubException`

### API — SFU HTTP client
- [x] Register named `"sfu"` `HttpClient` in `Program.cs` with 10 s timeout
- [x] Attach `X-Internal-Key` header when `Voice:SfuInternalKey` is configured
- [x] Log warning when SFU key is not configured

### Frontend — services and state
- [x] Create `VoiceService` class (`apps/web/src/lib/services/voice-service.ts`)
  - [x] `join()` — `getUserMedia` → `Device.load` → create transports → produce → consume existing members
  - [x] `consumeProducer()` — dedup via `consumedProducerIds` Set (guards group-join/member-snapshot race)
  - [x] `setMuted(muted)` — pause/resume Producer
  - [x] `leave()` — close all consumers, producer, transports; stop mic tracks
  - [x] Capture transport `const` locals in event handlers (avoids null dereference on concurrent `leave()`)
- [x] Add `VoiceChannelMember` type to `models.ts`
- [x] Add voice SignalR hub methods and events to `ChatHubService`
- [x] Add voice state and actions to `VoiceStore` (`voiceChannelId`, `voiceMembers`, `isMuted`, `joinVoiceChannel`, `leaveVoiceChannel`, `toggleMute`)

### Frontend — UI components
- [x] Update channel list to show voice channels with speaker icon and participant avatars
- [x] `VoiceChannel.svelte` — channel row, participant list, join/leave button
- [x] `VoiceControls.svelte` — mute/deafen controls shown while connected to a voice channel

### Verification
- [x] Backend builds successfully (`dotnet build`, 0 errors)
- [x] Frontend type-checks with zero errors (`npm run check`)

### Documentation
- [x] Rewrite `docs/VOICE.md` with actual implementation, architecture, API, security, and infrastructure details
- [x] Update `PLAN.md` with task breakdown
- [x] Update `README.md` with voice channels feature and doc link
- [x] Update `docs/FEATURES.md` to mark voice channels as implemented

---

## Task breakdown: Voice Infrastructure — Phase 4

### SFU Docker image
- [x] Multi-stage `Dockerfile` for `apps/sfu/` (build stage: compile TS; runtime stage: node + mediasoup native deps)
- [x] Push to Azure Container Registry via `cd.yml`

### Azure VM (`infra/modules/voice-vm.bicep`)
- [x] `Standard_B2s` VM (2 vCPU, 4 GiB) on Ubuntu 24.04 LTS
- [x] System-assigned managed identity with `AcrPull` role on ACR
- [x] Static public IP, NIC, NSG
- [x] NSG rules: SSH (parameterized source), SFU HTTP, UDP 40000–49999, deny-all
- [x] `sshAllowedSourcePrefix` param (default `'*'`; restrict in production)
- [x] `cloud-init` provisioner installs Docker, Docker Compose, `jq`

### docker-compose (`infra/voice/docker-compose.yml`)
- [x] `sfu` service with `network_mode: host` for mediasoup UDP
- [x] All secrets injected via environment variables (substituted by `envsubst` in CI/CD)

### Bicep wiring (`infra/main.bicep`, `infra/modules/container-app-api.bicep`)
- [x] `voiceSfuInternalKey` param → Key Vault secret → API container app env var (`Voice__SfuInternalKey`)
- [x] `voiceSshAllowedSourcePrefix` param wired to NSG rule
- [x] API `HttpClient("sfu")` reads `Voice:SfuInternalKey` from configuration

### CI/CD (`.github/workflows/cd.yml`)
- [x] `deploy-voice` job: build + push `sfu` Docker image
- [x] SSH to VM via `azure/CLI` + `az vm run-command invoke`
- [x] IMDS-based ACR login (no `az` CLI needed on VM): IMDS AAD token → ACR `/oauth2/exchange` → `docker login`
- [x] `envsubst` substitutes `SFU_INTERNAL_KEY`, `MEDIASOUP_ANNOUNCED_IP`, etc. into docker-compose
- [x] `VOICE_SFU_INTERNAL_KEY` GitHub Actions secret

### Verification
- [x] `az bicep build --file main.bicep` passes (0 errors)

---

## Task breakdown: Testing

### API Unit Tests (`apps/api/Codec.Api.Tests/`)
- [x] Create xUnit test project with test directory structure
- [x] Configure Moq for interface mocking
- [x] Configure FluentAssertions for readable test assertions
- [x] Configure coverlet for code coverage reporting
- [x] Write service unit tests (UserService, AvatarService, ImageUploadService, CustomEmojiService, LinkPreviewService, PresenceTracker, MessageCacheService) — 95%+ coverage
- [x] Write controller unit tests (HealthController, UsersController, AvatarsController, ImageUploadsController, IssuesController, PresenceController, FriendsController, ServersController, ChannelsController, DmController) — targeting all happy paths and error cases
- [x] Use InMemory EF Core for database operations in unit tests
- [x] Use ClaimsPrincipal setup for authenticated controller context
- [x] Verify 205 tests pass successfully

### API Integration Tests (`apps/api/Codec.Api.IntegrationTests/`)
- [x] Create xUnit integration test project
- [x] Configure Testcontainers for PostgreSQL and Redis
- [x] Create `CodecWebFactory` to boot complete API pipeline against disposable containers
- [x] Create `FakeAuthHandler` to bypass Google JWT validation (accepts base64-encoded claim payloads)
- [x] Create `IntegrationTestBase` with shared test helpers
- [x] Write integration tests for full HTTP request pipeline (routing, model binding, middleware, auth, exception handling)
- [x] Test `ExecuteDeleteAsync` code paths (server/channel/message deletion with cascade)
- [x] Test SignalR hub connections, channel groups, typing indicators, presence heartbeat
- [x] Test voice call lifecycle via hub (start → decline/answer → end)
- [x] Test server lifecycle (create → update → add channels → invite → join → delete)
- [x] Test message flow (post → edit → react → delete → purge)
- [x] Test DM conversation flow (friend request → accept → DM → send → close)
- [x] Test invite flow (create → join → already-member → expired → max-uses)
- [x] Test member management (kick, role promotion/demotion)
- [x] Test file uploads (avatars, images, server icons, custom emojis)
- [x] Test search (server messages, DM messages, with filters)
- [x] Test voice state (TURN credentials, voice channel states)
- [x] Test EF Core migrations against real PostgreSQL
- [x] Verify 109 tests pass successfully

### Web Unit Tests (`apps/web/src/**/*.spec.ts`)
- [x] Configure Vitest with jsdom environment
- [x] Configure @vitest/coverage-v8 for code coverage reporting
- [x] Add localStorage polyfill in test-setup.ts
- [x] Write util tests (format, YouTube URL extraction, emoji regex, emoji frequency, theme management) — 100% coverage
- [x] Write auth tests (session persistence, JWT expiry checks, token management) — 100% coverage
- [x] Write API client tests (all 50+ ApiClient methods, error handling, 401 retry logic) — 97.7% coverage
- [x] Verify 134 tests pass successfully

### Coverage Reporting
- [x] Generate and merge API unit + integration coverage reports (80%+ line coverage)
- [x] Generate web coverage report (98.59% line coverage)
- [x] Document coverage metrics in TESTING.md
- [x] Document untestable code sections (voice signaling, background services, Program.cs, external storage)

### Documentation
- [x] Create comprehensive `docs/TESTING.md` with test suites, running instructions, coverage details, architecture, and adding new tests guide
- [x] Update `README.md` with testing quick-start and coverage table
- [x] Update `PLAN.md` with testing task breakdown and metrics
- [x] Document test organization and naming conventions

---

## Task breakdown: Email/Password Sign-Up & Nickname

### Overview
Add email/password registration as a second auth method alongside Google Sign-In. Both flows require new users to choose a nickname during sign-up. Returning users can sign in directly with either method. Both methods must provide equivalent security guarantees.

### Data model changes

- [x] Add `PasswordHash` (nullable `string`) to `User` entity — stores bcrypt hash; null for Google-only users
- [x] Make `GoogleSubject` nullable — email/password users won't have one
- [x] Add unique index on `Email` (currently not unique) — prevents duplicate registrations
- [x] Create EF Core migration `AddEmailPasswordAuth`

### API — Email/password registration (`POST /auth/register`)

- [x] Add `RegisterRequest` DTO: `Email` (required, valid email), `Password` (required, min 8 chars), `Nickname` (required, 2–32 chars)
- [x] Validate email not already registered → 409 Conflict if taken
- [x] Hash password with bcrypt (cost factor 12)
- [x] Create `User` with `PasswordHash`, `Email`, `DisplayName` = nickname, `Nickname` = nickname, `GoogleSubject` = null
- [x] Auto-join default server (same as Google flow)
- [x] Issue a JWT access token (signed by the API, same claims shape as Google tokens: `sub` = user ID, `email`, `name`)
- [x] Return 201 with token + user payload

### API — Email/password sign-in (`POST /auth/login`)

- [x] Add `LoginRequest` DTO: `Email` (required), `Password` (required)
- [x] Look up user by email → 401 if not found or password hash is null (Google-only account)
- [x] Verify password against bcrypt hash → 401 on mismatch
- [x] Issue JWT access token (same format as registration)
- [x] Return 200 with token + user payload

### API — JWT issuance for email/password users

- [x] Add `JwtSettings` config section: `Secret` (min 32 chars), `Issuer`, `Audience`, `ExpiryMinutes` (default 60)
- [x] Create `TokenService` to generate signed JWTs with `sub`, `email`, `name`, `picture` claims
- [x] Token lifetime: 1 hour (matching Google ID token expiry)
- [x] Store `Jwt:Secret` in Key Vault (production) / `appsettings.Development.json` (dev)

### API — Dual-scheme authentication

- [x] Register a second authentication scheme (`"Local"`) for API-issued JWTs alongside the existing Google JWT Bearer scheme
- [x] Configure policy selector: accept tokens from either issuer (Google `accounts.google.com` OR local API issuer)
- [x] Update SignalR `OnMessageReceived` to accept tokens from both issuers via `?access_token=`
- [x] `GetOrCreateUserAsync` — for Google sign-in, match on `GoogleSubject`; for local tokens, match on user ID claim (`sub`)

### API — Token refresh (`POST /auth/refresh`)

- [x] Issue a refresh token (opaque, stored hashed in DB) alongside access tokens on register/login
- [x] Add `RefreshToken` entity: `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`
- [x] Refresh endpoint: validate refresh token → issue new access + refresh token pair → revoke old refresh token
- [x] Refresh token lifetime: 7 days (matching current Google session duration)
- [x] Revoke all refresh tokens on password change

### API — Google Sign-In nickname prompt

- [x] Modify `GetOrCreateUserAsync`: when creating a new user from Google sign-in, return a flag `isNewUser: true` in the `/me` response
- [x] Add `PATCH /me/nickname` endpoint (or use existing nickname update) — called by frontend after Google sign-up to set initial nickname
- [x] Frontend gates entry to the app on nickname being set for new Google users

### Web — Sign-in / sign-up page

- [x] Replace single Google Sign-In button with a two-option landing page:
  - **"Sign in with Google"** — existing Google Identity Services flow
  - **"Sign in" / "Create account"** toggle — email/password form
- [x] Sign-up form: email, password, confirm password, nickname (2–32 chars with character counter)
- [x] Sign-in form: email, password
- [x] Client-side validation: email format, password min length, password match, nickname length
- [x] Error display: email taken (409), invalid credentials (401), validation errors
- [x] On successful registration or login, store API-issued JWT in `localStorage` (same persistence as Google tokens)

### Web — Nickname prompt for Google sign-up

- [x] After Google sign-in, if `/me` returns `isNewUser: true`, show a nickname modal/screen before entering the app
- [x] Nickname input: 2–32 chars, character counter, submit calls `PATCH /me/nickname`
- [x] Pre-fill with Google display name; user can change before confirming
- [x] Block app navigation until nickname is confirmed

### Web — Token refresh for email/password users

- [x] Store refresh token in `localStorage` alongside access token
- [x] When access token expires, call `POST /auth/refresh` to obtain a new one
- [x] On refresh failure (expired/revoked), redirect to sign-in page
- [x] Unify token lifecycle: same `isTokenExpired` / `clearSession` helpers for both auth methods

### Security parity

- [x] **Equivalent protections for both methods:**
  - JWT signature verification (Google JWKS for Google tokens; HMAC-SHA256 or RSA for local tokens)
  - Audience and issuer validation on both token types
  - 1-hour access token expiry for both
  - 7-day session duration cap for both (Google One Tap refresh / refresh tokens)
  - Rate limiting on `/auth/login` and `/auth/register` (stricter: 10 req/min per IP) to prevent brute force
  - Account lockout after 5 failed login attempts (15-minute cooldown)
- [x] Bcrypt cost factor 12 for password hashing
- [x] Passwords never logged or returned in API responses
- [x] Refresh tokens stored hashed (SHA-256), not plaintext

### Documentation

- [x] Update `docs/AUTH.md` with dual-auth flow, email/password details, token refresh, and security comparison
- [x] Update `docs/FEATURES.md` to list email/password auth and nickname-on-signup as implemented
- [x] Update `docs/DATA.md` with `User.PasswordHash`, nullable `GoogleSubject`, `RefreshToken` entity
- [x] Update `CLAUDE.md` auth description to reflect both sign-in methods
- [x] Update `apps/web/.env.example` if any new env vars are needed

## Task breakdown: Server Settings Enhancements

### Backend — Entities and migration
- [x] Add `Description` (nullable string, max 256) to `Server` entity
- [x] Add `Description` (nullable string, max 256), `CategoryId` (nullable FK), `Position` (int) to `Channel` entity
- [x] Add `IsMuted` (bool, default false) to `ServerMember` entity
- [x] Create `ChannelCategory` entity (Id, ServerId, Name, Position)
- [x] Create `AuditLogEntry` entity (Id, ServerId, ActorUserId?, Action enum, TargetType?, TargetId?, Details?, CreatedAt)
- [x] Create `ChannelNotificationOverride` entity (composite PK: UserId + ChannelId, IsMuted)
- [x] Add DbSets and configure relationships in `CodecDbContext`
- [x] Create and apply EF Core migration

### Backend — Request DTOs
- [x] Extend `UpdateServerRequest` with optional `Description`
- [x] Extend `UpdateChannelRequest` with optional `Description`
- [x] Create `CreateCategoryRequest`, `UpdateCategoryRequest`
- [x] Create `ChannelOrderUpdate`, `CategoryOrderUpdate` list DTOs
- [x] Create `MuteRequest` DTO

### Backend — AuditService and cleanup
- [x] Create `AuditService` with `LogAsync(serverId, actorUserId, action, targetType, targetId, details)` method
- [x] Create `AuditLogCleanupService` background service — deletes entries older than 90 days on a timer

### Backend — Server description endpoint
- [x] Extend `PATCH /servers/{id}` to accept optional `description`; broadcast `ServerDescriptionChanged` via SignalR if changed

### Backend — Channel description endpoint
- [x] Extend `PATCH /servers/{id}/channels/{id}` to accept optional `description`; broadcast `ChannelDescriptionChanged` via SignalR if changed

### Backend — Category CRUD and channel ordering
- [x] Implement `GET /servers/{id}/categories`
- [x] Implement `POST /servers/{id}/categories` — create category; broadcast `CategoryCreated`
- [x] Implement `PATCH /servers/{id}/categories/{id}` — rename; broadcast `CategoryRenamed`
- [x] Implement `DELETE /servers/{id}/categories/{id}` — delete; nullify channel CategoryIds; broadcast `CategoryDeleted`
- [x] Implement `PUT /servers/{id}/channel-order` — bulk update position + categoryId; broadcast `ChannelOrderChanged`
- [x] Implement `PUT /servers/{id}/category-order` — bulk update position; broadcast `CategoryOrderChanged`

### Backend — Audit log and notification preference endpoints
- [x] Implement `GET /servers/{id}/audit-log?page&pageSize` — paginated entries newest-first
- [x] Implement `GET /servers/{id}/notification-preferences` — return server IsMuted + channel overrides
- [x] Implement `PUT /servers/{id}/mute` — update `ServerMember.IsMuted`
- [x] Implement `PUT /servers/{id}/channels/{id}/mute` — upsert `ChannelNotificationOverride`

### Backend — Audit logging for existing actions
- [x] Wire `AuditService.LogAsync` into all relevant controller actions (ServerRenamed, ServerDescriptionChanged, ChannelCreated, ChannelRenamed, ChannelDescriptionChanged, ChannelDeleted, ChannelPurged, MemberJoined, MemberLeft, MemberKicked, MemberRoleChanged, InviteCreated, InviteRevoked, EmojiUploaded, EmojiRenamed, EmojiDeleted, MessageDeleted, CategoryCreated, CategoryDeleted)

### Frontend — Types and API client
- [x] Add `description` to `Server` and `Channel` types in `models.ts`
- [x] Add `categoryId`, `position` to `Channel` type
- [x] Add `isMuted` to `ServerMember` type
- [x] Add `ChannelCategory`, `AuditLogEntry`, `NotificationPreferences`, `ChannelNotificationOverride` types
- [x] Add all new API methods to `ApiClient` (category CRUD, channel order, category order, audit log, notification preferences)

### Frontend — SignalR event handlers
- [x] Add event types and callbacks for `ServerDescriptionChanged`, `ChannelDescriptionChanged`, `CategoryCreated`, `CategoryRenamed`, `CategoryDeleted`, `ChannelOrderChanged`, `CategoryOrderChanged`
- [x] Register all new handlers in `ChatHubService.start()`
- [x] Wire all callbacks in SignalR orchestration (`signalr.svelte.ts`)

### Frontend — Stores (formerly AppState)
- [x] Add `categories`, `notificationPreferences`, `serverSettingsTab` state to `ChannelStore` / `ServerStore`
- [x] Add description update, category CRUD, channel/category order, and mute action methods
- [x] Update SignalR callbacks to keep categories and channel order in sync

### Frontend — Settings modal General tab
- [x] Add server description inline editor to `ServerSettings.svelte`
- [x] Tab structure updated to: General, Channels, Invites, Emojis, Members, Audit Log

### Frontend — Settings modal Channels tab (`ServerChannels.svelte`)
- [x] Draggable channel list grouped by category
- [x] Inline channel description/topic editor
- [x] Create/rename/delete category controls
- [x] Bulk position updates sent on drag drop

### Frontend — Settings modal Invites tab (`ServerInvites.svelte`)
- [x] Invite list with create (configurable expiry/max uses), revoke, and copy-link
- [x] Removed `InvitePanel` from channel sidebar

### Frontend — Settings modal Audit Log tab (`ServerAuditLog.svelte`)
- [x] Paginated audit log table with actor, action, target, details, timestamp
- [x] Pagination controls (previous/next page)

### Frontend — Channel sidebar category grouping
- [x] Channels grouped into collapsible `ChannelCategory` sections in `ChannelSidebar.svelte`
- [x] Uncategorized channels shown first
- [x] Collapse/expand state persisted per category

### Frontend — Chat area header descriptions
- [x] Server description shown in `ChatArea.svelte` header below server/channel name
- [x] Channel topic shown inline in chat area header with inline edit capability

### Frontend — Notification mute context menus
- [x] Create reusable `ContextMenu.svelte` component
- [x] Right-click on server icon → Mute/Unmute Server
- [x] Right-click on channel in sidebar → Mute/Unmute Channel

### Documentation
- [x] Update `docs/FEATURES.md` — move server settings/channel categories to Implemented; add Server Settings section
- [x] Update `docs/SERVER_SETTINGS.md` — document new tabs, categories, descriptions, notification preferences, new endpoints and SignalR events
- [x] Update `docs/ARCHITECTURE.md` — new endpoints, new entities, new SignalR events
- [x] Update `PLAN.md` — task breakdown, current status, next steps

---

## Next steps
- Update Google OAuth console: add `https://codec-chat.com` as authorized JavaScript origin
- Azure Monitor alerts (container restarts, 5xx rate, DB CPU)
- Container image vulnerability scanning (Trivy or Microsoft Defender)
- Image proxying (rewrite external image URLs through API to prevent IP leakage)
- File uploads for non-image documents
- API documentation (Swagger/OpenAPI)
- Svelte 5 component testing infrastructure: add `@testing-library/svelte`, establish component test patterns for representative components (e.g., `WizardStepProgress`, `ServerDiscordImport`, `MessageItem`), update vitest config to include component coverage, target 80% coverage on components with non-trivial logic

## Completed (previously planned)
- ~~Add richer validation and error surfaces in UI~~ (implemented: authorization helpers, global ProblemDetails exception handler, DataAnnotations on request DTOs, frontend ProblemDetails parsing, character counters on form inputs)
- ~~Presence indicators (online/offline/away)~~ (implemented: hybrid client+server heartbeat detection; PresenceTracker in-memory singleton with ConcurrentDictionary; PresenceBackgroundService for idle/offline scanning; PresenceState DB table; multi-tab support; push-based UserPresenceChanged SignalR events; PresenceDot component on member sidebar and DM list; online-first member sorting)
- ~~Light mode theme toggle~~ (implemented: 4-theme system — Phosphor Green, Midnight, Ember, Light — with Appearance settings, localStorage persistence, flash prevention)
- ~~Mobile slide-out navigation for server/channel sidebars~~ (implemented: left drawer for servers/channels, right drawer for members, hamburger buttons in chat headers, slide animations with backdrop dismiss)
- ~~Comprehensive unit and integration tests~~ (implemented: 1,542 tests across 3 suites; CI pipeline runs all tests on every PR)
- ~~Voice Phase 2~~ (completed: deafen, per-user volume, push-to-talk, responsive UserActionSheet)
- ~~Voice Phase 3~~ (completed: 1:1 DM voice calls with ringing, timeout, system messages)
- ~~Voice Phase 5~~ (completed: video chat and screen sharing; migrated from mediasoup to LiveKit)
- ~~Server settings/configuration~~ (implemented: descriptions, channel categories, invite management tab, audit log, notification preferences)
- ~~Message search~~ (completed: PostgreSQL trigram indexes, server/DM search endpoints, jump-to-message)
- ~~Custom roles and granular permissions~~ (implemented: ServerRoleEntity with 21 Permission flags, role hierarchy, CRUD endpoints, management UI)
- ~~User banning~~ (implemented: BannedMember entity, ban/unban endpoints, ban check on join, SignalR events)
- ~~Outgoing webhooks~~ (implemented: Webhook + WebhookDeliveryLog entities, background dispatch with retry, HMAC-SHA256 signing)
- ~~Web push notifications~~ (implemented: PushSubscription entity, VAPID keys, notifications for DMs/mentions/friend requests)
- ~~SAML 2.0 SSO~~ (implemented: SamlIdentityProvider entity, SP-initiated login, assertion validation, JIT provisioning)
- ~~GitHub OAuth~~ (implemented: authorization code flow, profile/email fetching, account linking)
- ~~Discord OAuth~~ (implemented: authorization code flow, profile fetching, account linking)
- ~~Status messages~~ (implemented: StatusText and StatusEmoji fields on User entity)
