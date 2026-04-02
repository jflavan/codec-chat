# Testing

Codec uses a multi-layer testing strategy combining unit tests and integration tests across both the API and web projects.

## Test Suites

| Suite | Framework | Tests | Coverage Target |
|-------|-----------|-------|----------------|
| API Unit Tests | xUnit + FluentAssertions + Moq | 1,188 | Services: 95%+ |
| API Integration Tests | xUnit + WebApplicationFactory + Testcontainers | 177 | Controllers + Hub: 80%+ |
| Web Unit Tests | Vitest + jsdom | 177 | Utilities + API client: 98%+ |

**Total: 1,542 tests**

## Running Tests

### Web Tests

```bash
cd apps/web
npm test              # Run all tests
npm run test:watch    # Watch mode
npm run test:coverage # Run with coverage report
```

### API Unit Tests

```bash
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
```

### API Integration Tests

Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) to spin up disposable PostgreSQL and Redis containers. **Requires Docker to be running.**

```bash
dotnet test apps/api/Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj
```

### Run All API Tests

```bash
dotnet test Codec.sln
```

### Coverage Reports

```bash
# Web coverage
cd apps/web && npm run test:coverage

# API combined coverage (unit + integration merged)
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj \
  /p:CollectCoverage=true /p:CoverletOutput=./coverage/ \
  /p:CoverletOutputFormat=json "/p:Include=[Codec.Api]*" \
  "/p:Exclude=[Codec.Api]Codec.Api.Migrations*"

dotnet test apps/api/Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj \
  /p:CollectCoverage=true /p:CoverletOutput=./coverage/ \
  /p:CoverletOutputFormat=cobertura "/p:Include=[Codec.Api]*" \
  "/p:Exclude=[Codec.Api]Codec.Api.Migrations*" \
  /p:MergeWith=./coverage/coverage.json
```

## Architecture

### Web Tests (`apps/web/src/**/*.spec.ts`)

Pure unit tests using Vitest with jsdom environment. No browser or API server required.

**What's tested:**
- `lib/utils/` — format, YouTube URL extraction, emoji regex, emoji frequency, theme management (100% coverage)
- `lib/auth/session.ts` — JWT expiry checks, session persistence, token management (100% coverage)
- `lib/api/client.ts` — All 50+ ApiClient methods, auth methods (register, login, refresh, link-google), error handling, 401 retry logic (97%+ coverage)

**What's excluded from coverage** (requires browser APIs or framework integration):
- `lib/state/*.svelte.ts` — Svelte 5 reactive stores with context injection
- `lib/services/chat-hub.ts` — SignalR client lifecycle
- `lib/services/voice-service.ts` — mediasoup WebRTC client
- `lib/auth/google.ts` — Google Identity Services SDK

### API Unit Tests (`apps/api/Codec.Api.Tests/`)

Tests services and controllers in isolation using mocked dependencies.

**Services tested** (95%+ line coverage):
- `UserService` — user creation/lookup, dual-auth resolution (Google + local JWT), membership checks, role enforcement, permission checks
- `TokenService` — JWT generation, refresh token creation/validation/revocation, bulk revocation
- `AvatarService` — file validation, upload paths, hash-based filenames
- `ImageUploadService` — image validation and storage
- `FileUploadService` — file attachment validation, storage, content-type detection
- `ImageProxyService` — external image proxying, SSRF protection, content-type validation
- `CustomEmojiService` — emoji validation, storage, deletion
- `LinkPreviewService` — URL extraction, SSRF protection, HTML metadata parsing
- `PresenceTracker` — connection tracking, status aggregation, timeout scanning
- `PresenceBackgroundService` — idle/offline transition scanning
- `MessageCacheService` — Redis cache operations, graceful degradation
- `OAuthProviderService` — GitHub/Discord OAuth code exchange, profile fetching, account linking
- `SamlService` — AuthnRequest generation, SAML response validation, XML signature verification, JIT provisioning
- `WebhookService` — event dispatch, HMAC-SHA256 signing, retry with exponential backoff
- `PushNotificationService` — VAPID push delivery, subscription management, 410 Gone handling
- `VoiceCallTimeoutService` — call timeout scanning, stale record cleanup
- `LocalFileStorageService` — local file storage operations

