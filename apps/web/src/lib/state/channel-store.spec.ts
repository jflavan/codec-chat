import { describe, it, expect, beforeEach, vi } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

import { ChannelStore } from './channel-store.svelte';
import type { ApiClient } from '$lib/api/client.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { Channel } from '$lib/types/index.js';

/* ───── Helpers ───── */

function makeChannel(overrides: Partial<Channel> = {}): Channel {
	return {
		id: 'ch-1',
		name: 'general',
		serverId: 'server-1',
		type: 'text',
		position: 0,
		...overrides
	};
}

/* ───── Mock factories ───── */

function createMocks() {
	const mockAuth = {
		idToken: 'test-token',
		isGlobalAdmin: false
	} as unknown as AuthStore;

	const mockApi = {
		getChannels: vi.fn(),
		createChannel: vi.fn(),
		deleteChannel: vi.fn(),
		updateChannel: vi.fn(),
		getChannelOverrides: vi.fn(),
		setChannelOverride: vi.fn(),
		deleteChannelOverride: vi.fn(),
		updateChannelOrder: vi.fn()
	} as unknown as ApiClient;

	const mockUi = {
		error: null as string | null,
		setError: vi.fn(),
		newChannelName: '',
		newChannelType: 'text' as string,
		showCreateChannel: false,
		mobileNavOpen: false
	} as unknown as UIStore;

	const mockHub = {
		joinChannel: vi.fn().mockResolvedValue(undefined),
		leaveChannel: vi.fn().mockResolvedValue(undefined)
	} as unknown as ChatHubService;

	return { mockAuth, mockApi, mockUi, mockHub };
}

