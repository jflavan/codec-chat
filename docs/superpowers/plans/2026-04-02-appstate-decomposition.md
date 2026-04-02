# AppState Decomposition Implementation Plan (v2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the 3,999-line `AppState` god class into 8 focused store classes with separate Svelte 5 context keys, migrating all 53 components to use direct store imports.

**Architecture:** Big-bang extraction with direct component migration (no facade). Each store is a Svelte 5 class with `$state`/`$derived` fields and its own context key. All 53 components are updated to import the specific stores they need. Cross-store navigation (`goHome()`, `selectServer()`) and SignalR wiring live in standalone orchestration functions.

**Tech Stack:** SvelteKit 2, Svelte 5 runes (`$state`, `$derived`), TypeScript strict mode, SignalR, mediasoup-client

---

## Why No Facade

Svelte 5 runes use proxy-based reactivity that tracks property access on the specific object where `$state` was declared. Reading `$state` through a JS getter on a different object (a facade) **breaks reactivity** — components won't re-render. Using `$derived` for each proxied field would work but creates ~100+ derived declarations, negating the benefit. Direct store imports are cleaner and fully reactive.

---

## File Structure

### New files to create:
- `apps/web/src/lib/state/ui-store.svelte.ts` — UI toggles, modals, theme, error state, navigation flags
- `apps/web/src/lib/state/auth-store.svelte.ts` — Auth flows, token management, user profile
- `apps/web/src/lib/state/server-store.svelte.ts` — Server list, settings, moderation, roles, invites, webhooks, categories
- `apps/web/src/lib/state/channel-store.svelte.ts` — Channel list, selection, permissions, mention tracking
- `apps/web/src/lib/state/message-store.svelte.ts` — Messages, reactions, pinning, search, attachments, reply (channel context)
- `apps/web/src/lib/state/dm-store.svelte.ts` — DM conversations, messages, typing, reply (DM context)
- `apps/web/src/lib/state/friend-store.svelte.ts` — Friends, requests, user search
- `apps/web/src/lib/state/voice-store.svelte.ts` — Voice/video/screen share, WebRTC, PTT, calls
- `apps/web/src/lib/state/signalr.svelte.ts` — SignalR orchestration wiring
- `apps/web/src/lib/state/navigation.svelte.ts` — Cross-store navigation (`goHome()`, `selectServer()`)
- `apps/web/src/lib/state/index.ts` — Re-exports all store accessors

### Files to modify:
- `apps/web/src/lib/state/app-state.svelte.ts` — Delete (replaced by individual stores)
- `apps/web/src/routes/+page.svelte` — Store creation and wiring
- `apps/web/src/lib/index.ts` — Update re-exports
- All 53 component files — Replace `getAppState()` with specific store imports

### Store Dependency Graph

```
UIStore          ← (none)
AuthStore        ← ApiClient, UIStore
FriendStore      ← AuthStore, ApiClient, UIStore
DmStore          ← AuthStore, ApiClient, UIStore, ChatHubService
ServerStore      ← AuthStore, ApiClient, UIStore, ChatHubService
ChannelStore     ← AuthStore, ApiClient, UIStore, ChatHubService
MessageStore     ← AuthStore, ChannelStore, ApiClient, UIStore, ChatHubService
VoiceStore       ← AuthStore, ApiClient, UIStore, ChatHubService, VoiceService
```

---

## Cross-Store Design Decisions

### Navigation orchestration
`goHome()` and `selectServer()` touch 5+ stores. They live in `navigation.svelte.ts` as standalone functions that take store references:
```typescript
export function goHome(ui, servers, channels, messages, friends, dms, hub): void
export async function selectServer(serverId, ui, servers, channels, messages, dms, hub): Promise<void>
```

### `replyingTo` — split by context
- `MessageStore.replyingTo` — for channel messages (`context: 'channel'`)
- `DmStore.replyingTo` — for DMs (`context: 'dm'`)
Components already check `replyingTo?.context`, so each store only holds its own.

### `pendingReactionKeys` / `isReactionPending()` — shared concern
Both `MessageItem` and `DmChatArea` call `isReactionPending()`. The `pendingReactionKeys` set and the `isReactionPending()` / `_setReactionPending()` helpers need to live somewhere both can access. **Put them on UIStore** since it's a cross-cutting UI concern (preventing double-click on reactions). Both MessageStore and DmStore write to `ui.pendingReactionKeys` and call `ui.isReactionPending()`. Similarly, `ignoredReactionUpdates` (used by both channel and DM reaction dedup) moves to UIStore.

### `userPresence` — shared SvelteMap
Owned by UIStore as `userPresence: SvelteMap<string, PresenceStatus>`. ServerStore and DmStore receive a reference to it for writing presence data. Components read it from UIStore.

### `showFriendsPanel` — UIStore
Pure navigation flag read by `+page.svelte`, `ServerSidebar`, `ChannelSidebar`.

### `homeBadgeCount` — computed in ServerSidebar
The only consumer is `ServerSidebar`. Instead of a cross-store `$derived`, the component computes it inline: `friends.incomingRequests.length + dms.unreadDmCounts.size`.

### `activeDmParticipant` — computed in DmStore
`$derived` on DmStore since it only depends on DmStore's own state.

### Sign-out ordering
`auth.onSignedOut` callback: hub.stop() first, then reset all stores, then render Google button.

### `friendsTab` — UIStore
Pure UI toggle used only by FriendsPanel.

---

## Task 1: Create UIStore

**Files:**
- Create: `apps/web/src/lib/state/ui-store.svelte.ts`

Zero dependencies, safest starting point.

- [ ] **Step 1: Create UIStore**

