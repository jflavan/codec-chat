import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));

import { goHome, selectServer } from './navigation.svelte';

function mockUi() {
	return {
		showFriendsPanel: false,
		mobileNavOpen: true
	} as any;
}

function mockServers() {
	return {
		selectedServerId: 's1' as string | null,
		members: [{ userId: 'u1' }],
		customEmojis: [{ id: 'e1' }],
		loadMembers: vi.fn().mockResolvedValue(undefined),
		loadCustomEmojis: vi.fn().mockResolvedValue(undefined),
		loadServerPresence: vi.fn(),
		loadCategories: vi.fn().mockResolvedValue(undefined),
		loadNotificationPreferences: vi.fn().mockResolvedValue(undefined)
	} as any;
}

function mockChannels() {
	return {
		selectedChannelId: 'ch1' as string | null,
		channels: [{ id: 'ch1' }],
		loadChannels: vi.fn().mockResolvedValue(undefined)
	} as any;
}

function mockMessages() {
	return {
		messages: [{ id: 'm1' }],
		hasMoreMessages: true
	} as any;
}

function mockFriends() {
	return {
		loadFriends: vi.fn(),
		loadFriendRequests: vi.fn()
	} as any;
}

function mockDms() {
	return {
		activeDmChannelId: 'dm1' as string | null,
		dmMessages: [{ id: 'dm-m1' }],
		dmTypingUsers: ['User1'],
		loadDmConversations: vi.fn()
	} as any;
}

function mockHub() {
	return {
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
		joinServer: vi.fn().mockResolvedValue(undefined)
	} as any;
}

describe('navigation', () => {
	let ui: ReturnType<typeof mockUi>;
	let servers: ReturnType<typeof mockServers>;
	let channels: ReturnType<typeof mockChannels>;
	let messages: ReturnType<typeof mockMessages>;
	let friends: ReturnType<typeof mockFriends>;
	let dms: ReturnType<typeof mockDms>;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		vi.clearAllMocks();
		ui = mockUi();
		servers = mockServers();
		channels = mockChannels();
		messages = mockMessages();
		friends = mockFriends();
		dms = mockDms();
		hub = mockHub();
	});

	describe('goHome', () => {
		it('should show friends panel and close mobile nav', () => {
			goHome(ui, servers, channels, messages, friends, dms);

			expect(ui.showFriendsPanel).toBe(true);
			expect(ui.mobileNavOpen).toBe(false);
		});

		it('should clear server and channel selection', () => {
			goHome(ui, servers, channels, messages, friends, dms);

			expect(servers.selectedServerId).toBeNull();
			expect(channels.selectedChannelId).toBeNull();
		});

		it('should clear channels, messages, and members', () => {
			goHome(ui, servers, channels, messages, friends, dms);

			expect(channels.channels).toEqual([]);
			expect(messages.messages).toEqual([]);
			expect(messages.hasMoreMessages).toBe(false);
			expect(servers.members).toEqual([]);
			expect(servers.customEmojis).toEqual([]);
		});

		it('should load friends, friend requests, and DM conversations', () => {
			goHome(ui, servers, channels, messages, friends, dms);

			expect(friends.loadFriends).toHaveBeenCalled();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
			expect(dms.loadDmConversations).toHaveBeenCalled();
		});
	});

	describe('selectServer', () => {
		it('should set server selection and hide friends panel', async () => {
			await selectServer('s2', ui, servers, channels, dms, hub);

			expect(ui.showFriendsPanel).toBe(false);
			expect(ui.mobileNavOpen).toBe(false);
			expect(servers.selectedServerId).toBe('s2');
		});

		it('should leave active DM channel and clean up DM state', async () => {
			dms.activeDmChannelId = 'dm1';

			await selectServer('s2', ui, servers, channels, dms, hub);

			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm1');
			expect(dms.activeDmChannelId).toBeNull();
			expect(dms.dmMessages).toEqual([]);
			expect(dms.dmTypingUsers).toEqual([]);
		});

		it('should not leave DM channel when none is active', async () => {
			dms.activeDmChannelId = null;

			await selectServer('s2', ui, servers, channels, dms, hub);

			expect(hub.leaveDmChannel).not.toHaveBeenCalled();
		});

		it('should load channels, members, emojis, presence, categories, and notifications', async () => {
			await selectServer('s2', ui, servers, channels, dms, hub);

			expect(channels.loadChannels).toHaveBeenCalledWith('s2');
			expect(servers.loadMembers).toHaveBeenCalledWith('s2');
			expect(servers.loadCustomEmojis).toHaveBeenCalledWith('s2');
			expect(servers.loadServerPresence).toHaveBeenCalledWith('s2');
			expect(servers.loadCategories).toHaveBeenCalled();
			expect(servers.loadNotificationPreferences).toHaveBeenCalled();
		});
	});
});
