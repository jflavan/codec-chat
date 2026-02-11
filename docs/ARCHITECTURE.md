# Architecture

## Overview
Codec is a modern Discord-like chat application built as a monorepo. The architecture follows a clean separation between the client and server, with Google Identity Services handling authentication through ID tokens.

### Technology Stack
- **Frontend:** SvelteKit 2.x, TypeScript, Vite
- **Backend:** ASP.NET Core 9 Web API (Minimal APIs)
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
  - Reactive state management with Svelte stores
  - Type-safe API client

### API Server (ASP.NET Core)
- **Location:** `apps/api/Codec.Api/`
- **Framework:** ASP.NET Core 9 with Minimal APIs
- **Language:** C# 14 (.NET 9)
- **Database:** SQLite via Entity Framework Core
- **Key Features:**
  - Stateless JWT validation
  - RESTful API design
  - Automatic migrations (development)
  - CORS support for local development

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

## API Endpoints

### Public Endpoints
- `GET /` - API info (development only)
- `GET /health` - Health check

### Authenticated Endpoints

#### User Profile
- `GET /me` - Get current user profile

#### Server Management
- `GET /servers` - List servers user is a member of
- `GET /servers/discover` - Browse all available servers
- `POST /servers/{serverId}/join` - Join a server
- `GET /servers/{serverId}/members` - List server members (requires membership)
- `GET /servers/{serverId}/channels` - List channels in a server (requires membership)

#### Messaging
- `GET /channels/{channelId}/messages` - Get messages in a channel (requires membership)
- `POST /channels/{channelId}/messages` - Post a message to a channel (requires membership)

### Request/Response Format
All endpoints use JSON for request bodies and responses.

**Example Request:**
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
  "channelId": "550e8400-e29b-41d4-a716-446655440000"
}
```

## Data Model

### Entity Relationships
```
User ────┬──── ServerMember ──── Server
         │                         │
         └──── Message ──────── Channel
```

### Core Entities

#### User
- Internal representation of authenticated users
- Linked to Google identity via `GoogleSubject`
- Fields: Id, GoogleSubject, DisplayName, Email, AvatarUrl

#### Server
- Top-level organizational unit (like Discord servers)
- Contains channels and has members
- Fields: Id, Name

#### ServerMember
- Join table linking users to servers
- Tracks role and join date
- Fields: ServerId, UserId, Role (Owner/Admin/Member), JoinedAt

#### Channel
- Text communication channel within a server
- Fields: Id, Name, ServerId

#### Message
- Individual chat message in a channel
- Fields: Id, ChannelId, AuthorUserId, AuthorName, Body, CreatedAt

## Configuration

### Web Client (`.env`)
```env
PUBLIC_API_BASE_URL=http://localhost:5000
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
    "AllowedOrigins": ["http://localhost:5173"]
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
│  │  Vite Dev  │  │ :5173
│  └────────────┘  │
│                  │
│  ┌────────────┐  │
│  │ ASP.NET    │  │ :5000
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

### Future Improvements
- Response caching
- Database indexing strategy
- SignalR for real-time updates (reduce polling)
- CDN for static assets
- Connection pooling
- Query optimization with compiled queries
