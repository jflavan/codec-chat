import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));

const { ChannelStore } = await import('./channel-store.svelte.js');

/* ───── helpers ───── */

function makeChannel(overrides: Record<string, unknown> = {}) {
	return {
		id: 'ch1',
		name: 'general',
		serverId: 's1',
		type: 'text' as const,
		position: 0,
		...overrides
	};
}

function makeMockAuth(overrides: Record<string, unknown> = {}) {
	return { idToken: 'test-token', me: null, ...overrides };
}

function makeMockApi() {
	return {
		getChannels: vi.fn().mockResolvedValue([]),
		createChannel: vi.fn().mockResolvedValue(makeChannel()),
		deleteChannel: vi.fn().mockResolvedValue(undefined),
		updateChannel: vi.fn().mockResolvedValue(undefined),
		updateChannelOrder: vi.fn().mockResolvedValue(undefined),
		getChannelOverrides: vi.fn().mockResolvedValue([]),
		setChannelOverride: vi.fn().mockResolvedValue(undefined),
		deleteChannelOverride: vi.fn().mockResolvedValue(undefined)
	};
}

function makeMockUi() {
	return {
		error: null as string | null,
		mobileNavOpen: false,
		newChannelName: '',
		newChannelType: 'text',
		showCreateChannel: false,
		setError: vi.fn()
	};
}

function makeMockHub() {
	return {
		leaveChannel: vi.fn().mockResolvedValue(undefined),
		joinChannel: vi.fn().mockResolvedValue(undefined)
	};
}

function createStore(
	authOverrides: Record<string, unknown> = {},
	apiOverrides: Record<string, unknown> = {},
	uiOverrides: Record<string, unknown> = {},
	hubOverrides: Record<string, unknown> = {}
) {
	const auth = { ...makeMockAuth(), ...authOverrides };
	const api = { ...makeMockApi(), ...apiOverrides };
	const ui = { ...makeMockUi(), ...uiOverrides };
	const hub = { ...makeMockHub(), ...hubOverrides };
	const store = new ChannelStore(auth as any, api as any, ui as any, hub as any);
	return { store, auth, api, ui, hub };
}

/* ───── tests ───── */

