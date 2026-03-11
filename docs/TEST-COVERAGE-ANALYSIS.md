# Test Coverage Analysis

**Date:** 2026-03-11

## Current State

The codebase currently has **zero automated tests** across both the API and web frontend. CI only verifies that the code compiles (`dotnet build`, `npm run build`) and passes type-checking (`npm run check`). There are no unit tests, integration tests, or end-to-end tests running in the pipeline.

---

## Recommended Test Strategy

### Phase 1 — High-Value, Low-Effort (Unit Tests)

These areas have complex logic that is relatively easy to test in isolation and would catch the most bugs per line of test code.

#### 1. API Services (xUnit + Moq)

**PresenceTracker** (`Services/PresenceTracker.cs`)
- State machine logic: online → idle → offline transitions
- Aggregate status across multiple connections (best-of)
- Timeout scanning (`ScanForTimeouts`)
- Concurrent connect/disconnect correctness
- *Why:* Pure in-memory logic, no DB dependency, high complexity

**LinkPreviewService** (`Services/LinkPreviewService.cs`)
- `ExtractUrls()` — regex URL extraction, max 5 per message
- `IsAllowedUrl()` — SSRF protection (private IPs, loopback, internal hostnames)
- `ParseMetadata()` — OG tag parsing, HTML fallbacks
- *Why:* Security-critical (SSRF), pure functions, easy to test with fixture HTML

**UserService** (`Services/UserService.cs`)
- `GetOrCreateUserAsync()` — concurrent creation race condition handling
- Role-based authorization checks (`EnsureAdminAsync`, `EnsureOwnerAsync`, `EnsureMemberAsync`)
- `GetEffectiveDisplayName()` — nickname vs. Google name fallback
- *Why:* Core authorization logic, testable with an in-memory DB

**VoiceCallTimeoutService** (`Services/VoiceCallTimeoutService.cs`)
- 30-second ringing timeout
- Stale call cleanup (60s background loop)
- State transitions: ringing → missed, active → ended
- *Why:* Timer-based state logic, bugs here cause stuck calls

**AvatarService / ImageUploadService** (`Services/`)
- File type/size validation
- Hash-based filename generation
- *Why:* Validation logic is pure, easy to test

#### 2. Web Utilities (Vitest)

**`lib/auth/session.ts`**
- `isTokenExpired()` — expiration check with 60s buffer
- `isSessionExpired()` — 7-day max session lifetime
- `decodeJwtPayload()` — base64 JWT decode
- *Why:* Pure functions, auth bugs are high-severity

**`lib/utils/youtube.ts`**
- `extractYouTubeVideoId()` — 6+ URL formats (watch, shorts, embed, live, youtu.be, m.youtube)
- `extractYouTubeUrls()` — deduplicate by video ID
- *Why:* Pure function with many edge cases

**`lib/utils/format.ts`**
- `formatTime()` — ISO → locale string with LRU cache
- *Why:* Quick win, trivial to test

**`lib/utils/emoji-frequency.ts`**
- `recordEmojiUse()` / `getFrequentEmojis()` — localStorage frequency tracking
- *Why:* Stateful logic with sort/pad behavior

**`lib/utils/emoji-regex.ts`**
- Custom emoji shortcode matching (`:pepe:` exact and global)
- *Why:* Regex correctness is fragile

---

### Phase 2 — Medium Effort, High Impact (Integration Tests)

#### 3. API Controller Integration Tests (WebApplicationFactory)

Use `WebApplicationFactory<Program>` with an in-memory or Testcontainers PostgreSQL database to test full request/response cycles.

**Priority endpoints:**

| Controller | Key Scenarios |
|---|---|
| **ChannelsController** | Create message, edit message, delete message (author-only + admin override), toggle reaction, cursor-based pagination (`before`, `around`), mention extraction |
| **FriendsController** | Full lifecycle: send request → accept → list friends → remove. Decline/cancel paths. Duplicate request handling |
| **ServersController** | Create server, manage members (add/remove/role), channel CRUD, channel reorder, custom emoji CRUD, invite create/use/revoke |
| **DmController** | Open DM (requires friendship), send/edit/delete DM, DM reactions, voice call initiate/answer/decline/end |
| **UsersController** | Get profile, set/remove nickname, user search |
| **AvatarsController** | Upload/delete global avatar, upload/delete server avatar, file type rejection |
| **VoiceController** | TURN credential generation (HMAC-SHA256), voice state updates, active call retrieval |

**Cross-cutting concerns to cover:**
- **Authentication:** Unauthenticated requests return 401
- **Authorization:** Non-members get 403 on server resources, non-admins blocked from admin actions, non-owners blocked from owner actions
- **Rate limiting:** 100 req/min fixed window
- **Input validation:** Oversized payloads, invalid MIME types, missing required fields

#### 4. API Client Tests (Vitest + MSW)

