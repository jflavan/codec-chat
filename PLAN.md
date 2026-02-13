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
- Server membership and roles with join flow (invite-only)
- Server member listing in API and UI
- Server creation endpoint and UI
- Channel creation endpoint and UI (Owner/Admin only)
- Discord-inspired dark theme with three-column layout
- Design spec documented in `docs/DESIGN.md`
- Session persistence via localStorage (1-week sessions survive page reload)
- Automatic token refresh via Google One Tap (`auto_select`)
- SignalR hub (`/hubs/chat`) for real-time messaging and typing indicators
- Real-time message broadcast on message post (via `IHubContext<ChatHub>`)
- Typing indicators (UserTyping / UserStoppedTyping events)
- WebSocket JWT authentication via `access_token` query parameter
- camelCase JSON serialization for SignalR payloads
- Client-side SignalR connection with automatic reconnect
- Frontend architecture refactored to modular layers (types, API client, auth, services, state, components)
- Central AppState class with Svelte 5 $state/$derived runes and context-based DI
- CSS design tokens extracted to $lib/styles/tokens.css and global.css
- Feature-grouped Svelte 5 components (server-sidebar, channel-sidebar, chat, members)
- +page.svelte reduced to ~75-line thin composition shell
- Sign-out button in user panel
- Emoji reactions on messages (toggle, real-time sync via SignalR, floating action bar, reaction pills)
- Friends feature fully implemented (friend requests, friends list, user search, real-time SignalR events, Friends panel UI, notification badge on Home icon)
- Direct Messages feature fully implemented (1-on-1 private messaging between friends, DM conversations list, real-time delivery via SignalR, typing indicators, close/reopen conversations, start DM from friends list)
- Kick member feature implemented (Owner/Admin can kick members, role hierarchy enforced, real-time notification via SignalR, frontend kick button with confirm step)
- Server invites feature implemented (Owner/Admin create/list/revoke invite codes, any user joins via code, configurable expiry and max uses, frontend invite panel and join-by-code UI)
- Image uploads feature implemented (upload from desktop or paste from clipboard, PNG/JPEG/WebP/GIF support, 10 MB limit, image preview in composer, inline image display in messages, works in both server channels and DMs)
- Link previews feature fully implemented (automatic URL detection, Open Graph metadata fetching with SSRF protection, clickable embed cards with title/description/thumbnail, real-time delivery via SignalR, clickable thumbnail images)
- @mentions feature implemented (autocomplete member picker in composer, mention badge counts on server icons and channel names, badge clearing on channel navigation, mentioned message highlighting, @here to notify all channel members)
- Message replies feature implemented (inline reply to any message in channels or DMs, reply-to-message context in feed, scroll-to-original with highlight animation, Escape to cancel, orphaned reply handling)
- Image preview lightbox implemented (full-size overlay on image click, Escape to close, open-original link, works in both server channels and DMs)

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

## Next steps
- Introduce role-based authorization rules for additional operations
- Add richer validation and error surfaces in UI
- Server settings and configuration
- Channel editing/deletion
- Presence indicators (online/offline/away)
- Real-time member list updates
- Light mode theme toggle
- Mobile slide-out navigation for server/channel sidebars
- Comprehensive unit and integration tests for frontend modules
