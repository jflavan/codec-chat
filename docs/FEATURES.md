# Features

This document tracks implemented, in-progress, and planned features for Codec.

## ✅ Implemented (MVP)

### Authentication & User Management
- ✅ Google Sign-In integration
- ✅ JWT ID token validation by API
- ✅ Persistent login sessions (1-week duration, survives page reload)
- ✅ Token stored in `localStorage` with expiration checking
- ✅ Automatic silent token refresh via Google One Tap (`auto_select`)
- ✅ User profile display (name, email, avatar)
- ✅ User identity mapping (Google subject to internal User ID)
- ✅ Auto user creation on first sign-in

### Avatar System
- ✅ Custom avatar upload (JPG, JPEG, PNG, WebP, GIF)
- ✅ File validation: 10 MB max, MIME type and extension whitelisting
- ✅ Content-hash filenames for cache busting
- ✅ Global user avatar (shown across all servers)
- ✅ Server-specific avatar (overrides global avatar within a single server)
- ✅ Fallback chain: server avatar → custom global avatar → Google profile picture → initial placeholder
- ✅ Click-to-upload UI in user panel with hover overlay
- ✅ Author avatar images displayed in chat messages
- ✅ Avatar images displayed in member list sidebar
- ✅ Static file serving for uploaded avatars
- ✅ Delete avatar endpoints (revert to Google profile picture or global avatar)

### Server Management
- ✅ Server creation (authenticated user becomes Owner)
- ✅ Server membership tracking
- ✅ Server member list display with real-time updates (member list auto-refreshes when members join or leave via SignalR)
- ✅ Role-based membership (Owner, Admin, Member)
- ✅ Member display with avatar and role
- ✅ Kick members (Owner can kick Admins and Members; Admins can kick Members only; Global Admin can kick any non-Owner member)
- ✅ Real-time kick notification via SignalR (kicked user is redirected automatically, transient overlay banner with 5-second fade-out)
- ✅ Server invite codes (Owner/Admin create, list, revoke invites; any user can join via code)
- ✅ Invite code generation (cryptographically random 8-character alphanumeric codes)
- ✅ Configurable invite expiry (default 7 days, custom hours, or never) and max uses
- ✅ **Server Settings UI** — gear icon in channel sidebar header opens server settings modal (Owner/Admin only)
- ✅ **Server name editing** — change server name from Server Settings (Owner/Admin only; real-time sync via SignalR)
- ✅ **Channel name editing** — rename channels from Server Settings (Owner/Admin only; real-time sync via SignalR)
- ✅ **Channel deletion** — delete channels from Server Settings (Owner/Admin/Global Admin; cascade-deletes all messages, reactions, and link previews; real-time removal via SignalR)
- ✅ **Server deletion** — delete entire server (Owner or Global Admin; cascade-deletes all channels, messages, members, invites; real-time removal via SignalR)
- ✅ **Server icons** — custom server icon upload (JPG, JPEG, PNG, WebP, GIF; 10 MB max); content-hash filenames; real-time sync via `ServerIconChanged` SignalR event; delete icon to revert to default
- ✅ **Custom emojis** — upload, rename, and delete custom emojis per server (PNG, JPEG, WebP, GIF; 256 KB max; 50 per server); name validation (2–32 alphanumeric/underscore chars); content-addressed storage; real-time sync via `CustomEmojiAdded`, `CustomEmojiUpdated`, `CustomEmojiDeleted` SignalR events; Owner/Admin only for management; all members can list
- ✅ **Role management** — Owner and Admin can promote Members to Admin via Members tab in server settings; Owner and Global Admin can demote Admins to Member; inline confirmation for demotions; real-time sync via `MemberRoleChanged` SignalR event; Owner/Admin role badges displayed in member sidebar