```typescript
// apps/web/src/lib/state/ui-store.svelte.ts
import { getContext, setContext } from 'svelte';
import { SvelteMap } from 'svelte/reactivity';
import { getTheme, applyTheme, type ThemeId } from '$lib/utils/theme.js';
import { ApiError } from '$lib/api/client.js';
import type { PresenceStatus } from '$lib/types/index.js';

const UI_KEY = Symbol('ui-store');

export function createUIStore(): UIStore {
	const store = new UIStore();
	setContext(UI_KEY, store);
	return store;
}

export function getUIStore(): UIStore {
	return getContext<UIStore>(UI_KEY);
}

export class UIStore {
	/* ───── loading ───── */
	isInitialLoading = $state(true);

	/* ───── navigation ───── */
	showFriendsPanel = $state(false);

	/* ───── modals ───── */
	showCreateServer = $state(false);
	showCreateChannel = $state(false);
	showAlphaNotification = $state(false);
	settingsOpen = $state(false);
	settingsCategory = $state<'profile' | 'account' | 'voice-audio' | 'appearance' | 'notifications'>('profile');
	bugReportOpen = $state(false);
	serverSettingsOpen = $state(false);
	serverSettingsCategory = $state<'general' | 'channels' | 'invites' | 'webhooks' | 'emojis' | 'roles' | 'members' | 'bans' | 'audit-log'>('general');

	/* ───── mobile ───── */
	mobileNavOpen = $state(false);
	mobileMembersOpen = $state(false);

	/* ───── theme ───── */
	theme = $state<ThemeId>(getTheme());

	/* ───── errors ───── */
	error = $state<string | null>(null);
	private transientErrorTimer: ReturnType<typeof setTimeout> | null = null;

	/* ───── image lightbox ───── */
	lightboxImageUrl = $state<string | null>(null);

	/* ───── hub connection ───── */
	isHubConnected = $state(false);

	/* ───── form fields ───── */
	newServerName = $state('');
	newChannelName = $state('');
	newChannelType = $state<'text' | 'voice'>('text');

	/* ───── friends tab ───── */
	friendsTab = $state<'all' | 'pending' | 'add'>('all');

	/* ───── reaction tracking (shared by MessageStore and DmStore) ───── */
	pendingReactionKeys = $state<Set<string>>(new Set());
	ignoredReactionUpdates = $state<Map<string, string[]>>(new Map());

	/* ───── presence (shared across stores) ───── */
	userPresence = new SvelteMap<string, PresenceStatus>();

	/* ───── methods ───── */

	openSettings(): void {
		this.settingsOpen = true;
		this.settingsCategory = 'profile';
	}

	closeSettings(): void {
		this.settingsOpen = false;
	}

	setTheme(id: ThemeId): void {
		this.theme = id;
		applyTheme(id);
	}

	openServerSettings(): void {
		this.serverSettingsOpen = true;
		this.serverSettingsCategory = 'general';
	}

	closeServerSettings(): void {
		this.serverSettingsOpen = false;
	}

	dismissAlphaNotification(): void {
		this.showAlphaNotification = false;
	}

	openImagePreview(url: string): void {
		this.lightboxImageUrl = url;
	}

	closeImagePreview(): void {
		this.lightboxImageUrl = null;
	}

	setError(e: unknown): void {
		if (e instanceof ApiError) {
			this.error = e.message;
		} else if (e instanceof Error) {
			this.error = e.message;
		} else {
			this.error = 'An unexpected error occurred.';
		}
	}

	/* ───── reaction helpers (used by MessageStore + DmStore) ───── */

	static reactionToggleKey(messageId: string, emoji: string): string {
		return `${messageId}:${emoji}`;
	}

	isReactionPending(messageId: string, emoji: string): boolean {
		return this.pendingReactionKeys.has(UIStore.reactionToggleKey(messageId, emoji));
	}

	setReactionPending(key: string, pending: boolean): void {
		const next = new Set(this.pendingReactionKeys);
		if (pending) next.add(key); else next.delete(key);
		this.pendingReactionKeys = next;
	}

	setTransientError(message: string, durationMs = 5000): void {
		if (this.transientErrorTimer) clearTimeout(this.transientErrorTimer);
		this.error = message;
		this.transientErrorTimer = setTimeout(() => {
			if (this.error === message) this.error = null;
			this.transientErrorTimer = null;
		}, durationMs);
	}

	resetNavigation(): void {
		this.showFriendsPanel = false;
		this.friendsTab = 'all';
		this.settingsOpen = false;
		this.serverSettingsOpen = false;
		this.mobileNavOpen = false;
		this.mobileMembersOpen = false;
		this.lightboxImageUrl = null;
		this.error = null;
	}
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd apps/web && npx tsc --noEmit --skipLibCheck 2>&1 | head -20`
Expected: No errors related to ui-store

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/state/ui-store.svelte.ts
git commit -m "refactor: extract UIStore from AppState"
```

---

## Task 2: Create AuthStore

**Files:**
- Create: `apps/web/src/lib/state/auth-store.svelte.ts`

Depends on UIStore and ApiClient. Owns deduplicated bootstrap (`completeSignIn()`).

- [ ] **Step 1: Create AuthStore**

The AuthStore code is identical to the v1 plan (Tasks 2 full code was already reviewed and approved), with one fix: sign-out calls `onSignedOut` which must stop the hub first.

Key changes from v1:
- `signOut()` calls `onSignedOut` first (which stops hub), then clears local state
- `init()` calls `applyTheme(this.ui.theme)` at the start (was missing)
- `uploadAvatar()` and `deleteAvatar()` call `onMembersChanged?.()` callback for member list refresh

```typescript
// apps/web/src/lib/state/auth-store.svelte.ts
import { getContext, setContext } from 'svelte';
import { tick } from 'svelte';
import type { UserProfile, AuthResponse } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import {
	persistToken, loadStoredToken, clearSession as clearStoredSession,
	isTokenExpired, isSessionExpired, type AuthType, setAuthType, getAuthType,
	persistRefreshToken, loadStoredRefreshToken, hasStoredAuthType
} from '$lib/auth/session.js';
import {
	initGoogleIdentity, renderGoogleButton, consumeRedirectCredential
} from '$lib/auth/google.js';
import { PushNotificationManager } from '$lib/services/push-notifications.js';
import { applyTheme } from '$lib/utils/theme.js';
import type { UIStore } from './ui-store.svelte.js';

const AUTH_KEY = Symbol('auth-store');

export function createAuthStore(api: ApiClient, ui: UIStore, googleClientId: string): AuthStore {
	const store = new AuthStore(api, ui, googleClientId);
	setContext(AUTH_KEY, store);
	return store;
}

export function getAuthStore(): AuthStore {
	return getContext<AuthStore>(AUTH_KEY);
}

export class AuthStore {
	/* ───── state ───── */
	idToken = $state<string | null>(null);
	me = $state<UserProfile | null>(null);
	status = $state('Signed out');
	authType: AuthType = $state<AuthType>('google');
	needsNickname = $state(false);
	needsLinking = $state(false);
	linkingEmail = $state('');
	pendingGoogleCredential = $state('');
	emailVerified = $state(true);
	isLoadingMe = $state(false);
	isUploadingAvatar = $state(false);

	/* ───── push ───── */
	pushNotificationsEnabled = $state(false);
	pushNotificationsSupported = $state(false);

	/* ───── derived ───── */
	readonly isSignedIn = $derived(Boolean(this.idToken));
	readonly isGlobalAdmin = $derived(Boolean(this.me?.user.isGlobalAdmin));
	readonly effectiveDisplayName = $derived(
		this.me?.user.effectiveDisplayName ?? this.me?.user.displayName ?? ''
	);

	/* ───── internals ───── */
	private refreshPromise: Promise<string | null> | null = null;
	private pushManager: PushNotificationManager;

	/** Wired by +page.svelte: loads servers, friends, DMs, starts SignalR. */
	onSignedIn: (() => Promise<void>) | null = null;
	/** Wired by +page.svelte: stops hub, resets all stores. */
	onSignedOut: (() => Promise<void>) | null = null;
	/** Wired by +page.svelte: refreshes member list after avatar change. */
	onMembersChanged: (() => Promise<void>) | null = null;

	constructor(
		private readonly api: ApiClient,
		private readonly ui: UIStore,
		private readonly googleClientId: string
	) {
		this.pushManager = new PushNotificationManager(this.api, () => this.idToken);
		this.pushNotificationsSupported = this.pushManager.isSupported;
	}

	/** Deduplicated bootstrap — replaces 8 copy-pasted blocks. */
	private async completeSignIn(): Promise<void> {
		await this.loadMe();
		if (this.onSignedIn) await this.onSignedIn();
		this.ui.isInitialLoading = false;
		this.ui.showAlphaNotification = true;
		this.checkPushSubscription();
	}

