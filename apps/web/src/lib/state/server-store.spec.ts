import { describe, it, expect, beforeEach, vi } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

import { ServerStore } from './server-store.svelte';
import type { ApiClient } from '$lib/api/client.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { MemberServer, Member, ServerRole, CustomEmoji } from '$lib/types/index.js';

/* ───── Helpers ───── */

function makeServer(overrides: Partial<MemberServer> = {}): MemberServer {
	return {
		serverId: 'server-1',
		name: 'Test Server',
		iconUrl: null,
		roles: [],
		sortOrder: 0,
		permissions: 0,
		isOwner: false,
		...overrides
	};
}

function makeMember(overrides: Partial<Member> = {}): Member {
	return {
		userId: 'user-1',
		displayName: 'Test User',
		avatarUrl: null,
		roles: [],
		permissions: 0,
		displayRole: null,
		highestPosition: 0,
		joinedAt: '2026-01-01T00:00:00Z',
		...overrides
	};
}

function makeRole(overrides: Partial<ServerRole> = {}): ServerRole {
	return {
		id: 'role-1',
		name: 'Test Role',
		color: null,
		position: 0,
		permissions: 0,
		isSystemRole: false,
		isHoisted: false,
		isMentionable: false,
		...overrides
	};
}

/* ───── Mock factories ───── */

function createMocks() {
	const mockAuth = {
		idToken: 'test-token',
		isGlobalAdmin: false,
		me: { user: { id: 'user-1', statusText: null, statusEmoji: null } }
	} as unknown as AuthStore;

	const mockApi = {
		getServers: vi.fn(),
		getMembers: vi.fn(),
		createServer: vi.fn(),
		deleteServer: vi.fn(),
		updateServer: vi.fn(),
		uploadServerIcon: vi.fn(),
		deleteServerIcon: vi.fn(),
		getCustomEmojis: vi.fn(),
		uploadCustomEmoji: vi.fn(),
		renameCustomEmoji: vi.fn(),
		deleteCustomEmoji: vi.fn(),
		kickMember: vi.fn(),
		banMember: vi.fn(),
		unbanMember: vi.fn(),
		getBans: vi.fn(),
		getRoles: vi.fn(),
		createRole: vi.fn(),
		updateRole: vi.fn(),
		deleteRole: vi.fn(),
		getInvites: vi.fn(),
		createInvite: vi.fn(),
		revokeInvite: vi.fn(),
		joinViaInvite: vi.fn(),
		reorderServers: vi.fn(),
		addMemberRole: vi.fn(),
		removeMemberRole: vi.fn(),
		setMemberRoles: vi.fn(),
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
		deleteServerAvatar: vi.fn()
	} as unknown as ApiClient;

	const mockUi = {
		error: null as string | null,
		setError: vi.fn(),
		setTransientError: vi.fn(),
		newServerName: '',
		showCreateServer: false,
		serverSettingsOpen: false,
		userPresence: new Map()
	} as unknown as UIStore;

	const mockHub = {
		joinServer: vi.fn().mockResolvedValue(undefined),
		leaveServer: vi.fn().mockResolvedValue(undefined)
	} as unknown as ChatHubService;

	return { mockAuth, mockApi, mockUi, mockHub };
}