### Channel & Messaging
- ✅ Channel list per server
- ✅ Channel creation (Owner/Admin only)
- ✅ Text message feed with persistence
- ✅ Post new messages
- ✅ Message history retrieval
- ✅ Author attribution (name, user ID)
- ✅ Timestamp display
- ✅ Real-time message delivery via SignalR (no page refresh needed)
- ✅ Typing indicators ("X is typing…")
- ✅ Emoji reactions on messages (toggle via floating action bar, reaction pills with counts, real-time sync via SignalR)
- ✅ Image uploads in messages (PNG, JPEG, WebP, GIF; file picker, clipboard paste, or drag-and-drop; 10 MB max)
- ✅ Inline image display in message feed (clickable, lazy-loaded, max 400×300px)
- ✅ Image lightbox — click any image in chat to open a full-size preview overlay with close/open-original controls
- ✅ @mentions with autocomplete (type `@` to see member picker, display names inserted in composer)
- ✅ @here mention to notify all channel members (appears as special entry in autocomplete picker)
- ✅ Mention badge notifications (unread mention count on server icons and channel names)
- ✅ Badge clearing on channel navigation (counts reset when user enters the mentioned channel)
- ✅ Mentioned message highlighting (accent border and tinted background on messages that mention the current user or use @here)
- ✅ Message replies — inline reply to any message with clickable reference preview, scroll-to-original with highlight animation, graceful handling of deleted parent messages
- ✅ Message deletion — authors can delete their own messages via action bar; global admin can delete any message; cascade-deletes reactions and link previews; real-time removal via SignalR
- ✅ Message editing — authors can edit their own messages via inline edit mode; "(edited)" label displayed on modified messages; real-time sync via SignalR
- ✅ Text formatting — bold (`*text*` or `**text**`) and italic (`_text_`) with live preview in composer
- ✅ Progressive message loading — initially loads last 100 messages; older messages load seamlessly on scroll-up via cursor-based pagination (`before`/`limit` query params); scroll position preserved during prepend; DM messages use same paginated `{ hasMore, messages }` response shape
- ✅ Message search — full-text search across server channels and DM conversations using PostgreSQL trigram indexes; right-side search panel with 300ms debounce; filters by scope (this channel/server or this DM/all DMs), date range, content type (image/link); paginated results with highlighted query matches; jump-to-message loads an around-window and scrolls to the target with a 2-second highlight animation

### Friends ([detailed spec](FRIENDS.md))
- ✅ Friend requests (send, accept, decline, cancel)
- ✅ Friends list with avatar and display name
- ✅ Remove friend
- ✅ User search for adding friends
- ✅ Real-time friend request notifications via SignalR
- ✅ Friends panel accessible from Home icon in server sidebar
- ✅ Notification badge on Home icon for pending incoming friend requests

### Direct Messages ([detailed spec](DIRECT_MESSAGES.md))
- ✅ 1-on-1 private conversations between friends
- ✅ DM conversations list in sidebar (sorted by most recent message)
- ✅ Real-time message delivery via SignalR
- ✅ Typing indicators in DM conversations
- ✅ Close / reopen DM conversations
- ✅ Start DM from friends list
- ✅ Home screen layout with DM sidebar + Friends panel / DM chat area
- ✅ Image uploads in DM messages (same format support: paste, file picker, and drag-and-drop as server channels)
- ✅ Message replies in DMs — same inline reply experience as server channels
- ✅ Message deletion in DMs — same delete-own-message experience as server channels
- ✅ Message editing in DMs — same inline edit experience as server channels
- ✅ Text formatting in DMs — same bold/italic formatting as server channels
- ✅ Emoji reactions in DMs — same reaction toggle experience as server channels (Reaction entity supports nullable MessageId/DirectMessageId with mutual exclusivity constraint)

