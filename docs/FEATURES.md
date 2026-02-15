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
- ✅ Kick members (Owner can kick Admins and Members; Admins can kick Members only)
- ✅ Real-time kick notification via SignalR (kicked user is redirected automatically, transient overlay banner with 5-second fade-out)
- ✅ Server invite codes (Owner/Admin create, list, revoke invites; any user can join via code)
- ✅ Invite code generation (cryptographically random 8-character alphanumeric codes)
- ✅ Configurable invite expiry (default 7 days, custom hours, or never) and max uses

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
- ✅ Message deletion — authors can delete their own messages via action bar; cascade-deletes reactions and link previews; real-time removal via SignalR
- ✅ Message editing — authors can edit their own messages via inline edit mode; "(edited)" label displayed on modified messages; real-time sync via SignalR
- ✅ Text formatting — bold (`*text*` or `**text**`) and italic (`_text_`) with live preview in composer

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

### Link Previews ([detailed spec](LINK_PREVIEWS.md))
- ✅ Automatic URL detection in message bodies (server channels and DMs)
- ✅ Open Graph + HTML meta tag metadata fetching (title, description, image, site name)
- ✅ Clickable link preview cards rendered below message text
- ✅ Clickable thumbnail images linking to the original URL
- ✅ Real-time preview delivery via `LinkPreviewsReady` SignalR event
- ✅ SSRF protection (private IP blocking, DNS rebinding prevention, redirect limits)
- ✅ Clickable hyperlinks in message body text
- ✅ Responsive card layout (side-by-side ≥ 600px, stacked < 600px)

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

### Alpha Notification & Bug Reporting
- ✅ Alpha notification modal shown on every login (ALPHA badge, welcome message, bug reporting guidance)
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

## 🚧 In Progress

### Authorization
- Authorization policies for endpoints

### Data Validation
- Enhanced input validation
- Error response standardization
- Client-side form validation

## 📋 Planned (Near-term)

### Messaging Features
- Message search

### Real-time Features
- Presence indicators (online/offline/away)

### Server Management
- Server settings/configuration
- Server icons/avatars
- Channel categories/organization
- Channel editing and deletion

### Link Previews
- Link preview caching
- Image proxying for `og:image` URLs
- Video embeds for YouTube/Vimeo

### File & Media
- File uploads (documents, other media)

## 🔮 Future (Later)

### Advanced Features
- Voice channels (WebRTC)
- Video chat
- Screen sharing
- Message pinning
- Notification system (push, email)

### Moderation & Administration
- User banning
- Message moderation
- Audit logs
- Report system
- Custom role creation
- Granular permissions

### Customization
- Web client themes
- Custom emojis
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
