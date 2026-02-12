# Features

This document tracks implemented, in-progress, and planned features for Codec.

## ✅ Implemented (MVP)

### Authentication & User Management
- ✅ Google Sign-In integration
- ✅ JWT ID token validation by API
- ✅ User profile display (name, email, avatar)
- ✅ User identity mapping (Google subject to internal User ID)
- ✅ Auto user creation on first sign-in

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

### UI/UX
- ✅ Discord-inspired dark theme (CSS custom properties, blurple accent)
- ✅ Three-column layout: server icon rail, channel sidebar, chat area
- ✅ Fourth column: members sidebar (responsive, hidden on smaller screens)
- ✅ Server icon rail with circular icons, active pill indicator, hover morph
- ✅ Channel list with `#` hash icons and active/hover states
- ✅ Message feed with avatars, grouped consecutive messages, hover timestamps
- ✅ Inline message composer with send icon and focus glow
- ✅ User panel pinned to bottom of channel sidebar
- ✅ Members sidebar grouped by role (Owner, Admin, Member)
- ✅ Loading states for async operations
- ✅ Error handling and display (banner in chat area)
- ✅ Responsive breakpoints (mobile, tablet, desktop)
- ✅ Accessibility: focus-visible outlines, prefers-reduced-motion, semantic HTML, ARIA labels
- ✅ Design specification documented in `docs/DESIGN.md`

### API Infrastructure
- ✅ Health check endpoint (`/health`)
- ✅ Controller-based RESTful API design (`[ApiController]`)
- ✅ Shared `UserService` for user resolution and membership checks
- ✅ CORS configuration for local development
- ✅ SQLite database with EF Core
- ✅ Automatic database migrations in dev
- ✅ Seed data for development

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
- Direct messages (1-on-1 chat)
- Message editing and deletion
- Message reactions/emojis
- Rich text formatting (markdown)
- @mentions
- Message search

### Real-time Features
- SignalR/WebSocket integration
- Live message updates
- Presence indicators (online/offline/away)
- Typing indicators
- Real-time member list updates

### Server Management
- Server settings/configuration
- Server invites (invite codes)
- Server icons/avatars
- Channel categories/organization
- Channel editing and deletion

### File & Media
- File uploads (images, documents)
- Image preview and gallery
- File size limits and validation
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
