import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));
vi.mock('$lib/types/index.js', () => ({
	Permission: {
		ManageChannels: 1,
		KickMembers: 2,
		BanMembers: 4,
		ManageInvites: 8,
		ManageRoles: 16,
		PinMessages: 32,
		ManageEmojis: 64,
		ManageServer: 128,
		ViewAuditLog: 256
	},
	hasPermission: (perms: number, perm: number) => (perms & perm) === perm
}));

import { ServerStore } from './server-store.svelte';

function mockAuth(overrides: Record<string, unknown> = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1', displayName: 'Test User' } },
		isGlobalAdmin: false,
		...overrides
	} as any;
}

function mockApi() {
	return {
		getServers: vi.fn(),
		getMembers: vi.fn(),
		getCustomEmojis: vi.fn(),
		createServer: vi.fn(),
		reorderServers: vi.fn(),
		deleteServer: vi.fn(),
		updateServer: vi.fn(),
		uploadServerIcon: vi.fn(),
		deleteServerIcon: vi.fn(),
		kickMember: vi.fn(),
		banMember: vi.fn(),
		unbanMember: vi.fn(),
		addMemberRole: vi.fn(),
		removeMemberRole: vi.fn(),
		setMemberRoles: vi.fn(),
		getBans: vi.fn(),
		getRoles: vi.fn(),
		createRole: vi.fn(),
		updateRole: vi.fn(),
		deleteRole: vi.fn(),
		getInvites: vi.fn(),
		createInvite: vi.fn(),
		revokeInvite: vi.fn(),
		joinViaInvite: vi.fn(),
		getWebhooks: vi.fn(),
		createWebhook: vi.fn(),
		updateWebhook: vi.fn(),
		deleteWebhook: vi.fn(),
		getWebhookDeliveries: vi.fn(),
		getCategories: vi.fn(),
		createCategory: vi.fn(),
		renameCategory: vi.fn(),
		deleteCategory: vi.fn(),
		updateChannelOrder: vi.fn(),
		updateCategoryOrder: vi.fn(),
		getAuditLog: vi.fn(),
		getNotificationPreferences: vi.fn(),
		muteServer: vi.fn(),
		muteChannel: vi.fn(),
		getServerPresence: vi.fn(),
		uploadServerAvatar: vi.fn(),
		deleteServerAvatar: vi.fn(),
		uploadCustomEmoji: vi.fn(),
		renameCustomEmoji: vi.fn(),
		deleteCustomEmoji: vi.fn(),
		getDiscordImportStatus: vi.fn(),
		startDiscordImport: vi.fn(),
		resyncDiscordImport: vi.fn(),
		cancelDiscordImport: vi.fn(),
		getDiscordUserMappings: vi.fn(),
		claimDiscordIdentity: vi.fn()
	} as any;
}

function mockUi() {
	return {
		setError: vi.fn(),
		setTransientError: vi.fn(),
		error: null as string | null,
		newServerName: '',
		showCreateServer: false,
		serverSettingsOpen: false,
		userPresence: new Map()
	} as any;
}

function mockHub() {
	return {
		joinServer: vi.fn().mockResolvedValue(undefined),
		leaveServer: vi.fn().mockResolvedValue(undefined)
	} as any;
}