Use [Mock Service Worker](https://mswjs.io/) to test `ApiClient` without a real backend:
- 401 auto-retry with token refresh (single concurrent refresh)
- Error response parsing
- FormData construction for image uploads
- All typed methods match expected request shape

---

### Phase 3 — Higher Effort, Comprehensive Coverage

#### 5. SignalR Hub Tests

**ChatHub** (`Hubs/ChatHub.cs`) — 49KB, the largest file in the codebase.
- Message broadcast to correct channel groups
- Typing indicator fan-out
- Voice signaling (WebRTC offer/answer/ICE relay)
- Group membership on connect/disconnect
- Presence heartbeat handling
- Connection lifecycle (auto-leave voice on disconnect)

*Approach:* Use `TestServer` + SignalR test client, or extract hub logic into testable services.

#### 6. Component Tests (Vitest + @testing-library/svelte)

Focus on components with non-trivial logic:

| Component | What to Test |
|---|---|
| **Composer** | Mention autocomplete (`@` trigger, selection, wire format conversion), image paste, Enter to send, Shift+Enter for newline |
| **MessageFeed** | Scroll-to-bottom on new message, load older messages on scroll-up, date separators |
| **EmojiPicker** | Category navigation, search filtering, recent emoji tracking, custom emoji display |
| **ChatArea / DmChatArea** | Channel switching clears messages, loading states |
| **SearchPanel** | Filter application, pagination, result rendering |

#### 7. AppState Tests (Vitest)

Test the 80+ methods in `app-state.svelte.ts`:
- Message operations: send, edit, delete, reaction toggle
- Friend lifecycle: request → accept/decline/cancel → remove
- Server/channel CRUD and reorder
- Voice state transitions
- Error handling (API failures set error state)
- Optimistic updates and rollback

*Challenge:* Requires mocking `ApiClient` and `ChatHubService`. Consider extracting pure business logic into testable functions.

#### 8. ChatHubService Event Handling (Vitest)

Test that inbound SignalR events correctly update state:
- `ReceiveMessage` → message appears in correct channel
- `ReactionUpdated` → reaction count changes
- `UserPresenceChanged` → presence dot updates
- `KickedFromServer` → server removed from list, redirect
- `VoiceCallRinging` → incoming call overlay appears

---

### Phase 4 — End-to-End Tests

#### 9. Playwright E2E Tests

A Playwright skill already exists at `.github/skills/webapp-testing/` but is not in CI.

**Critical user flows:**
- Sign in → see server list → select channel → send message → see message appear
- Create server → create channel → invite member
- Send friend request → accept → open DM → send DM
- Join voice channel → see other members → leave
- Upload avatar → see it in profile

*Note:* Requires test Google accounts or a mock auth bypass for testing.

---

## Infrastructure Setup Required

### API Test Project
```bash
# Create xUnit test project
dotnet new xunit -n Codec.Api.Tests -o apps/api/Codec.Api.Tests
cd apps/api/Codec.Api.Tests
dotnet add reference ../Codec.Api/Codec.Api.csproj
dotnet add package Moq
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package FluentAssertions
dotnet add package Testcontainers.PostgreSql  # for integration tests
```

### Web Test Setup
```bash
cd apps/web
npm install -D vitest @testing-library/svelte @testing-library/jest-dom jsdom
npm install -D msw  # for API client tests
```

Add to `vite.config.ts`:
```ts
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
    setupFiles: ['src/test-setup.ts']
  }
});
```

### CI Pipeline Updates (`.github/workflows/ci.yml`)
Add steps:
```yaml
- name: Run API tests
  run: dotnet test apps/api/Codec.Api.Tests --configuration Release

- name: Run web tests
  run: |
    cd apps/web
    npm run test:run
```

---

## Prioritized Implementation Order

| Priority | Area | Effort | Impact | Bug Risk Reduction |
|---|---|---|---|---|
| **P0** | API service unit tests (PresenceTracker, LinkPreviewService, UserService) | Low | High | Auth bypass, SSRF, presence bugs |
| **P0** | Web utility unit tests (session.ts, youtube.ts, emoji-regex.ts) | Low | Medium | Auth expiry, rendering bugs |
| **P1** | API controller integration tests (Channels, Friends, Servers, DMs) | Medium | High | Data integrity, authorization holes |
| **P1** | API client tests with MSW (401 retry, error handling) | Medium | Medium | Silent auth failures |
| **P2** | ChatHub tests (message broadcast, voice signaling) | High | High | Real-time message loss |
| **P2** | Component tests (Composer, MessageFeed, EmojiPicker) | Medium | Medium | UI regressions |
| **P3** | AppState tests | High | Medium | State corruption |
| **P3** | Playwright E2E tests | High | High | Full-flow regressions |

---

## Estimated Coverage Goals

| Milestone | Target | What's Covered |
|---|---|---|
| **v1** | ~30% | Service unit tests + utility tests + core controller integration tests |
| **v2** | ~55% | Full controller coverage + API client + ChatHub |
| **v3** | ~70% | Component tests + AppState |
| **v4** | ~80%+ | E2E flows + edge cases |
