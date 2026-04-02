# AppState Decomposition Design

## Problem

`app-state.svelte.ts` is a 3,999-line god class with ~100 `$state` fields, ~20 `$derived` computations, and all domain logic for auth, servers, channels, messages, DMs, friends, voice/WebRTC, search, and UI. It violates SRP, is untestable, and causes cascading issues: duplicated bootstrap code (8x), duplicated voice teardown (4x), a 600-line inline SignalR callback block, and tight coupling that prevents code splitting.

## Goal

Decompose `AppState` into 8 focused store classes with separate context keys, a backward-compatible facade, and a standalone SignalR orchestration function. The app must work identically after the change — no behavioral modifications, no new features.

## Approach

Big-bang extraction with risk mitigation:

1. **Backward-compatible facade** — `getAppState()` remains functional, returning an object that delegates to individual stores. Zero component breakage at cutover.
2. **Type-checking as verification** — `svelte-check` + TypeScript strict mode validate the full component tree.
3. **Build gate** — `npm run build` catches SSR and import issues.
4. **Existing tests pass unchanged** — 7 spec files (client, session, utils) are unaffected.

## Store Definitions

### File Structure

```
lib/state/
  auth-store.svelte.ts      (~200 lines)
  server-store.svelte.ts    (~500 lines)
  channel-store.svelte.ts   (~200 lines)
  message-store.svelte.ts   (~600 lines)
  dm-store.svelte.ts        (~400 lines)
  friend-store.svelte.ts    (~150 lines)
  voice-store.svelte.ts     (~500 lines)
  ui-store.svelte.ts        (~100 lines)
  signalr.svelte.ts         (~300 lines)
  app-state.svelte.ts       (~50 lines, facade)
  index.ts                  (re-exports)
```

### Store Responsibilities

| Store | State | Key Methods |
|-------|-------|-------------|
| **AuthStore** | `idToken`, `me`, `status`, `authType`, `needsNickname`, `needsLinking`, `emailVerified` | `init()`, `handleCredential()`, `handleOAuthCallback()`, `handleLocalAuth()`, `signOut()`, `refreshToken()`, `confirmNickname()`, `completeSignIn()` (private, deduplicates 8 bootstrap blocks) |
| **ServerStore** | `servers`, `selectedServerId`, invites, bans, roles, emojis, audit log, webhooks, categories, related loading flags | `loadServers()`, `selectServer()`, `createServer()`, `joinServer()`, `leaveServer()`, `deleteServer()`, server settings CRUD, `handleKicked()`, `handleBanned()` |
| **ChannelStore** | `channels`, `selectedChannelId`, channel permission state | `loadChannels()`, `selectChannel()`, `createChannel()`, `deleteChannel()`, `reorderChannels()` |
| **MessageStore** | `messages`, `typingUsers`, `pendingImage/File`, `replyingTo`, `messageBody`, `pendingMentions`, search state, pinned messages, reactions | `loadMessages()`, `sendMessage()`, `editMessage()`, `deleteMessage()`, `toggleReaction()`, `searchMessages()`, pin/unpin, `handleIncomingMessage()`, `handleTyping()`, `handleReactionUpdate()` |
| **DmStore** | `dmConversations`, `dmMessages`, `activeDmChannelId`, `dmTypingUsers`, `unreadDmCounts`, `dmMessageBody`, DM attachments | `loadDmConversations()`, `openDm()`, `sendDm()`, DM reactions, `handleIncomingDm()`, `handleTyping()` |
| **FriendStore** | `friends`, `incomingRequests`, `outgoingRequests`, `userSearchResults` | `loadFriends()`, `loadFriendRequests()`, `sendRequest()`, `acceptRequest()`, `removeFriend()`, `searchUsers()` |
| **VoiceStore** | Voice channel state, WebRTC state, PTT, video/screen tracks, audio graph, calls, `incomingCall` | `joinVoiceChannel()`, `leaveVoiceChannel()`, `toggleMute()`, `toggleDeafen()`, `toggleVideo()`, `toggleScreenShare()`, call methods, `teardownOnDisconnect()` (deduplicates 4 cleanup blocks) |
| **UIStore** | Modals open/close, mobile nav, theme, `error`, `isInitialLoading`, form fields (`newServerName`, `newChannelName`), `showAlphaNotification` | `openSettings()`, `closeSettings()`, `setTheme()`, `setError()`, `setTransientError()` |

Each domain store owns its own loading flags (e.g. `isLoadingMessages` on MessageStore, `isLoadingServers` on ServerStore). UIStore owns only cross-cutting UI state like `isInitialLoading` and `showAlphaNotification`.

### Dependencies (Constructor Injection)

```
AuthStore     <- ApiClient
ServerStore   <- AuthStore, ApiClient, ChatHubService
ChannelStore  <- AuthStore, ApiClient
MessageStore  <- AuthStore, ChannelStore, ApiClient
DmStore       <- AuthStore, ApiClient
FriendStore   <- AuthStore, ApiClient
VoiceStore    <- AuthStore, ApiClient, ChatHubService, VoiceService
UIStore       <- (none)
```

Stable dependencies (AuthStore, ApiClient) are injected via constructor. Stores do not depend on each other except through AuthStore and ChannelStore where strictly necessary.

### Context Keys & Accessors