### Link Previews ([detailed spec](LINK_PREVIEWS.md))
- ✅ Automatic URL detection in message bodies (server channels and DMs)
- ✅ Open Graph + HTML meta tag metadata fetching (title, description, image, site name)
- ✅ Clickable link preview cards rendered below message text
- ✅ Clickable thumbnail images linking to the original URL
- ✅ Real-time preview delivery via `LinkPreviewsReady` SignalR event
- ✅ SSRF protection (private IP blocking, DNS rebinding prevention, redirect limits)
- ✅ Clickable hyperlinks in message body text
- ✅ Responsive card layout (side-by-side ≥ 600px, stacked < 600px)
- ✅ YouTube video embeds — YouTube links render as click-to-play inline video players via `svelte-youtube-embed` package (thumbnail preview with title from oEmbed API, iframe loads on click, privacy-enhanced via `youtube-nocookie.com`)

### Nicknames ([detailed spec](NICKNAMES.md))
- ✅ User-chosen display name that overrides Google-provided name
- ✅ Set/update nickname via `PUT /me/nickname`, remove via `DELETE /me/nickname`
- ✅ `GET /me` returns `nickname` and `effectiveDisplayName` fields
- ✅ Effective display name shown in messages, member lists, friends, User Panel, typing indicators
- ✅ Fallback chain: nickname → Google display name
- ✅ Validation: 1–32 characters, Unicode supported, trimmed whitespace
- ✅ User search includes nickname matching and shows `effectiveDisplayName`
- ✅ Managed via User Settings → My Profile section

### User Settings ([detailed spec](USER_SETTINGS.md))
- ✅ Centralized settings screen accessed from gear icon (⚙) in User Panel
- ✅ Full-screen modal overlay with category navigation sidebar
- ✅ My Profile section: nickname editing with character counter, avatar upload/remove, profile preview
- ✅ My Account section: read-only account info (email, Google display name), sign-out
- ✅ Keyboard accessible: Escape to close, focus trapping via `<dialog>`
- ✅ Responsive layout: two-column (≥ 900px), tabbed (< 900px)
- ✅ Extensible for future categories (notifications, privacy, appearance)
- ✅ **Appearance settings** — theme picker with 4 preset themes: Phosphor Green (default CRT), Midnight (dark navy/blue), Ember (warm amber/orange), Light (Apple-inspired light mode); live preview cards; localStorage persistence; flash-prevention via inline script; `theme-color` meta tag sync for mobile browsers

### Alpha Notification & Bug Reporting
- ✅ Alpha notification modal shown on every login (ALPHA badge, welcome message, bug reporting guidance)
- ✅ In-app bug report submission — `BugReportModal` dialog with title/description form; auto-collects browser/OS, display name, and current page as metadata; proxied to GitHub Issues API via `POST /issues` endpoint (server-side PAT, never exposed to frontend); accessible from alpha banner and settings sidebar "Report a Bug" button; loading/success/error states with link to created issue

### Global Admin
- ✅ Configurable global admin role via `GlobalAdmin:Email` application setting
- ✅ `IsGlobalAdmin` flag on User entity, seeded at application startup
- ✅ **Full access to all servers** — global admin sees every server in the sidebar regardless of membership; role is `null` for servers the admin has not joined
- ✅ Global admin can read messages, post messages, edit own messages, delete any message, and toggle reactions in any channel (bypasses membership check)
- ✅ Global admin can manage channels in any server — create, rename, and delete channels (bypasses Owner/Admin role check)
- ✅ Global admin can manage invites in any server — create, list, and revoke invite codes (bypasses Owner/Admin role check)
- ✅ Global admin can update server settings (rename) for any server (bypasses Owner/Admin role check)
- ✅ Global admin can delete any server (cascade-deletes all channels, messages, members, invites)
- ✅ Global admin can delete any channel (cascade-deletes all messages, reactions, link previews)
- ✅ Global admin can delete any message in channels (bypasses author-only restriction)
- ✅ Global admin can purge all messages from a text channel (bulk-delete via Server Settings; cascade-deletes reactions and link previews; real-time `ChannelPurged` SignalR event clears connected clients)
- ✅ Global admin can kick any non-Owner member from any server (bypasses membership/role check)
- ✅ SignalR: global admin auto-joins all server groups on connect (receives real-time updates for every server)
- ✅ `isGlobalAdmin` flag exposed in `/me` API response and frontend state
- ✅ Server Settings UI: danger zone with delete server and delete channel buttons (confirmation dialogs)
- ✅ Message action bar: delete button visible for own messages and global admin; edit restricted to own messages only
- ✅ Real-time `ServerDeleted` and `ChannelDeleted` SignalR events for all connected clients
- ✅ Azure Key Vault integration for production (`GlobalAdmin--Email` secret)
- ✅ CD pipeline sets global admin email in Key Vault from GitHub Actions secret
- ✅ Direct link to GitHub bug report template from notification banner
- ✅ Dismissable via "Got it" button or Escape key
- ✅ GitHub Issues bug report template (`.github/ISSUE_TEMPLATE/bug-report.yml`) with structured fields (description, repro steps, expected/actual behavior, screenshots, browser, device)
- ✅ Issues auto-labeled `bug` and `alpha-tester`

