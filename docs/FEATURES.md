# Features

This document tracks implemented and planned features for Codec.

## Implemented

### Authentication & Users
- Google Sign-In with persistent sessions (1-week, `localStorage`)
- Automatic silent token refresh via Google One Tap
- User profile display (name, email, avatar)
- Nicknames — user-chosen display name overriding Google name across all surfaces
- Custom avatar upload (JPG, PNG, WebP, GIF; 10 MB max; content-hash filenames)
- Server-specific avatar overrides with fallback chain (server → global → Google → placeholder)
- User search by name, nickname, or email
- Global admin role — configurable via `GlobalAdmin:Email`; full access to all servers, channels, and messages regardless of membership

### Servers & Channels
- Server creation (creator becomes Owner)
- Role-based membership (Owner, Admin, Member) with role management UI
- Channel creation, renaming, and deletion (Owner/Admin)
- Server name editing, server icons, and server deletion
- Custom emojis — upload, rename, delete per server (PNG, JPEG, WebP, GIF; 256 KB max; 50 per server)
- Server invite codes with configurable expiry and max uses
- Kick members with real-time notification and auto-redirect

### Messaging
- Real-time message delivery via SignalR WebSockets
- Typing indicators
- Message editing and deletion (author or global admin)
- Message replies with clickable reference and scroll-to-original
- Text formatting — bold (`*text*` / `**text**`) and italic (`_text_`)
- @mentions with autocomplete picker and @here; mention badges on server/channel icons
- Emoji reactions (toggle, pills with counts, real-time sync)
- Image uploads via file picker, clipboard paste, or drag-and-drop (10 MB max)
- Image lightbox — full-size preview overlay with keyboard dismiss
- Link previews — Open Graph metadata, clickable embed cards, SSRF protection
- YouTube embeds — click-to-play inline video players via `svelte-youtube-embed`
- Progressive loading — cursor-based pagination with scroll position preservation
- Message search — full-text search with PostgreSQL trigram indexes; filters by scope, date range, and content type; jump-to-message with highlight animation

### Friends & Direct Messages
- Friend requests (send, accept, decline, cancel) with real-time SignalR notifications
- Friends list and remove friend
- Notification badge on Home icon for pending requests
- 1:1 DM conversations with real-time delivery, typing indicators, and unread badges
- Close/reopen DM conversations
- DMs support all messaging features (replies, reactions, images, formatting, search)

### Voice
- **Voice channels** — persistent voice channel type in servers; join/leave freely; real-time audio via mediasoup v3 SFU (Opus/WebRTC); mute/unmute with broadcast; participant list with avatars and mute indicators; per-user volume control; push-to-talk with configurable keybind
- **DM voice calls** — 1:1 calls from DM chat header; incoming call overlay with ring tone; 30-second ringing timeout; system messages for call events (missed, duration); call state recovery on reconnect; collision detection prevents concurrent calls

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

### API & Infrastructure
- Controller-based RESTful API with `[Authorize]` on all endpoints
- Health endpoints: `/health/live` (liveness), `/health/ready` (readiness + DB check)
- Rate limiting — fixed window, 100 req/min (429 on exceed)
- Response compression (Brotli + Gzip)
- Structured JSON logging via Serilog (Console → Container Apps → Log Analytics)
- SignalR hub (`/hubs/chat`) with WebSocket JWT auth via query string
- Global exception handler with RFC 7807 ProblemDetails responses
- EF Core with automatic migrations (dev) and migration bundles (prod CD pipeline)
- Azure deployment: Container Apps, Key Vault secrets, Bicep IaC, GitHub Actions CI/CD

## Planned

### Near-Term
- Presence indicators (online/offline/away)
- Channel categories/organization
- Link preview caching and image proxying
- Video embeds for Vimeo
- File uploads (documents, other media)

### Future
- Video chat and screen sharing
- Message pinning
- Push notifications
- User banning and message moderation
- Audit logs for admin actions
- Custom roles and granular permissions
- Status messages
- Additional OAuth providers (GitHub, Discord)
- SAML/SSO support
- Webhooks

## Technical Debt
- [ ] Unit and integration tests
- [ ] API documentation (Swagger/OpenAPI)
- [x] Structured logging (Serilog)
- [x] Production database migration strategy (EF Core bundles in CD)
- [x] Container deployment (Docker multi-stage builds, Azure Container Apps)
- [x] CI/CD pipeline (GitHub Actions with OIDC, Bicep IaC)
