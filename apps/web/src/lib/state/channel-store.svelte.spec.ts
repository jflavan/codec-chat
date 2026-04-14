import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ChannelStore } from './channel-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';

function mockAuth(overrides = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1' } },
		isGlobalAdmin: false,
		effectiveDisplayName: 'TestUser',
		...overrides
	} as any;
}

function mockApi() {
	return {
		getChannels: vi.fn().mockResolvedValue([]),
		createChannel: vi.fn().mockResolvedValue({ id: 'ch-new', type: 'text' }),
		deleteChannel: vi.fn().mockResolvedValue(undefined),
		updateChannel: vi.fn().mockResolvedValue(undefined),
		getChannelOverrides: vi.fn().mockResolvedValue([]),
		setChannelOverride: vi.fn().mockResolvedValue(undefined),
		deleteChannelOverride: vi.fn().mockResolvedValue(undefined),
		updateChannelOrder: vi.fn().mockResolvedValue(undefined)
	} as any;
}

function mockHub() {
	return {
		joinChannel: vi.fn().mockResolvedValue(undefined),
		leaveChannel: vi.fn().mockResolvedValue(undefined)
	} as any;
}

describe('ChannelStore', () => {
	let store: ChannelStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		auth = mockAuth();
		api = mockApi();
		ui = new UIStore();
		hub = mockHub();
		store = new ChannelStore(auth, api, ui, hub);
	});

	describe('initial state', () => {
		it('starts with empty channels', () => {
			expect(store.channels).toEqual([]);
		});

		it('starts with null selected channel', () => {
			expect(store.selectedChannelId).toBe(null);
		});

		it('starts with empty mention counts', () => {
			expect(store.channelMentionCounts.size).toBe(0);
		});

		it('starts with loading flags false', () => {
			expect(store.isLoadingChannels).toBe(false);
			expect(store.isCreatingChannel).toBe(false);
			expect(store.isUpdatingChannelName).toBe(false);
		});
	});

	describe('loadChannels', () => {
		it('loads channels and selects first text channel', async () => {
			const channels = [
				{ id: 'ch-1', name: 'general', type: 'text' },
				{ id: 'ch-2', name: 'voice', type: 'voice' }
			];
			api.getChannels.mockResolvedValue(channels);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);

			await store.loadChannels('srv-1');

			expect(api.getChannels).toHaveBeenCalledWith('test-token', 'srv-1');
			expect(store.channels).toEqual(channels);
			expect(store.selectedChannelId).toBe('ch-1');
			expect(hub.joinChannel).toHaveBeenCalledWith('ch-1');
		});

		it('does nothing when not authenticated', async () => {
			auth.idToken = null;
			await store.loadChannels('srv-1');
			expect(api.getChannels).not.toHaveBeenCalled();
		});

		it('leaves previous channel before joining new one', async () => {
			store.selectedChannelId = 'old-ch';
			api.getChannels.mockResolvedValue([{ id: 'ch-1', name: 'general', type: 'text' }]);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);

			await store.loadChannels('srv-1');

			expect(hub.leaveChannel).toHaveBeenCalledWith('old-ch');
			expect(hub.joinChannel).toHaveBeenCalledWith('ch-1');
		});

		it('updates channelServerMap', async () => {
			api.getChannels.mockResolvedValue([{ id: 'ch-1', name: 'general', type: 'text' }]);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);

			await store.loadChannels('srv-1');

			expect(store.channelServerMap.get('ch-1')).toBe('srv-1');
		});

		it('clears mention badge for auto-selected channel', async () => {
			store.channelMentionCounts = new Map([['ch-1', 3]]);
			api.getChannels.mockResolvedValue([{ id: 'ch-1', name: 'general', type: 'text' }]);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);

			await store.loadChannels('srv-1');

			expect(store.channelMentionCounts.has('ch-1')).toBe(false);
		});

		it('sets error on API failure', async () => {
			api.getChannels.mockRejectedValue(new Error('Failed'));

			await store.loadChannels('srv-1');

			expect(ui.error).toBe('Failed');
			expect(store.isLoadingChannels).toBe(false);
		});
	});

	describe('selectChannel', () => {
		it('switches to a new channel', async () => {
			store.selectedChannelId = 'ch-1';
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadPinnedMessages = vi.fn().mockResolvedValue(undefined);
			store.onChannelSwitch = vi.fn();

			await store.selectChannel('ch-2');

			expect(store.selectedChannelId).toBe('ch-2');
			expect(hub.leaveChannel).toHaveBeenCalledWith('ch-1');
			expect(hub.joinChannel).toHaveBeenCalledWith('ch-2');
			expect(store.onChannelSwitch).toHaveBeenCalled();
			expect(store.onLoadMessages).toHaveBeenCalledWith('ch-2');
		});

		it('clears mention count for selected channel', async () => {
			store.channelMentionCounts = new Map([['ch-2', 5]]);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadPinnedMessages = vi.fn().mockResolvedValue(undefined);

			await store.selectChannel('ch-2');

			expect(store.channelMentionCounts.has('ch-2')).toBe(false);
		});

		it('closes mobile nav', async () => {
			ui.mobileNavOpen = true;
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadPinnedMessages = vi.fn().mockResolvedValue(undefined);

			await store.selectChannel('ch-1');

			expect(ui.mobileNavOpen).toBe(false);
		});
	});

	describe('createChannel', () => {
		it('creates a channel and reloads', async () => {
			ui.newChannelName = 'new-channel';
			ui.newChannelType = 'text';
			store.getSelectedServerId = () => 'srv-1';
			api.createChannel.mockResolvedValue({ id: 'ch-new', type: 'text' });
			api.getChannels.mockResolvedValue([{ id: 'ch-new', name: 'new-channel', type: 'text' }]);
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);

			await store.createChannel();

			expect(api.createChannel).toHaveBeenCalledWith('test-token', 'srv-1', 'new-channel', 'text');
			expect(ui.newChannelName).toBe('');
			expect(ui.showCreateChannel).toBe(false);
		});

		it('shows error when name is empty', async () => {
			ui.newChannelName = '';

			await store.createChannel();

			expect(ui.error).toBe('Channel name is required.');
			expect(api.createChannel).not.toHaveBeenCalled();
		});

		it('does nothing without server id', async () => {
			ui.newChannelName = 'test';
			store.getSelectedServerId = () => null;

			await store.createChannel();

			expect(api.createChannel).not.toHaveBeenCalled();
		});
	});

	describe('deleteChannel', () => {
		it('removes channel and selects first remaining', async () => {
			store.channels = [
				{ id: 'ch-1', name: 'general' },
				{ id: 'ch-2', name: 'other' }
			] as any;
			store.selectedChannelId = 'ch-1';
			store.getSelectedServerId = () => 'srv-1';
			store.onLoadMessages = vi.fn().mockResolvedValue(undefined);

			await store.deleteChannel('ch-1');

			expect(api.deleteChannel).toHaveBeenCalledWith('test-token', 'srv-1', 'ch-1');
			expect(store.channels).toHaveLength(1);
			expect(store.selectedChannelId).toBe('ch-2');
		});

		it('sets selectedChannelId to null when last channel deleted', async () => {
			store.channels = [{ id: 'ch-1', name: 'general' }] as any;
			store.selectedChannelId = 'ch-1';
			store.getSelectedServerId = () => 'srv-1';

			await store.deleteChannel('ch-1');

			expect(store.selectedChannelId).toBe(null);
		});
	});

	describe('updateChannelName', () => {
		it('calls API to update name', async () => {
			store.getSelectedServerId = () => 'srv-1';

			await store.updateChannelName('ch-1', 'new-name');

			expect(api.updateChannel).toHaveBeenCalledWith('test-token', 'srv-1', 'ch-1', { name: 'new-name' });
		});

		it('shows error for empty name', async () => {
			await store.updateChannelName('ch-1', '');
			expect(ui.error).toBe('Channel name is required.');
		});

		it('trims whitespace from name', async () => {
			store.getSelectedServerId = () => 'srv-1';
			await store.updateChannelName('ch-1', '  trimmed  ');
			expect(api.updateChannel).toHaveBeenCalledWith('test-token', 'srv-1', 'ch-1', { name: 'trimmed' });
		});
	});

	describe('channelMentionCount', () => {
		it('returns count for channel', () => {
			store.channelMentionCounts = new Map([['ch-1', 5]]);
			expect(store.channelMentionCount('ch-1')).toBe(5);
		});

		it('returns 0 for unknown channel', () => {
			expect(store.channelMentionCount('unknown')).toBe(0);
		});
	});

	describe('SignalR handlers', () => {
		it('handleChannelNameChanged updates channel name', () => {
			store.channels = [{ id: 'ch-1', name: 'old' }] as any;

			store.handleChannelNameChanged(
				{ channelId: 'ch-1', serverId: 'srv-1', name: 'new' },
				'srv-1'
			);

			expect(store.channels[0].name).toBe('new');
		});

		it('handleChannelNameChanged ignores different server', () => {
			store.channels = [{ id: 'ch-1', name: 'old' }] as any;

			store.handleChannelNameChanged(
				{ channelId: 'ch-1', serverId: 'srv-2', name: 'new' },
				'srv-1'
			);

			expect(store.channels[0].name).toBe('old');
		});

		it('handleChannelDeleted removes channel', () => {
			store.channels = [
				{ id: 'ch-1', name: 'general', type: 'text' },
				{ id: 'ch-2', name: 'other', type: 'text' }
			] as any;
			store.selectedChannelId = 'ch-1';

			store.handleChannelDeleted({ channelId: 'ch-1', serverId: 'srv-1' }, 'srv-1');

			expect(store.channels).toHaveLength(1);
			expect(store.selectedChannelId).toBe('ch-2');
		});

		it('handleChannelDescriptionChanged updates description', () => {
			store.channels = [{ id: 'ch-1', name: 'general', description: 'old' }] as any;

			store.handleChannelDescriptionChanged(
				{ channelId: 'ch-1', serverId: 'srv-1', description: 'new desc' },
				'srv-1'
			);

			expect(store.channels[0].description).toBe('new desc');
		});

		it('handleMentionReceived increments count', () => {
			store.handleMentionReceived({
				channelId: 'ch-1',
				serverId: 'srv-1',
				messageId: 'msg-1',
				authorName: 'User'
			} as any);

			expect(store.channelMentionCounts.get('ch-1')).toBe(1);
		});

		it('handleMentionReceived does not increment for selected channel', () => {
			store.selectedChannelId = 'ch-1';

			store.handleMentionReceived({
				channelId: 'ch-1',
				serverId: 'srv-1',
				messageId: 'msg-1',
				authorName: 'User'
			} as any);

			expect(store.channelMentionCounts.has('ch-1')).toBe(false);
		});
	});

	describe('reset', () => {
		it('resets all state', () => {
			store.channels = [{ id: 'ch-1' }] as any;
			store.selectedChannelId = 'ch-1';
			store.isLoadingChannels = true;
			store.channelMentionCounts = new Map([['ch-1', 3]]);
			store.channelServerMap = new Map([['ch-1', 'srv-1']]);

			store.reset();

			expect(store.channels).toEqual([]);
			expect(store.selectedChannelId).toBe(null);
			expect(store.isLoadingChannels).toBe(false);
			expect(store.isCreatingChannel).toBe(false);
			expect(store.isUpdatingChannelName).toBe(false);
			expect(store.channelMentionCounts.size).toBe(0);
			expect(store.channelServerMap.size).toBe(0);
		});
	});
});