### UI/UX
- ✅ CODEC CRT phosphor-green theme (CSS custom properties, design tokens)
- ✅ Three-column layout: server icon rail, channel sidebar, chat area
- ✅ Fourth column: members sidebar (responsive, hidden on smaller screens)
- ✅ Server icon rail with circular icons, active pill indicator, hover morph
- ✅ Channel list with `#` hash icons and active/hover states
- ✅ Message feed with avatars, grouped consecutive messages, hover timestamps
- ✅ Floating reaction action bar on message hover (emoji picker with 8 quick emojis)
- ✅ Reaction pills below messages (emoji + count, highlighted when user has reacted)
- ✅ Inline message composer with send icon and focus glow
- ✅ Image attach button (`+`), clipboard paste, and drag-and-drop support in composer
- ✅ Drag-and-drop overlay with visual drop zone indicator
- ✅ Image preview with remove button above composer input
- ✅ User panel pinned to bottom of channel sidebar (gear icon for settings + sign-out icon)
- ✅ Members sidebar grouped by role (Owner, Admin, Member)
- ✅ Loading screen — full-screen branded splash with animated progress bar, CRT scanlines, and glowing logo; shown during initial data bootstrap (servers, channels, messages) after sign-in; fades out smoothly once all data is loaded
- ✅ Loading states for async operations
- ✅ Error handling and display (transient overlay banner with fade-out animation)
- ✅ Responsive breakpoints (mobile, tablet, desktop)
- ✅ Accessibility: focus-visible outlines, prefers-reduced-motion, semantic HTML, ARIA labels
- ✅ Design specification documented in `docs/DESIGN.md`
- ✅ **Client-side form validation** — character counters on server/channel name inputs, disabled submit states, inline error messages from API responses

### Connection Status
- ✅ SignalR reconnection lifecycle tracking (`onReconnecting`, `onReconnected`, `onClose` callbacks)
- ✅ `isHubConnected` reactive state in `AppState` — tracks real-time connection health
- ✅ Composer disconnected state — shows "Codec connecting..." with animated ellipsis when SignalR is not connected (both server channels and DMs)
- ✅ Automatic restoration of full composer input on reconnection
- ✅ Auto-refresh on persistent disconnect — if SignalR cannot reconnect within 5 seconds, or if the WebSocket closes with an error (e.g. status code 1006), the page refreshes automatically