**Controllers tested:**
- `AuthController` — register (201, 409 conflict), login (200, 401), refresh (token rotation, expiry), email normalization, OAuth callbacks (GitHub, Discord)
- `HealthController`, `UsersController` (status messages, profile), `AvatarsController`, `ImageUploadsController`
- `ImageProxyController` — URL proxying, SSRF rejection, content-type validation
- `IssuesController`, `PresenceController`, `FriendsController`, `VoiceController`
- `PushSubscriptionsController` — subscribe/unsubscribe, VAPID key endpoint
- `SamlController` — SSO login redirect, ACS callback, IdP CRUD, metadata import/export
- `RolesController` — role CRUD, permission management, position reordering
- `ServersController` — server CRUD, channels, invites, emojis, member management, bans, webhooks
- `ChannelsController` — messages, pagination, reactions, around mode, purge, file attachments
- `DmController` — DM channels, messages, reactions, close/reopen, file attachments

**Testing patterns:**
- `Microsoft.EntityFrameworkCore.InMemory` for database operations
- `Moq` for interface mocking (IUserService, IAvatarService, IHubContext, etc.)
- `ClaimsPrincipal` setup for authenticated controller context

### API Integration Tests (`apps/api/Codec.Api.IntegrationTests/`)

Full-pipeline tests using `WebApplicationFactory<Program>` with real PostgreSQL and Redis via Testcontainers.

**Infrastructure:**
- `CodecWebFactory` — boots the complete API pipeline against disposable containers
- `FakeAuthHandler` — bypasses Google JWT validation; accepts base64-encoded claim payloads as Bearer tokens (also supports SignalR `?access_token=` query parameter)
- `IntegrationTestBase` — shared test helpers for creating servers, channels, messages, friendships

**What's tested:**
- Full HTTP request pipeline (routing, model binding, middleware, auth, exception handling)
- `ExecuteDeleteAsync` code paths (server/channel/message deletion with cascade)
- SignalR hub connections, channel groups, typing indicators, presence heartbeat
- Voice call lifecycle via hub (start → decline/answer → end)
- Server lifecycle (create → update → add channels → invite → join → delete)
- Message flow (post → edit → react → delete → purge)
- DM conversation flow (friend request → accept → DM → send → close)
- Invite flow (create → join → already-member → expired → max-uses)
- Member management (kick, role promotion/demotion)
- Custom roles and permissions (create → assign → hierarchy enforcement)
- User banning (ban → invite-blocked → unban)
- File uploads (avatars, images, server icons, custom emojis, file attachments)
- Search (server messages, DM messages, with filters)
- Voice state (TURN credentials, voice channel states)
- SAML SSO (AuthnRequest → ACS callback → JIT provisioning)
- Webhook lifecycle (create → event dispatch → delivery log)
- Push subscriptions (subscribe → notification → unsubscribe)
- User status messages (set → broadcast → clear)
- EF Core migrations against real PostgreSQL

## Coverage Summary

### Web (unit-testable code)
- **98.59% line coverage**, 97.77% function coverage
- 92.33% statement coverage, 74.81% branch coverage
- utils: 100%, auth/session: 100%, api/client: 97.7%

### API (combined unit + integration)
- **80%+ line coverage**, 80%+ method coverage
- Core services: 95%+ line coverage
- Controllers + services: 80%+ combined line coverage

### Untestable Code (requires external infrastructure)
The following code is excluded from unit/integration test coverage targets because it requires infrastructure not available in the test environment:

- **ChatHub voice signaling** (~500 lines) — `ConnectTransport`, `Produce`, `Consume` require a running mediasoup SFU server
- **Program.cs** — DI container setup, middleware pipeline configuration
- **AzureBlobStorageService** — requires Azure Blob Storage (tests use `LocalFileStorageService`)

## Continuous Integration

All test suites run automatically on every push and pull request to `main` via GitHub Actions (`.github/workflows/ci.yml`). The CI pipeline includes three parallel test jobs:

| Job | What it runs | Notes |
|-----|-------------|-------|
| `test-web` | `npm test` (Vitest) | Runs after web build succeeds |
| `test-api` | `dotnet test` on `Codec.Api.Tests` | Runs after API build succeeds |
| `test-api-integration` | `dotnet test` on `Codec.Api.IntegrationTests` | Uses Testcontainers (Docker available on GitHub-hosted runners) |

Tests must pass before Docker image builds and deployment can proceed.

## Adding New Tests

### Web
Create `*.spec.ts` files alongside the source files. Tests run in jsdom with `localStorage` polyfill (see `src/test-setup.ts`).

### API Unit Tests
Add test classes under `Codec.Api.Tests/Services/` or `Codec.Api.Tests/Controllers/`. Use `InMemory` EF Core for DB tests, `Moq` for service mocks.

### API Integration Tests
Add test classes under `Codec.Api.IntegrationTests/Tests/`. Extend `IntegrationTestBase` for shared factory and helpers. Use unique `googleSubject` strings per test to avoid user collisions.
