# Codec Plan

## Purpose
Create a Discord-like app called Codec with a SvelteKit web front-end and an ASP.NET Core Web API backend. Authentication uses Google Sign-In (ID tokens validated by the API).

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
- Auth: Frontend obtains Google ID token; API validates per request
- Layout: apps/web + apps/api + docs + .github
- Package manager: npm
- Data: PostgreSQL + EF Core (Npgsql); Azure Database for PostgreSQL Flexible Server in production
- Hosting: Azure Container Apps (Consumption tier), Docker multi-stage builds
- IaC: Bicep modules under `infra/`
- CI/CD: GitHub Actions (CI, CD, Infrastructure pipelines)
- Auth (Azure): OIDC federated credentials (no long-lived secrets)
- Secrets: Azure Key Vault + GitHub Secrets
- File storage: Azure Blob Storage (production), local disk (development)
- Logging: Serilog with structured JSON → Log Analytics

## Current status
- **All features implemented** — see [FEATURES.md](docs/FEATURES.md) for full list
- Real-time member list updates via SignalR server-scoped groups
- Alpha notification banner with GitHub bug report link shown on every login
- GitHub Issues bug report template for alpha testers (`.github/ISSUE_TEMPLATE/bug-report.yml`)
- **Deployed to Azure** via Container Apps (Central US)
- Database migrated from SQLite to PostgreSQL (Azure Database for PostgreSQL Flexible Server)
- File storage migrated to Azure Blob Storage (avatars + images containers)
- SvelteKit switched to `adapter-node` for containerized deployment
- API hardened: health probes, Serilog structured logging, CORS, forwarded headers, rate limiting
- Both apps containerized with optimized multi-stage Dockerfiles
- Infrastructure as Code via Bicep modules under `infra/`
- CI pipeline: build, lint, Docker image validation on every push/PR
- CD pipeline: build → push to ACR → EF Core migration bundle → blue-green deploy (staging revisions → health verification → traffic switch) → smoke tests
- Infrastructure pipeline: Bicep what-if → deploy on push to `infra/` or manual dispatch
- OIDC federated credentials for GitHub Actions → Azure (no long-lived secrets)
- Content Security Policy with SvelteKit nonce-based inline script support
- All health checks passing (API `/health/ready` 200, Web `/health` 200)
- Custom domain (`codec-chat.com`) with managed TLS certificates via two-phase Bicep deployment (HTTP validation)
- `PUBLIC_API_BASE_URL` GitHub Secret set to `https://api.codec-chat.com`

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
- [x] Create `$lib/state/app-state.svelte.ts` — central `AppState` class with `$state`/`$derived` runes and context-based DI

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
- [x] Add `toggleReaction(messageId, emoji)` action to `AppState`
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
- [x] Add friends state management to `AppState`
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
- [x] Add DM state management to `AppState` (conversations list, active conversation, messages)
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
- [x] Add image attachment state and methods to `AppState` (`attachImage`, `clearPendingImage`, etc.)
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
- [x] Add `replyingTo` reactive state to `AppState` with `startReply()` and `cancelReply()` methods
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
- [x] Add `lightboxImageUrl` reactive state to `AppState`
- [x] Add `openImagePreview(url)` and `closeImagePreview()` methods to `AppState`
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
- [x] Add `isInitialLoading = $state(true)` flag to `AppState`
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
- [x] Wire `onMemberJoined` callback in `AppState.startSignalR()` to reload member list
- [x] Wire `onMemberLeft` callback in `AppState.startSignalR()` to reload member list
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
- [x] Add `showAlphaNotification` flag to `AppState` (set `true` at end of `handleCredential`)
- [x] Add `dismissAlphaNotification()` method to `AppState`
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
- [x] Add `deleteMessage(messageId)` action to `AppState` — calls API, falls back to local removal if SignalR disconnected
- [x] Add `deleteDmMessage(messageId)` action to `AppState` — calls API, falls back to local removal if SignalR disconnected
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
- [x] Add `editMessage(messageId, newBody)` action to `AppState` — calls API, falls back to local update if SignalR disconnected
- [x] Add `editDmMessage(messageId, newBody)` action to `AppState` — calls API, falls back to local update if SignalR disconnected
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
- [x] Add `hasMoreMessages` and `isLoadingOlderMessages` reactive state fields to `AppState`
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

## Next steps
- Update Google OAuth console: add `https://codec-chat.com` as authorized JavaScript origin
- Azure Monitor alerts (container restarts, 5xx rate, DB CPU)
- Introduce role-based authorization rules for additional operations
- Add richer validation and error surfaces in UI
- Server settings and configuration
- Channel editing/deletion
- Presence indicators (online/offline/away)
- Light mode theme toggle
- Mobile slide-out navigation for server/channel sidebars
- Comprehensive unit and integration tests
- Container image vulnerability scanning (Trivy or Microsoft Defender)