describe('ServerStore', () => {
	let store: ServerStore;
	let mockAuth: AuthStore;
	let mockApi: ApiClient;
	let mockUi: UIStore;
	let mockHub: ChatHubService;

	beforeEach(() => {
		vi.clearAllMocks();
		const mocks = createMocks();
		mockAuth = mocks.mockAuth;
		mockApi = mocks.mockApi;
		mockUi = mocks.mockUi;
		mockHub = mocks.mockHub;
		store = new ServerStore(mockAuth, mockApi, mockUi, mockHub);
	});

	/* ═══════════════════ Initial State ═══════════════════ */

	describe('initial state', () => {
		it('should have empty servers list', () => {
			expect(store.servers).toEqual([]);
		});

		it('should have null selectedServerId', () => {
			expect(store.selectedServerId).toBeNull();
		});

		it('should have empty members list', () => {
			expect(store.members).toEqual([]);
		});

		it('should have all loading flags set to false', () => {
			expect(store.isLoadingServers).toBe(false);
			expect(store.isLoadingMembers).toBe(false);
			expect(store.isLoadingInvites).toBe(false);
			expect(store.isCreatingInvite).toBe(false);
			expect(store.isLoadingBans).toBe(false);
			expect(store.isLoadingRoles).toBe(false);
			expect(store.isUpdatingServerName).toBe(false);
			expect(store.isUploadingServerIcon).toBe(false);
			expect(store.isDeletingServer).toBe(false);
			expect(store.isJoining).toBe(false);
			expect(store.isCreatingServer).toBe(false);
		});
	});

	/* ═══════════════════ loadServers ═══════════════════ */

	describe('loadServers', () => {
		it('should populate servers from API', async () => {
			const servers = [makeServer({ serverId: 's1' }), makeServer({ serverId: 's2' })];
			vi.mocked(mockApi.getServers).mockResolvedValue(servers);

			await store.loadServers();

			expect(mockApi.getServers).toHaveBeenCalledWith('test-token');
			expect(store.servers).toEqual(servers);
		});

		it('should auto-select first server', async () => {
			vi.mocked(mockApi.getServers).mockResolvedValue([
				makeServer({ serverId: 's1' }),
				makeServer({ serverId: 's2' })
			]);

			await store.loadServers();

			expect(store.selectedServerId).toBe('s1');
		});

		it('should set selectedServerId to null when no servers', async () => {
			vi.mocked(mockApi.getServers).mockResolvedValue([]);

			await store.loadServers();

			expect(store.selectedServerId).toBeNull();
		});

		it('should set isLoadingServers during load', async () => {
			let loadingDuringCall = false;
			vi.mocked(mockApi.getServers).mockImplementation(async () => {
				loadingDuringCall = store.isLoadingServers;
				return [];
			});

			await store.loadServers();

			expect(loadingDuringCall).toBe(true);
			expect(store.isLoadingServers).toBe(false);
		});

		it('should handle API errors gracefully', async () => {
			const error = new Error('Network error');
			vi.mocked(mockApi.getServers).mockRejectedValue(error);

			await store.loadServers();

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingServers).toBe(false);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;

			await store.loadServers();

			expect(mockApi.getServers).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ loadMembers ═══════════════════ */

	describe('loadMembers', () => {
		it('should populate members from API', async () => {
			const members = [makeMember({ userId: 'u1' }), makeMember({ userId: 'u2' })];
			vi.mocked(mockApi.getMembers).mockResolvedValue(members);

			await store.loadMembers('s1');

			expect(mockApi.getMembers).toHaveBeenCalledWith('test-token', 's1');
			expect(store.members).toEqual(members);
		});

		it('should set isLoadingMembers during load', async () => {
			let loadingDuringCall = false;
			vi.mocked(mockApi.getMembers).mockImplementation(async () => {
				loadingDuringCall = store.isLoadingMembers;
				return [];
			});

			await store.loadMembers('s1');

			expect(loadingDuringCall).toBe(true);
			expect(store.isLoadingMembers).toBe(false);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;

			await store.loadMembers('s1');

			expect(mockApi.getMembers).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ createServer ═══════════════════ */

	describe('createServer', () => {
		it('should call API and reload servers', async () => {
			(mockUi as any).newServerName = 'New Server';
			vi.mocked(mockApi.createServer).mockResolvedValue({ id: 's-new', name: 'New Server' } as any);
			vi.mocked(mockApi.getServers).mockResolvedValue([makeServer({ serverId: 's-new' })]);

			await store.createServer();

			expect(mockApi.createServer).toHaveBeenCalledWith('test-token', 'New Server');
			expect(mockApi.getServers).toHaveBeenCalled();
		});

		it('should reset UI state after creation', async () => {
			(mockUi as any).newServerName = 'New Server';
			vi.mocked(mockApi.createServer).mockResolvedValue({ id: 's-new' } as any);
			vi.mocked(mockApi.getServers).mockResolvedValue([]);

			await store.createServer();

			expect(mockUi.newServerName).toBe('');
			expect(mockUi.showCreateServer).toBe(false);
		});

		it('should set error when name is empty', async () => {
			(mockUi as any).newServerName = '';

			await store.createServer();

			expect(mockUi.error).toBe('Server name is required.');
			expect(mockApi.createServer).not.toHaveBeenCalled();
		});

		it('should trim whitespace-only name', async () => {
			(mockUi as any).newServerName = '   ';

			await store.createServer();

			expect(mockUi.error).toBe('Server name is required.');
		});

		it('should call onSelectServer callback', async () => {
			(mockUi as any).newServerName = 'New Server';
			vi.mocked(mockApi.createServer).mockResolvedValue({ id: 's-new' } as any);
			vi.mocked(mockApi.getServers).mockResolvedValue([]);
			const onSelectServer = vi.fn().mockResolvedValue(undefined);
			store.onSelectServer = onSelectServer;

			await store.createServer();

			expect(onSelectServer).toHaveBeenCalledWith('s-new');
		});

		it('should handle API errors', async () => {
			(mockUi as any).newServerName = 'New Server';
			const error = new Error('Server creation failed');
			vi.mocked(mockApi.createServer).mockRejectedValue(error);

			await store.createServer();

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isCreatingServer).toBe(false);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;
			(mockUi as any).newServerName = 'New Server';

			await store.createServer();

			expect(mockApi.createServer).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ deleteServer ═══════════════════ */

	describe('deleteServer', () => {
		it('should call API and remove server from list', async () => {
			store.servers = [makeServer({ serverId: 's1' }), makeServer({ serverId: 's2' })];
			vi.mocked(mockApi.deleteServer).mockResolvedValue(undefined as any);

			await store.deleteServer('s1');

			expect(mockApi.deleteServer).toHaveBeenCalledWith('test-token', 's1');
			expect(store.servers).toHaveLength(1);
			expect(store.servers[0].serverId).toBe('s2');
		});

		it('should close server settings', async () => {
			store.servers = [makeServer({ serverId: 's1' })];
			vi.mocked(mockApi.deleteServer).mockResolvedValue(undefined as any);

			await store.deleteServer('s1');

			expect(mockUi.serverSettingsOpen).toBe(false);
		});

		it('should call onGoHome when deleting selected server', async () => {
			store.servers = [makeServer({ serverId: 's1' })];
			store.selectedServerId = 's1';
			const onGoHome = vi.fn();
			store.onGoHome = onGoHome;
			vi.mocked(mockApi.deleteServer).mockResolvedValue(undefined as any);

			await store.deleteServer('s1');

			expect(onGoHome).toHaveBeenCalled();
		});

		it('should not call onGoHome when deleting non-selected server', async () => {
			store.servers = [makeServer({ serverId: 's1' }), makeServer({ serverId: 's2' })];
			store.selectedServerId = 's2';
			const onGoHome = vi.fn();
			store.onGoHome = onGoHome;
			vi.mocked(mockApi.deleteServer).mockResolvedValue(undefined as any);

			await store.deleteServer('s1');

			expect(onGoHome).not.toHaveBeenCalled();
		});

		it('should handle API errors and reset loading flag', async () => {
			const error = new Error('Delete failed');
			vi.mocked(mockApi.deleteServer).mockRejectedValue(error);

			await store.deleteServer('s1');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isDeletingServer).toBe(false);
		});
	});

	/* ═══════════════════ updateServerName ═══════════════════ */

	describe('updateServerName', () => {
		it('should call API with trimmed name', async () => {
			store.selectedServerId = 's1';
			vi.mocked(mockApi.updateServer).mockResolvedValue(undefined as any);

			await store.updateServerName('  New Name  ');

			expect(mockApi.updateServer).toHaveBeenCalledWith('test-token', 's1', { name: 'New Name' });
		});

		it('should set error when name is empty', async () => {
			store.selectedServerId = 's1';

			await store.updateServerName('');

			expect(mockUi.error).toBe('Server name is required.');
			expect(mockApi.updateServer).not.toHaveBeenCalled();
		});

		it('should set isUpdatingServerName during update', async () => {
			store.selectedServerId = 's1';
			let loadingDuringCall = false;
			vi.mocked(mockApi.updateServer).mockImplementation(async () => {
				loadingDuringCall = store.isUpdatingServerName;
				return undefined as any;
			});

			await store.updateServerName('New Name');

			expect(loadingDuringCall).toBe(true);
			expect(store.isUpdatingServerName).toBe(false);
		});
	});

	/* ═══════════════════ serverMentionCount ═══════════════════ */

	describe('serverMentionCount', () => {
		it('should return total mentions for a server', () => {
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

			expect(store.serverMentionCount('s1', channelMentionCounts, channelServerMap)).toBe(5);
		});

		it('should return 0 when no mentions for server', () => {
			const channelMentionCounts = new Map([['ch1', 3]]);
			const channelServerMap = new Map([['ch1', 's2']]);

			expect(store.serverMentionCount('s1', channelMentionCounts, channelServerMap)).toBe(0);
		});

		it('should return 0 with empty maps', () => {
			expect(store.serverMentionCount('s1', new Map(), new Map())).toBe(0);
		});
	});

	/* ═══════════════════ hasPermission ═══════════════════ */

	describe('hasPermission', () => {
		it('should return true for global admin', () => {
			(mockAuth as any).isGlobalAdmin = true;

			expect(store.hasPermission(1)).toBe(true);
		});

		it('should delegate to hasPermission utility', () => {
			store.servers = [makeServer({ serverId: 's1', permissions: 0b11 })]; // ViewChannels + ManageChannels
			store.selectedServerId = 's1';

			// Permission.ManageChannels = 1 << 1 = 2
			expect(store.hasPermission(2)).toBe(true);
			// Permission.ManageServer = 1 << 2 = 4
			expect(store.hasPermission(4)).toBe(false);
		});
	});

	/* ═══════════════════ loadRoles ═══════════════════ */

	describe('loadRoles', () => {
		it('should populate serverRoles from API', async () => {
			store.selectedServerId = 's1';
			const roles = [makeRole({ id: 'r1' }), makeRole({ id: 'r2' })];
			vi.mocked(mockApi.getRoles).mockResolvedValue(roles);

			await store.loadRoles();

			expect(mockApi.getRoles).toHaveBeenCalledWith('test-token', 's1');
			expect(store.serverRoles).toEqual(roles);
		});

		it('should not call API when no selectedServerId', async () => {
			store.selectedServerId = null;

			await store.loadRoles();

			expect(mockApi.getRoles).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ createRole ═══════════════════ */

	describe('createRole', () => {
		it('should add created role to serverRoles', async () => {
			store.selectedServerId = 's1';
			const newRole = makeRole({ id: 'r-new', position: 1 });
			vi.mocked(mockApi.createRole).mockResolvedValue(newRole);

			const result = await store.createRole('New Role');

			expect(result).toEqual(newRole);
			expect(store.serverRoles).toContainEqual(newRole);
		});

		it('should return null on error', async () => {
			store.selectedServerId = 's1';
			vi.mocked(mockApi.createRole).mockRejectedValue(new Error('fail'));

			const result = await store.createRole('New Role');

			expect(result).toBeNull();
		});
	});

	/* ═══════════════════ joinViaInvite ═══════════════════ */

	describe('joinViaInvite', () => {
		it('should join server, reload servers and select', async () => {
			vi.mocked(mockApi.joinViaInvite).mockResolvedValue({ serverId: 's-joined' } as any);
			vi.mocked(mockApi.getServers).mockResolvedValue([makeServer({ serverId: 's-joined' })]);
			const onSelectServer = vi.fn().mockResolvedValue(undefined);
			store.onSelectServer = onSelectServer;

			await store.joinViaInvite('INVITE123');

			expect(mockApi.joinViaInvite).toHaveBeenCalledWith('test-token', 'INVITE123');
			expect(mockHub.joinServer).toHaveBeenCalledWith('s-joined');
			expect(onSelectServer).toHaveBeenCalledWith('s-joined');
		});

		it('should set isJoining during operation', async () => {
			let joiningDuringCall = false;
			vi.mocked(mockApi.joinViaInvite).mockImplementation(async () => {
				joiningDuringCall = store.isJoining;
				return { serverId: 's1' } as any;
			});
			vi.mocked(mockApi.getServers).mockResolvedValue([]);

			await store.joinViaInvite('CODE');

			expect(joiningDuringCall).toBe(true);
			expect(store.isJoining).toBe(false);
		});
	});

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	describe('handleServerNameChanged', () => {
		it('should update server name in list', () => {
			store.servers = [makeServer({ serverId: 's1', name: 'Old' })];

			store.handleServerNameChanged({ serverId: 's1', name: 'New Name' });

			expect(store.servers[0].name).toBe('New Name');
		});

		it('should not affect other servers', () => {
			store.servers = [
				makeServer({ serverId: 's1', name: 'Server 1' }),
				makeServer({ serverId: 's2', name: 'Server 2' })
			];

			store.handleServerNameChanged({ serverId: 's1', name: 'Updated' });

			expect(store.servers[1].name).toBe('Server 2');
		});
	});

	describe('handleServerIconChanged', () => {
		it('should update server icon URL', () => {
			store.servers = [makeServer({ serverId: 's1', iconUrl: null })];

			store.handleServerIconChanged({ serverId: 's1', iconUrl: 'https://example.com/icon.png' });

			expect(store.servers[0].iconUrl).toBe('https://example.com/icon.png');
		});
	});

	describe('handleServerDeleted', () => {
		it('should remove server from list', () => {
			store.servers = [makeServer({ serverId: 's1' }), makeServer({ serverId: 's2' })];

			store.handleServerDeleted({ serverId: 's1' });

			expect(store.servers).toHaveLength(1);
			expect(store.servers[0].serverId).toBe('s2');
		});

		it('should call onGoHome when selected server is deleted', () => {
			store.servers = [makeServer({ serverId: 's1' })];
			store.selectedServerId = 's1';
			const onGoHome = vi.fn();
			store.onGoHome = onGoHome;

			store.handleServerDeleted({ serverId: 's1' });

			expect(onGoHome).toHaveBeenCalled();
			expect(mockUi.serverSettingsOpen).toBe(false);
		});
	});

	describe('handleKicked', () => {
		it('should remove server and show error message', () => {
			store.servers = [makeServer({ serverId: 's1', name: 'Test' })];
			store.selectedServerId = 's1';
			const onGoHome = vi.fn();
			store.onGoHome = onGoHome;

			store.handleKicked({ serverId: 's1', serverName: 'Test' });

			expect(store.servers).toHaveLength(0);
			expect(onGoHome).toHaveBeenCalled();
			expect(mockUi.setTransientError).toHaveBeenCalledWith('You were kicked from "Test".');
			expect(mockHub.leaveServer).toHaveBeenCalledWith('s1');
		});
	});

	describe('handleBanned', () => {
		it('should remove server and show error message', () => {
			store.servers = [makeServer({ serverId: 's1', name: 'Test' })];
			store.selectedServerId = 's1';
			const onGoHome = vi.fn();
			store.onGoHome = onGoHome;

			store.handleBanned({ serverId: 's1', serverName: 'Test' });

			expect(store.servers).toHaveLength(0);
			expect(onGoHome).toHaveBeenCalled();
			expect(mockUi.setTransientError).toHaveBeenCalledWith('You were banned from "Test".');
		});
	});

	describe('handleMemberBanned', () => {
		it('should remove banned member from members list', () => {
			store.selectedServerId = 's1';
			store.members = [makeMember({ userId: 'u1' }), makeMember({ userId: 'u2' })];

			store.handleMemberBanned({ serverId: 's1', userId: 'u1', deletedMessageCount: 0 });

			expect(store.members).toHaveLength(1);
			expect(store.members[0].userId).toBe('u2');
		});

		it('should call onFilterMessages when messages were deleted', () => {
			store.selectedServerId = 's1';
			store.members = [makeMember({ userId: 'u1' })];
			const filterMessages = vi.fn();

			store.handleMemberBanned(
				{ serverId: 's1', userId: 'u1', deletedMessageCount: 5 },
				filterMessages
			);

			expect(filterMessages).toHaveBeenCalledWith('u1');
		});

		it('should not affect members for different server', () => {
			store.selectedServerId = 's1';
			store.members = [makeMember({ userId: 'u1' })];

			store.handleMemberBanned({ serverId: 's2', userId: 'u1', deletedMessageCount: 0 });

			expect(store.members).toHaveLength(1);
		});
	});

	describe('handleCustomEmojiAdded', () => {
		it('should add emoji when on matching server', () => {
			store.selectedServerId = 's1';
			store.customEmojis = [];
			const emoji: CustomEmoji = {
				id: 'e1',
				name: 'test',
				imageUrl: 'https://example.com/emoji.png',
				contentType: 'image/png',
				isAnimated: false,
				createdAt: '2026-01-01T00:00:00Z',
				uploadedByUserId: 'u1'
			};

			store.handleCustomEmojiAdded({ serverId: 's1', emoji });

			expect(store.customEmojis).toHaveLength(1);
			expect(store.customEmojis[0].id).toBe('e1');
		});

		it('should not duplicate existing emoji', () => {
			store.selectedServerId = 's1';
			const emoji: CustomEmoji = {
				id: 'e1',
				name: 'test',
				imageUrl: 'https://example.com/emoji.png',
				contentType: 'image/png',
				isAnimated: false,
				createdAt: '2026-01-01T00:00:00Z',
				uploadedByUserId: 'u1'
			};
			store.customEmojis = [emoji];

			store.handleCustomEmojiAdded({ serverId: 's1', emoji });

			expect(store.customEmojis).toHaveLength(1);
		});
	});

	describe('handleCustomEmojiDeleted', () => {
		it('should remove emoji from list', () => {
			store.customEmojis = [
				{
					id: 'e1',
					name: 'test',
					imageUrl: '',
					contentType: 'image/png',
					isAnimated: false,
					createdAt: '',
					uploadedByUserId: null
				}
			];

			store.handleCustomEmojiDeleted({ emojiId: 'e1' });

			expect(store.customEmojis).toHaveLength(0);
		});
	});

	describe('handleUserStatusChanged', () => {
		it('should update member status in list', () => {
			store.members = [makeMember({ userId: 'u1', statusText: null })];

			store.handleUserStatusChanged({ userId: 'u1', statusText: 'Away', statusEmoji: null });

			expect(store.members[0].statusText).toBe('Away');
		});

		it('should update own profile when userId matches', () => {
			store.handleUserStatusChanged({
				userId: 'user-1',
				statusText: 'Busy',
				statusEmoji: null
			});

			expect((mockAuth as any).me.user.statusText).toBe('Busy');
		});
	});

	/* ═══════════════════ Reset ═══════════════════ */

	describe('reset', () => {
		it('should reset all state to defaults', () => {
			store.servers = [makeServer()];
			store.selectedServerId = 's1';
			store.members = [makeMember()];
			store.isLoadingServers = true;

			store.reset();

			expect(store.servers).toEqual([]);
			expect(store.selectedServerId).toBeNull();
			expect(store.members).toEqual([]);
			expect(store.isLoadingServers).toBe(false);
			expect(store.isLoadingMembers).toBe(false);
			expect(store.serverInvites).toEqual([]);
			expect(store.serverRoles).toEqual([]);
			expect(store.bans).toEqual([]);
			expect(store.customEmojis).toEqual([]);
			expect(store.categories).toEqual([]);
			expect(store.webhooks).toEqual([]);
			expect(store.notificationPreferences).toBeNull();
		});
	});

	/* ═══════════════════ Categories ═══════════════════ */

	describe('handleCategoryCreated', () => {
		it('should add category when on matching server', () => {
			store.selectedServerId = 's1';
			store.categories = [];

			store.handleCategoryCreated({
				serverId: 's1',
				categoryId: 'cat-1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(1);
			expect(store.categories[0].name).toBe('General');
		});

		it('should not add duplicate category', () => {
			store.selectedServerId = 's1';
			store.categories = [{ id: 'cat-1', serverId: 's1', name: 'General', position: 0 }];

			store.handleCategoryCreated({
				serverId: 's1',
				categoryId: 'cat-1',
				name: 'General',
				position: 0
			});

			expect(store.categories).toHaveLength(1);
		});
	});

	describe('handleCategoryDeleted', () => {
		it('should remove category from list', () => {
			store.selectedServerId = 's1';
			store.categories = [{ id: 'cat-1', serverId: 's1', name: 'General', position: 0 }];

			store.handleCategoryDeleted({ serverId: 's1', categoryId: 'cat-1' });

			expect(store.categories).toHaveLength(0);
		});

		it('should clear categoryId from affected channels', () => {
			store.selectedServerId = 's1';
			store.categories = [{ id: 'cat-1', serverId: 's1', name: 'General', position: 0 }];
			const channels = [
				{ id: 'ch1', name: 'test', serverId: 's1', categoryId: 'cat-1', position: 0 },
				{ id: 'ch2', name: 'other', serverId: 's1', categoryId: 'cat-2', position: 1 }
			];

			const result = store.handleCategoryDeleted({ serverId: 's1', categoryId: 'cat-1' }, channels);

			expect(result![0].categoryId).toBeUndefined();
			expect(result![1].categoryId).toBe('cat-2');
		});
	});

	/* ═══════════════════ Webhooks ═══════════════════ */

	describe('deleteWebhook', () => {
		it('should remove webhook and clear selection', async () => {
			store.selectedServerId = 's1';
			store.webhooks = [
				{ id: 'wh1' } as any,
				{ id: 'wh2' } as any
			];
			store.selectedWebhookId = 'wh1';
			vi.mocked(mockApi.deleteWebhook).mockResolvedValue(undefined as any);

			await store.deleteWebhook('wh1');

			expect(store.webhooks).toHaveLength(1);
			expect(store.selectedWebhookId).toBeNull();
			expect(store.webhookDeliveries).toEqual([]);
		});
	});
});
