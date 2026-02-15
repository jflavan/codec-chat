# Architecture

## Overview
Codec is a modern Discord-like chat application built as a monorepo. The architecture follows a clean separation between the client and server, with Google Identity Services handling authentication through ID tokens.

### Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 10 Web API (Controller-based APIs)
- **Real-time:** SignalR (WebSockets with automatic fallback)
- **Database:** PostgreSQL with Entity Framework Core 10 (Npgsql)
- **Authentication:** Google Identity Services (ID token validation)
- **Deployment:** Containerized on Azure Container Apps (Docker multi-stage builds)

## System Components

### Web Client (SvelteKit)
- **Location:** `apps/web/`
- **Framework:** SvelteKit 2.x with Svelte 5 runes
- **Language:** TypeScript (ES2022 target)
- **Build Tool:** Vite 7
- **Key Features:**
  - Server-side rendering (SSR) capable
  - Client-side Google Sign-In integration
  - Reactive state management with Svelte 5 runes (`$state`, `$derived`)
  - Type-safe API client
  - Modular component architecture with context-based dependency injection

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
│   ├── utils/
│   │   └── format.ts       # Date/time formatting helpers
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
│   │   │   ├── ImagePreview.svelte       # Full-screen image lightbox overlay
│   │   │   ├── LinkPreviewCard.svelte    # Open Graph link preview embed card
│   │   │   ├── LinkifiedText.svelte      # Auto-linked URLs in message body
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
│   │   │   └── DmChatArea.svelte         # DM chat (header, feed, composer, typing)
│   │   ├── settings/
│   │   │   ├── UserSettingsModal.svelte   # Full-screen modal overlay shell
│   │   │   ├── SettingsSidebar.svelte     # Category navigation sidebar
│   │   │   ├── ProfileSettings.svelte     # Nickname + avatar management
│   │   │   └── AccountSettings.svelte     # Read-only info + sign out
│   │   └── members/
│   │       ├── MembersSidebar.svelte     # Members grouped by role
│   │       └── MemberItem.svelte         # Single member card
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
| `styles/` | Design tokens as CSS custom properties; global base styles |
| `utils/` | Pure utility functions (formatting, etc.) |
| `components/` | Presentational Svelte 5 components grouped by feature area |

### API Server (ASP.NET Core)
- **Location:** `apps/api/Codec.Api/`
- **Framework:** ASP.NET Core 10 with Controller-based APIs
- **Language:** C# 14 (.NET 10)
- **Database:** PostgreSQL via Entity Framework Core (Npgsql)
- **Key Features:**
  - Stateless JWT validation
  - RESTful controller-based API design (`[ApiController]`)
  - SignalR hub for real-time messaging and typing indicators
  - Shared `UserService` for cross-cutting user resolution
  - Automatic migrations (development)
  - CORS support for local development

### Real-time Layer (SignalR)
- **Hub endpoint:** `/hubs/chat`
- **Transport:** WebSockets (with automatic fallback to Server-Sent Events / Long Polling)
- **Authentication:** JWT passed via `access_token` query parameter for WebSocket connections
- **JSON serialization:** camelCase payload naming (configured via `AddJsonProtocol`) to match REST API conventions
- **Key Features:**
  - Channel-scoped groups — clients join/leave groups per channel
  - User-scoped groups — clients auto-join `user-{userId}` on connect for friend events
  - Server-scoped groups — clients auto-join `server-{serverId}` on connect for all joined servers; receives membership events (MemberJoined, MemberLeft)
  - DM channel groups — clients join `dm-{dmChannelId}` for DM-specific events
  - Real-time message broadcast on `POST /channels/{channelId}/messages`
  - DM message broadcast on `POST /dm/channels/{channelId}/messages`
  - Typing indicators (`UserTyping` / `UserStoppedTyping` events)
  - DM typing indicators (`DmTyping` / `DmStoppedTyping` events)
  - Friend-related event delivery (request received/accepted/declined/cancelled, friend removed)
  - Automatic reconnect via `withAutomaticReconnect()`

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
- `GET /servers` - List servers user is a member of
- `POST /servers` - Create a new server (authenticated user becomes Owner)
- `GET /servers/{serverId}/members` - List server members (requires membership)
- `GET /servers/{serverId}/channels` - List channels in a server (requires membership)
- `POST /servers/{serverId}/channels` - Create a channel in a server (requires Owner or Admin role)
- `POST /servers/{serverId}/avatar` - Upload a server-specific avatar (multipart/form-data, overrides global avatar in this server)
- `DELETE /servers/{serverId}/avatar` - Remove server-specific avatar, fall back to global avatar
- `DELETE /servers/{serverId}/members/{userId}` - Kick a member from the server (requires Owner or Admin role; broadcasts `KickedFromServer` via SignalR)

