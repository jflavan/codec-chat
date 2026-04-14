import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ServerStore } from './server-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';

function mockAuth(overrides = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1', isGlobalAdmin: false } },
		isGlobalAdmin: false,
		effectiveDisplayName: 'TestUser',
		...overrides
	} as any;
}

function mockApi() {
	return {
		getServers: vi.fn().mockResolvedValue([]),
		getMembers: vi.fn().mockResolvedValue([]),
		getCustomEmojis: vi.fn().mockResolvedValue([]),
		createServer: vi.fn().mockResolvedValue({ id: 'srv-new' }),
		deleteServer: vi.fn().mockResolvedValue(undefined),
		updateServer: vi.fn().mockResolvedValue(undefined),
		uploadServerIcon: vi.fn().mockResolvedValue(undefined),
		deleteServerIcon: vi.fn().mockResolvedValue(undefined),
		reorderServers: vi.fn().mockResolvedValue(undefined),
		kickMember: vi.fn().mockResolvedValue(undefined),
		banMember: vi.fn().mockResolvedValue(undefined),
		unbanMember: vi.fn().mockResolvedValue(undefined),
		getBans: vi.fn().mockResolvedValue([]),
		getRoles: vi.fn().mockResolvedValue([]),
		createRole: vi.fn().mockResolvedValue({ id: 'role-new', name: 'New', position: 0 }),
		updateRole: vi.fn().mockResolvedValue({ id: 'role-1', name: 'Updated' }),
		deleteRole: vi.fn().mockResolvedValue(undefined),
		getInvites: vi.fn().mockResolvedValue([]),
		createInvite: vi.fn().mockResolvedValue({ id: 'inv-new', code: 'abc' }),
		revokeInvite: vi.fn().mockResolvedValue(undefined),
		joinViaInvite: vi.fn().mockResolvedValue({ serverId: 'srv-joined' }),
		getWebhooks: vi.fn().mockResolvedValue([]),
		createWebhook: vi.fn().mockResolvedValue({ id: 'wh-new', name: 'Hook' }),
		updateWebhook: vi.fn().mockResolvedValue({ id: 'wh-1', name: 'Updated' }),
		deleteWebhook: vi.fn().mockResolvedValue(undefined),
		getWebhookDeliveries: vi.fn().mockResolvedValue([]),
		getCategories: vi.fn().mockResolvedValue([]),
		createCategory: vi.fn().mockResolvedValue({ id: 'cat-new', name: 'New' }),
		renameCategory: vi.fn().mockResolvedValue(undefined),
		deleteCategory: vi.fn().mockResolvedValue(undefined),
		updateChannelOrder: vi.fn().mockResolvedValue(undefined),
		updateCategoryOrder: vi.fn().mockResolvedValue(undefined),
		getAuditLog: vi.fn().mockResolvedValue({ entries: [], hasMore: false }),
		getNotificationPreferences: vi.fn().mockResolvedValue({ serverMuted: false, channelOverrides: [] }),
		muteServer: vi.fn().mockResolvedValue(undefined),
		muteChannel: vi.fn().mockResolvedValue(undefined),
		getServerPresence: vi.fn().mockResolvedValue([]),
		uploadServerAvatar: vi.fn().mockResolvedValue(undefined),
		deleteServerAvatar: vi.fn().mockResolvedValue(undefined),
		uploadCustomEmoji: vi.fn().mockResolvedValue({ id: 'emoji-new', name: 'test' }),
		renameCustomEmoji: vi.fn().mockResolvedValue(undefined),
		deleteCustomEmoji: vi.fn().mockResolvedValue(undefined),
		addMemberRole: vi.fn().mockResolvedValue(undefined),
		removeMemberRole: vi.fn().mockResolvedValue(undefined),
		setMemberRoles: vi.fn().mockResolvedValue(undefined),
		getDiscordImportStatus: vi.fn().mockResolvedValue(null),
		startDiscordImport: vi.fn().mockResolvedValue(undefined),
		resyncDiscordImport: vi.fn().mockResolvedValue(undefined),
		cancelDiscordImport: vi.fn().mockResolvedValue(undefined),
		getDiscordUserMappings: vi.fn().mockResolvedValue([]),
		claimDiscordIdentity: vi.fn().mockResolvedValue(undefined)
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
	let ui: UIStore;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		auth = mockAuth();
		api = mockApi();
		ui = new UIStore();
		hub = mockHub();
		store = new ServerStore(auth, api, ui, hub);
	});

	describe('initial state', () => {
		it('starts with empty servers', () => {
			expect(store.servers).toEqual([]);
			expect(store.selectedServerId).toBe(null);
		});

		it('starts with empty members', () => {
			expect(store.members).toEqual([]);
		});

		it('starts with loading flags false', () => {
			expect(store.isLoadingServers).toBe(false);
			expect(store.isLoadingMembers).toBe(false);
			expect(store.isCreatingServer).toBe(false);
		});
	});

	describe('loadServers', () => {
		it('loads servers and selects first', async () => {
			const servers = [
				{ serverId: 'srv-1', name: 'Server1', permissions: 0, isOwner: false },
				{ serverId: 'srv-2', name: 'Server2', permissions: 0, isOwner: false }
			];
			api.getServers.mockResolvedValue(servers);

			await store.loadServers();

			expect(store.servers).toEqual(servers);
			expect(store.selectedServerId).toBe('srv-1');
		});

		it('does nothing when not authenticated', async () => {
			auth.idToken = null;
			await store.loadServers();
			expect(api.getServers).not.toHaveBeenCalled();
		});

		it('sets error on failure', async () => {
			api.getServers.mockRejectedValue(new Error('Network'));
			await store.loadServers();
			expect(ui.error).toBe('Network');
		});
	});

	describe('loadMembers', () => {
		it('loads members for a server', async () => {
			const members = [{ userId: 'u1', displayName: 'User1' }];
			api.getMembers.mockResolvedValue({ members });

			await store.loadMembers('srv-1');

			expect(api.getMembers).toHaveBeenCalledWith('test-token', 'srv-1');
			expect(store.members).toEqual(members);
		});
	});

	describe('createServer', () => {
		it('creates server and navigates to it', async () => {
			ui.newServerName = 'My Server';
			store.onSelectServer = vi.fn().mockResolvedValue(undefined);

			await store.createServer();

			expect(api.createServer).toHaveBeenCalledWith('test-token', 'My Server');
			expect(ui.newServerName).toBe('');
			expect(ui.showCreateServer).toBe(false);
		});

		it('shows error for empty name', async () => {
			ui.newServerName = '';
			await store.createServer();
			expect(ui.error).toBe('Server name is required.');
			expect(api.createServer).not.toHaveBeenCalled();
		});

		it('trims whitespace-only name', async () => {
			ui.newServerName = '   ';
			await store.createServer();
			expect(ui.error).toBe('Server name is required.');
		});
	});

	describe('deleteServer', () => {
		it('removes server and navigates home if active', async () => {
			store.servers = [{ serverId: 'srv-1' }] as any;
			store.selectedServerId = 'srv-1';
			store.onGoHome = vi.fn();

			await store.deleteServer('srv-1');

			expect(api.deleteServer).toHaveBeenCalledWith('test-token', 'srv-1');
			expect(store.servers).toEqual([]);
			expect(store.onGoHome).toHaveBeenCalled();
		});

		it('does not navigate home if different server deleted', async () => {
			store.servers = [{ serverId: 'srv-1' }, { serverId: 'srv-2' }] as any;
			store.selectedServerId = 'srv-1';
			store.onGoHome = vi.fn();

			await store.deleteServer('srv-2');

			expect(store.onGoHome).not.toHaveBeenCalled();
		});
	});

	describe('updateServerName', () => {
		it('calls API with trimmed name', async () => {
			store.selectedServerId = 'srv-1';
			await store.updateServerName('  New Name  ');
			expect(api.updateServer).toHaveBeenCalledWith('test-token', 'srv-1', { name: 'New Name' });
		});

		it('shows error for empty name', async () => {
			await store.updateServerName('');
			expect(ui.error).toBe('Server name is required.');
		});
	});

	describe('serverMentionCount', () => {
		it('sums mention counts for channels in a server', () => {
			const channelMentions = new Map([
				['ch-1', 3],
				['ch-2', 2],
				['ch-3', 5]
			]);
			const channelServerMap = new Map([
				['ch-1', 'srv-1'],
				['ch-2', 'srv-1'],
				['ch-3', 'srv-2']
			]);

			expect(store.serverMentionCount('srv-1', channelMentions, channelServerMap)).toBe(5);
			expect(store.serverMentionCount('srv-2', channelMentions, channelServerMap)).toBe(5);
			expect(store.serverMentionCount('srv-3', channelMentions, channelServerMap)).toBe(0);
		});
	});

	describe('invites', () => {
		beforeEach(() => {
			store.selectedServerId = 'srv-1';
		});

		it('loadInvites fetches from API', async () => {
			const invites = [{ id: 'inv-1', code: 'abc' }];
			api.getInvites.mockResolvedValue(invites);

			await store.loadInvites();

			expect(store.serverInvites).toEqual(invites);
		});

		it('createInvite adds to list', async () => {
			await store.createInvite();
			expect(store.serverInvites).toHaveLength(1);
		});

		it('revokeInvite removes from list', async () => {
			store.serverInvites = [{ id: 'inv-1' }, { id: 'inv-2' }] as any;

			await store.revokeInvite('inv-1');

			expect(store.serverInvites).toHaveLength(1);
			expect(store.serverInvites[0].id).toBe('inv-2');
		});
	});

	describe('joinViaInvite', () => {
		it('joins server and loads servers', async () => {
			store.onSelectServer = vi.fn().mockResolvedValue(undefined);

			await store.joinViaInvite('abc-code');

			expect(api.joinViaInvite).toHaveBeenCalledWith('test-token', 'abc-code');
			expect(hub.joinServer).toHaveBeenCalledWith('srv-joined');
		});
	});

	describe('roles', () => {
		beforeEach(() => {
			store.selectedServerId = 'srv-1';
		});

		it('loadRoles fetches from API', async () => {
			const roles = [{ id: 'role-1', name: 'Admin' }];
			api.getRoles.mockResolvedValue(roles);

			await store.loadRoles();

			expect(store.serverRoles).toEqual(roles);
		});

		it('createRole adds to list', async () => {
			const result = await store.createRole('Mod');
			expect(result).toBeDefined();
			expect(store.serverRoles).toHaveLength(1);
		});

		it('deleteRole removes from list', async () => {
			store.serverRoles = [{ id: 'role-1' }, { id: 'role-2' }] as any;

			await store.deleteRole('role-1');

			expect(store.serverRoles).toHaveLength(1);
		});
	});

	describe('SignalR handlers', () => {
		it('handleServerNameChanged updates server name', () => {
			store.servers = [{ serverId: 'srv-1', name: 'Old' }] as any;

			store.handleServerNameChanged({ serverId: 'srv-1', name: 'New' });

			expect(store.servers[0].name).toBe('New');
		});

		it('handleServerIconChanged updates icon', () => {
			store.servers = [{ serverId: 'srv-1', iconUrl: null }] as any;

			store.handleServerIconChanged({ serverId: 'srv-1', iconUrl: 'https://icon.url' });

			expect(store.servers[0].iconUrl).toBe('https://icon.url');
		});

		it('handleServerDeleted removes server and navigates home', () => {
			store.servers = [{ serverId: 'srv-1' }] as any;
			store.selectedServerId = 'srv-1';
			store.onGoHome = vi.fn();

			store.handleServerDeleted({ serverId: 'srv-1' });

			expect(store.servers).toEqual([]);
			expect(store.onGoHome).toHaveBeenCalled();
		});

		it('handleMemberBanned removes member from list', () => {
			store.selectedServerId = 'srv-1';
			store.members = [{ userId: 'u1' }, { userId: 'u2' }] as any;

			store.handleMemberBanned({ serverId: 'srv-1', userId: 'u1', deletedMessageCount: 0 });

			expect(store.members).toHaveLength(1);
			expect(store.members[0].userId).toBe('u2');
		});

		it('handleMemberBanned calls message filter when messages deleted', () => {
			store.selectedServerId = 'srv-1';
			store.members = [{ userId: 'u1' }] as any;
			const filter = vi.fn();

			store.handleMemberBanned({ serverId: 'srv-1', userId: 'u1', deletedMessageCount: 5 }, filter);

			expect(filter).toHaveBeenCalledWith('u1');
		});

		it('handleMemberUnbanned removes from bans list', () => {
			store.selectedServerId = 'srv-1';
			store.bans = [{ userId: 'u1' }, { userId: 'u2' }] as any;

			store.handleMemberUnbanned({ serverId: 'srv-1', userId: 'u1' });

			expect(store.bans).toHaveLength(1);
		});

		it('handleCustomEmojiAdded adds emoji', () => {
			store.selectedServerId = 'srv-1';
			store.customEmojis = [];

			store.handleCustomEmojiAdded({
				serverId: 'srv-1',
				emoji: { id: 'e1', name: 'test' } as any
			});

			expect(store.customEmojis).toHaveLength(1);
		});

		it('handleCustomEmojiAdded does not duplicate', () => {
			store.selectedServerId = 'srv-1';
			store.customEmojis = [{ id: 'e1', name: 'test' }] as any;

			store.handleCustomEmojiAdded({
				serverId: 'srv-1',
				emoji: { id: 'e1', name: 'test' } as any
			});

			expect(store.customEmojis).toHaveLength(1);
		});

		it('handleCustomEmojiUpdated renames emoji', () => {
			store.customEmojis = [{ id: 'e1', name: 'old' }] as any;

			store.handleCustomEmojiUpdated({ emojiId: 'e1', name: 'new' });

			expect(store.customEmojis[0].name).toBe('new');
		});

		it('handleCustomEmojiDeleted removes emoji', () => {
			store.customEmojis = [{ id: 'e1' }, { id: 'e2' }] as any;

			store.handleCustomEmojiDeleted({ emojiId: 'e1' });

			expect(store.customEmojis).toHaveLength(1);
		});

		it('handleCategoryCreated adds category', () => {
			store.selectedServerId = 'srv-1';
			store.categories = [];

			store.handleCategoryCreated({
				serverId: 'srv-1',
				categoryId: 'cat-1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(1);
		});

		it('handleCategoryRenamed updates name', () => {
			store.selectedServerId = 'srv-1';
			store.categories = [{ id: 'cat-1', name: 'Old' }] as any;

			store.handleCategoryRenamed({ serverId: 'srv-1', categoryId: 'cat-1', name: 'New' });

			expect(store.categories[0].name).toBe('New');
		});

		it('handleCategoryDeleted removes category and clears channel refs', () => {
			store.selectedServerId = 'srv-1';
			store.categories = [{ id: 'cat-1' }] as any;
			const channels = [
				{ id: 'ch-1', categoryId: 'cat-1' },
				{ id: 'ch-2', categoryId: 'cat-2' }
			] as any;

			const result = store.handleCategoryDeleted(
				{ serverId: 'srv-1', categoryId: 'cat-1' },
				channels
			);

			expect(store.categories).toEqual([]);
			expect(result![0].categoryId).toBeUndefined();
			expect(result![1].categoryId).toBe('cat-2');
		});

		it('handleServerDescriptionChanged updates description', () => {
			store.servers = [{ serverId: 'srv-1', description: 'old' }] as any;

			store.handleServerDescriptionChanged({ serverId: 'srv-1', description: 'new desc' });

			expect(store.servers[0].description).toBe('new desc');
		});

		it('handleUserStatusChanged updates member status', () => {
			store.members = [{ userId: 'u1', statusText: null, statusEmoji: null }] as any;

			store.handleUserStatusChanged({
				userId: 'u1',
				statusText: 'Busy',
				statusEmoji: 'fire'
			});

			expect(store.members[0].statusText).toBe('Busy');
			expect(store.members[0].statusEmoji).toBe('fire');
		});
	});

	describe('notification preferences', () => {
		beforeEach(() => {
			store.selectedServerId = 'srv-1';
		});

		it('loadNotificationPreferences fetches from API', async () => {
			const prefs = { serverMuted: true, channelOverrides: [] };
			api.getNotificationPreferences.mockResolvedValue(prefs);

			await store.loadNotificationPreferences();

			expect(store.notificationPreferences).toEqual(prefs);
		});

		it('toggleServerMute optimistically toggles', async () => {
			store.notificationPreferences = { serverMuted: false, channelOverrides: [] };

			await store.toggleServerMute();

			expect(store.notificationPreferences!.serverMuted).toBe(true);
			expect(api.muteServer).toHaveBeenCalledWith('test-token', 'srv-1', true);
		});

		it('toggleServerMute reverts on failure', async () => {
			store.notificationPreferences = { serverMuted: false, channelOverrides: [] };
			api.muteServer.mockRejectedValue(new Error('fail'));

			await store.toggleServerMute();

			expect(store.notificationPreferences!.serverMuted).toBe(false);
		});

		it('isChannelMuted returns correct state', () => {
			store.notificationPreferences = {
				serverMuted: false,
				channelOverrides: [{ channelId: 'ch-1', isMuted: true }]
			};

			expect(store.isChannelMuted('ch-1')).toBe(true);
			expect(store.isChannelMuted('ch-2')).toBe(false);
		});
	});

	describe('reset', () => {
		it('resets all state to defaults', () => {
			store.servers = [{ serverId: 'srv-1' }] as any;
			store.selectedServerId = 'srv-1';
			store.members = [{ userId: 'u1' }] as any;
			store.isLoadingServers = true;

			store.reset();

			expect(store.servers).toEqual([]);
			expect(store.selectedServerId).toBe(null);
			expect(store.members).toEqual([]);
			expect(store.isLoadingServers).toBe(false);
		});
	});
});
