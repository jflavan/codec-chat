# Features

This document tracks implemented and planned features for Codec.

## Implemented

### Authentication & Users
- Google Sign-In with backend token exchange (`POST /auth/google`) — Google ID tokens are exchanged for backend-issued JWTs with rotating refresh tokens, giving Google users the same reliable 7-day session persistence as all other auth methods
- Automatic token refresh via `POST /auth/refresh` for all auth types (Google, email/password, GitHub, Discord, SAML)
- Email/password registration and sign-in (bcrypt hashing, API-issued JWTs, rotating refresh tokens)
- Nickname selection during sign-up for both email/password and Google flows
- Account linking — email/password users can link a Google account after confirming their password
- Email verification — required for email/password registrations; 24-hour token emailed to user; `[RequireEmailVerified]` filter gates data endpoints; Google users auto-verified; Azure Communication Services in production
- Account lockout — 15-minute lockout after 5 consecutive failed login attempts (brute-force protection)
- reCAPTCHA v3 bot protection — Google reCAPTCHA Enterprise scores login and register requests; fail-closed (blocks on API errors); configurable score threshold; disabled by default in local dev
- Server-side logout — `POST /auth/logout` revokes refresh tokens on sign-out
- Refresh token cleanup — background service purges expired and stale revoked tokens every 6 hours
- Optimistic concurrency on refresh token rotation (PostgreSQL `xmin` concurrency token)
- User profile display (name, email, avatar)
- Nicknames — user-chosen display name overriding Google name across all surfaces
- Custom avatar upload (JPG, PNG, WebP, GIF; 10 MB max; content-hash filenames)
- Server-specific avatar overrides with fallback chain (server → global → Google → placeholder)
- User search by name, nickname, or email
- Global admin role — configurable via `GlobalAdmin:Email`; full access to all servers, channels, and messages regardless of membership
- Account disabling — global admins can disable user accounts; disabled users are blocked from all auth flows and admin endpoints; refresh tokens revoked immediately

### Servers & Channels
- Server creation (creator becomes Owner)
- Role-based membership (Owner, Admin, Member) with role management UI
- Channel creation, renaming, and deletion (Owner/Admin)
- Server name editing, server icons, and server deletion
- Custom emojis — upload, rename, delete per server (PNG, JPEG, WebP, GIF; 256 KB max; 50 per server)
- Server invite codes with configurable expiry and max uses
- Kick members with real-time notification and auto-redirect

### Server Settings
- **Invite management tab** — invite CRUD (create, list, revoke) moved into server settings as a dedicated Invites tab; no longer embedded in the channel sidebar
- **Server descriptions** — optional description field (256 char max) editable in the General tab; displayed in the chat area header; broadcast to all members via `ServerDescriptionChanged` SignalR event
- **Channel descriptions/topics** — optional per-channel description (256 char max) editable in the Channels settings tab and inline in the chat area header; broadcast via `ChannelDescriptionChanged` SignalR event
- **Channel categories** — `ChannelCategory` entity groups channels into collapsible sidebar sections; full CRUD (create, rename, delete) with drag-and-drop reordering; channels can be assigned to categories with drag-and-drop ordering within categories; bulk position update endpoints keep order consistent across clients
- **Audit log** — `AuditLogEntry` entity tracks 21 action types (server/channel/member/invite/emoji/message events); paginated `GET /servers/{id}/audit-log` endpoint with actor name, target, and detail fields; 90-day automatic cleanup via background service; dedicated Audit Log tab in server settings
- **Notification preferences** — per-server mute (`ServerMember.IsMuted`) and per-channel mute (`ChannelNotificationOverride` entity); right-click context menus on server icons and channel list items to toggle mute; `GET /servers/{id}/notification-preferences` endpoint returns current mute state

### Messaging
- Real-time message delivery via SignalR WebSockets
- Typing indicators
- Message editing and deletion (author or global admin)
- Message replies with clickable reference and scroll-to-original
- Text formatting — bold (`*text*` / `**text**`) and italic (`_text_`)
- @mentions with autocomplete picker and @here; mention badges on server/channel icons
- Emoji reactions (toggle, pills with counts, real-time sync)
- Image uploads via file picker, clipboard paste, or drag-and-drop (10 MB max)
- File attachments for documents and non-image media (25 MB max) with download cards
- Image lightbox — full-size preview overlay with keyboard dismiss
- Link previews — Open Graph metadata, clickable embed cards, SSRF protection
- YouTube embeds — click-to-play inline video players via `svelte-youtube-embed`
- Message pinning — Owner/Admin can pin up to 50 messages per channel; pin indicator on messages; slide-in pinned messages panel; system notification messages on pin; real-time sync via SignalR; audit logged
- Progressive loading — cursor-based pagination with scroll position preservation
- Message search — full-text search with PostgreSQL trigram indexes; filters by scope, date range, and content type; jump-to-message with highlight animation