	init(): void {
		applyTheme(this.ui.theme);
		this.ui.isInitialLoading = true;
		this.authType = getAuthType();

		const redirectCredential = consumeRedirectCredential();
		if (redirectCredential) {
			this.handleCredential(redirectCredential);
			return;
		}

		if (!isSessionExpired()) {
			const stored = loadStoredToken();
			if (stored && !isTokenExpired(stored)) {
				this.idToken = stored;
				this.status = 'Signed in';
				this.completeSignIn().catch(() => {
					this.ui.isInitialLoading = false;
					this.renderSignIn();
				});
				return;
			}
			this.refreshAccessToken().then(async (success) => {
				if (success && this.idToken) {
					this.status = 'Signed in';
					await this.completeSignIn();
				} else {
					this.ui.isInitialLoading = false;
					this.renderSignIn();
				}
			}).catch(() => {
				this.ui.isInitialLoading = false;
				this.renderSignIn();
			});
			return;
		}
		clearStoredSession();
		this.ui.isInitialLoading = false;
		this.renderSignIn();
	}

	private renderSignIn(): void {
		initGoogleIdentity(this.googleClientId, (token) => this.handleCredential(token), {
			renderButtonIds: ['google-button', 'login-google-button'],
			autoSelect: hasStoredAuthType() && this.authType === 'google'
		});
	}

	async handleCredential(token: string): Promise<void> {
		this.ui.isInitialLoading = true;
		try {
			const response = await this.api.googleSignIn(token);
			if (response.needsLinking) {
				this.needsLinking = true;
				this.linkingEmail = response.email ?? '';
				this.pendingGoogleCredential = token;
				this.ui.isInitialLoading = false;
				return;
			}
			this.idToken = response.accessToken;
			this.status = 'Signed in';
			persistToken(response.accessToken);
			persistRefreshToken(response.refreshToken);
			setAuthType('google');
			this.authType = 'google';
			if (response.isNewUser) {
				this.needsNickname = true;
				this.ui.isInitialLoading = false;
				return;
			}
			await this.completeSignIn();
		} catch {
			this.ui.isInitialLoading = false;
			this.renderSignIn();
		}
	}

	async register(email: string, password: string, nickname: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.api.register(email, password, nickname, recaptchaToken);
	}

