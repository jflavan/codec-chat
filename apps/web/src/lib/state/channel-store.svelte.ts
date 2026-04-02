// apps/web/src/lib/state/channel-store.svelte.ts
import { getContext, setContext } from 'svelte';
import type { Channel, ChannelPermissionOverride } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type {
	MentionReceivedEvent,
	ChannelNameChangedEvent,
	ChannelDeletedEvent,
	ChannelDescriptionChangedEvent,
	ChannelOrderChangedEvent,
	ChannelOverrideUpdatedEvent
} from '$lib/services/chat-hub.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

const CHANNEL_KEY = Symbol('channel-store');

export function createChannelStore(
	auth: AuthStore,
	api: ApiClient,
	ui: UIStore,
	hub: ChatHubService
): ChannelStore {
	const store = new ChannelStore(auth, api, ui, hub);
	setContext(CHANNEL_KEY, store);
	return store;
}

export function getChannelStore(): ChannelStore {
	return getContext<ChannelStore>(CHANNEL_KEY);
}

export class ChannelStore {
	/* ───── dependencies ───── */
	private readonly auth!: AuthStore;
	private readonly api!: ApiClient;
	private readonly ui!: UIStore;
	private readonly hub!: ChatHubService;

	/* ───── domain data ───── */
	channels = $state<Channel[]>([]);
	selectedChannelId = $state<string | null>(null);
	channelMentionCounts = $state<Map<string, number>>(new Map());
	channelServerMap = $state<Map<string, string>>(new Map());

	/* ───── loading flags ───── */
	isLoadingChannels = $state(false);
	isCreatingChannel = $state(false);
	isUpdatingChannelName = $state(false);

	/* ───── derived ───── */
	selectedChannelName = $derived(
		this.channels.find((c) => c.id === this.selectedChannelId)?.name ?? null
	);

	/* ───── callbacks (wired by orchestration layer to avoid circular deps) ───── */
	getSelectedServerId: (() => string | null) | null = null;
	onLoadMessages: ((channelId: string) => Promise<void>) | null = null;
	onLoadVoiceStates: (() => Promise<void>) | null = null;
	onLoadPinnedMessages: ((channelId: string) => Promise<void>) | null = null;
	/** Called when switching channels to reset typing, pendingMentions, replyingTo, pinned state. */
	onChannelSwitch: (() => void) | null = null;

	constructor(auth: AuthStore, api: ApiClient, ui: UIStore, hub: ChatHubService) {
		this.auth = auth;
		this.api = api;
		this.ui = ui;
		this.hub = hub;
	}

	/* ═══════════════════ Data Loading ═══════════════════ */

	async loadChannels(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingChannels = true;
		try {
			this.channels = await this.api.getChannels(this.auth.idToken, serverId);

			// Keep channel→server mapping up to date for mention badge aggregation
			const mapNext = new Map(this.channelServerMap);
			for (const ch of this.channels) {
				mapNext.set(ch.id, serverId);
			}
			this.channelServerMap = mapNext;

			const previousChannelId = this.selectedChannelId;
			const firstTextChannel =
				this.channels.find((c) => c.type !== 'voice') ?? this.channels[0] ?? null;
			this.selectedChannelId = firstTextChannel?.id ?? null;

			// Clear mention badge for the auto-selected channel
			if (this.selectedChannelId) {
				const mentionNext = new Map(this.channelMentionCounts);
				mentionNext.delete(this.selectedChannelId);
				this.channelMentionCounts = mentionNext;
			}

			if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
			if (this.selectedChannelId) await this.hub.joinChannel(this.selectedChannelId);

			if (this.selectedChannelId) {
				await this.onLoadMessages?.(this.selectedChannelId);
			}

			// Load voice state membership for each voice channel
			await this.onLoadVoiceStates?.();
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingChannels = false;
		}
	}

	/* ═══════════════════ Actions ═══════════════════ */

	async selectChannel(channelId: string): Promise<void> {
		const previousChannelId = this.selectedChannelId;
		this.selectedChannelId = channelId;

		// Reset cross-store state via callback
		this.onChannelSwitch?.();
		this.ui.mobileNavOpen = false;

		// Clear mention badge for this channel
		const next = new Map(this.channelMentionCounts);
		next.delete(channelId);
		this.channelMentionCounts = next;

		if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
		await this.hub.joinChannel(channelId);
		await this.onLoadMessages?.(channelId);
		await this.onLoadPinnedMessages?.(channelId);
	}