### Progressive Web App (PWA)
- ✅ Installable PWA via `@vite-pwa/sveltekit` plugin (Workbox `generateSW` strategy)
- ✅ Web app manifest with branded icons (192x192, 512x512) derived from favicon.ico
- ✅ Apple touch icon (180x180) for iOS home screen
- ✅ Offline-capable service worker with precached static assets (HTML, JS, CSS, images)
- ✅ Offline fallback page — branded "You're offline" page with retry button when network is unavailable
- ✅ User-prompted update flow — toast notification when new version is available ("Reload" / "Close")
- ✅ Periodic service worker update check (hourly)
- ✅ `ReloadPrompt.svelte` component styled with CRT phosphor-green theme
- ✅ Desktop and mobile screenshots in manifest for enhanced install experience
- ✅ App shortcuts — "Direct Messages" shortcut for quick access from OS launcher
- ✅ Share target — receive shared links and text from the OS share sheet
- ✅ Protocol handler — registered `web+codec://` custom protocol
- ✅ Runtime caching for Google Fonts (CacheFirst strategy, 1-year TTL)
- ✅ Display override stack (`standalone` → `minimal-ui`) and `launch_handler` for navigate-existing
- ✅ Edge Side Panel support (`preferred_width: 400`)

### Voice Channels ([detailed spec](VOICE.md))
- ✅ Persistent voice channel type (`ChannelType.Voice`) on servers — visible in channel sidebar with speaker icon
- ✅ Join/leave voice channels freely — no ringing, drop in and out at will
- ✅ Real-time audio via custom mediasoup v3 SFU — Opus codec, WebRTC media plane
- ✅ SignalR used for signaling (join, leave, new-producer notifications); audio flows directly via WebRTC
- ✅ Per-participant send transport (mic → SFU) and recv transport (SFU → speakers)
- ✅ Mute/unmute — pauses/resumes the audio Producer; `VoiceMuteChanged` event broadcast to channel
- ✅ Participant list with avatars and mute indicators shown on voice channel rows
- ✅ Per-user volume controls accessible via tap/click or right-click — responsive `UserActionSheet` with positioned popup (desktop) or bottom sheet (mobile)
- ✅ `VoiceControls` bar shown while connected (mute toggle, leave button)
- ✅ Double-consume race guard — `consumedProducerIds` Set prevents duplicate consumer creation
- ✅ Concurrent join protection — unique DB index on `VoiceStates.UserId`; surfaced as clear error
- ✅ Reliable disconnect cleanup — `OnDisconnectedAsync` try-catch with fallback delete by `ConnectionId`
- ✅ Transport ownership validation on SFU — prevents cross-participant transport IDOR
- ✅ Producer room validation on SFU — verifies producer exists in room before consuming
- ✅ Microphone permission errors surfaced with user-friendly messages
- ✅ SFU secured with shared internal key (`X-Internal-Key`), rate limiting (120 req/min), and JSON body size cap

### DM Voice Calls ([detailed spec](VOICE.md))
- ✅ 1:1 voice calls initiated from DM conversations via call button in chat header
- ✅ Incoming call overlay — full-screen modal with caller info, ring tone (Web Audio API), accept/decline buttons
- ✅ Call signaling via SignalR — `StartCall`, `AcceptCall`, `DeclineCall`, `EndCall` hub methods
- ✅ WebRTC audio via mediasoup SFU — DM calls use `call-{callId}` room IDs distinct from server channel rooms
- ✅ `VoiceCall` entity tracks call lifecycle (Ringing → Active → Ended) with caller/recipient IDs and timestamps
- ✅ `VoiceCallTimeoutService` — background service manages 30-second ringing timeout and 60-second stale call cleanup
- ✅ System messages in DM history — "Missed voice call" for unanswered calls, "Voice call — {duration}" for completed calls
- ✅ `DmCallHeader` component — compact header during active calls with elapsed time, mute/deafen controls, end button
- ✅ `VoiceConnectedBar` updated — shows "In call with {name}" during DM calls; end call button
- ✅ Dual-mode voice sessions — users can join server voice channels OR DM calls, not both simultaneously
- ✅ Call state recovery on reconnect — `GET /voice/active-call` endpoint restores call UI after page reload
- ✅ Collision detection — prevents starting a call when either party is already in a call or voice channel