	async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.api.login(email, password, recaptchaToken);
	}

	async linkGoogle(email: string, password: string, googleCredential: string): Promise<AuthResponse> {
		return this.api.linkGoogle(email, password, googleCredential);
	}

	async handleLinkGoogleSuccess(response: AuthResponse): Promise<void> {
		this.needsLinking = false;
		this.linkingEmail = '';
		this.pendingGoogleCredential = '';
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType('google');
		this.authType = 'google';
		this.ui.isInitialLoading = true;
		await this.completeSignIn();
	}

	async handleOAuthCallback(provider: 'github' | 'discord', code: string): Promise<void> {
		const response = await this.api.oauthCallback(provider, code);
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType(provider);
		this.authType = provider;
		if (response.isNewUser) {
			this.needsNickname = true;
			this.ui.isInitialLoading = false;
			return;
		}
		this.ui.isInitialLoading = true;
		await this.completeSignIn();
	}

	async handleLocalAuth(response: AuthResponse): Promise<void> {
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType('local');
		this.authType = 'local';
		this.emailVerified = response.user.emailVerified ?? false;
		if (!this.emailVerified) {
			this.ui.isInitialLoading = false;
			return;
		}
		this.ui.isInitialLoading = true;
		await this.completeSignIn();
	}

	async resendVerification(): Promise<void> {
		if (!this.idToken) return;
		await this.api.resendVerification(this.idToken);
	}

	async checkEmailVerified(): Promise<boolean> {
		if (!this.idToken) return false;
		try {
			const profile = await this.api.getMe(this.idToken);
			if (profile.user.emailVerified) {
				this.emailVerified = true;
				this.me = profile;
				this.ui.isInitialLoading = true;
				if (this.onSignedIn) await this.onSignedIn();
				this.ui.isInitialLoading = false;
				this.ui.showAlphaNotification = true;
				this.checkPushSubscription();
				return true;
			}
		} catch { /* ignore */ }
		return false;
	}

	async confirmNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		await this.api.setNickname(this.idToken, nickname);
		this.needsNickname = false;
		this.ui.isInitialLoading = true;
		await this.completeSignIn();
	}

	async refreshAccessToken(): Promise<boolean> {
		const refreshToken = loadStoredRefreshToken();
		if (!refreshToken) return false;
		try {
			const response = await this.api.refreshToken(refreshToken);
			this.idToken = response.accessToken;
			persistToken(response.accessToken);
			persistRefreshToken(response.refreshToken);
			return true;
		} catch {
			clearStoredSession();
			return false;
		}
	}

	async refreshToken(): Promise<string | null> {
		if (this.refreshPromise) return this.refreshPromise;
		this.refreshPromise = (async () => {
			try {
				const success = await this.refreshAccessToken();
				return success ? this.idToken : null;
			} catch {
				await this.signOut();
				return null;
			} finally {
				this.refreshPromise = null;
			}
		})();
		return this.refreshPromise;
	}

	async signOut(): Promise<void> {
		// Stop hub and reset stores FIRST (before clearing auth state)
		if (this.onSignedOut) await this.onSignedOut();

		const refreshToken = loadStoredRefreshToken();
		if (refreshToken) {
			try { await this.api.logout(refreshToken); } catch { /* best-effort */ }
		}

		clearStoredSession();
		this.ui.isInitialLoading = false;
		this.ui.isHubConnected = false;
		this.idToken = null;
		this.me = null;
		this.status = 'Signed out';

		await tick();
		renderGoogleButton('google-button');
		renderGoogleButton('login-google-button');
	}

	async loadMe(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		this.isLoadingMe = true;
		try {
			this.me = await this.api.getMe(this.idToken);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingMe = false;
		}
	}

	async submitBugReport(title: string, description: string, userAgent: string, currentPage: string): Promise<{ issueUrl: string }> {
		if (!this.idToken) throw new Error('Not authenticated');
		return this.api.submitBugReport(this.idToken, title, description, userAgent, currentPage);
	}

	async uploadAvatar(file: File): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.ui.error = null;
		try {
			const { avatarUrl } = await this.api.uploadAvatar(this.idToken, file);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, avatarUrl } };
			if (this.onMembersChanged) await this.onMembersChanged();
		} catch (e) { this.ui.setError(e); }
		finally { this.isUploadingAvatar = false; }
	}

	async deleteAvatar(): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.ui.error = null;
		try {
			const { avatarUrl } = await this.api.deleteAvatar(this.idToken);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, avatarUrl } };
			if (this.onMembersChanged) await this.onMembersChanged();
		} catch (e) { this.ui.setError(e); }
		finally { this.isUploadingAvatar = false; }
	}

	async setNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.setNickname(this.idToken, nickname);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, nickname: result.nickname, effectiveDisplayName: result.effectiveDisplayName } };
		} catch (e) { this.ui.setError(e); }
	}

	async removeNickname(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.removeNickname(this.idToken);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, nickname: result.nickname, effectiveDisplayName: result.effectiveDisplayName } };
		} catch (e) { this.ui.setError(e); }
	}

	async setStatus(statusText?: string | null, statusEmoji?: string | null): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.setStatus(this.idToken, statusText, statusEmoji);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, statusText: result.statusText, statusEmoji: result.statusEmoji } };
		} catch (e) { this.ui.setError(e); }
	}

	async clearStatus(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.clearStatus(this.idToken);
			if (this.me) this.me = { ...this.me, user: { ...this.me.user, statusText: result.statusText, statusEmoji: result.statusEmoji } };
		} catch (e) { this.ui.setError(e); }
	}

	async checkPushSubscription(): Promise<void> {
		this.pushNotificationsEnabled = await this.pushManager.isSubscribed();
	}

	async enablePushNotifications(): Promise<boolean> {
		const success = await this.pushManager.subscribe();
		this.pushNotificationsEnabled = success;
		return success;
	}

	async disablePushNotifications(): Promise<boolean> {
		const success = await this.pushManager.unsubscribe();
		if (success) this.pushNotificationsEnabled = false;
		return success;
	}
}
```

- [ ] **Step 2: Verify and commit**

```bash
cd apps/web && npx tsc --noEmit --skipLibCheck 2>&1 | head -20
git add src/lib/state/auth-store.svelte.ts
git commit -m "refactor: extract AuthStore with deduplicated bootstrap"
```

---

## Task 3: Create FriendStore

**Files:**
- Create: `apps/web/src/lib/state/friend-store.svelte.ts`

Identical to v1 plan code. All fields, methods, and `reset()` are fully specified there.

**Fields:** `friends`, `incomingRequests`, `outgoingRequests`, `userSearchResults`, `isLoadingFriends`, `isSearchingUsers`, `friendSearchQuery`

**Methods:** `loadFriends()`, `loadFriendRequests()`, `sendFriendRequest()`, `acceptFriendRequest()`, `declineFriendRequest()`, `cancelFriendRequest()`, `removeFriend()`, `searchUsers()`, `reset()`

- [ ] **Step 1:** Create `apps/web/src/lib/state/friend-store.svelte.ts` using the full code from v1 plan Task 3.
- [ ] **Step 2:** Verify and commit.

---

## Task 4: Create DmStore

**Files:**
- Create: `apps/web/src/lib/state/dm-store.svelte.ts`

Based on v1 plan code with these changes:
- `replyingTo` for DM context lives here (not shared with MessageStore)
- `activeDmParticipant` is a `$derived` here
- `_presenceCallback` replaced by direct `ui.userPresence` writes
- Adds `patchDmLinkPreviews()`, `handleDmMessageDeleted()`, `handleDmMessageEdited()`, `handleDmReactionUpdate()` methods for SignalR

**Fields:** `dmConversations`, `dmMessages`, `activeDmChannelId`, `dmTypingUsers`, `unreadDmCounts`, `dmMessageBody`, `isLoadingDmConversations`, `isLoadingDmMessages`, `isSendingDm`, `pendingDmImage`, `pendingDmImagePreview`, `pendingDmFile`, `replyingTo`

**Derived:** `activeDmParticipant` — `$derived(this.dmConversations.find((c) => c.id === this.activeDmChannelId)?.participant ?? null)`

**Methods:** `loadDmConversations()`, `loadDmPresence()`, `selectDmConversation()`, `loadDmMessages()`, `sendDmMessage()`, `openDmWithUser()`, `closeDmConversation()`, `handleDmComposerInput()`, `deleteDmMessage()`, `editDmMessage()`, `toggleDmReaction()`, `attachDmImage()`, `clearPendingDmImage()`, `attachDmFile()`, `clearPendingDmFile()`, `startReply()`, `cancelReply()`, `reset()`

**SignalR handler methods:**
- `handleIncomingDm(msg, isViewingDms)` — from `onReceiveDm` callback
- `handleDmTyping(dmChannelId, displayName)` — from `onDmTyping`
- `handleDmStoppedTyping(dmChannelId, displayName)` — from `onDmStoppedTyping`
- `handleDmMessageDeleted(event)` — from `onDmMessageDeleted`
- `handleDmMessageEdited(event)` — from `onDmMessageEdited`
- `handleDmReactionUpdate(update)` — from `onDmReactionUpdated`
- `patchDmLinkPreviews(messageId, previews)` — from `onLinkPreviewsReady`

Constructor takes `AuthStore`, `ApiClient`, `UIStore`, `ChatHubService`.

- [ ] **Step 1:** Create `apps/web/src/lib/state/dm-store.svelte.ts`. Copy each method body from the original `app-state.svelte.ts`, applying the mechanical transformation: `this.idToken` → `this.auth.idToken`, `this.setError(e)` → `this.ui.setError(e)`, `this.error = '...'` → `this.ui.error = '...'`, `this.mobileNavOpen` → `this.ui.mobileNavOpen`. For presence writes, use `this.ui.userPresence.set(userId, status)`.
- [ ] **Step 2:** Verify and commit.

---

## Task 5: Create ServerStore

**Files:**
- Create: `apps/web/src/lib/state/server-store.svelte.ts`

The largest store (~500 lines). Constructor takes `AuthStore`, `ApiClient`, `UIStore`, `ChatHubService`.

**Fields (all `$state`):**
- `servers: MemberServer[]` (from line 86)
- `selectedServerId: string | null` (line 142)
- `members: Member[]` (line 90)
- `serverInvites: ServerInvite[]` (line 110)
- `serverRoles: ServerRole[]` (line 1346)
- `bans: BannedMember[]` (line 179)
- `customEmojis: CustomEmoji[]` (line 197)
- `categories: ChannelCategory[]` (line 113)
- `auditLogEntries: AuditLogEntry[]` (line 116)
- `hasMoreAuditLog: boolean` (line 117)
- `isLoadingAuditLog: boolean` (line 118)
- `notificationPreferences: NotificationPreferences | null` (line 121)
- `webhooks: Webhook[]` (line 134)
- `webhookDeliveries: WebhookDelivery[]` (line 135)
- `selectedWebhookId: string | null` (line 138)
- Loading flags: `isLoadingServers`, `isLoadingMembers`, `isLoadingInvites`, `isCreatingInvite`, `isLoadingBans`, `isLoadingRoles`, `isLoadingWebhooks`, `isCreatingWebhook`, `isLoadingDeliveries`, `isUpdatingServerName`, `isUploadingServerIcon`, `isUploadingEmoji`, `isDeletingServer`, `isJoining`, `isCreatingServer`

**Derived fields:**
- `isServerOwner` — `$derived(this.servers.find((s) => s.serverId === this.selectedServerId)?.isOwner ?? false)`
- `currentServerPermissions` — `$derived(this.servers.find((s) => s.serverId === this.selectedServerId)?.permissions ?? 0)`
- `canManageChannels`, `canKickMembers`, `canBanMembers`, `canManageInvites`, `canDeleteServer`, `canDeleteChannel`, `canManageRoles`, `canPinMessages`, `canManageEmojis`, `canViewAuditLog` — all use `hasPermission(this.currentServerPermissions, Permission.XXX)` with `this.auth.isGlobalAdmin` check
- `selectedServerName`, `selectedServerIconUrl` — lookup by `selectedServerId`
- `isServerMuted` — getter from `notificationPreferences`

**Methods (copy from original, applying mechanical transform):**
- Data loading: `loadServers()`, `loadMembers()`, `loadCustomEmojis()`
- CRUD: `createServer()`, `reorderServers()`, `deleteServer()`, `updateServerName()`, `uploadServerIcon()`, `removeServerIcon()`, `updateServerDescription()`
- Moderation: `kickMember()`, `banMember()`, `unbanMember()`, `loadBans()`, `addMemberRole()`, `removeMemberRole()`, `setMemberRoles()`
- Roles: `loadRoles()`, `createRole()`, `updateRole()`, `deleteRole()`
- Invites: `loadInvites()`, `createInvite()`, `revokeInvite()`, `joinViaInvite()`
- Webhooks: `loadWebhooks()`, `createWebhook()`, `updateWebhook()`, `deleteWebhook()`, `loadWebhookDeliveries()`
- Categories: `loadCategories()`, `createCategory()`, `renameCategory()`, `deleteCategory()`, `saveCategoryOrder()`
- Audit log: `loadAuditLog()`, `loadOlderAuditLog()`
- Notifications: `loadNotificationPreferences()`, `toggleServerMute()`, `toggleChannelMute()`, `isChannelMuted()`
- Emojis: `uploadCustomEmoji()`, `renameCustomEmoji()`, `deleteCustomEmoji()`
- Server avatars: `uploadServerAvatar()`, `deleteServerAvatar()`
- Presence: `loadServerPresence()` — writes to `this.ui.userPresence`
- Permissions: `hasPermission(perm)`, `serverMentionCount(serverId)` (reads `channelMentionCounts` from ChannelStore — accept as parameter or callback)

**SignalR handler methods:**
- `handleKicked(event)` — removes server, navigates if needed (calls `goHome()` callback)
- `handleBanned(event)` — same pattern as kicked
- `handleMemberBanned(event)` — filters members/messages for current server
- `handleMemberUnbanned(event)` — filters bans
- `handleMemberJoined(event)` — reloads members
- `handleMemberLeft(event)` — reloads members
- `handleMemberRoleChanged(event)` — reloads members, reloads servers if self
- `handleServerNameChanged(event)` — updates server in list
- `handleServerIconChanged(event)` — updates server icon
- `handleServerDeleted(event)` — removes server, navigates if needed
- `handleServerDescriptionChanged(event)` — updates description
- `handleCustomEmojiAdded/Updated/Deleted(event)` — updates emoji list
- `handleCategoryCreated/Renamed/Deleted(event)` — updates categories
- `handleCategoryOrderChanged(event)` — reloads categories
- `handleUserStatusChanged(event)` — updates member list, updates auth.me if self

**`reset()` method:** resets all fields to initial values.

- [ ] **Step 1:** Create `apps/web/src/lib/state/server-store.svelte.ts`.
- [ ] **Step 2:** Verify and commit.

---

## Task 6: Create ChannelStore

**Files:**
- Create: `apps/web/src/lib/state/channel-store.svelte.ts`

Constructor takes `AuthStore`, `ApiClient`, `UIStore`, `ChatHubService`.

**Fields (all `$state`):**
- `channels: Channel[]` (line 87)
- `selectedChannelId: string | null` (line 143)
- `isLoadingChannels: boolean` (line 160)
- `isCreatingChannel: boolean` (line 169)
- `isUpdatingChannelName: boolean` (line 195)
- `channelMentionCounts: Map<string, number>` (line 105)
- `channelServerMap: Map<string, string>` (line 107)

**Derived:**
- `selectedChannelName` — `$derived(this.channels.find((c) => c.id === this.selectedChannelId)?.name ?? null)`

**Methods:**
- `loadChannels(serverId)` — loads channels, updates `channelServerMap`, selects first text channel, joins SignalR channel group. Takes callbacks: `onLoadMessages(channelId)`, `onLoadVoiceStates()` to avoid circular deps with MessageStore/VoiceStore.
- `selectChannel(channelId)` — leaves old channel group, joins new, clears mention badge. Takes `onLoadMessages(channelId)` callback.
- `createChannel()` — uses `this.ui.newChannelName`, `this.ui.newChannelType`
- `deleteChannel(channelId)` — removes channel, selects next
- `updateChannelName(channelId, name)`
- `updateChannelDescription(channelId, description)`
- `getChannelOverrides(channelId)`, `setChannelOverride(...)`, `deleteChannelOverride(...)`
- `saveChannelOrder(channels)` — delegates to API
- `channelMentionCount(channelId)` — reads from `channelMentionCounts`

**SignalR handler methods:**
- `handleChannelNameChanged(event, selectedServerId)` — updates name if current server
- `handleChannelDeleted(event, selectedServerId)` — removes channel, selects next
- `handleChannelDescriptionChanged(event, selectedServerId)` — updates description
- `handleMentionReceived(event)` — updates `channelServerMap` and `channelMentionCounts`
- `handleChannelOrderChanged(event, selectedServerId)` — reloads channels
- `handleChannelOverrideUpdated(event, selectedServerId)` — reloads channels

**`reset()` method:** resets all fields.

- [ ] **Step 1:** Create `apps/web/src/lib/state/channel-store.svelte.ts`.
- [ ] **Step 2:** Verify and commit.

---

## Task 7: Create MessageStore

**Files:**
- Create: `apps/web/src/lib/state/message-store.svelte.ts`

Constructor takes `AuthStore`, `ChannelStore`, `ApiClient`, `UIStore`, `ChatHubService`.

**Fields (all `$state`):**
- `messages: Message[]` (line 88)
- `isPurgingChannel: boolean` (line 89)
- `typingUsers: string[]` (line 234)
- `pendingImage: File | null`, `pendingImagePreview: string | null` (lines 221-222)
- `pendingFile: File | null` (line 226)
- `messageBody: string` (line 216)
- `pendingMentions: Map<string, string>` (line 218)
- `replyingTo: { messageId, authorName, bodyPreview, context: 'channel' } | null` — channel context only
- `isSearchOpen`, `searchQuery`, `searchFilters`, `searchResults`, `isSearching`, `highlightedMessageId` (lines 201-206)
- `pinnedMessages`, `showPinnedPanel` (lines 128-129)
- `isSending`, `isLoadingMessages`, `isLoadingOlderMessages`, `hasMoreMessages` (lines 161-163, 166)
- `ignoredReactionUpdates: Map<string, string[]>` (line 236)
- `pendingReactionKeys: Set<string>` (line 237)

**Derived:**
- `pinnedMessageIds` — `$derived(new Set(this.pinnedMessages.map((p) => p.messageId)))`
- `pinnedMessageCount` — `$derived(this.pinnedMessages.length)`

**Static helpers (private):** `ALLOWED_IMAGE_TYPES`, `ALLOWED_FILE_EXTENSIONS`, `reactionToggleKey()`, `serializeReactionSnapshot()`

**Methods:**
- `loadMessages(channelId)`, `loadOlderMessages()`
- `sendMessage()` — uses `this.channels.selectedChannelId`, `this.messageBody`
- `editMessage(messageId, newBody)`, `deleteMessage(messageId)`
- `toggleReaction(messageId, emoji)`, `isReactionPending(messageId, emoji)`
- `purgeChannel(channelId)`
- `attachImage(file)`, `clearPendingImage()`, `attachFile(file)`, `clearPendingFile()`
- `resolveMentions(text)`, `handleComposerInput()`
- `startReply(messageId, authorName, bodyPreview)`, `cancelReply()`
- `toggleSearch()`, `searchMessages(query, filters)`, `searchPage(page)`, `jumpToMessage(...)`
- `loadPinnedMessages(channelId?)`, `pinMessage(messageId)`, `unpinMessage(messageId)`, `togglePinnedPanel()`

**SignalR handler methods:**
- `handleIncomingMessage(msg)` — adds to messages if current channel
- `handleTyping(channelId, displayName)` — adds to typingUsers
- `handleStoppedTyping(channelId, displayName)` — removes from typingUsers
- `handleReactionUpdate(update)` — checks ignore list, updates message reactions
- `handleMessageDeleted(event)` — removes message and pinned message
- `handleChannelPurged(event)` — clears messages
- `handleMessageEdited(event)` — updates message body/editedAt
- `handleMessagePinned(event)` — adds to pinned messages
- `handleMessageUnpinned(event)` — removes from pinned messages
- `patchLinkPreviews(messageId, previews)` — updates linkPreviews on message

**`reset()` method:** resets all fields.

- [ ] **Step 1:** Create `apps/web/src/lib/state/message-store.svelte.ts`.
- [ ] **Step 2:** Verify and commit.

---

## Task 8: Create VoiceStore

**Files:**
- Create: `apps/web/src/lib/state/voice-store.svelte.ts`

Constructor takes `AuthStore`, `ApiClient`, `UIStore`, `ChatHubService`, `VoiceService`.

**Fields (all `$state`):**
- `activeVoiceChannelId: string | null` (line 243)
- `voiceChannelMembers: Map<string, VoiceChannelMember[]>` (line 245)
- `isMuted`, `isDeafened`, `isJoiningVoice` (lines 246-248)
- `userVolumes: Map<string, number>` (line 250)
- `voiceInputMode: 'voice-activity' | 'push-to-talk'` (line 252)
- `pttKey: string` (line 254)
- `isPttActive: boolean` (line 256)
- `isVideoEnabled`, `isScreenSharing` (lines 258-260)
- `localVideoTrack`, `localScreenTrack: MediaStreamTrack | null` (lines 262-264)
- `remoteVideoTracks: Map<string, { track, label }>` (line 266)
- `activeCall: { callId, dmChannelId, otherUserId, otherDisplayName, otherAvatarUrl?, status, startedAt, answeredAt? } | null` (lines 269-278)
- `incomingCall: { callId, dmChannelId, callerUserId, callerDisplayName, callerAvatarUrl? } | null` (lines 280-286)

**Private fields:** `audioContext`, `audioNodes`, `audioParticipantUserMap`, `pttKeydownHandler`, `pttKeyupHandler`

**Methods:**
- `joinVoiceChannel(channelId)`, `leaveVoiceChannel()`
- `toggleMute()`, `toggleDeafen()`
- `toggleVideo()`, `toggleScreenShare()`
- `setUserVolume(userId, volume)`, `resetUserVolume(userId)`
- `setVoiceInputMode(mode)`, `setPttKey(code)`
- `startCall(dmChannelId)`, `acceptCall(callId)`, `declineCall(callId)`, `endCall()`
- `checkActiveCall()`
- **`teardownOnDisconnect()`** — consolidates 4 duplicated cleanup blocks:
  ```typescript
  teardownOnDisconnect(): void {
      this.voice.leave();
      this._cleanupRemoteAudio();
      this._cleanupRemoteVideo();
      this._removePttListeners();
      this.activeVoiceChannelId = null;
      this.activeCall = null;
      this.incomingCall = null;
      this.isMuted = false;
      this.isDeafened = false;
      this.isPttActive = false;
      this.isVideoEnabled = false;
      this.isScreenSharing = false;
      this.localVideoTrack = null;
      this.localScreenTrack = null;
  }
  ```
- `teardownVoiceSync()` — sync cleanup for beforeunload
- `destroy()` — ends call/voice, closes audio context, stops hub

**Private methods:** `_attachRemoteAudio()`, `_detachRemoteAudio()`, `_cleanupRemoteAudio()`, `_attachRemoteVideo()`, `_detachRemoteVideo()`, `_cleanupRemoteVideo()`, `_findUserIdByParticipant()`, `_loadUserVolumes()`, `_saveUserVolumes()`, `_loadVoicePreferences()`, `_saveVoicePreferences()`, `_registerPttListeners()`, `_removePttListeners()`, `_loadAllVoiceStates()`

Constructor calls `_loadUserVolumes()` and `_loadVoicePreferences()` (moved from `AppState.init()`).

**SignalR handler methods:**
- `handleUserJoinedVoice(event)`, `handleUserLeftVoice(event)`, `handleVoiceStateUpdated(event)`
- `handleNewProducer(event)`, `handleProducerClosed(event)`
- `handleIncomingCall(event)`, `handleCallAccepted(event)`, `handleCallDeclined(event)`, `handleCallEnded(event)`, `handleCallMissed(event)`

**`reset()` method:** calls `teardownOnDisconnect()`, resets `voiceChannelMembers`, `userVolumes`.

- [ ] **Step 1:** Create `apps/web/src/lib/state/voice-store.svelte.ts`.
- [ ] **Step 2:** Verify and commit.

---

## Task 9: Create Navigation Orchestration

**Files:**
- Create: `apps/web/src/lib/state/navigation.svelte.ts`

These functions cross multiple stores so they can't belong to any single store.

- [ ] **Step 1: Create navigation.svelte.ts**

```typescript
// apps/web/src/lib/state/navigation.svelte.ts
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { UIStore } from './ui-store.svelte.js';
import type { ServerStore } from './server-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import type { MessageStore } from './message-store.svelte.js';
import type { FriendStore } from './friend-store.svelte.js';
import type { DmStore } from './dm-store.svelte.js';

