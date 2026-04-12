// apps/web/src/lib/state/server-store.svelte.ts
import { getContext, setContext } from 'svelte';
import type {
	MemberServer,
	Member,
	ServerInvite,
	ServerRole,
	CustomEmoji,
	ChannelCategory,
	AuditLogEntry,
	NotificationPreferences,
	Webhook,
	WebhookDelivery,
	BannedMember,
	Channel,
	PresenceStatus,
	DiscordImport,
	DiscordUserMapping
} from '$lib/types/index.js';
import { Permission, hasPermission } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

const SERVER_KEY = Symbol('server-store');

export function createServerStore(
	auth: AuthStore,
	api: ApiClient,
	ui: UIStore,
	hub: ChatHubService
): ServerStore {
	const store = new ServerStore(auth, api, ui, hub);
	setContext(SERVER_KEY, store);
	return store;
}

export function getServerStore(): ServerStore {
	return getContext<ServerStore>(SERVER_KEY);
}

export class ServerStore {
	/* ───── dependencies (declared first so $derived fields can reference them) ───── */
	private readonly auth!: AuthStore;
	private readonly api!: ApiClient;
	private readonly ui!: UIStore;
	private readonly hub!: ChatHubService;

	/* ───── domain data ───── */
	servers = $state<MemberServer[]>([]);
	selectedServerId = $state<string | null>(null);
	members = $state<Member[]>([]);
	serverInvites = $state<ServerInvite[]>([]);
	serverRoles = $state<ServerRole[]>([]);
	bans = $state<BannedMember[]>([]);
	customEmojis = $state<CustomEmoji[]>([]);
	categories = $state<ChannelCategory[]>([]);

	/* ───── audit log ───── */
	auditLogEntries = $state<AuditLogEntry[]>([]);
	hasMoreAuditLog = $state(false);
	isLoadingAuditLog = $state(false);

	/* ───── notification preferences ───── */
	notificationPreferences = $state<NotificationPreferences | null>(null);

	/* ───── webhooks ───── */
	webhooks = $state<Webhook[]>([]);
	webhookDeliveries = $state<WebhookDelivery[]>([]);
	isLoadingWebhooks = $state(false);
	isCreatingWebhook = $state(false);
	selectedWebhookId = $state<string | null>(null);
	isLoadingDeliveries = $state(false);

	/* ───── discord import ───── */
	discordImport = $state<DiscordImport | null>(null);
	discordUserMappings = $state<DiscordUserMapping[]>([]);
	isLoadingImport = $state(false);
	isStartingImport = $state(false);

	/* ───── loading flags ───── */
	isLoadingServers = $state(false);
	isLoadingMembers = $state(false);
	isLoadingInvites = $state(false);
	isCreatingInvite = $state(false);
	isLoadingBans = $state(false);
	isLoadingRoles = $state(false);
	isUpdatingServerName = $state(false);
	isUploadingServerIcon = $state(false);
	isUploadingEmoji = $state(false);
	isDeletingServer = $state(false);
	isJoining = $state(false);
	isCreatingServer = $state(false);

