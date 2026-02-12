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
- ✅ Server discovery (browse all servers)
- ✅ Server creation (authenticated user becomes Owner)
- ✅ Server joining flow
- ✅ Server membership tracking
- ✅ Server member list display
- ✅ Role-based membership (Owner, Admin, Member)
- ✅ Member display with avatar and role

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
- ✅ User panel pinned to bottom of channel sidebar
- ✅ Members sidebar grouped by role (Owner, Admin, Member)
- ✅ Loading states for async operations
- ✅ Error handling and display (banner in chat area)
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
- ✅ Thin page composition shell (`+page.svelte` ~75 lines)
- ✅ Barrel exports via `$lib/index.ts`

### API Infrastructure
- ✅ Health check endpoint (`/health`)
- ✅ Controller-based RESTful API design (`[ApiController]`)
- ✅ Shared `UserService` for user resolution and membership checks
- ✅ CORS configuration for local development
- ✅ SQLite database with EF Core
- ✅ Automatic database migrations in dev
- ✅ Seed data for development
- ✅ SignalR hub (`/hubs/chat`) for real-time communication
- ✅ WebSocket JWT authentication via query string
- ✅ camelCase JSON serialization for SignalR payloads

## 🚧 In Progress

### Authorization
- 🔄 Role-based permissions (Owner/Admin privileges)
- 🔄 Authorization policies for endpoints
- 🔄 Admin-only operations (channel creation, member management)

### Data Validation
- 🔄 Enhanced input validation
- 🔄 Error response standardization
- 🔄 Client-side form validation

## 📋 Planned (Near-term)

### Messaging Features
- Message editing and deletion
- Rich text formatting (markdown)
- @mentions
- Message search

### Real-time Features
- ✅ SignalR/WebSocket integration
- ✅ Live message updates (no page refresh)
- ✅ Typing indicators
- Presence indicators (online/offline/away)
- Real-time member list updates

### Server Management
- Server settings/configuration
- Server invites (invite codes)
- Server icons/avatars
- Channel categories/organization
- Channel editing and deletion

### File & Media
- ✅ Avatar image uploads (user and server-specific)
- ✅ Image format validation and size limits
- File uploads (images, documents)
- Image preview and gallery
- Drag-and-drop upload

## 🔮 Future (Later)

### Advanced Features
- Voice channels (WebRTC)
- Video chat
- Screen sharing
- Threads/replies
- Message pinning
- Notification system (push, email)

### Moderation & Administration
- User banning/kicking
- Message moderation
- Audit logs
- Report system
- Custom role creation
- Granular permissions

### Customization
- User preferences/settings
- Server themes
- Custom emojis
- Profile customization
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
- [ ] Implement proper logging (Serilog)
- [ ] Add API documentation (Swagger/OpenAPI)
- [ ] Performance monitoring and metrics
- [ ] Production database migration strategy
- [ ] Container deployment (Docker)
- [ ] CI/CD pipeline enhancements