### Friends & Direct Messages
- Friend requests (send, accept, decline, cancel) with real-time SignalR notifications
- Friends list and remove friend
- Notification badge on Home icon for pending requests
- 1:1 DM conversations with real-time delivery, typing indicators, and unread badges
- Close/reopen DM conversations
- DMs support all messaging features (replies, reactions, images, formatting, search)

### Voice & Video
- **Voice channels** — persistent voice channel type in servers; join/leave freely; real-time audio via mediasoup v3 SFU (Opus/WebRTC); mute/unmute with broadcast; participant list with avatars and mute indicators; per-user volume control; push-to-talk with configurable keybind
- **DM voice calls** — 1:1 calls from DM chat header; incoming call overlay with ring tone; 30-second ringing timeout; system messages for call events (missed, duration); call state recovery on reconnect; collision detection prevents concurrent calls
- **Video chat** — webcam video via mediasoup video producers; `VideoTile` and `VideoGrid` components; `IsVideoEnabled` state tracked per participant; toggle on/off during voice sessions
- **Screen sharing** — `getDisplayMedia()` screen capture; separate screen producer track; `IsScreenSharing` state broadcast via SignalR; viewers consume screen track alongside audio

### User Settings
- Full-screen modal with category sidebar
- My Profile: nickname editing, avatar upload/remove, preview
- My Account: read-only info, sign-out
- Appearance: 4 themes (Phosphor Green, Midnight, Ember, Light) with live preview and `localStorage` persistence
- Voice & Audio: input mode (voice activity / push-to-talk), PTT keybind configuration

### PWA
- Installable on desktop and mobile via `@vite-pwa/sveltekit`
- Offline fallback page with retry button
- Update toast when new version is available
- Runtime Google Fonts caching (CacheFirst, 1-year TTL)
- App shortcuts, share target, `web+codec://` protocol handler
- Edge Side Panel support

### UI/UX
- CRT phosphor-green theme with CSS custom properties
- Three-column layout (server rail, channel sidebar, chat area) + members sidebar
- Loading screen with animated progress bar and CRT scanlines
- Connection status awareness — composer shows "Codec connecting..." on disconnect; auto-refresh on persistent failure
- Alpha notification modal on login with bug report link
- In-app bug reporting proxied to GitHub Issues
- Responsive breakpoints (mobile, tablet, desktop)
- Accessibility: focus-visible outlines, `prefers-reduced-motion`, semantic HTML, ARIA labels

### Presence Indicators
- ✅ Real-time presence status (Online, Idle, Offline) — automatic, no user-set statuses
- ✅ Hybrid detection — client sends 30-second heartbeats with `isActive` flag; server tracks in-memory via `PresenceTracker` singleton with `ConcurrentDictionary`
- ✅ `PresenceBackgroundService` — scans every 30 seconds; transitions to Idle after 5 minutes of inactivity, Offline after 2 minutes of missed heartbeats
- ✅ `PresenceState` DB table — canonical status written only on transitions; purged on server startup
- ✅ Multi-tab support — tracks multiple SignalR connections per user; aggregate status uses best across connections (Online > Idle > Offline)
- ✅ Push-based broadcasting — `UserPresenceChanged` SignalR event sent to all `server-{serverId}` groups and `user-{friendId}` groups on status change
- ✅ REST endpoints — `GET servers/{serverId}/presence` and `GET dm/presence` for initial presence load
- ✅ `PresenceDot` component — colored indicator (green=online, yellow=idle, gray=offline) on member avatars
- ✅ Member sidebar — presence dots on member avatars with online-first sorting within each role group
- ✅ DM list — presence dots next to conversation participant avatars
- ✅ Offline dimming — offline members shown at 50% opacity in member sidebar

### Connection Status
- ✅ SignalR reconnection lifecycle tracking (`onReconnecting`, `onReconnected`, `onClose` callbacks)
- ✅ `isHubConnected` reactive state in `AppState` — tracks real-time connection health
- ✅ Composer disconnected state — shows "Codec connecting..." with animated ellipsis when SignalR is not connected (both server channels and DMs)
- ✅ Automatic restoration of full composer input on reconnection
- ✅ Auto-refresh on persistent disconnect — if SignalR cannot reconnect within 5 seconds, or if the WebSocket closes with an error (e.g. status code 1006), the page refreshes automatically