/** Navigate to the friends panel (Home view). */
export function goHome(
	ui: UIStore,
	servers: ServerStore,
	channels: ChannelStore,
	messages: MessageStore,
	friends: FriendStore,
	dms: DmStore
): void {
	ui.showFriendsPanel = true;
	ui.mobileNavOpen = false;
	servers.selectedServerId = null;
	channels.selectedChannelId = null;
	channels.channels = [];
	messages.messages = [];
	messages.hasMoreMessages = false;
	servers.members = [];
	servers.customEmojis = [];
	friends.loadFriends();
	friends.loadFriendRequests();
	dms.loadDmConversations();
}

/** Navigate back to a server view. */
export async function selectServer(
	serverId: string,
	ui: UIStore,
	servers: ServerStore,
	channels: ChannelStore,
	dms: DmStore,
	hub: ChatHubService
): Promise<void> {
	ui.showFriendsPanel = false;
	ui.mobileNavOpen = false;
	servers.selectedServerId = serverId;

	// Clean up active DM state
	if (dms.activeDmChannelId) {
		await hub.leaveDmChannel(dms.activeDmChannelId);
		dms.activeDmChannelId = null;
		dms.dmMessages = [];
		dms.dmTypingUsers = [];
	}

	await channels.loadChannels(serverId);
	await servers.loadMembers(serverId);
	await servers.loadCustomEmojis(serverId);
	servers.loadServerPresence(serverId);
	servers.loadCategories();
	servers.loadNotificationPreferences();
}
```

- [ ] **Step 2:** Verify and commit.

---

## Task 10: Create SignalR Orchestration

**Files:**
- Create: `apps/web/src/lib/state/signalr.svelte.ts`

Identical structure to v1 plan Task 9, with these changes:
- Uses `voice.teardownOnDisconnect()` (deduplicated)
- `onReceiveDm` passes `ui.showFriendsPanel` as the `isViewingDms` flag
- `onKickedFromServer` and `onBannedFromServer` use `goHome()` from navigation
- `onUserPresenceChanged` writes to `ui.userPresence`
- `onUserStatusChanged` calls both `servers.handleUserStatusChanged()` and updates `auth.me` if self

- [ ] **Step 1:** Create `apps/web/src/lib/state/signalr.svelte.ts` following the v1 plan Task 9 code with fixes above.
- [ ] **Step 2:** Verify and commit.

---

## Task 11: Create Index and Delete Old AppState

**Files:**
- Create: `apps/web/src/lib/state/index.ts`
- Delete: `apps/web/src/lib/state/app-state.svelte.ts`

- [ ] **Step 1: Create index.ts**

```typescript
// apps/web/src/lib/state/index.ts
export { createUIStore, getUIStore, UIStore } from './ui-store.svelte.js';
export { createAuthStore, getAuthStore, AuthStore } from './auth-store.svelte.js';
export { createServerStore, getServerStore, ServerStore } from './server-store.svelte.js';
export { createChannelStore, getChannelStore, ChannelStore } from './channel-store.svelte.js';
export { createMessageStore, getMessageStore, MessageStore } from './message-store.svelte.js';
export { createDmStore, getDmStore, DmStore } from './dm-store.svelte.js';
export { createFriendStore, getFriendStore, FriendStore } from './friend-store.svelte.js';
export { createVoiceStore, getVoiceStore, VoiceStore } from './voice-store.svelte.js';
export { setupSignalR } from './signalr.svelte.js';
export { goHome, selectServer } from './navigation.svelte.js';
```

- [ ] **Step 2: Delete app-state.svelte.ts**

```bash
git rm apps/web/src/lib/state/app-state.svelte.ts
```

- [ ] **Step 3: Update `apps/web/src/lib/index.ts`** if it re-exports from app-state.

- [ ] **Step 4:** Commit.

---

## Task 12: Update +page.svelte

**Files:**
- Modify: `apps/web/src/routes/+page.svelte`

- [ ] **Step 1: Rewrite store creation and wiring**

Replace `createAppState()` with individual store creation:

```typescript
<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { env } from '$env/dynamic/public';
	import { ApiClient } from '$lib/api/client.js';
	import { ChatHubService } from '$lib/services/chat-hub.js';
	import { VoiceService } from '$lib/services/voice-service.js';
	import {
		createUIStore, createAuthStore, createServerStore, createChannelStore,
		createMessageStore, createDmStore, createFriendStore, createVoiceStore,
		setupSignalR, goHome, selectServer
	} from '$lib/state/index.js';
	// Import components (unchanged)...

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';
	const googleClientId = env.PUBLIC_GOOGLE_CLIENT_ID ?? '';

	// Create stores in dependency order
	const ui = createUIStore();
	let authRef: ReturnType<typeof createAuthStore>;
	const api = new ApiClient(apiBaseUrl, () => authRef.refreshToken());
	const hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);
	const auth = authRef = createAuthStore(api, ui, googleClientId);
	const servers = createServerStore(auth, api, ui, hub);
	const channels = createChannelStore(auth, api, ui, hub);
	const messages = createMessageStore(auth, channels, api, ui, hub);
	const dms = createDmStore(auth, api, ui, hub);
	const friends = createFriendStore(auth, api, ui);
	const voice = createVoiceStore(auth, api, ui, hub, new VoiceService());

	const reconnectTimerRef = { current: null as ReturnType<typeof setTimeout> | null };

	// Wire bootstrap callback
	auth.onSignedIn = async () => {
		await Promise.all([
			servers.loadServers(),
			friends.loadFriends(),
			friends.loadFriendRequests(),
			dms.loadDmConversations(),
			setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui, reconnectTimerRef)
		]);
	};

	// Wire sign-out callback (hub stops FIRST, then stores reset)
	auth.onSignedOut = async () => {
		await hub.stop();
		servers.reset();
		channels.reset();
		messages.reset();
		dms.reset();
		friends.reset();
		voice.reset();
		ui.resetNavigation();
	};

	// Wire avatar change callback
	auth.onMembersChanged = async () => {
		if (servers.selectedServerId) {
			await servers.loadMembers(servers.selectedServerId);
		}
	};

	// Wire channel loading callbacks for ChannelStore
	channels.onLoadMessages = (channelId) => messages.loadMessages(channelId);
	channels.onLoadVoiceStates = () => voice.loadAllVoiceStates(channels.channels);

	// Wire goHome/selectServer for ServerSidebar
	// (exposed via stores or imported directly by components)

	onMount(() => {
		if (!googleClientId) {
			ui.error = 'Missing PUBLIC_GOOGLE_CLIENT_ID.';
			ui.isInitialLoading = false;
			return;
		}
		if (!apiBaseUrl) {
			ui.error = 'Missing PUBLIC_API_BASE_URL.';
			ui.isInitialLoading = false;
			return;
		}
		auth.init();

		const handleBeforeUnload = () => voice.teardownVoiceSync();
		window.addEventListener('beforeunload', handleBeforeUnload);
		return () => window.removeEventListener('beforeunload', handleBeforeUnload);
	});

	onDestroy(() => voice.destroy());

	function closeMobileNav(): void { ui.mobileNavOpen = false; }
	function closeMobileMembers(): void { ui.mobileMembersOpen = false; }
	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			if (ui.mobileMembersOpen) { ui.mobileMembersOpen = false; e.stopPropagation(); }
			else if (ui.mobileNavOpen) { ui.mobileNavOpen = false; e.stopPropagation(); }
		}
	}