### Frontend Architecture
- ✅ Modular layered architecture (types, API client, auth, services, state, components)
- ✅ Central `AppState` class with Svelte 5 `$state` / `$derived` runes
- ✅ Context-based dependency injection (`setContext` / `getContext`)
- ✅ Typed HTTP client (`ApiClient` class with `ApiError`)
- ✅ Auth module: token persistence (`localStorage`), session management, Google SDK wrapper
- ✅ SignalR service: `ChatHubService` for hub connection lifecycle
- ✅ CSS design tokens (`tokens.css`) and global base styles (`global.css`)
- ✅ Feature-grouped component directories (server-sidebar, channel-sidebar, chat, members, friends)
- ✅ Loading screen component (`LoadingScreen.svelte`) with `isInitialLoading` state flag
- ✅ Thin page composition shell (`+page.svelte` ~85 lines)
- ✅ Barrel exports via `$lib/index.ts`

### API Infrastructure
- ✅ Health check endpoint (`/health`)
- ✅ Controller-based RESTful API design (`[ApiController]`)
- ✅ Shared `UserService` for user resolution and membership checks
- ✅ CORS configuration for local development
- ✅ PostgreSQL database with EF Core (Npgsql)
- ✅ Automatic database migrations in dev
- ✅ Seed data for development
- ✅ SignalR hub (`/hubs/chat`) for real-time communication
- ✅ WebSocket JWT authentication via query string
- ✅ camelCase JSON serialization for SignalR payloads
- ✅ Response compression (Brotli + Gzip, `CompressionLevel.Fastest`) for `application/json` payloads
- ✅ Optimized user profile writes — skips `SaveChangesAsync` when Google profile fields are unchanged
- ✅ Cached mention parsing — regex results cached per message batch to eliminate redundant execution
- ✅ **Authorization helpers** — centralized membership and role checks in `UserService` (`EnsureMemberAsync`, `EnsureAdminAsync`, `EnsureOwnerAsync`, `EnsureDmParticipantAsync`); global admin bypass; custom exceptions with global ProblemDetails handler
- ✅ **DataAnnotations validation** — request DTOs annotated with `[Required]`, `[StringLength]`, etc.; automatic model validation returns RFC 7807 ProblemDetails
- ✅ **Global exception handler** — `ForbiddenException` → 403, `NotFoundException` → 404, unhandled → 500; all errors return ProblemDetails JSON

## 📋 Planned (Near-term)

### Messaging Features
- ~~Message search~~ (implemented)

### Real-time Features
- Presence indicators (online/offline/away)

### Server Management
- Server settings/configuration
- Channel categories/organization

### Link Previews
- Link preview caching
- Image proxying for `og:image` URLs
- Video embeds for Vimeo

### File & Media
- File uploads (documents, other media)

## 🔮 Future (Later)

### Advanced Features
- Video chat
- Screen sharing
- Message pinning
- Notification system (push, email)

### Moderation & Administration
- User banning
- Message moderation
- Audit logs (global admin actions)
- Report system
- Custom role creation
- Granular permissions

### Customization
- Status messages

### Enterprise Features
- OAuth integrations (GitHub, Discord, etc.)
- SAML/SSO support
- Analytics dashboard
- Export/backup tools
- API rate limiting
- Webhooks

## Technical Debt & Improvements
- [ ] Add comprehensive unit tests
- [ ] Add integration tests
- [x] Implement proper logging (Serilog → Azure Log Analytics)
- [ ] Add API documentation (Swagger/OpenAPI)
- [x] Performance monitoring and metrics (Log Analytics workspace, Serilog structured logging)
- [x] Production database migration strategy (EF Core migration bundle in CD pipeline)
- [x] Container deployment (Docker multi-stage builds, Azure Container Apps)
- [x] CI/CD pipeline (GitHub Actions CI + CD with OIDC, Bicep IaC)