### API & Infrastructure
- Controller-based RESTful API with `[Authorize]` on all endpoints
- Health endpoints: `/health/live` (liveness), `/health/ready` (readiness + DB + Redis check), `/alive` (simple alive check)
- Redis distributed cache — message history pages cached with 5-minute TTL; channel-level invalidation on send, edit, delete, purge, and reactions; graceful degradation when Redis is unavailable
- SignalR Redis backplane — enables real-time events across multiple API instances for horizontal scaling
- Rate limiting — fixed window, 100 req/min (429 on exceed)
- Response compression (Brotli + Gzip)
- Structured JSON logging via Serilog (Console → Container Apps → Log Analytics)
- SignalR hub (`/hubs/chat`) with WebSocket JWT auth via query string
- Swagger/OpenAPI documentation with Scalar UI at `/scalar/v1`
- Image proxy endpoint (`GET /images/proxy?url=`) with SSRF protection
- Global exception handler with RFC 7807 ProblemDetails responses
- EF Core with automatic migrations (dev) and migration bundles (prod CD pipeline)
- Azure deployment: Container Apps, Key Vault secrets, Bicep IaC, GitHub Actions CI/CD
- .NET Aspire AppHost for single-command local dev orchestration (Postgres, Redis, Azurite, API, Web; dashboard at `https://localhost:17222`)
- OpenTelemetry observability — distributed traces, metrics, and structured logs exported to Azure Monitor (Application Insights) in production and OTLP (Aspire dashboard) locally
- SFU telemetry — custom spans on room/transport/producer/consumer operations with Azure Monitor export
- Azure Monitor alerts — container restart, 5xx error rate, and database CPU monitoring via Bicep modules
- Trivy container vulnerability scanning in CI and CD pipelines (advisory mode)
- VAPID key rotation to Azure Key Vault secrets for push notification security

### Global Admin Panel
- **Admin dashboard** — standalone SvelteKit app (`apps/admin/`) with live stats (users, servers, messages, open reports, active connections, messages/min) via SignalR `AdminHub`; stats broadcast every 5 seconds
- **User management** — paginated user list with search; user detail with profile, auth providers, server memberships, recent messages, report history, and admin action log; actions: disable/enable account, force logout, reset password, promote/demote global admin
- **Server management** — paginated server list with search and quarantine status; server detail with members, channels, roles, and owner; actions: quarantine/unquarantine, delete server, transfer ownership
- **Moderation queue** — report queue with status (Open/InProgress/Resolved/Dismissed) and type (User/Message/Server) filters; report detail with related report count; actions: assign, resolve, dismiss; full-text message search across all servers
- **System tools** — paginated admin action audit log; system announcement CRUD (title, content, active flag, optional expiry); live connection count
- **User reports** — any authenticated user can submit reports (`POST /reports`) for users, messages, or servers
- **System announcements** — platform-wide announcements with active flag and optional expiry; public endpoint (`GET /announcements`) for active announcements

### Moderation
- **User banning** — ban members with optional message purge; `BannedMember` entity with reason and actor tracking; ban check on invite join (prevents re-entry); real-time `BannedFromServer` and `MemberBanned` SignalR events; ban management UI in server settings (list, ban, unban)
- **Custom roles** — `ServerRoleEntity` with 21 granular permission flags (bitmask); role hierarchy with position-based ordering; system roles (Owner, Admin, Member, @everyone) plus custom roles; full CRUD via `RolesController`; role management UI with permission editor and drag-and-drop reordering; role badges with custom colors on member avatars; `IsMentionable` and `IsHoisted` options
- **Granular permissions** — `Permission` flags enum covering channel management, server management, roles, emojis, audit log, invites, kick/ban, messaging, voice, and a special `Administrator` flag that bypasses all checks
- **Multi-role support** — members can hold multiple roles simultaneously; permissions are OR-merged across all assigned roles; `ServerMemberRoles` join table replaces the old single `RoleId` column; new endpoints to add/remove individual roles or replace the full set (`PUT/POST/DELETE /servers/{id}/members/{userId}/roles/{roleId}`); server Owners and Administrators bypass all permission checks
- **Per-channel permission overrides** — roles can define per-channel allow/deny bitmasks via the `ChannelPermissionOverrides` table; deny always wins over allow (deny-wins model); `PermissionResolverService` merges base role permissions and applies channel-level overrides at check time; managed via `GET/PUT/DELETE /channels/{id}/overrides/{roleId}`; frontend `ChannelPermissions` component exposes three-state (allow/neutral/deny) override editing