</script>
```

Template changes:
- Replace all `app.xxx` references with the appropriate store variable
- `app.showFriendsPanel` → `ui.showFriendsPanel`
- `app.isSignedIn` → `auth.isSignedIn`
- `app.isInitialLoading` → `ui.isInitialLoading`
- `app.activeDmChannelId` → `dms.activeDmChannelId`
- `app.settingsOpen` → `ui.settingsOpen`
- `app.serverSettingsOpen` → `ui.serverSettingsOpen`
- `app.bugReportOpen` → `ui.bugReportOpen`
- `app.incomingCall` → `voice.incomingCall`
- `app.needsNickname` → `auth.needsNickname`
- `app.needsLinking` → `auth.needsLinking`
- `app.emailVerified` → `auth.emailVerified`
- `app.mobileNavOpen` → `ui.mobileNavOpen`
- `app.mobileMembersOpen` → `ui.mobileMembersOpen`
- `app.error` → `ui.error`

- [ ] **Step 2:** Verify and commit.

---

## Task 13: Migrate All 53 Components

**Files:**
- Modify: All 53 `.svelte` files that import `getAppState`

This is the largest task by file count but is fully mechanical.

**Migration pattern for each component:**

1. Remove `import { getAppState } from '$lib/state/app-state.svelte.js';`
2. Add imports for the stores the component needs: `import { getAuthStore } from '$lib/state/auth-store.svelte.js';` etc.
3. Replace `const app = getAppState();` with individual store bindings: `const auth = getAuthStore();` etc.
4. Replace each `app.xxx` reference with the appropriate store reference.

**Component → Store mapping** (verified by reading all 53 files):

Components needing only 1-2 stores (simplest migrations):
- `AlphaNotification.svelte` → UIStore
- `LoadingScreen.svelte` → (no store, just a visual component)
- `ImagePreview.svelte` → UIStore
- `TypingIndicator.svelte` → MessageStore
- `SettingsSidebar.svelte` → UIStore
- `SearchFilterBar.svelte` → MessageStore, ServerStore

Components needing 3+ stores (need careful mapping):
- `ServerSidebar.svelte` → UIStore, AuthStore, ServerStore, FriendStore, DmStore + `goHome()`, `selectServer()` from navigation
- `ChannelSidebar.svelte` → UIStore, AuthStore, ServerStore, ChannelStore, MessageStore, VoiceStore
- `ChatArea.svelte` → UIStore, AuthStore, ServerStore, ChannelStore, MessageStore
- `DmChatArea.svelte` → UIStore, AuthStore, DmStore, VoiceStore
- `Composer.svelte` → UIStore, AuthStore, MessageStore, ServerStore
- `MessageItem.svelte` → UIStore, AuthStore, MessageStore, ServerStore
- `LoginScreen.svelte` → UIStore, AuthStore
- `UserSettingsModal.svelte` → UIStore, AuthStore
- etc.

**For `homeBadgeCount`**: In `ServerSidebar.svelte`, replace `app.homeBadgeCount` with inline computation: `friends.incomingRequests.length + dms.unreadDmCounts.size`

**For `activeDmParticipant`**: In `DmChatArea.svelte`, replace `app.activeDmParticipant` with `dms.activeDmParticipant`

**For `goHome()`/`selectServer()`**: In `ServerSidebar.svelte`, import `goHome` and `selectServer` from `$lib/state/navigation.svelte.js` and call with store refs:
```typescript
import { goHome, selectServer } from '$lib/state/navigation.svelte.js';
// In template:
onclick={() => goHome(ui, servers, channels, messages, friends, dms)}
onclick={() => selectServer(server.serverId, ui, servers, channels, dms, hub)}
```

Note: `ServerSidebar` doesn't currently have access to `hub`. Either: (a) pass it via context, (b) make `selectServer` a method on ServerStore that takes a callback, or (c) expose `hub` via a context key. Option (c) is simplest — add a `HUB_KEY` context in `+page.svelte` and `getHub()` accessor.

**Full store mapping per component:**

| Component | Auth | UI | Server | Channel | Message | DM | Friend | Voice |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| AlphaNotification | | X | | | | | | |
| LinkAccountModal | X | | | | | | | |
| LoginScreen | X | | | | | | | |
| NicknameModal | X | | | | | | | |
| VerificationGate | X | | | | | | | |
| ImagePreview | | X | | | | | | |
| TypingIndicator | | | | | X | | | |
| AppearanceSettings | | X | | | | | | |
| SettingsSidebar | | X | | | | | | |
| ServerSettingsModal | | X | | | | | | |
| UserSettingsModal | | X | | | | | | |
| NotificationSettings | X | | | | | | | |
| ProfileSettings | X | | | | | | | |
| AccountSettings | X | X | | | | | | |
| BugReportModal | X | X | | | | | | |
| VoiceAudioSettings | | | | | | | | X |
| DmCallHeader | | | | | | | | X |
| IncomingCallOverlay | | | | | | | | X |
| UserActionSheet | | | | | | | | X |
| PendingRequests | | | | | | | X | |
| AddFriend | | | | | | X | X | |
| FriendsPanel | | X | | | | | X | |
| FriendsList | | | | | | X | X | |
| SearchPanel | | | | | X | | | |
| MessageActionBar | | | X | | | | | |
| ServerAuditLog | | | X | | | | | |
| ServerBans | | | X | | | | | |
| ServerEmojis | | | X | | | | | |
| ServerInvites | | | X | | | | | |
| ServerRoles | | | X | | | | | |
| ServerSettings | | | X | | | | | |
| ServerWebhooks | | | X | | | | | |
| ServerSettingsSidebar | | X | X | | | | | |
| VoiceConnectedBar | | | | X | | | | X |
| VideoGrid | X | | | | | | | X |
| MessageFeed | | | | X | X | | | |
| SearchFilterBar | | | | X | X | X | | |
| UserPanel | X | X | X | | | | | |
| MembersSidebar | X | X | X | | | | | |
| ChannelPermissions | X | | X | X | | | | |
| PinnedMessagesPanel | | | X | | X | | | |
| ServerChannels | X | | X | X | X | | | |
| ServerMembers | X | | X | | | | | |
| Composer | | X | X | X | X | | | |
| MessageItem | X | X | X | | X | | | |
| HomeSidebar | | X | | | | X | X | X |
| DmList | | X | | | | X | | |
| ServerSidebar | X | X | X | | | X | X | |
| ChatArea | | X | X | X | X | | | X |
| ChannelSidebar | X | X | X | X | | | | X |
| DmChatArea | X | X | X | | X | X | | X |
| discord/+page | X | | | | | | | |
| github/+page | X | | | | | | | |

- [ ] **Step 1:** Migrate all components. Work through them alphabetically or by feature area. For each file:
  1. Read the file to determine which `app.xxx` properties are used
  2. Map each to the correct store
  3. Update imports and references
- [ ] **Step 2:** After all migrations, delete any remaining references to `getAppState` or `app-state.svelte`
- [ ] **Step 3:** Commit.

---

## Task 14: Verification Gates

- [ ] **Step 1: Run svelte-check**

```bash
cd apps/web && npm run check
```

Expected: Zero errors.

- [ ] **Step 2: Run build**

```bash
cd apps/web && npm run build
```

Expected: Successful build.

- [ ] **Step 3: Run existing tests**

```bash
cd apps/web && npm test
```

Expected: All 7 spec files pass unchanged.

- [ ] **Step 4:** Fix any issues found.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "refactor: complete AppState decomposition — 8 domain stores, 53 components migrated"
```