#### Server Invites
- `POST /servers/{serverId}/invites` - Create an invite code (requires Owner or Admin role; generates 8-char alphanumeric code)
- `GET /servers/{serverId}/invites` - List active invites (requires Owner or Admin role; filters expired invites)
- `DELETE /servers/{serverId}/invites/{inviteId}` - Revoke an invite code (requires Owner or Admin role)
- `POST /invites/{code}` - Join a server via invite code (any authenticated user; validates expiry and max uses)

#### Messaging
- `GET /channels/{channelId}/messages` - Get messages in a channel (requires membership; includes `imageUrl`, `replyContext`)
- `POST /channels/{channelId}/messages` - Post a message to a channel (requires membership; accepts optional `imageUrl`, `replyToMessageId`; broadcasts via SignalR)
- `POST /channels/{channelId}/messages/{messageId}/reactions` - Toggle an emoji reaction on a message (requires membership; broadcasts via SignalR)

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
- `GET /dm/channels/{channelId}/messages` - Get messages in a DM conversation (paginated via `before`/`limit`; includes `imageUrl`, `replyContext`)
- `POST /dm/channels/{channelId}/messages` - Send a direct message (accepts optional `imageUrl`, `replyToDirectMessageId`; broadcasts `ReceiveDm` via SignalR; reopens closed conversations)
- `DELETE /dm/channels/{channelId}` - Close a DM conversation (sets `IsOpen = false` for current user; messages preserved)

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
| `StartTyping` | `channelId: string, displayName: string` | Broadcast typing indicator to channel |
| `StopTyping` | `channelId: string, displayName: string` | Clear typing indicator |
| `JoinDmChannel` | `dmChannelId: string` | Join a DM channel group for real-time events |
| `LeaveDmChannel` | `dmChannelId: string` | Leave a DM channel group |
| `StartDmTyping` | `dmChannelId: string, displayName: string` | Broadcast typing indicator to DM partner |
| `StopDmTyping` | `dmChannelId: string, displayName: string` | Clear typing indicator |