	async createChannel(): Promise<void> {
		const name = this.ui.newChannelName.trim();
		if (!name) {
			this.ui.error = 'Channel name is required.';
			return;
		}
		const selectedServerId = this.getSelectedServerId?.();
		if (!this.auth.idToken || !selectedServerId) return;

		this.isCreatingChannel = true;
		try {
			const created = await this.api.createChannel(
				this.auth.idToken,
				selectedServerId,
				name,
				this.ui.newChannelType
			);
			this.ui.newChannelName = '';
			this.ui.newChannelType = 'text';
			this.ui.showCreateChannel = false;
			await this.loadChannels(selectedServerId);
			if (created.type !== 'voice') {
				await this.selectChannel(created.id);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isCreatingChannel = false;
		}
	}

	async deleteChannel(channelId: string): Promise<void> {
		const selectedServerId = this.getSelectedServerId?.();
		if (!this.auth.idToken || !selectedServerId) return;
		try {
			await this.api.deleteChannel(this.auth.idToken, selectedServerId, channelId);
			this.channels = this.channels.filter((c) => c.id !== channelId);
			if (this.selectedChannelId === channelId) {
				const firstChannel = this.channels[0];
				if (firstChannel) {
					this.selectedChannelId = firstChannel.id;
					await this.onLoadMessages?.(firstChannel.id);
				} else {
					this.selectedChannelId = null;
				}
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async updateChannelName(channelId: string, name: string): Promise<void> {
		if (!name.trim()) {
			this.ui.error = 'Channel name is required.';
			return;
		}
		const selectedServerId = this.getSelectedServerId?.();
		if (!this.auth.idToken || !selectedServerId) return;

		this.isUpdatingChannelName = true;
		try {
			await this.api.updateChannel(this.auth.idToken, selectedServerId, channelId, {
				name: name.trim()
			});
			// Update will be reflected via SignalR event
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUpdatingChannelName = false;
		}
	}

	async updateChannelDescription(channelId: string, description: string): Promise<void> {
		const selectedServerId = this.getSelectedServerId?.();
		if (!this.auth.idToken || !selectedServerId) return;
		try {
			await this.api.updateChannel(this.auth.idToken, selectedServerId, channelId, {
				description
			});
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Channel Permission Overrides ═══════════════════ */

	async getChannelOverrides(channelId: string): Promise<ChannelPermissionOverride[]> {
		if (!this.auth.idToken) return [];
		return this.api.getChannelOverrides(this.auth.idToken, channelId);
	}

	async setChannelOverride(
		channelId: string,
		roleId: string,
		allow: number,
		deny: number
	): Promise<void> {
		if (!this.auth.idToken) return;
		await this.api.setChannelOverride(this.auth.idToken, channelId, roleId, allow, deny);
	}

	async deleteChannelOverride(channelId: string, roleId: string): Promise<void> {
		if (!this.auth.idToken) return;
		await this.api.deleteChannelOverride(this.auth.idToken, channelId, roleId);
	}

	/* ═══════════════════ Channel Ordering ═══════════════════ */

	async saveChannelOrder(
		channels: { channelId: string; categoryId?: string; position: number }[]
	): Promise<void> {
		const selectedServerId = this.getSelectedServerId?.();
		if (!this.auth.idToken || !selectedServerId) return;
		try {
			await this.api.updateChannelOrder(this.auth.idToken, selectedServerId, channels);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Mention Count ═══════════════════ */

	channelMentionCount(channelId: string): number {
		return this.channelMentionCounts.get(channelId) ?? 0;
	}

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	handleChannelNameChanged(event: ChannelNameChangedEvent, selectedServerId: string | null): void {
		if (event.serverId === selectedServerId) {
			this.channels = this.channels.map((c) =>
				c.id === event.channelId ? { ...c, name: event.name } : c
			);
		}
	}

	handleChannelDeleted(event: ChannelDeletedEvent, selectedServerId: string | null): void {
		if (event.serverId === selectedServerId) {
			this.channels = this.channels.filter((c) => c.id !== event.channelId);
			if (this.selectedChannelId === event.channelId) {
				const next = this.channels.find((c) => c.type !== 'voice');
				this.selectedChannelId = next?.id ?? null;
			}
		}
	}

	handleChannelDescriptionChanged(
		event: ChannelDescriptionChangedEvent,
		selectedServerId: string | null
	): void {
		if (event.serverId === selectedServerId) {
			this.channels = this.channels.map((c) =>
				c.id === event.channelId ? { ...c, description: event.description } : c
			);
		}
	}

	handleMentionReceived(event: MentionReceivedEvent): void {
		// Track channel→server mapping for badge aggregation
		const mapCopy = new Map(this.channelServerMap);
		mapCopy.set(event.channelId, event.serverId);
		this.channelServerMap = mapCopy;

		// Don't increment badge for the channel the user is currently viewing
		if (event.channelId === this.selectedChannelId) return;

		const next = new Map(this.channelMentionCounts);
		next.set(event.channelId, (next.get(event.channelId) ?? 0) + 1);
		this.channelMentionCounts = next;
	}

	async handleChannelOrderChanged(
		event: ChannelOrderChangedEvent,
		selectedServerId: string | null
	): Promise<void> {
		if (event.serverId === selectedServerId) {
			await this.loadChannels(event.serverId).catch(() => {});
		}
	}

	async handleChannelOverrideUpdated(
		event: ChannelOverrideUpdatedEvent,
		selectedServerId: string | null
	): Promise<void> {
		if (selectedServerId) {
			const channel = this.channels.find((c) => c.id === event.channelId);
			if (channel?.serverId === selectedServerId || !channel) {
				await this.loadChannels(selectedServerId).catch(() => {});
			}
		}
	}

	/* ═══════════════════ Reset ═══════════════════ */

	reset(): void {
		this.channels = [];
		this.selectedChannelId = null;
		this.isLoadingChannels = false;
		this.isCreatingChannel = false;
		this.isUpdatingChannelName = false;
		this.channelMentionCounts = new Map();
		this.channelServerMap = new Map();
	}
}