---

## Cross-Cutting Notes

**Every store must implement a `reset()` method** that resets all `$state` fields to their initial values. Called during `signOut()` via `auth.onSignedOut` callback.

**`handle*` methods for SignalR events** — Each store that receives SignalR callbacks must expose public `handle*` methods matching the names used in `signalr.svelte.ts`. Copy each callback body from the original `startSignalR()` method.

**ChatHubService context** — Since `ServerSidebar` needs `hub` for `selectServer()`, add a context key for it in `+page.svelte`:
```typescript
const HUB_KEY = Symbol('hub');
setContext(HUB_KEY, hub);
export function getHub(): ChatHubService { return getContext(HUB_KEY); }
```
Or export from `signalr.svelte.ts`.

**ChannelStore callbacks** — `loadChannels()` needs to trigger message loading and voice state loading without depending on MessageStore/VoiceStore directly. Use callback properties:
```typescript
onLoadMessages: ((channelId: string) => Promise<void>) | null = null;
onLoadVoiceStates: (() => Promise<void>) | null = null;
```
Wired in `+page.svelte`.

---

## Verification Summary

| Gate | Command | Must Pass |
|------|---------|-----------|
| TypeScript | `npm run check` | Zero errors |
| Build | `npm run build` | Success |
| Tests | `npm test` | All 7 specs pass |