describe('ChannelStore', () => {
	let store: ChannelStore;
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
		store = new ChannelStore(mockAuth, mockApi, mockUi, mockHub);
	});

	/* ═══════════════════ Initial State ═══════════════════ */

	describe('initial state', () => {
		it('should have empty channels list', () => {
			expect(store.channels).toEqual([]);
		});

		it('should have null selectedChannelId', () => {
			expect(store.selectedChannelId).toBeNull();
		});

		it('should have empty channelMentionCounts', () => {
			expect(store.channelMentionCounts.size).toBe(0);
		});

		it('should have empty channelServerMap', () => {
			expect(store.channelServerMap.size).toBe(0);
		});

		it('should have all loading flags set to false', () => {
			expect(store.isLoadingChannels).toBe(false);
			expect(store.isCreatingChannel).toBe(false);
			expect(store.isUpdatingChannelName).toBe(false);
		});
	});

	/* ═══════════════════ loadChannels ═══════════════════ */

	describe('loadChannels', () => {
		it('should populate channels from API', async () => {
			const channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2' })];
			vi.mocked(mockApi.getChannels).mockResolvedValue(channels);

			await store.loadChannels('s1');

			expect(mockApi.getChannels).toHaveBeenCalledWith('test-token', 's1');
			expect(store.channels).toEqual(channels);
		});

		it('should auto-select first text channel', async () => {
			vi.mocked(mockApi.getChannels).mockResolvedValue([
				makeChannel({ id: 'voice-1', type: 'voice' }),
				makeChannel({ id: 'text-1', type: 'text' }),
				makeChannel({ id: 'text-2', type: 'text' })
			]);

			await store.loadChannels('s1');

			expect(store.selectedChannelId).toBe('text-1');
		});

		it('should fall back to first channel when no text channels', async () => {
			vi.mocked(mockApi.getChannels).mockResolvedValue([
				makeChannel({ id: 'voice-1', type: 'voice' })
			]);

			await store.loadChannels('s1');

			expect(store.selectedChannelId).toBe('voice-1');
		});

		it('should set selectedChannelId to null when no channels', async () => {
			vi.mocked(mockApi.getChannels).mockResolvedValue([]);

			await store.loadChannels('s1');

			expect(store.selectedChannelId).toBeNull();
		});

		it('should set isLoadingChannels during load', async () => {
			let loadingDuringCall = false;
			vi.mocked(mockApi.getChannels).mockImplementation(async () => {
				loadingDuringCall = store.isLoadingChannels;
				return [];
			});

			await store.loadChannels('s1');

			expect(loadingDuringCall).toBe(true);
			expect(store.isLoadingChannels).toBe(false);
		});

		it('should update channelServerMap', async () => {
			vi.mocked(mockApi.getChannels).mockResolvedValue([
				makeChannel({ id: 'ch1' }),
				makeChannel({ id: 'ch2' })
			]);

			await store.loadChannels('s1');

			expect(store.channelServerMap.get('ch1')).toBe('s1');
			expect(store.channelServerMap.get('ch2')).toBe('s1');
		});

		it('should leave previous channel and join new one via hub', async () => {
			store.selectedChannelId = 'old-ch';
			vi.mocked(mockApi.getChannels).mockResolvedValue([makeChannel({ id: 'ch1' })]);

			await store.loadChannels('s1');

			expect(mockHub.leaveChannel).toHaveBeenCalledWith('old-ch');
			expect(mockHub.joinChannel).toHaveBeenCalledWith('ch1');
		});

		it('should call onLoadMessages for selected channel', async () => {
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;
			vi.mocked(mockApi.getChannels).mockResolvedValue([makeChannel({ id: 'ch1' })]);

			await store.loadChannels('s1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch1');
		});

		it('should call onLoadVoiceStates', async () => {
			const onLoadVoiceStates = vi.fn().mockResolvedValue(undefined);
			store.onLoadVoiceStates = onLoadVoiceStates;
			vi.mocked(mockApi.getChannels).mockResolvedValue([makeChannel({ id: 'ch1' })]);

			await store.loadChannels('s1');

			expect(onLoadVoiceStates).toHaveBeenCalled();
		});

		it('should clear mention badge for auto-selected channel', async () => {
			store.channelMentionCounts = new Map([['ch1', 5]]);
			vi.mocked(mockApi.getChannels).mockResolvedValue([makeChannel({ id: 'ch1' })]);

			await store.loadChannels('s1');

			expect(store.channelMentionCounts.has('ch1')).toBe(false);
		});

		it('should handle API errors gracefully', async () => {
			const error = new Error('Network error');
			vi.mocked(mockApi.getChannels).mockRejectedValue(error);

			await store.loadChannels('s1');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingChannels).toBe(false);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;

			await store.loadChannels('s1');

			expect(mockApi.getChannels).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ selectChannel ═══════════════════ */

	describe('selectChannel', () => {
		it('should set selectedChannelId', async () => {
			await store.selectChannel('ch1');

			expect(store.selectedChannelId).toBe('ch1');
		});

		it('should leave previous channel and join new one', async () => {
			store.selectedChannelId = 'old-ch';

			await store.selectChannel('new-ch');

			expect(mockHub.leaveChannel).toHaveBeenCalledWith('old-ch');
			expect(mockHub.joinChannel).toHaveBeenCalledWith('new-ch');
		});

		it('should clear mention badge for selected channel', async () => {
			store.channelMentionCounts = new Map([
				['ch1', 3],
				['ch2', 5]
			]);

			await store.selectChannel('ch1');

			expect(store.channelMentionCounts.has('ch1')).toBe(false);
			expect(store.channelMentionCounts.get('ch2')).toBe(5);
		});

		it('should close mobile nav', async () => {
			(mockUi as any).mobileNavOpen = true;

			await store.selectChannel('ch1');

			expect(mockUi.mobileNavOpen).toBe(false);
		});

		it('should call onChannelSwitch callback', async () => {
			const onChannelSwitch = vi.fn();
			store.onChannelSwitch = onChannelSwitch;

			await store.selectChannel('ch1');

			expect(onChannelSwitch).toHaveBeenCalled();
		});

		it('should call onLoadMessages', async () => {
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;

			await store.selectChannel('ch1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch1');
		});

		it('should call onLoadPinnedMessages', async () => {
			const onLoadPinnedMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadPinnedMessages = onLoadPinnedMessages;

			await store.selectChannel('ch1');

			expect(onLoadPinnedMessages).toHaveBeenCalledWith('ch1');
		});
	});

	/* ═══════════════════ createChannel ═══════════════════ */

	describe('createChannel', () => {
		it('should call API and reload channels', async () => {
			(mockUi as any).newChannelName = 'new-channel';
			(mockUi as any).newChannelType = 'text';
			store.getSelectedServerId = () => 's1';
			const created = makeChannel({ id: 'ch-new', type: 'text' });
			vi.mocked(mockApi.createChannel).mockResolvedValue(created);
			vi.mocked(mockApi.getChannels).mockResolvedValue([created]);

			await store.createChannel();

			expect(mockApi.createChannel).toHaveBeenCalledWith('test-token', 's1', 'new-channel', 'text');
		});

		it('should reset UI state after creation', async () => {
			(mockUi as any).newChannelName = 'new-channel';
			(mockUi as any).newChannelType = 'voice';
			(mockUi as any).showCreateChannel = true;
			store.getSelectedServerId = () => 's1';
			vi.mocked(mockApi.createChannel).mockResolvedValue(makeChannel({ id: 'ch-new', type: 'voice' }));
			vi.mocked(mockApi.getChannels).mockResolvedValue([]);

			await store.createChannel();

			expect(mockUi.newChannelName).toBe('');
			expect(mockUi.newChannelType).toBe('text');
			expect(mockUi.showCreateChannel).toBe(false);
		});

		it('should set error when name is empty', async () => {
			(mockUi as any).newChannelName = '';

			await store.createChannel();

			expect(mockUi.error).toBe('Channel name is required.');
			expect(mockApi.createChannel).not.toHaveBeenCalled();
		});

		it('should trim whitespace-only name', async () => {
			(mockUi as any).newChannelName = '   ';

			await store.createChannel();

			expect(mockUi.error).toBe('Channel name is required.');
		});

		it('should set isCreatingChannel during creation', async () => {
			(mockUi as any).newChannelName = 'test';
			store.getSelectedServerId = () => 's1';
			let creatingDuringCall = false;
			vi.mocked(mockApi.createChannel).mockImplementation(async () => {
				creatingDuringCall = store.isCreatingChannel;
				return makeChannel({ id: 'ch-new' });
			});
			vi.mocked(mockApi.getChannels).mockResolvedValue([]);

			await store.createChannel();

			expect(creatingDuringCall).toBe(true);
			expect(store.isCreatingChannel).toBe(false);
		});

		it('should handle API errors', async () => {
			(mockUi as any).newChannelName = 'test';
			store.getSelectedServerId = () => 's1';
			const error = new Error('Creation failed');
			vi.mocked(mockApi.createChannel).mockRejectedValue(error);

			await store.createChannel();

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isCreatingChannel).toBe(false);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;
			(mockUi as any).newChannelName = 'test';

			await store.createChannel();

			expect(mockApi.createChannel).not.toHaveBeenCalled();
		});

		it('should not call API when no selectedServerId', async () => {
			(mockUi as any).newChannelName = 'test';
			store.getSelectedServerId = () => null;

			await store.createChannel();

			expect(mockApi.createChannel).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ deleteChannel ═══════════════════ */

	describe('deleteChannel', () => {
		it('should call API and remove channel from list', async () => {
			store.getSelectedServerId = () => 's1';
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2' })];
			vi.mocked(mockApi.deleteChannel).mockResolvedValue(undefined as any);

			await store.deleteChannel('ch1');

			expect(mockApi.deleteChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1');
			expect(store.channels).toHaveLength(1);
			expect(store.channels[0].id).toBe('ch2');
		});

		it('should select first remaining channel when active channel is deleted', async () => {
			store.getSelectedServerId = () => 's1';
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2' })];
			store.selectedChannelId = 'ch1';
			vi.mocked(mockApi.deleteChannel).mockResolvedValue(undefined as any);

			await store.deleteChannel('ch1');

			expect(store.selectedChannelId).toBe('ch2');
		});

		it('should set selectedChannelId to null when no channels remain', async () => {
			store.getSelectedServerId = () => 's1';
			store.channels = [makeChannel({ id: 'ch1' })];
			store.selectedChannelId = 'ch1';
			vi.mocked(mockApi.deleteChannel).mockResolvedValue(undefined as any);

			await store.deleteChannel('ch1');

			expect(store.selectedChannelId).toBeNull();
		});

		it('should call onLoadMessages for newly selected channel', async () => {
			store.getSelectedServerId = () => 's1';
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2' })];
			store.selectedChannelId = 'ch1';
			const onLoadMessages = vi.fn().mockResolvedValue(undefined);
			store.onLoadMessages = onLoadMessages;
			vi.mocked(mockApi.deleteChannel).mockResolvedValue(undefined as any);

			await store.deleteChannel('ch1');

			expect(onLoadMessages).toHaveBeenCalledWith('ch2');
		});

		it('should handle API errors', async () => {
			store.getSelectedServerId = () => 's1';
			const error = new Error('Delete failed');
			vi.mocked(mockApi.deleteChannel).mockRejectedValue(error);

			await store.deleteChannel('ch1');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;
			store.getSelectedServerId = () => 's1';

			await store.deleteChannel('ch1');

			expect(mockApi.deleteChannel).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ updateChannelName ═══════════════════ */

	describe('updateChannelName', () => {
		it('should call API with trimmed name', async () => {
			store.getSelectedServerId = () => 's1';
			vi.mocked(mockApi.updateChannel).mockResolvedValue(undefined as any);

			await store.updateChannelName('ch1', '  New Name  ');

			expect(mockApi.updateChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1', {
				name: 'New Name'
			});
		});

		it('should set error when name is empty', async () => {
			store.getSelectedServerId = () => 's1';

			await store.updateChannelName('ch1', '');

			expect(mockUi.error).toBe('Channel name is required.');
			expect(mockApi.updateChannel).not.toHaveBeenCalled();
		});

		it('should set isUpdatingChannelName during update', async () => {
			store.getSelectedServerId = () => 's1';
			let loadingDuringCall = false;
			vi.mocked(mockApi.updateChannel).mockImplementation(async () => {
				loadingDuringCall = store.isUpdatingChannelName;
				return undefined as any;
			});

			await store.updateChannelName('ch1', 'New Name');

			expect(loadingDuringCall).toBe(true);
			expect(store.isUpdatingChannelName).toBe(false);
		});

		it('should handle API errors', async () => {
			store.getSelectedServerId = () => 's1';
			const error = new Error('Update failed');
			vi.mocked(mockApi.updateChannel).mockRejectedValue(error);

			await store.updateChannelName('ch1', 'New Name');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isUpdatingChannelName).toBe(false);
		});
	});

	/* ═══════════════════ updateChannelDescription ═══════════════════ */

	describe('updateChannelDescription', () => {
		it('should call API with description', async () => {
			store.getSelectedServerId = () => 's1';
			vi.mocked(mockApi.updateChannel).mockResolvedValue(undefined as any);

			await store.updateChannelDescription('ch1', 'A cool channel');

			expect(mockApi.updateChannel).toHaveBeenCalledWith('test-token', 's1', 'ch1', {
				description: 'A cool channel'
			});
		});

		it('should not call API when no idToken', async () => {
			(mockAuth as any).idToken = null;
			store.getSelectedServerId = () => 's1';

			await store.updateChannelDescription('ch1', 'desc');

			expect(mockApi.updateChannel).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ Channel Permission Overrides ═══════════════════ */

	describe('getChannelOverrides', () => {
		it('should return overrides from API', async () => {
			const overrides = [
				{ channelId: 'ch1', roleId: 'r1', roleName: 'Admin', allow: 1, deny: 0 }
			];
			vi.mocked(mockApi.getChannelOverrides).mockResolvedValue(overrides);

			const result = await store.getChannelOverrides('ch1');

			expect(result).toEqual(overrides);
		});

		it('should return empty array when no idToken', async () => {
			(mockAuth as any).idToken = null;

			const result = await store.getChannelOverrides('ch1');

			expect(result).toEqual([]);
		});
	});

	/* ═══════════════════ channelMentionCount ═══════════════════ */

	describe('channelMentionCount', () => {
		it('should return count for channel', () => {
			store.channelMentionCounts = new Map([['ch1', 5]]);

			expect(store.channelMentionCount('ch1')).toBe(5);
		});

		it('should return 0 for unknown channel', () => {
			expect(store.channelMentionCount('unknown')).toBe(0);
		});
	});

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	describe('handleChannelNameChanged', () => {
		it('should update channel name for matching server', () => {
			store.channels = [makeChannel({ id: 'ch1', name: 'old-name' })];

			store.handleChannelNameChanged(
				{ channelId: 'ch1', serverId: 's1', name: 'new-name' },
				's1'
			);

			expect(store.channels[0].name).toBe('new-name');
		});

		it('should not update for different server', () => {
			store.channels = [makeChannel({ id: 'ch1', name: 'old-name' })];

			store.handleChannelNameChanged(
				{ channelId: 'ch1', serverId: 's2', name: 'new-name' },
				's1'
			);

			expect(store.channels[0].name).toBe('old-name');
		});
	});

	describe('handleChannelDeleted', () => {
		it('should remove channel from list', () => {
			store.channels = [makeChannel({ id: 'ch1' }), makeChannel({ id: 'ch2' })];

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' }, 's1');

			expect(store.channels).toHaveLength(1);
			expect(store.channels[0].id).toBe('ch2');
		});

		it('should select next text channel when active channel is deleted', () => {
			store.channels = [
				makeChannel({ id: 'ch1', type: 'text' }),
				makeChannel({ id: 'ch2', type: 'voice' }),
				makeChannel({ id: 'ch3', type: 'text' })
			];
			store.selectedChannelId = 'ch1';

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' }, 's1');

			expect(store.selectedChannelId).toBe('ch3');
		});

		it('should set selectedChannelId to null when no text channels remain', () => {
			store.channels = [
				makeChannel({ id: 'ch1', type: 'text' }),
				makeChannel({ id: 'ch2', type: 'voice' })
			];
			store.selectedChannelId = 'ch1';

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's1' }, 's1');

			expect(store.selectedChannelId).toBeNull();
		});

		it('should not remove channel for different server', () => {
			store.channels = [makeChannel({ id: 'ch1' })];

			store.handleChannelDeleted({ channelId: 'ch1', serverId: 's2' }, 's1');

			expect(store.channels).toHaveLength(1);
		});
	});

	describe('handleChannelDescriptionChanged', () => {
		it('should update channel description', () => {
			store.channels = [makeChannel({ id: 'ch1', description: 'old' })];

			store.handleChannelDescriptionChanged(
				{ channelId: 'ch1', serverId: 's1', description: 'new desc' },
				's1'
			);

			expect(store.channels[0].description).toBe('new desc');
		});

		it('should not update for different server', () => {
			store.channels = [makeChannel({ id: 'ch1', description: 'old' })];

			store.handleChannelDescriptionChanged(
				{ channelId: 'ch1', serverId: 's2', description: 'new' },
				's1'
			);

			expect(store.channels[0].description).toBe('old');
		});
	});

	describe('handleMentionReceived', () => {
		it('should increment mention count for channel', () => {
			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCounts.get('ch1')).toBe(1);
		});

		it('should accumulate multiple mentions', () => {
			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);
			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCounts.get('ch1')).toBe(2);
		});

		it('should not increment for currently selected channel', () => {
			store.selectedChannelId = 'ch1';

			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelMentionCounts.has('ch1')).toBe(false);
		});

		it('should update channelServerMap', () => {
			store.handleMentionReceived({ channelId: 'ch1', serverId: 's1' } as any);

			expect(store.channelServerMap.get('ch1')).toBe('s1');
		});
	});

	describe('handleChannelOrderChanged', () => {
		it('should reload channels for matching server', async () => {
			vi.mocked(mockApi.getChannels).mockResolvedValue([]);

			await store.handleChannelOrderChanged({ serverId: 's1' }, 's1');

			expect(mockApi.getChannels).toHaveBeenCalled();
		});

		it('should not reload for different server', async () => {
			await store.handleChannelOrderChanged({ serverId: 's2' }, 's1');

			expect(mockApi.getChannels).not.toHaveBeenCalled();
		});
	});

	/* ═══════════════════ Reset ═══════════════════ */

	describe('reset', () => {
		it('should reset all state to defaults', () => {
			store.channels = [makeChannel()];
			store.selectedChannelId = 'ch1';
			store.isLoadingChannels = true;
			store.isCreatingChannel = true;
			store.isUpdatingChannelName = true;
			store.channelMentionCounts = new Map([['ch1', 5]]);
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
});