#### Server → Client Events
| Event | Payload | Description |
|-------|---------|-------------|
| `ReceiveMessage` | `{ id, authorName, authorUserId, body, createdAt, channelId, reactions, imageUrl, linkPreviews, replyContext }` | New message posted to current channel |
| `UserTyping` | `channelId: string, displayName: string` | Another user started typing |
| `UserStoppedTyping` | `channelId: string, displayName: string` | Another user stopped typing |
| `ReactionUpdated` | `{ messageId, channelId, reactions: [{ emoji, count, userIds }] }` | Reaction toggled on a message |
| `FriendRequestReceived` | `{ requestId, requester: { id, displayName, avatarUrl }, createdAt }` | Friend request received (sent to recipient's user group) |
| `FriendRequestAccepted` | `{ friendshipId, user: { id, displayName, avatarUrl }, since }` | Friend request accepted (sent to requester's user group) |
| `FriendRequestDeclined` | `{ requestId }` | Friend request declined (sent to requester's user group) |
| `FriendRequestCancelled` | `{ requestId }` | Friend request cancelled (sent to recipient's user group) |
| `FriendRemoved` | `{ friendshipId, userId }` | Friend removed (sent to the other participant's user group) |
| `ReceiveDm` | `{ id, dmChannelId, authorUserId, authorName, body, createdAt, imageUrl, linkPreviews, replyContext }` | New DM received (sent to DM channel group + recipient user group) |
| `DmTyping` | `{ dmChannelId, displayName }` | DM partner started typing |
| `DmStoppedTyping` | `{ dmChannelId, displayName }` | DM partner stopped typing |
| `DmConversationOpened` | `{ dmChannelId, participant: { id, displayName, avatarUrl } }` | A new DM conversation was opened (recipient's user group) |
| `KickedFromServer` | `{ serverId, serverName }` | User was kicked from a server (sent to kicked user's user group; displayed as transient overlay banner with 5s fade-out) |
| `MemberJoined` | `{ serverId }` | A new member joined the server (sent to server group; triggers member list refresh) |
| `MemberLeft` | `{ serverId }` | A member left or was kicked from the server (sent to server group; triggers member list refresh) |
| `LinkPreviewsReady` | `{ messageId, channelId?, dmChannelId?, linkPreviews: [...] }` | Link preview metadata fetched — frontend patches the message's `linkPreviews` array |

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
User ────┬──── ServerMember ──── Server
         │                         │
         ├──── Message ──────── Channel
         │        │
         ├──── Reaction ────────┘
         │
         ├──── Friendship ──── User
         │    (Requester)     (Recipient)
         │
         └──── DmChannelMember ──── DmChannel
                                       │
                                  DirectMessage

Message ───────┐
               ├──── LinkPreview
DirectMessage ─┘
```

### Core Entities

#### User
- Internal representation of authenticated users
- Linked to Google identity via `GoogleSubject`
- Fields: Id, GoogleSubject, DisplayName, Nickname, Email, AvatarUrl, CustomAvatarPath
- Effective display name: `Nickname ?? DisplayName`

#### Server
- Top-level organizational unit (like Discord servers)
- Contains channels and has members
- Fields: Id, Name

#### ServerMember
- Join table linking users to servers
- Tracks role and join date
- Fields: ServerId, UserId, Role (Owner/Admin/Member), JoinedAt, CustomAvatarPath

#### Channel
- Text communication channel within a server
- Fields: Id, Name, ServerId

#### Message
- Individual chat message in a channel
- Fields: Id, ChannelId, AuthorUserId, AuthorName, Body, ImageUrl (nullable), ReplyToMessageId (nullable, self-referencing FK), CreatedAt
- Has many `Reaction` entries
- Has many `LinkPreview` entries (max 5, fetched asynchronously after message is posted)
- Self-referencing FK with ON DELETE SET NULL (orphaned replies show "Original message was deleted")

#### Reaction
- Emoji reaction on a message by a user
- Fields: Id, MessageId, UserId, Emoji, CreatedAt
- Unique constraint on (MessageId, UserId, Emoji) — one reaction per emoji per user per message

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
- Fields: Id, DmChannelId, AuthorUserId, AuthorName, Body, ImageUrl (nullable), ReplyToDirectMessageId (nullable, self-referencing FK), CreatedAt
- Follows the same shape as the server `Message` entity
- Has many `LinkPreview` entries (max 5, fetched asynchronously after message is posted)
- Self-referencing FK with ON DELETE SET NULL (orphaned replies show "Original message was deleted")

#### LinkPreview
- URL metadata extracted from a message body (Open Graph + HTML meta fallbacks)
- Fields: Id, MessageId (nullable FK), DirectMessageId (nullable FK), Url, Title, Description, ImageUrl, SiteName, CanonicalUrl, FetchedAt, Status
- Check constraint: exactly one of MessageId or DirectMessageId must be non-null
- Fetched asynchronously by `LinkPreviewService` after message posting; delivered to clients via `LinkPreviewsReady` SignalR event
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
  }
}
```

## Security Considerations

### Current Implementation
- ✅ JWT signature validation
- ✅ Audience validation (client ID)
- ✅ Issuer validation (Google)
- ✅ Token expiration checking
- ✅ CORS restrictions (environment-driven)
- ✅ User identity isolation (membership checks)
- ✅ Controller-level `[Authorize]` attribute enforcement
- ✅ Rate limiting (fixed window, 100 req/min)
- ✅ Structured request logging (Serilog)
- ✅ Content Security Policy (CSP) headers
- ✅ Security headers (X-Content-Type-Options, X-Frame-Options, Referrer-Policy)
- ✅ Forwarded headers for reverse proxy (Azure Container Apps)
- ✅ SSRF protection on link preview fetching (private IP blocking, DNS rebinding prevention)
- ✅ Secrets management via Azure Key Vault (production)
- ✅ Managed Identity for all Azure service-to-service auth (no connection strings for blob/ACR)

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
                     │  CI → CD (build, push, migrate, deploy, smoke)      │
                     │  Infra (Bicep what-if → deploy)                     │
                     │  OIDC federated credentials (no long-lived secrets) │
                     └─────────────────────────────────────────────────────┘
```

See [DEPLOYMENT.md](DEPLOYMENT.md) for full deployment instructions, rollback procedures, and troubleshooting.

## Performance Considerations

### Current Optimizations
- Async/await throughout API
- Efficient EF Core queries (AsNoTracking)
- Vite build optimization
- Tree-shaking and code splitting
- SignalR for real-time message delivery and typing indicators (eliminates polling)
- Channel-scoped SignalR groups (targeted broadcasts, not global fan-out)

### Future Improvements
- Response caching
- Database indexing strategy
- CDN for static assets
- Connection pooling
- Query optimization with compiled queries