### Integrations
- **Outgoing webhooks** — per-server webhook configuration with name, URL, and optional HMAC-SHA256 signing secret; subscribable event types (MessageCreated, MessageUpdated, MessageDeleted, MemberJoined, MemberLeft, MemberRoleChanged, ChannelCreated, ChannelUpdated, ChannelDeleted); background dispatch (non-blocking); retry with exponential backoff (5s, 30s, 5m); per-attempt delivery logging via `WebhookDeliveryLog`; `X-Webhook-Signature`, `X-Webhook-Event`, and `X-Webhook-Id` headers
- **Web push notifications** — `PushSubscription` entity with Web Push API endpoint and VAPID keys; `PushSubscriptionsController` for subscribe/unsubscribe; VAPID public key endpoint (anonymous); notifications for DMs, @mentions, and friend requests; auto-deactivation on 410 Gone responses
- **Status messages** — per-user `StatusText` (128 char max) and `StatusEmoji` (8 char max, supports multi-codepoint emoji); displayed in member lists and user profiles

### Authentication Providers
- **SAML 2.0 SSO** — `SamlIdentityProvider` entity with EntityId, SSO URL, and PEM X.509 certificate; SP-initiated login flow with HTTP-Redirect binding; Assertion Consumer Service (`POST /auth/saml/acs`) with XML signature verification; SP metadata endpoint; IdP metadata import; just-in-time (JIT) user provisioning; cookie-based SAML request correlation; admin CRUD for IdP management
- **GitHub OAuth** — `POST /auth/oauth/github` callback; authorization code exchange; user profile and private email fetching; `GitHubSubject` field on User entity; account linking to existing email/password accounts
- **Discord OAuth** — `POST /auth/oauth/discord` callback; authorization code exchange; user profile and avatar URL fetching; `DiscordSubject` field on User entity; account linking to existing email/password accounts
- **OAuth configuration** — `GET /auth/oauth/config` returns enabled provider status (public endpoint)

### Testing
- 1,542 automated tests across 3 test suites (1,188 API unit, 177 API integration, 177 web)
- API unit tests: xUnit + FluentAssertions + Moq; InMemory EF Core for database tests
- API integration tests: WebApplicationFactory + Testcontainers (disposable PostgreSQL + Redis); full HTTP pipeline with real migrations; FakeAuthHandler bypasses Google JWT; SignalR hub tests via SignalR client
- Web unit tests: Vitest + jsdom; localStorage polyfill; mocked fetch for API client
- Coverage: core services 95%+, web utilities 98%+, combined API 80%+
- See [TESTING.md](TESTING.md) for full details

### File Attachments
- File uploads for documents and non-image media (PDF, ZIP, etc.) via `FileUploadService`
- Attachment fields on `Message` and `DirectMessage` entities (`FileName`, `FileSize`, `FileMimeType`, `FileUrl`)
- `FileCard` component renders file attachments with icon, name, size, and download link
- Composer supports file picker and drag-and-drop for file attachments
- 25 MB max file size; content-type validation

### Image Proxy
- `GET /images/proxy?url=` endpoint proxies external images through the API
- SSRF protection with DNS rebinding checks and private IP blocking
- Content-type validation (only image/* allowed)
- 10 MB max response size
- Configurable allowlist/blocklist

### API Documentation
- Swagger/OpenAPI documentation with Scalar UI at `/scalar/v1`
- Auto-generated from controller metadata and XML documentation comments

### Security & Operations
- **Trivy container scanning** — vulnerability scanning of Docker images in both CI and CD pipelines; advisory mode (non-blocking)
- **Azure Monitor alerts** — container restart alerts, 5xx error rate alerts, database CPU alerts via Bicep modules (`monitor-alerts.bicep`, `monitor-action-group.bicep`)
- **VAPID key rotation** — push notification VAPID keys rotated to Azure Key Vault secrets (no longer in appsettings)
- **Security hardening** — SAML XML signature validation strengthened, OAuth redirect URI validation, webhook URL validation, input length validation on all DTOs

## Planned

### Near-Term
- Group DMs (multi-party DM conversations)
- Thread/forum channels

## Technical Debt
- [x] API documentation (Swagger/OpenAPI with Scalar UI)
- [x] Unit and integration tests (1,542 tests across 3 suites)
- [x] Structured logging (Serilog)
- [x] Production database migration strategy (EF Core bundles in CD)
- [x] Container deployment (Docker multi-stage builds, Azure Container Apps)
- [x] CI/CD pipeline (GitHub Actions with OIDC, Bicep IaC)
- [x] Response caching (Redis distributed cache for message history)
