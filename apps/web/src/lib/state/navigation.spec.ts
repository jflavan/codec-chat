import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { UIStore } from './ui-store.svelte.js';
import type { ServerStore } from './server-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import type { MessageStore } from './message-store.svelte.js';
import type { FriendStore } from './friend-store.svelte.js';
import type { DmStore } from './dm-store.svelte.js';
import { goHome, selectServer } from './navigation.svelte.js';

function createMockUI(): UIStore {
	return {
		showFriendsPanel: false,
		mobileNavOpen: true,
	} as unknown as UIStore;
}

function createMockServers(): ServerStore {
	return {
		selectedServerId: 'old-server',
		members: [{ id: '1' }],
		customEmojis: [{ id: 'e1' }],
		loadMembers: vi.fn().mockResolvedValue(undefined),
		loadCustomEmojis: vi.fn().mockResolvedValue(undefined),
		loadServerPresence: vi.fn(),
		loadCategories: vi.fn().mockResolvedValue(undefined),
		loadNotificationPreferences: vi.fn().mockResolvedValue(undefined),
	} as unknown as ServerStore;
}

function createMockChannels(): ChannelStore {
	return {
		selectedChannelId: 'old-channel',
		channels: [{ id: 'c1' }],
		loadChannels: vi.fn().mockResolvedValue(undefined),
	} as unknown as ChannelStore;
}

function createMockMessages(): MessageStore {
	return {
		messages: [{ id: 'm1' }],
		hasMoreMessages: true,
	} as unknown as MessageStore;
}

function createMockFriends(): FriendStore {
	return {
		loadFriends: vi.fn().mockResolvedValue(undefined),
		loadFriendRequests: vi.fn().mockResolvedValue(undefined),
	} as unknown as FriendStore;
}

function createMockDms(): DmStore {
	return {
		activeDmChannelId: null,
		dmMessages: [],
		dmTypingUsers: [],
		loadDmConversations: vi.fn().mockResolvedValue(undefined),
	} as unknown as DmStore;
}

function createMockHub(): ChatHubService {
	return {
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
	} as unknown as ChatHubService;
}

describe('goHome', () => {
	let ui: UIStore;
	let servers: ServerStore;
	let channels: ChannelStore;
	let messages: MessageStore;
	let friends: FriendStore;
	let dms: DmStore;

	beforeEach(() => {
		ui = createMockUI();
		servers = createMockServers();
		channels = createMockChannels();
		messages = createMockMessages();
		friends = createMockFriends();
		dms = createMockDms();
	});

	it('sets showFriendsPanel to true', () => {
		goHome(ui, servers, channels, messages, friends, dms);
		expect(ui.showFriendsPanel).toBe(true);
	});

	it('closes mobile nav', () => {
		goHome(ui, servers, channels, messages, friends, dms);
		expect(ui.mobileNavOpen).toBe(false);
	});

	it('clears selected server and channel', () => {
		goHome(ui, servers, channels, messages, friends, dms);
		expect(servers.selectedServerId).toBeNull();
		expect(channels.selectedChannelId).toBeNull();
	});

	it('clears channels, messages, and members', () => {
		goHome(ui, servers, channels, messages, friends, dms);
		expect(channels.channels).toEqual([]);
		expect(messages.messages).toEqual([]);
		expect(messages.hasMoreMessages).toBe(false);
		expect(servers.members).toEqual([]);
		expect(servers.customEmojis).toEqual([]);
	});

	it('loads friends, friend requests, and DM conversations', () => {
		goHome(ui, servers, channels, messages, friends, dms);
		expect(friends.loadFriends).toHaveBeenCalledOnce();
		expect(friends.loadFriendRequests).toHaveBeenCalledOnce();
		expect(dms.loadDmConversations).toHaveBeenCalledOnce();
	});
});

describe('selectServer', () => {
	let ui: UIStore;
	let servers: ServerStore;
	let channels: ChannelStore;
	let dms: DmStore;
	let hub: ChatHubService;

	beforeEach(() => {
		ui = createMockUI();
		servers = createMockServers();
		channels = createMockChannels();
		dms = createMockDms();
		hub = createMockHub();
	});

	it('hides friends panel and closes mobile nav', async () => {
		await selectServer('s1', ui, servers, channels, dms, hub);
		expect(ui.showFriendsPanel).toBe(false);
		expect(ui.mobileNavOpen).toBe(false);
	});

	it('sets the selected server id', async () => {
		await selectServer('new-server', ui, servers, channels, dms, hub);
		expect(servers.selectedServerId).toBe('new-server');
	});

	it('loads channels, members, emojis, presence, categories, and notification preferences', async () => {
		await selectServer('s1', ui, servers, channels, dms, hub);
		expect(channels.loadChannels).toHaveBeenCalledWith('s1');
		expect(servers.loadMembers).toHaveBeenCalledWith('s1');
		expect(servers.loadCustomEmojis).toHaveBeenCalledWith('s1');
		expect(servers.loadServerPresence).toHaveBeenCalledWith('s1');
		expect(servers.loadCategories).toHaveBeenCalled();
		expect(servers.loadNotificationPreferences).toHaveBeenCalled();
	});

	it('does not leave DM channel when no active DM', async () => {
		dms.activeDmChannelId = null;
		await selectServer('s1', ui, servers, channels, dms, hub);
		expect(hub.leaveDmChannel).not.toHaveBeenCalled();
	});

	it('leaves active DM channel and clears DM state', async () => {
		dms.activeDmChannelId = 'dm-123';
		dms.dmMessages = [{ id: 'msg1' }] as any;
		dms.dmTypingUsers = ['Alice'] as any;

		await selectServer('s1', ui, servers, channels, dms, hub);

		expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-123');
		expect(dms.activeDmChannelId).toBeNull();
		expect(dms.dmMessages).toEqual([]);
		expect(dms.dmTypingUsers).toEqual([]);
	});
});