	/* ───── derived ───── */
	readonly isServerOwner = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.isOwner ?? false
	);

	readonly currentServerPermissions = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.permissions ?? 0
	);

	readonly canManageChannels = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageChannels)
	);

	readonly canKickMembers = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.KickMembers)
	);

	readonly canBanMembers = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.BanMembers)
	);

	readonly canManageInvites = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageInvites)
	);

	readonly canDeleteServer = $derived(
		this.auth.isGlobalAdmin || this.isServerOwner
	);

	readonly canDeleteChannel = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageChannels)
	);

	readonly canManageRoles = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageRoles)
	);

	readonly canPinMessages = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.PinMessages)
	);

	readonly canManageEmojis = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageEmojis)
	);

	readonly canManageServer = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageServer)
	);

	readonly canViewAuditLog = $derived(
		this.auth.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ViewAuditLog)
	);

	readonly selectedServerName = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.name ?? 'Codec'
	);

	readonly selectedServerIconUrl = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.iconUrl ?? null
	);

	get isServerMuted(): boolean {
		return this.notificationPreferences?.serverMuted ?? false;
	}

	/* ───── callbacks (wired by orchestration layer) ───── */
	onGoHome: (() => void) | null = null;
	onSelectServer: ((serverId: string) => Promise<void>) | null = null;

	constructor(auth: AuthStore, api: ApiClient, ui: UIStore, hub: ChatHubService) {
		this.auth = auth;
		this.api = api;
		this.ui = ui;
		this.hub = hub;
	}

	/* ═══════════════════ Permission check ═══════════════════ */

	/** Check if the current user has a specific permission in the selected server. */
	hasPermission(perm: number): boolean {
		if (this.auth.isGlobalAdmin) return true;
		return hasPermission(this.currentServerPermissions, perm);
	}

	/* ═══════════════════ Server mention count ═══════════════════ */

	serverMentionCount(
		serverId: string,
		channelMentionCounts: Map<string, number>,
		channelServerMap: Map<string, string>
	): number {
		let total = 0;
		for (const [channelId, count] of channelMentionCounts) {
			if (channelServerMap.get(channelId) === serverId) {
				total += count;
			}
		}
		return total;
	}

	/* ═══════════════════ Data Loading ═══════════════════ */

	async loadServers(): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingServers = true;
		try {
			this.servers = await this.api.getServers(this.auth.idToken);
			this.selectedServerId = this.servers[0]?.serverId ?? null;
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingServers = false;
		}
	}

	async loadMembers(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingMembers = true;
		try {
			this.members = await this.api.getMembers(this.auth.idToken, serverId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingMembers = false;
		}
	}

	async loadCustomEmojis(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			this.customEmojis = await this.api.getCustomEmojis(this.auth.idToken, serverId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Server CRUD ═══════════════════ */

	async createServer(): Promise<void> {
		const name = this.ui.newServerName.trim();
		if (!name) {
			this.ui.error = 'Server name is required.';
			return;
		}
		if (!this.auth.idToken) return;

		this.isCreatingServer = true;
		try {
			const created = await this.api.createServer(this.auth.idToken, name);
			this.ui.newServerName = '';
			this.ui.showCreateServer = false;
			await this.loadServers();
			if (this.onSelectServer) {
				await this.onSelectServer(created.id);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isCreatingServer = false;
		}
	}

	/** Persist a new server display order for the current user. */
	async reorderServers(serverIds: string[]): Promise<void> {
		if (!this.auth.idToken) return;
		// Optimistically reorder the local list.
		const ordered: typeof this.servers = [];
		for (let i = 0; i < serverIds.length; i++) {
			const s = this.servers.find((srv) => srv.serverId === serverIds[i]);
			if (s) ordered.push({ ...s, sortOrder: i });
		}
		this.servers = ordered;

		try {
			await this.api.reorderServers(this.auth.idToken, serverIds);
		} catch (e) {
			this.ui.setError(e);
			// Reload original order on failure.
			await this.loadServers();
		}
	}

	/** Delete a server. Requires Owner role or global admin privileges. */
	async deleteServer(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isDeletingServer = true;
		try {
			await this.api.deleteServer(this.auth.idToken, serverId);
			this.servers = this.servers.filter((s) => s.serverId !== serverId);
			this.ui.serverSettingsOpen = false;
			if (this.selectedServerId === serverId) {
				this.onGoHome?.();
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isDeletingServer = false;
		}
	}

	async updateServerName(name: string): Promise<void> {
		if (!name.trim()) {
			this.ui.error = 'Server name is required.';
			return;
		}
		if (!this.auth.idToken || !this.selectedServerId) return;

		this.isUpdatingServerName = true;
		try {
			await this.api.updateServer(this.auth.idToken, this.selectedServerId, { name: name.trim() });
			// Update will be reflected via SignalR event
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUpdatingServerName = false;
		}
	}

	/** Upload or update the server icon image. */
	async uploadServerIcon(file: File): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;

		this.isUploadingServerIcon = true;
		try {
			await this.api.uploadServerIcon(this.auth.idToken, this.selectedServerId, file);
			// Update will be reflected via SignalR event
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUploadingServerIcon = false;
		}
	}

	/** Remove the server icon. */
	async removeServerIcon(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;

		this.isUploadingServerIcon = true;
		try {
			await this.api.deleteServerIcon(this.auth.idToken, this.selectedServerId);
			// Update will be reflected via SignalR event
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUploadingServerIcon = false;
		}
	}

	async updateServerDescription(description: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateServer(this.auth.idToken, this.selectedServerId, { description });
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Custom Emojis ═══════════════════ */

	async uploadCustomEmoji(name: string, file: File): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isUploadingEmoji = true;
		try {
			const emoji = await this.api.uploadCustomEmoji(this.auth.idToken, this.selectedServerId, name, file);
			this.customEmojis = [...this.customEmojis, emoji];
		} finally {
			this.isUploadingEmoji = false;
		}
	}

	async renameCustomEmoji(emojiId: string, name: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.renameCustomEmoji(this.auth.idToken, this.selectedServerId, emojiId, name);
			this.customEmojis = this.customEmojis.map((e) => (e.id === emojiId ? { ...e, name } : e));
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async deleteCustomEmoji(emojiId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteCustomEmoji(this.auth.idToken, this.selectedServerId, emojiId);
			this.customEmojis = this.customEmojis.filter((e) => e.id !== emojiId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Server Moderation ═══════════════════ */

	/** Kick a member from the currently selected server. */
	async kickMember(userId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.kickMember(this.auth.idToken, this.selectedServerId, userId);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Ban a member from the currently selected server. */
	async banMember(userId: string, options?: { reason?: string; deleteMessages?: boolean }): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.banMember(this.auth.idToken, this.selectedServerId, userId, options);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Unban a user from the currently selected server. */
	async unbanMember(userId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.unbanMember(this.auth.idToken, this.selectedServerId, userId);
			await this.loadBans();
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Add a role to a member in the currently selected server. */
	async addMemberRole(userId: string, roleId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.addMemberRole(this.auth.idToken, this.selectedServerId, userId, roleId);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Remove a role from a member in the currently selected server. */
	async removeMemberRole(userId: string, roleId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.removeMemberRole(this.auth.idToken, this.selectedServerId, userId, roleId);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Replace all roles for a member in the currently selected server. */
	async setMemberRoles(userId: string, roleIds: string[]): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.setMemberRoles(this.auth.idToken, this.selectedServerId, userId, roleIds);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Server Bans ═══════════════════ */

	/** Load banned users for the currently selected server. */
	async loadBans(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingBans = true;
		try {
			this.bans = await this.api.getBans(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingBans = false;
		}
	}

	/* ═══════════════════ Server Roles ═══════════════════ */

	/** Load roles for the currently selected server. */
	async loadRoles(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingRoles = true;
		try {
			this.serverRoles = await this.api.getRoles(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingRoles = false;
		}
	}

	/** Create a new role in the currently selected server. */
	async createRole(
		name: string,
		options?: { color?: string; permissions?: number; isHoisted?: boolean; isMentionable?: boolean }
	): Promise<ServerRole | null> {
		if (!this.auth.idToken || !this.selectedServerId) return null;
		try {
			const role = await this.api.createRole(this.auth.idToken, this.selectedServerId, name, options);
			this.serverRoles = [...this.serverRoles, role].sort((a, b) => a.position - b.position);
			return role;
		} catch (e) {
			this.ui.setError(e);
			return null;
		}
	}

	/** Update a role in the currently selected server. */
	async updateRole(
		roleId: string,
		updates: { name?: string; color?: string | null; permissions?: number; isHoisted?: boolean; isMentionable?: boolean }
	): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			const updated = await this.api.updateRole(this.auth.idToken, this.selectedServerId, roleId, updates);
			this.serverRoles = this.serverRoles.map((r) => (r.id === roleId ? updated : r));
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Delete a role from the currently selected server. */
	async deleteRole(roleId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteRole(this.auth.idToken, this.selectedServerId, roleId);
			this.serverRoles = this.serverRoles.filter((r) => r.id !== roleId);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Server Invites ═══════════════════ */

	/** Load active invites for the currently selected server. */
	async loadInvites(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingInvites = true;
		try {
			this.serverInvites = await this.api.getInvites(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingInvites = false;
		}
	}

	/** Create a new invite for the currently selected server. */
	async createInvite(options?: { maxUses?: number | null; expiresInHours?: number | null }): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isCreatingInvite = true;
		try {
			const invite = await this.api.createInvite(this.auth.idToken, this.selectedServerId, options);
			this.serverInvites = [invite, ...this.serverInvites];
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isCreatingInvite = false;
		}
	}

	/** Revoke an invite from the currently selected server. */
	async revokeInvite(inviteId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.revokeInvite(this.auth.idToken, this.selectedServerId, inviteId);
			this.serverInvites = this.serverInvites.filter((i) => i.id !== inviteId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Join a server via invite code. */
	async joinViaInvite(code: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isJoining = true;
		try {
			const result = await this.api.joinViaInvite(this.auth.idToken, code);
			await this.hub.joinServer(result.serverId);
			await this.loadServers();
			if (this.onSelectServer) {
				await this.onSelectServer(result.serverId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isJoining = false;
		}
	}

	/* ═══════════════════ Webhooks ═══════════════════ */

	/** Load webhooks for the currently selected server. */
	async loadWebhooks(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingWebhooks = true;
		try {
			this.webhooks = await this.api.getWebhooks(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingWebhooks = false;
		}
	}

	/** Create a new webhook for the currently selected server. */
	async createWebhook(data: {
		name: string;
		url: string;
		secret?: string;
		eventTypes: string[];
	}): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isCreatingWebhook = true;
		try {
			const webhook = await this.api.createWebhook(this.auth.idToken, this.selectedServerId, data);
			this.webhooks = [webhook, ...this.webhooks];
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isCreatingWebhook = false;
		}
	}

	/** Update an existing webhook. */
	async updateWebhook(
		webhookId: string,
		data: { name?: string; url?: string; secret?: string; eventTypes?: string[]; isActive?: boolean }
	): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			const updated = await this.api.updateWebhook(this.auth.idToken, this.selectedServerId, webhookId, data);
			this.webhooks = this.webhooks.map((w) => (w.id === webhookId ? updated : w));
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Delete a webhook from the currently selected server. */
	async deleteWebhook(webhookId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteWebhook(this.auth.idToken, this.selectedServerId, webhookId);
			this.webhooks = this.webhooks.filter((w) => w.id !== webhookId);
			if (this.selectedWebhookId === webhookId) {
				this.selectedWebhookId = null;
				this.webhookDeliveries = [];
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Load delivery logs for a specific webhook. */
	async loadWebhookDeliveries(webhookId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingDeliveries = true;
		this.selectedWebhookId = webhookId;
		try {
			this.webhookDeliveries = await this.api.getWebhookDeliveries(
				this.auth.idToken,
				this.selectedServerId,
				webhookId
			);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingDeliveries = false;
		}
	}

	/* ═══════════════════ Categories ═══════════════════ */

	async loadCategories(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			this.categories = await this.api.getCategories(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async createCategory(name: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			const created = await this.api.createCategory(this.auth.idToken, this.selectedServerId, name);
			this.categories = [...this.categories, created];
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async renameCategory(categoryId: string, name: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.renameCategory(this.auth.idToken, this.selectedServerId, categoryId, name);
			this.categories = this.categories.map((c) =>
				c.id === categoryId ? { ...c, name } : c
			);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async deleteCategory(categoryId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteCategory(this.auth.idToken, this.selectedServerId, categoryId);
			this.categories = this.categories.filter((c) => c.id !== categoryId);
			// Note: channel categoryId clearing is handled by the orchestration layer
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async saveChannelOrder(channels: { channelId: string; categoryId?: string; position: number }[]): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateChannelOrder(this.auth.idToken, this.selectedServerId, channels);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async saveCategoryOrder(categories: { categoryId: string; position: number }[]): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateCategoryOrder(this.auth.idToken, this.selectedServerId, categories);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Audit Log ═══════════════════ */

	async loadAuditLog(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		this.isLoadingAuditLog = true;
		this.auditLogEntries = [];
		try {
			const result = await this.api.getAuditLog(this.auth.idToken, this.selectedServerId, { limit: 50 });
			this.auditLogEntries = result.entries;
			this.hasMoreAuditLog = result.hasMore;
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingAuditLog = false;
		}
	}

	async loadOlderAuditLog(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId || !this.hasMoreAuditLog || this.isLoadingAuditLog) return;
		const last = this.auditLogEntries[this.auditLogEntries.length - 1];
		if (!last) return;

		this.isLoadingAuditLog = true;
		try {
			const result = await this.api.getAuditLog(this.auth.idToken, this.selectedServerId, {
				before: last.createdAt,
				limit: 50
			});
			this.auditLogEntries = [...this.auditLogEntries, ...result.entries];
			this.hasMoreAuditLog = result.hasMore;
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingAuditLog = false;
		}
	}

	/* ═══════════════════ Notification Preferences ═══════════════════ */

	async loadNotificationPreferences(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		try {
			this.notificationPreferences = await this.api.getNotificationPreferences(this.auth.idToken, this.selectedServerId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async toggleServerMute(): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId) return;
		const current = this.notificationPreferences?.serverMuted ?? false;
		const next = !current;
		if (this.notificationPreferences) {
			this.notificationPreferences = { ...this.notificationPreferences, serverMuted: next };
		}
		try {
			await this.api.muteServer(this.auth.idToken, this.selectedServerId, next);
		} catch (e) {
			// Revert on failure
			if (this.notificationPreferences) {
				this.notificationPreferences = { ...this.notificationPreferences, serverMuted: current };
			}
			this.ui.setError(e);
		}
	}

	async toggleChannelMute(channelId: string): Promise<void> {
		if (!this.auth.idToken || !this.selectedServerId || !this.notificationPreferences) return;
		const override = this.notificationPreferences.channelOverrides.find((o) => o.channelId === channelId);
		const current = override?.isMuted ?? false;
		const next = !current;

		// Optimistic update
		const overrides = this.notificationPreferences.channelOverrides.filter((o) => o.channelId !== channelId);
		this.notificationPreferences = {
			...this.notificationPreferences,
			channelOverrides: [...overrides, { channelId, isMuted: next }]
		};

		try {
			await this.api.muteChannel(this.auth.idToken, this.selectedServerId, channelId, next);
		} catch (e) {
			// Revert on failure
			await this.loadNotificationPreferences();
			this.ui.setError(e);
		}
	}

	isChannelMuted(channelId: string): boolean {
		return this.notificationPreferences?.channelOverrides.find((o) => o.channelId === channelId)?.isMuted ?? false;
	}

	/* ═══════════════════ Presence ═══════════════════ */

	async loadServerPresence(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			const entries = await this.api.getServerPresence(this.auth.idToken, serverId);
			for (const entry of entries) {
				this.ui.userPresence.set(entry.userId, entry.status);
			}
		} catch (e) {
			console.warn('Failed to load server presence:', e);
		}
	}

	/* ═══════════════════ Server Avatar ═══════════════════ */

	/** Upload a server-specific avatar for the current user. */
	async uploadServerAvatar(serverId: string, file: File): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.uploadServerAvatar(this.auth.idToken, serverId, file);
			await this.loadMembers(serverId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Remove the server-specific avatar for the current user. */
	async deleteServerAvatar(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.deleteServerAvatar(this.auth.idToken, serverId);
			await this.loadMembers(serverId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	handleKicked(event: { serverId: string; serverName: string }): void {
		// Leave the server's SignalR group since we're no longer a member.
		this.hub.leaveServer(event.serverId).catch((error) => {
			console.error('Failed to leave server after being kicked:', error);
			this.ui.setTransientError(
				'There was a problem updating your connection after being kicked. Some data may be out of date.'
			);
		});

		// Remove the server from the local list and navigate away if needed.
		this.servers = this.servers.filter((s) => s.serverId !== event.serverId);
		if (this.selectedServerId === event.serverId) {
			this.onGoHome?.();
		}
		this.ui.setTransientError(`You were kicked from "${event.serverName}".`);
	}

	handleBanned(event: { serverId: string; serverName: string }): void {
		this.hub.leaveServer(event.serverId).catch((error) => {
			console.error('Failed to leave server after being banned:', error);
			this.ui.setTransientError(
				'There was a problem updating your connection after being banned. Some data may be out of date.'
			);
		});
		this.servers = this.servers.filter((s) => s.serverId !== event.serverId);
		if (this.selectedServerId === event.serverId) {
			this.onGoHome?.();
		}
		this.ui.setTransientError(`You were banned from "${event.serverName}".`);
	}

	handleMemberBanned(
		event: { serverId: string; userId: string; deletedMessageCount: number },
		onFilterMessages?: (userId: string) => void
	): void {
		if (event.serverId === this.selectedServerId) {
			this.members = this.members.filter((m) => m.userId !== event.userId);
			if (event.deletedMessageCount > 0 && onFilterMessages) {
				onFilterMessages(event.userId);
			}
		}
	}

	handleMemberUnbanned(event: { serverId: string; userId: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.bans = this.bans.filter((b) => b.userId !== event.userId);
		}
	}

	handleMemberJoined(event: { serverId: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.loadMembers(event.serverId);
		}
	}

	handleMemberLeft(event: { serverId: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.loadMembers(event.serverId);
		}
	}

	handleMemberRoleChanged(event: { serverId: string; userId: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.loadMembers(event.serverId);
		}
		// Refresh own server membership if own roles changed
		if (event.userId === this.auth.me?.user.id) {
			this.loadServers();
		}
	}

	handleServerNameChanged(event: { serverId: string; name: string }): void {
		this.servers = this.servers.map((s) =>
			s.serverId === event.serverId ? { ...s, name: event.name } : s
		);
	}

	handleServerIconChanged(event: { serverId: string; iconUrl: string | null }): void {
		this.servers = this.servers.map((s) =>
			s.serverId === event.serverId ? { ...s, iconUrl: event.iconUrl } : s
		);
	}

	handleServerDeleted(event: { serverId: string }): void {
		this.servers = this.servers.filter((s) => s.serverId !== event.serverId);
		if (this.selectedServerId === event.serverId) {
			this.ui.serverSettingsOpen = false;
			this.onGoHome?.();
		}
	}

	handleServerDescriptionChanged(event: { serverId: string; description: string }): void {
		this.servers = this.servers.map((s) =>
			s.serverId === event.serverId ? { ...s, description: event.description } : s
		);
	}

	handleCustomEmojiAdded(event: { serverId: string; emoji: CustomEmoji }): void {
		if (this.selectedServerId === event.serverId) {
			if (!this.customEmojis.some((e) => e.id === event.emoji.id)) {
				this.customEmojis = [...this.customEmojis, event.emoji];
			}
		}
	}

	handleCustomEmojiUpdated(event: { emojiId: string; name: string }): void {
		this.customEmojis = this.customEmojis.map((e) =>
			e.id === event.emojiId ? { ...e, name: event.name } : e
		);
	}

	handleCustomEmojiDeleted(event: { emojiId: string }): void {
		this.customEmojis = this.customEmojis.filter((e) => e.id !== event.emojiId);
	}

	handleCategoryCreated(event: { serverId: string; categoryId: string; name: string; position: number }): void {
		if (event.serverId === this.selectedServerId) {
			if (!this.categories.some((c) => c.id === event.categoryId)) {
				this.categories = [
					...this.categories,
					{
						id: event.categoryId,
						serverId: event.serverId,
						name: event.name,
						position: event.position
					}
				];
			}
		}
	}

	handleCategoryRenamed(event: { serverId: string; categoryId: string; name: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.categories = this.categories.map((c) =>
				c.id === event.categoryId ? { ...c, name: event.name } : c
			);
		}
	}

	handleCategoryDeleted(
		event: { serverId: string; categoryId: string },
		channels?: Channel[]
	): Channel[] | undefined {
		if (event.serverId === this.selectedServerId) {
			this.categories = this.categories.filter((c) => c.id !== event.categoryId);
			if (channels) {
				return channels.map((ch) =>
					ch.categoryId === event.categoryId ? { ...ch, categoryId: undefined } : ch
				);
			}
		}
		return channels;
	}

	handleCategoryOrderChanged(event: { serverId: string }): void {
		if (event.serverId === this.selectedServerId) {
			this.loadCategories().catch(() => {});
		}
	}

	handleUserStatusChanged(
		event: { userId: string; statusText: string | null; statusEmoji: string | null }
	): void {
		// Update member list with new status
		this.members = this.members.map((m) =>
			m.userId === event.userId
				? { ...m, statusText: event.statusText, statusEmoji: event.statusEmoji }
				: m
		);
		// Update own profile if it's our status
		if (this.auth.me && event.userId === this.auth.me.user.id) {
			this.auth.me = {
				...this.auth.me,
				user: {
					...this.auth.me.user,
					statusText: event.statusText,
					statusEmoji: event.statusEmoji
				}
			};
		}
	}

	/* ═══════════════════ Discord Import ═══════════════════ */

	async loadDiscordImport(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingImport = true;
		try {
			this.discordImport = await this.api.getDiscordImportStatus(this.auth.idToken, serverId);
		} catch {
			this.discordImport = null;
		} finally {
			this.isLoadingImport = false;
		}
	}

	async startDiscordImport(serverId: string, botToken: string, guildId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isStartingImport = true;
		try {
			await this.api.startDiscordImport(this.auth.idToken, serverId, botToken, guildId);
			await this.loadDiscordImport(serverId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isStartingImport = false;
		}
	}

	async resyncDiscordImport(serverId: string, botToken: string, guildId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isStartingImport = true;
		try {
			await this.api.resyncDiscordImport(this.auth.idToken, serverId, botToken, guildId);
			await this.loadDiscordImport(serverId);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isStartingImport = false;
		}
	}

	async cancelDiscordImport(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.cancelDiscordImport(this.auth.idToken, serverId);
			await this.loadDiscordImport(serverId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async loadDiscordUserMappings(serverId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			this.discordUserMappings = await this.api.getDiscordUserMappings(this.auth.idToken, serverId);
		} catch {
			this.discordUserMappings = [];
		}
	}

	async claimDiscordIdentity(serverId: string, discordUserId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.claimDiscordIdentity(this.auth.idToken, serverId, discordUserId);
			await this.loadDiscordUserMappings(serverId);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Reset ═══════════════════ */

	reset(): void {
		this.servers = [];
		this.selectedServerId = null;
		this.members = [];
		this.serverInvites = [];
		this.serverRoles = [];
		this.bans = [];
		this.customEmojis = [];
		this.categories = [];
		this.auditLogEntries = [];
		this.hasMoreAuditLog = false;
		this.isLoadingAuditLog = false;
		this.notificationPreferences = null;
		this.webhooks = [];
		this.webhookDeliveries = [];
		this.isLoadingWebhooks = false;
		this.isCreatingWebhook = false;
		this.selectedWebhookId = null;
		this.isLoadingDeliveries = false;
		this.discordImport = null;
		this.discordUserMappings = [];
		this.isLoadingImport = false;
		this.isStartingImport = false;
		this.isLoadingServers = false;
		this.isLoadingMembers = false;
		this.isLoadingInvites = false;
		this.isCreatingInvite = false;
		this.isLoadingBans = false;
		this.isLoadingRoles = false;
		this.isUpdatingServerName = false;
		this.isUploadingServerIcon = false;
		this.isUploadingEmoji = false;
		this.isDeletingServer = false;
		this.isJoining = false;
		this.isCreatingServer = false;
	}
}