Each store has its own Symbol context key and paired `createXxxStore()` / `getXxxStore()` functions:

```typescript
const AUTH_KEY = Symbol('auth-store');
export function createAuthStore(api: ApiClient): AuthStore {
    const store = new AuthStore(api);
    setContext(AUTH_KEY, store);
    return store;
}
export function getAuthStore(): AuthStore {
    return getContext<AuthStore>(AUTH_KEY);
}
```

## Backward-Compatible Facade

`app-state.svelte.ts` shrinks to ~50 lines. `createAppState()` creates all stores, sets each in context individually, then returns a facade:

```typescript
class AppState {
    constructor(
        private auth: AuthStore,
        private servers: ServerStore,
        private channels: ChannelStore,
        private messages: MessageStore,
        private dms: DmStore,
        private friends: FriendStore,
        private voice: VoiceStore,
        private ui: UIStore
    ) {}

    // Delegate all ~100 properties and methods to the appropriate store
    get idToken() { return this.auth.idToken; }
    get servers() { return this.servers.servers; }
    sendMessage(...args) { return this.messages.sendMessage(...args); }
    // ... etc
}
```

Components continue to work via `getAppState()` with zero changes. Components can then be migrated to direct store imports at any time.

## SignalR Orchestration

The 600-line inline callback block becomes a standalone `setupSignalR()` function in `signalr.svelte.ts`:

```typescript
export function setupSignalR(
    hub: ChatHubService,
    auth: AuthStore,
    servers: ServerStore,
    channels: ChannelStore,
    messages: MessageStore,
    dms: DmStore,
    friends: FriendStore,
    voice: VoiceStore,
    ui: UIStore
): void
```

Each hub callback delegates to a named `handle*` method on the appropriate store. The function contains only wiring logic — no business logic.

**Reconnection handling**: `onReconnecting` calls `voice.teardownOnDisconnect()` (the deduplicated voice cleanup) and starts the reconnect timer. `onReconnected` re-joins SignalR groups. `onClose` with error triggers `window.location.reload()` (preserving current behavior; graceful recovery is a follow-up).

## Bootstrap Deduplication

`AuthStore` gets a single private method:

```typescript
private async completeSignIn(): Promise<void> {
    await this.loadMe();
    await Promise.all([
        this.serverStore.loadServers(),
        this.friendStore.loadFriends(),
        this.friendStore.loadFriendRequests(),
        this.dmStore.loadDmConversations(),
        this.startSignalR()
    ]);
    this.ui.isInitialLoading = false;
    this.ui.showAlphaNotification = true;
    this.pushManager.checkSubscription();
}
```

Called by `init()`, `handleCredential()`, `handleOAuthCallback()`, `handleLocalAuth()`, `confirmNickname()`, etc. — replacing 8 duplicated blocks.

Note: `completeSignIn()` needs references to the other stores to trigger their loading. `AuthStore` receives a `bootstrap` callback in its constructor (a function that loads servers/friends/DMs/starts SignalR), keeping `AuthStore` decoupled from domain stores:

```typescript
class AuthStore {
    constructor(
        private api: ApiClient,
        private onSignedIn: () => Promise<void>,  // bootstrap callback
        private ui: UIStore
    ) {}

    private async completeSignIn(): Promise<void> {
        await this.loadMe();
        await this.onSignedIn();
        this.ui.isInitialLoading = false;
        this.ui.showAlphaNotification = true;
    }
}
```

The callback is wired in `+page.svelte` during initialization.

## +page.svelte Initialization

```typescript
const apiClient = new ApiClient(apiBaseUrl, () => authStore.refreshToken());
const hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);
const uiStore = createUIStore();
const authStore = createAuthStore(apiClient, bootstrapFn, uiStore);
const serverStore = createServerStore(authStore, apiClient);
const channelStore = createChannelStore(authStore, apiClient);
const messageStore = createMessageStore(authStore, channelStore, apiClient);
const dmStore = createDmStore(authStore, apiClient);
const friendStore = createFriendStore(authStore, apiClient);
const voiceStore = createVoiceStore(authStore, apiClient, hub);

setupSignalR(hub, authStore, serverStore, channelStore, messageStore, dmStore, friendStore, voiceStore, uiStore);

const app = createAppState(authStore, serverStore, channelStore, messageStore, dmStore, friendStore, voiceStore, uiStore);
```

Note: `apiClient` and `authStore` have a circular dependency (ApiClient needs token refresh, AuthStore needs ApiClient). Resolved by passing refresh as a callback: `new ApiClient(baseUrl, () => authStore.refreshToken())` — the closure captures `authStore` before it's assigned, but it's only called after initialization.

## Verification Gates

After the full extraction:

1. `npm run check` — svelte-check + TypeScript validates all component property access
2. `npm run build` — catches SSR and import resolution issues
3. `npm test` — existing 7 spec files pass unchanged
4. Manual smoke: sign in, send message, join server, open DM, open settings

## Out of Scope (Follow-ups)

- Moving tokens from localStorage to httpOnly cookies
- Replacing `window.location.reload()` with graceful reconnection UI
- Route-based code splitting / lazy loading voice
- Virtual scrolling for message feeds
- Adding component-level tests
- Toast notification system replacing single error string
- Migrating individual components off the `getAppState()` facade to direct store imports