describe('ChannelStore', () => {
	describe('initial state', () => {
		it('has empty defaults', () => {
			const { store } = createStore();
			expect(store.channels).toEqual([]);
			expect(store.selectedChannelId).toBeNull();
			expect(store.isLoadingChannels).toBe(false);
			expect(store.isCreatingChannel).toBe(false);
			expect(store.isUpdatingChannelName).toBe(false);
		});
	});

	describe('selectedChannelName (derived)', () => {
		it('returns null when no channels', () => {
			const { store } = createStore();
			expect(store.selectedChannelName).toBeNull();
		});

		it('returns name of selected channel', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1', name: 'general' }), makeChannel({ id: 'ch2', name: 'random' })];
			store.selectedChannelId = 'ch2';
			expect(store.selectedChannelName).toBe('random');
		});

		it('returns null when selectedChannelId does not match any channel', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1', name: 'general' })];
			store.selectedChannelId = 'nonexistent';
			expect(store.selectedChannelName).toBeNull();
		});
	});

	describe('loadChannels', () => {
		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.loadChannels('s1');
			expect(api.getChannels).not.toHaveBeenCalled();
		});

		it('sets channels and selects first text channel', async () => {
			const channels = [
				makeChannel({ id: 'v1', name: 'voice', type: 'voice', position: 0 }),
				makeChannel({ id: 'ch1', name: 'general', type: 'text', position: 1 })
			];
			const { store, api } = createStore();
			api.getChannels.mockResolvedValueOnce(channels);

			await store.loadChannels('s1');

			expect(store.channels).toEqual(channels);
			expect(store.selectedChannelId).toBe('ch1');
			expect(store.isLoadingChannels).toBe(false);
		});

		it('calls hub.leaveChannel for previous and hub.joinChannel for new', async () => {
			const channels = [makeChannel({ id: 'ch2', name: 'new' })];
			const { store, api, hub } = createStore();
			store.selectedChannelId = 'ch-old';
			api.getChannels.mockResolvedValueOnce(channels);

			await store.loadChannels('s1');

			expect(hub.leaveChannel).toHaveBeenCalledWith('ch-old');
			expect(hub.joinChannel).toHaveBeenCalledWith('ch2');
		});

		it('calls onLoadMessages callback with selected channel', async () => {
			const channels = [makeChannel({ id: 'ch1' })];
			const { store, api } = createStore();
			api.getChannels.mockResolvedValueOnce(channels);
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;

			await store.loadChannels('s1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch1');
		});

		it('calls onLoadVoiceStates callback', async () => {
			const channels = [makeChannel({ id: 'ch1' })];
			const { store, api } = createStore();
			api.getChannels.mockResolvedValueOnce(channels);
			const onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = onLoadVoiceStates;

			await store.loadChannels('s1');

			expect(onLoadVoiceStates).toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			api.getChannels.mockRejectedValueOnce(error);

			await store.loadChannels('s1');

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingChannels).toBe(false);
		});

		it('clears mention badge for auto-selected channel', async () => {
			const channels = [makeChannel({ id: 'ch1' })];
			const { store, api } = createStore();
			api.getChannels.mockResolvedValueOnce(channels);
			store.channelMentionCounts = new Map([['ch1', 3]]);

			await store.loadChannels('s1');

			expect(store.channelMentionCount('ch1')).toBe(0);
		});
	});

	describe('selectChannel', () => {
		it('updates selectedChannelId and calls hub leave/join', async () => {
			const { store, hub } = createStore();
			store.selectedChannelId = 'old-ch';

			await store.selectChannel('new-ch');

			expect(store.selectedChannelId).toBe('new-ch');
			expect(hub.leaveChannel).toHaveBeenCalledWith('old-ch');
			expect(hub.joinChannel).toHaveBeenCalledWith('new-ch');
		});

		it('clears mention count for selected channel', async () => {
			const { store } = createStore();
			store.channelMentionCounts = new Map([['ch1', 5]]);

			await store.selectChannel('ch1');

			expect(store.channelMentionCount('ch1')).toBe(0);
		});

		it('calls onLoadMessages callback', async () => {
			const { store } = createStore();
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;

			await store.selectChannel('ch1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch1');
		});

		it('calls onLoadPinnedMessages callback', async () => {
			const { store } = createStore();
			const onLoadPinnedMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadPinnedMessages = onLoadPinnedMessages;

			await store.selectChannel('ch1');

			expect(onLoadPinnedMessages).toHaveBeenCalledWith('ch1');
		});

		it('calls onChannelSwitch callback', async () => {
			const { store } = createStore();
			const onChannelSwitch = vi.fn();
			store.onChannelSwitch = onChannelSwitch;

			await store.selectChannel('ch1');

			expect(onChannelSwitch).toHaveBeenCalled();
		});

		it('sets mobileNavOpen to false', async () => {
			const { store, ui } = createStore();
			ui.mobileNavOpen = true;

			await store.selectChannel('ch1');

			expect(ui.mobileNavOpen).toBe(false);
		});
	});

	describe('createChannel', () => {
		it('sets error if name is empty', async () => {
			const { store, ui } = createStore();
			ui.newChannelName = '   ';

			await store.createChannel();

			expect(ui.error).toBe('Channel name is required.');
		});

		it('does nothing if no selectedServerId', async () => {
			const { store, api, ui } = createStore();
			ui.newChannelName = 'test';
			store.getSelectedServerId = () => null;

			await store.createChannel();

			expect(api.createChannel).not.toHaveBeenCalled();
		});

		it('calls api.createChannel and reloads channels on success', async () => {
			const created = makeChannel({ id: 'new-ch', name: 'test', type: 'text' });
			const { store, api, ui } = createStore();
			ui.newChannelName = 'test';
			ui.newChannelType = 'text';
			store.getSelectedServerId = () => 's1';
			api.createChannel.mockResolvedValueOnce(created);
			api.getChannels.mockResolvedValueOnce([created]);

			await store.createChannel();

			expect(api.createChannel).toHaveBeenCalledWith('test-token', 's1', 'test', 'text');
			expect(ui.newChannelName).toBe('');
			expect(ui.showCreateChannel).toBe(false);
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			ui.newChannelName = 'test';
			store.getSelectedServerId = () => 's1';
			api.createChannel.mockRejectedValueOnce(error);

			await store.createChannel();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isCreatingChannel).toBe(false);
		});
	});

	describe('deleteChannel', () => {
		it('calls api.deleteChannel and removes from channels array', async () => {
			const { store, api } = createStore();
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2', name: 'other' })];
			store.selectedChannelId = 'ch2';
			store.getSelectedServerId = () => 's1';

			await store.deleteChannel('ch1');

			expect(api.deleteChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1');
			expect(store.channels).toHaveLength(1);
			expect(store.channels[0].id).toBe('ch2');
		});

		it('updates selectedChannelId when deleted channel was selected', async () => {
			const { store, api } = createStore();
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2', name: 'other' })];
			store.selectedChannelId = 'ch1';
			store.getSelectedServerId = () => 's1';

			await store.deleteChannel('ch1');

			// After deleting ch1, remaining is ch2, so selectedChannelId should be ch2
			expect(store.selectedChannelId).toBe('ch2');
		});

		it('sets selectedChannelId to null when last channel is deleted', async () => {
			const { store, api } = createStore();
			store.channels = [makeChannel({ id: 'ch1' })];
			store.selectedChannelId = 'ch1';
			store.getSelectedServerId = () => 's1';

			await store.deleteChannel('ch1');

			expect(store.selectedChannelId).toBeNull();
		});

		it('calls onLoadMessages when selecting fallback channel', async () => {
			const { store, api } = createStore();
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2', name: 'other' })];
			store.selectedChannelId = 'ch1';
			store.getSelectedServerId = () => 's1';
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;

			await store.deleteChannel('ch1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch2');
		});

		it('does nothing if no selectedServerId', async () => {
			const { store, api } = createStore();
			store.getSelectedServerId = () => null;

			await store.deleteChannel('ch1');

			expect(api.deleteChannel).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			store.getSelectedServerId = () => 's1';
			api.deleteChannel.mockRejectedValueOnce(error);

			await store.deleteChannel('ch1');

			expect(ui.setError).toHaveBeenCalledWith(error);
		});
	});

	describe('updateChannelName', () => {
		it('sets error if name is empty', async () => {
			const { store, ui } = createStore();

			await store.updateChannelName('ch1', '  ');

			expect(ui.error).toBe('Channel name is required.');
		});

		it('calls api.updateChannel with trimmed name', async () => {
			const { store, api } = createStore();
			store.getSelectedServerId = () => 's1';

			await store.updateChannelName('ch1', '  new-name  ');

			expect(api.updateChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1', { name: 'new-name' });
		});

		it('sets isUpdatingChannelName during update', async () => {
			const { store, api } = createStore();
			store.getSelectedServerId = () => 's1';
			let capturedFlag = false;
			api.updateChannel.mockImplementation(() => {
				capturedFlag = store.isUpdatingChannelName;
				return Promise.resolve(undefined);
			});

			await store.updateChannelName('ch1', 'new');

			expect(capturedFlag).toBe(true);
			expect(store.isUpdatingChannelName).toBe(false);
		});
	});

	describe('handleChannelNameChanged', () => {
		it('updates channel name in array when serverId matches', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1', name: 'old' })];

			store.handleChannelNameChanged({ channelId: 'ch1', serverId: 's1', name: 'new-name' } as any, 's1');

			expect(store.channels[0].name).toBe('new-name');
		});

		it('does not update when serverId does not match', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1', name: 'old' })];

			store.handleChannelNameChanged({ channelId: 'ch1', serverId: 's2', name: 'new-name' } as any, 's1');

			expect(store.channels[0].name).toBe('old');
		});
	});

	describe('handleChannelDeleted', () => {
		it('removes channel from array when serverId matches', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2', name: 'other' })];

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' } as any, 's1');

			expect(store.channels).toHaveLength(1);
			expect(store.channels[0].id).toBe('ch2');
		});

		it('updates selection when deleted channel was selected', () => {
			const { store } = createStore();
			store.channels = [
				makeChannel({ id: 'ch1', type: 'text' }),
				makeChannel({ id: 'ch2', name: 'other', type: 'text' })
			];
			store.selectedChannelId = 'ch1';

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' } as any, 's1');

			expect(store.selectedChannelId).toBe('ch2');
		});

		it('sets selectedChannelId to null when no text channels remain', () => {
			const { store } = createStore();
			store.channels = [
				makeChannel({ id: 'ch1', type: 'text' }),
				makeChannel({ id: 'v1', type: 'voice' })
			];
			store.selectedChannelId = 'ch1';

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' } as any, 's1');

			// Only voice channel remains, so next text channel is undefined
			expect(store.selectedChannelId).toBeNull();
		});

		it('does not update when serverId does not match', () => {
			const { store } = createStore();
			store.channels = [makeChannel({ id: 'ch1' })];

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's2' } as any, 's1');

			expect(store.channels).toHaveLength(1);
		});
	});

	describe('handleMentionReceived', () => {
		it('increments mention count for channel', () => {
			const { store } = createStore();

			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCount('ch1')).toBe(1);
		});

		it('increments existing count', () => {
			const { store } = createStore();
			store.channelMentionCounts = new Map([['ch1', 2]]);

			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCount('ch1')).toBe(3);
		});

		it('skips increment for currently selected channel', () => {
			const { store } = createStore();
			store.selectedChannelId = 'ch1';

			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCount('ch1')).toBe(0);
		});

		it('tracks channel-to-server mapping', () => {
			const { store } = createStore();

			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelServerMap.get('ch1')).toBe('s1');
		});
	});

	describe('channelMentionCount', () => {
		it('returns 0 for unknown channel', () => {
			const { store } = createStore();
			expect(store.channelMentionCount('unknown')).toBe(0);
		});

		it('returns stored count', () => {
			const { store } = createStore();
			store.channelMentionCounts = new Map([['ch1', 7]]);
			expect(store.channelMentionCount('ch1')).toBe(7);
		});
	});

	describe('reset', () => {
		it('clears all state to defaults', () => {
			const { store } = createStore();
			store.channels = [makeChannel()];
			store.selectedChannelId = 'ch1';
			store.isLoadingChannels = true;
			store.isCreatingChannel = true;
			store.isUpdatingChannelName = true;
			store.channelMentionCounts = new Map([['ch1', 3]]);
			store.channelServerMap = new Map([['ch1', 's1']]);

			store.reset();

			expect(store.channels).toEqual([]);
			expect(store.selectedChannelId).toBeNull();
			expect(store.isLoadingChannels).toBe(false);
			expect(store.isCreatingChannel).toBe(false);
			expect(store.isUpdatingChannelName).toBe(false);
			expect(store.channelMentionCounts.size).toBe(0);
			expect(store.channelServerMap.size).toBe(0);
		});
	});

	describe('getChannelOverrides', () => {
		it('returns empty array when idToken is null', async () => {
			const { store } = createStore({ idToken: null });
			const result = await store.getChannelOverrides('ch1');
			expect(result).toEqual([]);
		});

		it('calls api.getChannelOverrides', async () => {
			const overrides = [{ roleId: 'r1', allow: 1, deny: 0 }];
			const { store, api } = createStore();
			api.getChannelOverrides.mockResolvedValueOnce(overrides);

			const result = await store.getChannelOverrides('ch1');

			expect(api.getChannelOverrides).toHaveBeenCalledWith('test-token', 'ch1');
			expect(result).toEqual(overrides);
		});
	});

	describe('updateChannelDescription', () => {
		it('calls api.updateChannel with description', async () => {
			const { store, api } = createStore();
			store.getSelectedServerId = () => 's1';

			await store.updateChannelDescription('ch1', 'new desc');

			expect(api.updateChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1', { description: 'new desc' });
		});

		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			store.getSelectedServerId = () => 's1';

			await store.updateChannelDescription('ch1', 'desc');

			expect(api.updateChannel).not.toHaveBeenCalled();
		});
	});
});
