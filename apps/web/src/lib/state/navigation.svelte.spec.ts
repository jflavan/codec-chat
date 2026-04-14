import { describe, it, expect, beforeEach, vi } from 'vitest';
import { goHome, selectServer } from './navigation.svelte.js';
import { UIStore } from './ui-store.svelte.js';

function makeStores() {
	const ui = new UIStore();
	const servers = {
		selectedServerId: 'srv-1',
		members: [{ userId: 'u1' }],
		customEmojis: [{ id: 'e1' }],
		loadMembers: vi.fn().mockResolvedValue(undefined),
		loadCustomEmojis: vi.fn().mockResolvedValue(undefined),
		loadServerPresence: vi.fn(),
		loadCategories: vi.fn().mockResolvedValue(undefined),
		loadNotificationPreferences: vi.fn().mockResolvedValue(undefined)
	} as any;
	const channels = {
		selectedChannelId: 'ch-1',
		channels: [{ id: 'ch-1' }],
		loadChannels: vi.fn().mockResolvedValue(undefined)
	} as any;
	const messages = {
		messages: [{ id: 'msg-1' }],
		hasMoreMessages: true
	} as any;
	const friends = {
		loadFriends: vi.fn().mockResolvedValue(undefined),
		loadFriendRequests: vi.fn().mockResolvedValue(undefined)
	} as any;
	const dms = {
		activeDmChannelId: null as string | null,
		dmMessages: [] as any[],
		dmTypingUsers: [] as string[],
		loadDmConversations: vi.fn().mockResolvedValue(undefined)
	} as any;
	const hub = {
		leaveDmChannel: vi.fn().mockResolvedValue(undefined)
	} as any;

	return { ui, servers, channels, messages, friends, dms, hub };
}

describe('navigation', () => {
	describe('goHome', () => {
		it('shows friends panel and clears server state', () => {
			const { ui, servers, channels, messages, friends, dms } = makeStores();

			goHome(ui, servers, channels, messages, friends, dms);

			expect(ui.showFriendsPanel).toBe(true);
			expect(ui.mobileNavOpen).toBe(false);
			expect(servers.selectedServerId).toBe(null);
			expect(channels.selectedChannelId).toBe(null);
			expect(channels.channels).toEqual([]);
			expect(messages.messages).toEqual([]);
			expect(messages.hasMoreMessages).toBe(false);
			expect(servers.members).toEqual([]);
			expect(servers.customEmojis).toEqual([]);
		});

		it('loads friends and DM conversations', () => {
			const { ui, servers, channels, messages, friends, dms } = makeStores();

			goHome(ui, servers, channels, messages, friends, dms);

			expect(friends.loadFriends).toHaveBeenCalled();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
			expect(dms.loadDmConversations).toHaveBeenCalled();
		});
	});

	describe('selectServer', () => {
		it('selects server and loads data', async () => {
			const { ui, servers, channels, dms, hub } = makeStores();

			await selectServer('srv-2', ui, servers, channels, dms, hub);

			expect(ui.showFriendsPanel).toBe(false);
			expect(servers.selectedServerId).toBe('srv-2');
			expect(channels.loadChannels).toHaveBeenCalledWith('srv-2');
			expect(servers.loadMembers).toHaveBeenCalledWith('srv-2');
			expect(servers.loadCustomEmojis).toHaveBeenCalledWith('srv-2');
			expect(servers.loadServerPresence).toHaveBeenCalledWith('srv-2');
			expect(servers.loadCategories).toHaveBeenCalled();
			expect(servers.loadNotificationPreferences).toHaveBeenCalled();
		});

		it('cleans up active DM state when switching to server', async () => {
			const { ui, servers, channels, dms, hub } = makeStores();
			dms.activeDmChannelId = 'dm-1';
			dms.dmMessages = [{ id: 'msg-1' }];
			dms.dmTypingUsers = ['Alice'];

			await selectServer('srv-1', ui, servers, channels, dms, hub);

			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-1');
			expect(dms.activeDmChannelId).toBe(null);
			expect(dms.dmMessages).toEqual([]);
			expect(dms.dmTypingUsers).toEqual([]);
		});

		it('does not leave DM if none active', async () => {
			const { ui, servers, channels, dms, hub } = makeStores();
			dms.activeDmChannelId = null;

			await selectServer('srv-1', ui, servers, channels, dms, hub);

			expect(hub.leaveDmChannel).not.toHaveBeenCalled();
		});

		it('closes mobile nav', async () => {
			const { ui, servers, channels, dms, hub } = makeStores();
			ui.mobileNavOpen = true;

			await selectServer('srv-1', ui, servers, channels, dms, hub);

			expect(ui.mobileNavOpen).toBe(false);
		});
	});
});
