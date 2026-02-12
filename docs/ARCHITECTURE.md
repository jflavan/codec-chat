# Architecture

## Overview
Codec is a modern Discord-like chat application built as a monorepo. The architecture follows a clean separation between the client and server, with Google Identity Services handling authentication through ID tokens.

### Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 9 Web API (Controller-based APIs)
- **Real-time:** SignalR (WebSockets with automatic fallback)
- **Database:** SQLite with Entity Framework Core 9
- **Authentication:** Google Identity Services (ID token validation)
- **Deployment:** Containerized (Docker support)

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
│   │   │   └── ServerSidebar.svelte      # Server icon rail (create/discover/join)
│   │   ├── channel-sidebar/
│   │   │   ├── ChannelSidebar.svelte     # Channel list & create form
│   │   │   └── UserPanel.svelte          # User avatar/name/role & sign-out
│   │   ├── chat/
│   │   │   ├── ChatArea.svelte           # Chat shell (header, feed, composer)
│   │   │   ├── MessageFeed.svelte        # Scrollable message list with grouping
│   │   │   ├── MessageItem.svelte        # Single message (grouped/ungrouped)
│   │   │   ├── ReactionBar.svelte        # Reaction pills (emoji + count)
│   │   │   ├── Composer.svelte           # Message input with send button
│   │   │   └── TypingIndicator.svelte    # Animated typing dots
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
      ├── ChatArea           → getAppState()
      │   ├── MessageFeed    → getAppState()
      │   ├── Composer       → getAppState()
      │   └── TypingIndicator → getAppState()
      └── MembersSidebar     → getAppState()
          └── MemberItem     (receives props, no context needed)
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
- **Framework:** ASP.NET Core 9 with Controller-based APIs
- **Language:** C# 14 (.NET 9)
- **Database:** SQLite via Entity Framework Core
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
  - Real-time message broadcast on `POST /channels/{channelId}/messages`
  - Typing indicators (`UserTyping` / `UserStoppedTyping` events)
  - Automatic reconnect via `withAutomaticReconnect()`

### Data Layer
- **ORM:** Entity Framework Core 9
- **Database:** SQLite (development and initial production)
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
- `GET /me` - Get current user profile
- `POST /me/avatar` - Upload a custom global avatar (multipart/form-data, 10 MB max; JPG, JPEG, PNG, WebP, GIF)
- `DELETE /me/avatar` - Remove custom avatar, revert to Google profile picture

#### Server Management
- `GET /servers` - List servers user is a member of
- `GET /servers/discover` - Browse all available servers
- `POST /servers` - Create a new server (authenticated user becomes Owner)
- `POST /servers/{serverId}/join` - Join a server
- `GET /servers/{serverId}/members` - List server members (requires membership)
- `GET /servers/{serverId}/channels` - List channels in a server (requires membership)
- `POST /servers/{serverId}/channels` - Create a channel in a server (requires Owner or Admin role)
- `POST /servers/{serverId}/avatar` - Upload a server-specific avatar (multipart/form-data, overrides global avatar in this server)
- `DELETE /servers/{serverId}/avatar` - Remove server-specific avatar, fall back to global avatar

#### Messaging
- `GET /channels/{channelId}/messages` - Get messages in a channel (requires membership)
- `POST /channels/{channelId}/messages` - Post a message to a channel (requires membership; broadcasts via SignalR)
- `POST /channels/{channelId}/messages/{messageId}/reactions` - Toggle an emoji reaction on a message (requires membership; broadcasts via SignalR)

### SignalR Hub (`/hubs/chat`)

The SignalR hub provides real-time communication. Clients connect with their JWT token via query string.

**Connection URL:** `{API_BASE_URL}/hubs/chat?access_token={JWT}`

#### Client → Server Methods
| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinChannel` | `channelId: string` | Join a channel group to receive real-time events |
| `LeaveChannel` | `channelId: string` | Leave a channel group |
| `StartTyping` | `channelId: string, displayName: string` | Broadcast typing indicator to channel |
| `StopTyping` | `channelId: string, displayName: string` | Clear typing indicator |

#### Server → Client Events
| Event | Payload | Description |
|-------|---------|-------------|
| `ReceiveMessage` | `{ id, authorName, authorUserId, body, createdAt, channelId, reactions }` | New message posted to current channel |
| `UserTyping` | `channelId: string, displayName: string` | Another user started typing |
| `UserStoppedTyping` | `channelId: string, displayName: string` | Another user stopped typing |
| `ReactionUpdated` | `{ messageId, channelId, reactions: [{ emoji, count, userIds }] }` | Reaction toggled on a message |

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
         └──── Reaction ────────┘
```

### Core Entities

#### User
- Internal representation of authenticated users
- Linked to Google identity via `GoogleSubject`
- Fields: Id, GoogleSubject, DisplayName, Email, AvatarUrl, CustomAvatarPath

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
- Fields: Id, ChannelId, AuthorUserId, AuthorName, Body, CreatedAt
- Has many `Reaction` entries

#### Reaction
- Emoji reaction on a message by a user
- Fields: Id, MessageId, UserId, Emoji, CreatedAt
- Unique constraint on (MessageId, UserId, Emoji) — one reaction per emoji per user per message

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
    "Default": "Data Source=codec-dev.db"
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
- ✅ CORS restrictions
- ✅ User identity isolation (membership checks)
- ✅ Controller-level `[Authorize]` attribute enforcement

### Production Requirements
- 🔒 HTTPS enforcement
- 🔒 Rate limiting
- 🔒 Request logging and monitoring
- 🔒 Secrets management (Azure Key Vault, etc.)
- 🔒 Database encryption at rest
- 🔒 Content Security Policy (CSP)
- 🔒 Input sanitization and validation

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
│  │  SQLite    │  │
│  │  codec-dev │  │
│  └────────────┘  │
└──────────────────┘
```

### Production (Future)
```
┌──────────────┐       ┌──────────────┐
│   CDN /      │       │  Container   │
│   Static     │       │  Registry    │
│   Hosting    │       └──────────────┘
└──────┬───────┘              │
       │                      │
       v                      v
┌────────────────────────────────┐
│   Load Balancer / Ingress      │
└────────┬───────────────────────┘
         │
    ┌────┴────┐
    │         │
    v         v
┌────────┐ ┌────────┐
│  Web   │ │  API   │
│  SPA   │ │  Pod   │
└────────┘ └───┬────┘
               │
               v
        ┌──────────────┐
        │  PostgreSQL  │
        │  or Azure    │
        │  SQL         │
        └──────────────┘
```

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
