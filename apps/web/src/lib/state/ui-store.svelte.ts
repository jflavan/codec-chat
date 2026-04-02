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

	/* ───── presence (shared across stores) ───── */
	userPresence = new SvelteMap<string, PresenceStatus>();

	/* ───── reaction tracking (shared by MessageStore and DmStore) ───── */
	pendingReactionKeys = $state<Set<string>>(new Set());
	ignoredReactionUpdates = $state<Map<string, string[]>>(new Map());

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