describe('ServerStore', () => {
	let store: ServerStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: ReturnType<typeof mockUi>;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		vi.clearAllMocks();
		auth = mockAuth();
		api = mockApi();
		ui = mockUi();
		hub = mockHub();
		store = new ServerStore(auth, api, ui, hub);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have empty servers array', () => {
			expect(store.servers).toEqual([]);
		});

		it('should have null selectedServerId', () => {
			expect(store.selectedServerId).toBeNull();
		});

		it('should have empty members', () => {
			expect(store.members).toEqual([]);
		});

		it('should have empty serverRoles', () => {
			expect(store.serverRoles).toEqual([]);
		});

		it('should have empty categories', () => {
			expect(store.categories).toEqual([]);
		});

		it('should have false loading flags', () => {
			expect(store.isLoadingServers).toBe(false);
			expect(store.isLoadingMembers).toBe(false);
			expect(store.isLoadingInvites).toBe(false);
			expect(store.isCreatingServer).toBe(false);
			expect(store.isDeletingServer).toBe(false);
		});

		it('should have null notificationPreferences', () => {
			expect(store.notificationPreferences).toBeNull();
		});
	});

	// --- loadServers ---

	describe('loadServers', () => {
		it('should fetch servers and set first as selected', async () => {
			const servers = [
				{ serverId: 's1', name: 'Server 1', roles: [], sortOrder: 0, permissions: 0, isOwner: false },
				{ serverId: 's2', name: 'Server 2', roles: [], sortOrder: 1, permissions: 0, isOwner: false }
			];
			api.getServers.mockResolvedValue(servers);

			await store.loadServers();

			expect(api.getServers).toHaveBeenCalledWith('test-token');
			expect(store.servers).toEqual(servers);
			expect(store.selectedServerId).toBe('s1');
			expect(store.isLoadingServers).toBe(false);
		});

		it('should set selectedServerId to null when no servers returned', async () => {
			api.getServers.mockResolvedValue([]);

			await store.loadServers();

			expect(store.selectedServerId).toBeNull();
		});

		it('should not call API when idToken is null', async () => {
			auth.idToken = null;

			await store.loadServers();

			expect(api.getServers).not.toHaveBeenCalled();
		});

		it('should call ui.setError on API failure', async () => {
			const error = new Error('Network error');
			api.getServers.mockRejectedValue(error);

			await store.loadServers();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingServers).toBe(false);
		});
	});

	// --- loadMembers ---

	describe('loadMembers', () => {
		it('should fetch members for a server', async () => {
			const members = [{ userId: 'u1', displayName: 'User 1' }];
			api.getMembers.mockResolvedValue(members);

			await store.loadMembers('s1');

			expect(api.getMembers).toHaveBeenCalledWith('test-token', 's1');
			expect(store.members).toEqual(members);
			expect(store.isLoadingMembers).toBe(false);
		});

		it('should not call API when idToken is null', async () => {
			auth.idToken = null;

			await store.loadMembers('s1');

			expect(api.getMembers).not.toHaveBeenCalled();
		});
	});

	// --- createServer ---

	describe('createServer', () => {
		it('should create server and reload server list', async () => {
			ui.newServerName = '  My Server  ';
			api.createServer.mockResolvedValue({ id: 'new-s1' });
			api.getServers.mockResolvedValue([]);
			const selectFn = vi.fn();
			store.onSelectServer = selectFn;

			await store.createServer();

			expect(api.createServer).toHaveBeenCalledWith('test-token', 'My Server');
			expect(ui.newServerName).toBe('');
			expect(ui.showCreateServer).toBe(false);
			expect(selectFn).toHaveBeenCalledWith('new-s1');
		});

		it('should set error when name is empty', async () => {
			ui.newServerName = '   ';

			await store.createServer();

			expect(ui.error).toBe('Server name is required.');
			expect(api.createServer).not.toHaveBeenCalled();
		});

		it('should set isCreatingServer during request', async () => {
			ui.newServerName = 'Test';
			let loadingDuringCall = false;
			api.createServer.mockImplementation(async () => {
				loadingDuringCall = store.isCreatingServer;
				return { id: 'x' };
			});
			api.getServers.mockResolvedValue([]);

			await store.createServer();

			expect(loadingDuringCall).toBe(true);
			expect(store.isCreatingServer).toBe(false);
		});
	});

	// --- deleteServer ---

	describe('deleteServer', () => {
		it('should delete server and remove from list', async () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: true },
				{ serverId: 's2', name: 'S2', roles: [], sortOrder: 1, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;
			api.deleteServer.mockResolvedValue(undefined);

			await store.deleteServer('s1');

			expect(api.deleteServer).toHaveBeenCalledWith('test-token', 's1');
			expect(store.servers).toHaveLength(1);
			expect(store.servers[0].serverId).toBe('s2');
			expect(ui.serverSettingsOpen).toBe(false);
			expect(goHomeFn).toHaveBeenCalled();
		});

		it('should not call goHome when deleting a different server', async () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: true },
				{ serverId: 's2', name: 'S2', roles: [], sortOrder: 1, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;
			api.deleteServer.mockResolvedValue(undefined);

			await store.deleteServer('s2');

			expect(goHomeFn).not.toHaveBeenCalled();
		});
	});

	// --- updateServerName ---

	describe('updateServerName', () => {
		it('should call API with trimmed name', async () => {
			store.selectedServerId = 's1';
			api.updateServer.mockResolvedValue(undefined);

			await store.updateServerName('  New Name  ');

			expect(api.updateServer).toHaveBeenCalledWith('test-token', 's1', { name: 'New Name' });
		});

		it('should set error when name is blank', async () => {
			store.selectedServerId = 's1';

			await store.updateServerName('   ');

			expect(ui.error).toBe('Server name is required.');
			expect(api.updateServer).not.toHaveBeenCalled();
		});
	});

	// --- Roles ---

	describe('loadRoles', () => {
		it('should fetch roles for selected server', async () => {
			store.selectedServerId = 's1';
			const roles = [{ id: 'r1', name: 'Admin', position: 0 }];
			api.getRoles.mockResolvedValue(roles);

			await store.loadRoles();

			expect(api.getRoles).toHaveBeenCalledWith('test-token', 's1');
			expect(store.serverRoles).toEqual(roles);
		});
	});

	describe('createRole', () => {
		it('should create role and add to sorted list', async () => {
			store.selectedServerId = 's1';
			const existing = { id: 'r1', name: 'Member', position: 0 };
			store.serverRoles = [existing] as any;
			const newRole = { id: 'r2', name: 'Admin', position: 1 };
			api.createRole.mockResolvedValue(newRole);

			const result = await store.createRole('Admin', { color: '#ff0000' });

			expect(result).toEqual(newRole);
			expect(store.serverRoles).toHaveLength(2);
		});

		it('should return null when no auth', async () => {
			auth.idToken = null;

			const result = await store.createRole('Test');

			expect(result).toBeNull();
		});
	});

	describe('deleteRole', () => {
		it('should remove role and reload members', async () => {
			store.selectedServerId = 's1';
			store.serverRoles = [
				{ id: 'r1', name: 'Admin', position: 0 },
				{ id: 'r2', name: 'Mod', position: 1 }
			] as any;
			api.deleteRole.mockResolvedValue(undefined);
			api.getMembers.mockResolvedValue([]);

			await store.deleteRole('r1');

			expect(store.serverRoles).toHaveLength(1);
			expect(store.serverRoles[0].id).toBe('r2');
			expect(api.getMembers).toHaveBeenCalled();
		});
	});

	// --- Invites ---

	describe('createInvite', () => {
		it('should create invite and prepend to list', async () => {
			store.selectedServerId = 's1';
			const invite = { id: 'i1', code: 'abc' };
			api.createInvite.mockResolvedValue(invite);

			await store.createInvite({ maxUses: 10 });

			expect(store.serverInvites[0]).toEqual(invite);
			expect(store.isCreatingInvite).toBe(false);
		});
	});

	describe('revokeInvite', () => {
		it('should remove invite from list', async () => {
			store.selectedServerId = 's1';
			store.serverInvites = [{ id: 'i1', code: 'abc' }, { id: 'i2', code: 'def' }] as any;
			api.revokeInvite.mockResolvedValue(undefined);

			await store.revokeInvite('i1');

			expect(store.serverInvites).toHaveLength(1);
			expect(store.serverInvites[0].id).toBe('i2');
		});
	});

	// --- joinViaInvite ---

	describe('joinViaInvite', () => {
		it('should join server via invite code and reload', async () => {
			api.joinViaInvite.mockResolvedValue({ serverId: 's1' });
			api.getServers.mockResolvedValue([]);
			const selectFn = vi.fn();
			store.onSelectServer = selectFn;

			await store.joinViaInvite('invite-code');

			expect(api.joinViaInvite).toHaveBeenCalledWith('test-token', 'invite-code');
			expect(hub.joinServer).toHaveBeenCalledWith('s1');
			expect(selectFn).toHaveBeenCalledWith('s1');
		});
	});

	// --- Moderation ---

	describe('kickMember', () => {
		it('should kick member and reload members', async () => {
			store.selectedServerId = 's1';
			api.kickMember.mockResolvedValue(undefined);
			api.getMembers.mockResolvedValue([]);

			await store.kickMember('u1');

			expect(api.kickMember).toHaveBeenCalledWith('test-token', 's1', 'u1');
			expect(api.getMembers).toHaveBeenCalled();
		});
	});

	describe('banMember', () => {
		it('should ban member with options and reload members', async () => {
			store.selectedServerId = 's1';
			api.banMember.mockResolvedValue(undefined);
			api.getMembers.mockResolvedValue([]);

			await store.banMember('u1', { reason: 'spam', deleteMessages: true });

			expect(api.banMember).toHaveBeenCalledWith('test-token', 's1', 'u1', {
				reason: 'spam',
				deleteMessages: true
			});
		});
	});

	// --- SignalR Handlers ---

	describe('handleServerNameChanged', () => {
		it('should update server name in list', () => {
			store.servers = [
				{ serverId: 's1', name: 'Old', roles: [], sortOrder: 0, permissions: 0, isOwner: false }
			] as any;

			store.handleServerNameChanged({ serverId: 's1', name: 'New Name' });

			expect(store.servers[0].name).toBe('New Name');
		});

		it('should not change other servers', () => {
			store.servers = [
				{ serverId: 's1', name: 'One', roles: [], sortOrder: 0, permissions: 0, isOwner: false },
				{ serverId: 's2', name: 'Two', roles: [], sortOrder: 1, permissions: 0, isOwner: false }
			] as any;

			store.handleServerNameChanged({ serverId: 's1', name: 'Changed' });

			expect(store.servers[1].name).toBe('Two');
		});
	});

	describe('handleServerIconChanged', () => {
		it('should update server icon in list', () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', iconUrl: null, roles: [], sortOrder: 0, permissions: 0, isOwner: false }
			] as any;

			store.handleServerIconChanged({ serverId: 's1', iconUrl: '/icon.png' });

			expect(store.servers[0].iconUrl).toBe('/icon.png');
		});
	});

	describe('handleServerDeleted', () => {
		it('should remove server from list and go home if selected', () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;

			store.handleServerDeleted({ serverId: 's1' });

			expect(store.servers).toHaveLength(0);
			expect(goHomeFn).toHaveBeenCalled();
			expect(ui.serverSettingsOpen).toBe(false);
		});

		it('should not call goHome if different server deleted', () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: false },
				{ serverId: 's2', name: 'S2', roles: [], sortOrder: 1, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;

			store.handleServerDeleted({ serverId: 's2' });

			expect(store.servers).toHaveLength(1);
			expect(goHomeFn).not.toHaveBeenCalled();
		});
	});

	describe('handleMemberBanned', () => {
		it('should remove member from list when same server selected', () => {
			store.selectedServerId = 's1';
			store.members = [
				{ userId: 'u1', displayName: 'User 1' },
				{ userId: 'u2', displayName: 'User 2' }
			] as any;

			store.handleMemberBanned({ serverId: 's1', userId: 'u1', deletedMessageCount: 0 });

			expect(store.members).toHaveLength(1);
			expect(store.members[0].userId).toBe('u2');
		});

		it('should call onFilterMessages when messages were deleted', () => {
			store.selectedServerId = 's1';
			store.members = [{ userId: 'u1', displayName: 'U1' }] as any;
			const filterFn = vi.fn();

			store.handleMemberBanned(
				{ serverId: 's1', userId: 'u1', deletedMessageCount: 5 },
				filterFn
			);

			expect(filterFn).toHaveBeenCalledWith('u1');
		});

		it('should not modify members for different server', () => {
			store.selectedServerId = 's1';
			store.members = [{ userId: 'u1', displayName: 'U1' }] as any;

			store.handleMemberBanned({ serverId: 's2', userId: 'u1', deletedMessageCount: 0 });

			expect(store.members).toHaveLength(1);
		});
	});

	describe('handleMemberUnbanned', () => {
		it('should remove user from bans list when same server selected', () => {
			store.selectedServerId = 's1';
			store.bans = [{ userId: 'u1' }, { userId: 'u2' }] as any;

			store.handleMemberUnbanned({ serverId: 's1', userId: 'u1' });

			expect(store.bans).toHaveLength(1);
			expect(store.bans[0].userId).toBe('u2');
		});
	});

	describe('handleMemberJoined', () => {
		it('should reload members when same server selected', async () => {
			store.selectedServerId = 's1';
			api.getMembers.mockResolvedValue([]);

			store.handleMemberJoined({ serverId: 's1' });

			expect(api.getMembers).toHaveBeenCalledWith('test-token', 's1');
		});

		it('should not reload for different server', () => {
			store.selectedServerId = 's1';

			store.handleMemberJoined({ serverId: 's2' });

			expect(api.getMembers).not.toHaveBeenCalled();
		});
	});

	describe('handleKicked', () => {
		it('should remove server and call goHome if selected', () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;

			store.handleKicked({ serverId: 's1', serverName: 'S1' });

			expect(store.servers).toHaveLength(0);
			expect(goHomeFn).toHaveBeenCalled();
			expect(ui.setTransientError).toHaveBeenCalledWith('You were kicked from "S1".');
		});
	});

	describe('handleBanned', () => {
		it('should remove server and show message', () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', roles: [], sortOrder: 0, permissions: 0, isOwner: false }
			] as any;
			store.selectedServerId = 's1';
			const goHomeFn = vi.fn();
			store.onGoHome = goHomeFn;

			store.handleBanned({ serverId: 's1', serverName: 'S1' });

			expect(store.servers).toHaveLength(0);
			expect(goHomeFn).toHaveBeenCalled();
			expect(ui.setTransientError).toHaveBeenCalledWith('You were banned from "S1".');
		});
	});

	// --- Categories ---

	describe('handleCategoryCreated', () => {
		it('should add new category when server matches', () => {
			store.selectedServerId = 's1';
			store.categories = [];

			store.handleCategoryCreated({
				serverId: 's1',
				categoryId: 'c1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(1);
			expect(store.categories[0].name).toBe('General');
		});

		it('should not add duplicate category', () => {
			store.selectedServerId = 's1';
			store.categories = [{ id: 'c1', serverId: 's1', name: 'General', position: 0 }];

			store.handleCategoryCreated({
				serverId: 's1',
				categoryId: 'c1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(1);
		});

		it('should not add category for different server', () => {
			store.selectedServerId = 's1';
			store.categories = [];

			store.handleCategoryCreated({
				serverId: 's2',
				categoryId: 'c1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(0);
		});
	});

	describe('handleCategoryDeleted', () => {
		it('should remove category and clear channel categoryId', () => {
			store.selectedServerId = 's1';
			store.categories = [
				{ id: 'c1', serverId: 's1', name: 'General', position: 0 },
				{ id: 'c2', serverId: 's1', name: 'Other', position: 1 }
			];
			const channels = [
				{ id: 'ch1', categoryId: 'c1', name: 'text' },
				{ id: 'ch2', categoryId: 'c2', name: 'voice' }
			] as any;

			const result = store.handleCategoryDeleted({ serverId: 's1', categoryId: 'c1' }, channels);

			expect(store.categories).toHaveLength(1);
			expect(result![0].categoryId).toBeUndefined();
			expect(result![1].categoryId).toBe('c2');
		});
	});

	// --- Custom Emojis ---

	describe('handleCustomEmojiAdded', () => {
		it('should add emoji to list for selected server', () => {
			store.selectedServerId = 's1';
			store.customEmojis = [];
			const emoji = { id: 'e1', name: 'smile', imageUrl: '/img.png' };

			store.handleCustomEmojiAdded({ serverId: 's1', emoji: emoji as any });

			expect(store.customEmojis).toHaveLength(1);
		});

		it('should not add duplicate emoji', () => {
			store.selectedServerId = 's1';
			const emoji = { id: 'e1', name: 'smile', imageUrl: '/img.png' };
			store.customEmojis = [emoji as any];

			store.handleCustomEmojiAdded({ serverId: 's1', emoji: emoji as any });

			expect(store.customEmojis).toHaveLength(1);
		});
	});

	describe('handleCustomEmojiDeleted', () => {
		it('should remove emoji from list', () => {
			store.customEmojis = [
				{ id: 'e1', name: 'smile' },
				{ id: 'e2', name: 'wave' }
			] as any;

			store.handleCustomEmojiDeleted({ emojiId: 'e1' });

			expect(store.customEmojis).toHaveLength(1);
			expect(store.customEmojis[0].id).toBe('e2');
		});
	});

	// --- serverMentionCount ---

	describe('serverMentionCount', () => {
		it('should sum mention counts for channels belonging to the server', () => {
			const channelMentionCounts = new Map([
				['ch1', 3],
				['ch2', 2],
				['ch3', 5]
			]);
			const channelServerMap = new Map([
				['ch1', 's1'],
				['ch2', 's1'],
				['ch3', 's2']
			]);

			const count = store.serverMentionCount('s1', channelMentionCounts, channelServerMap);

			expect(count).toBe(5);
		});

		it('should return 0 when no channels belong to server', () => {
			const count = store.serverMentionCount('s1', new Map(), new Map());

			expect(count).toBe(0);
		});
	});

	// --- reorderServers ---

	describe('reorderServers', () => {
		it('should optimistically reorder servers', async () => {
			store.servers = [
				{ serverId: 's1', name: 'S1', sortOrder: 0, roles: [], permissions: 0, isOwner: false },
				{ serverId: 's2', name: 'S2', sortOrder: 1, roles: [], permissions: 0, isOwner: false }
			] as any;
			api.reorderServers.mockResolvedValue(undefined);

			await store.reorderServers(['s2', 's1']);

			expect(store.servers[0].serverId).toBe('s2');
			expect(store.servers[0].sortOrder).toBe(0);
			expect(store.servers[1].serverId).toBe('s1');
			expect(store.servers[1].sortOrder).toBe(1);
		});
	});

	// --- Webhooks ---

	describe('deleteWebhook', () => {
		it('should remove webhook and clear selection if it was selected', async () => {
			store.selectedServerId = 's1';
			store.webhooks = [{ id: 'w1' }, { id: 'w2' }] as any;
			store.selectedWebhookId = 'w1';
			store.webhookDeliveries = [{ id: 'd1' }] as any;
			api.deleteWebhook.mockResolvedValue(undefined);

			await store.deleteWebhook('w1');

			expect(store.webhooks).toHaveLength(1);
			expect(store.selectedWebhookId).toBeNull();
			expect(store.webhookDeliveries).toEqual([]);
		});
	});

	// --- handleUserStatusChanged ---

	describe('handleUserStatusChanged', () => {
		it('should update member status in list', () => {
			store.members = [
				{ userId: 'u1', displayName: 'U1', statusText: null, statusEmoji: null }
			] as any;

			store.handleUserStatusChanged({
				userId: 'u1',
				statusText: 'Working',
				statusEmoji: null
			});

			expect(store.members[0].statusText).toBe('Working');
		});

		it('should update own profile status', () => {
			auth.me = { user: { id: 'u1', displayName: 'Me', statusText: null, statusEmoji: null } };
			store.members = [];

			store.handleUserStatusChanged({
				userId: 'u1',
				statusText: 'AFK',
				statusEmoji: null
			});

			expect(auth.me.user.statusText).toBe('AFK');
		});
	});

	// --- reset ---

	describe('reset', () => {
		it('should clear all state', () => {
			store.servers = [{ serverId: 's1' }] as any;
			store.selectedServerId = 's1';
			store.members = [{ userId: 'u1' }] as any;
			store.serverRoles = [{ id: 'r1' }] as any;
			store.categories = [{ id: 'c1' }] as any;
			store.webhooks = [{ id: 'w1' }] as any;

			store.reset();

			expect(store.servers).toEqual([]);
			expect(store.selectedServerId).toBeNull();
			expect(store.members).toEqual([]);
			expect(store.serverRoles).toEqual([]);
			expect(store.categories).toEqual([]);
			expect(store.webhooks).toEqual([]);
			expect(store.notificationPreferences).toBeNull();
			expect(store.isLoadingServers).toBe(false);
		});
	});
});
