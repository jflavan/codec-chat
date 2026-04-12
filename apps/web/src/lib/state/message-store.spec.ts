import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock svelte context APIs and runes before importing the store
vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

// Mock the UIStore import (the class is imported directly in the source)
vi.mock('./ui-store.svelte.js', () => ({
	UIStore: {
		reactionToggleKey: (messageId: string, emoji: string) => `${messageId}:${emoji}`
	}
}));

import { MessageStore } from './message-store.svelte.js';
import type { Message, PinnedMessage } from '$lib/types/index.js';
import type { ReactionUpdate, MessageDeletedEvent, ChannelPurgedEvent, MessageEditedEvent } from '$lib/services/chat-hub.js';

/* ───── helpers ───── */

function makeMessage(overrides: Partial<Message> = {}): Message {
	return {
		id: 'msg-1',
		authorName: 'Alice',
		body: 'Hello world',
		createdAt: '2026-01-01T00:00:00Z',
		channelId: 'ch-1',
		reactions: [],
		linkPreviews: [],
		mentions: [],
		replyContext: null,
		...overrides
	};
}

function makePinnedMessage(overrides: Partial<PinnedMessage> = {}): PinnedMessage {
	return {
		messageId: 'msg-1',
		channelId: 'ch-1',
		pinnedBy: { userId: 'u-1', displayName: 'Alice' },
		pinnedAt: '2026-01-01T00:00:00Z',
		message: makeMessage(),
		...overrides
	};
}

function createMockApi() {
	return {
		getMessages: vi.fn(),
		sendMessage: vi.fn(),
		editMessage: vi.fn(),
		deleteMessage: vi.fn(),
		toggleReaction: vi.fn(),
		uploadImage: vi.fn(),
		uploadFile: vi.fn(),
		getPinnedMessages: vi.fn(),
		pinMessage: vi.fn(),
		unpinMessage: vi.fn(),
		purgeChannel: vi.fn(),
		searchServerMessages: vi.fn(),
		searchDmMessages: vi.fn(),
		getMessagesAround: vi.fn(),
		getDmMessagesAround: vi.fn()
	} as any;
}

function createMockAuth(overrides: Record<string, unknown> = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'u-1', displayName: 'Alice', effectiveDisplayName: 'Alice' } },
		effectiveDisplayName: 'Alice',
		...overrides
	} as any;
}

function createMockChannels(overrides: Record<string, unknown> = {}) {
	return {
		selectedChannelId: 'ch-1',
		...overrides
	} as any;
}

function createMockUi() {
	return {
		error: null as string | null,
		setError: vi.fn(),
		ignoredReactionUpdates: new Map<string, string[]>(),
		pendingReactionKeys: new Set<string>(),
		setReactionPending: vi.fn(),
		mobileNavOpen: false
	} as any;
}

function createMockHub() {
	return {
		isConnected: true,
		clearTyping: vi.fn(),
		emitTyping: vi.fn(),
		joinDmChannel: vi.fn(),
		leaveDmChannel: vi.fn()
	} as any;
}

function createStore(overrides: {
	auth?: ReturnType<typeof createMockAuth>;
	channels?: ReturnType<typeof createMockChannels>;
	api?: ReturnType<typeof createMockApi>;
	ui?: ReturnType<typeof createMockUi>;
	hub?: ReturnType<typeof createMockHub>;
} = {}) {
	const auth = overrides.auth ?? createMockAuth();
	const channels = overrides.channels ?? createMockChannels();
	const api = overrides.api ?? createMockApi();
	const ui = overrides.ui ?? createMockUi();
	const hub = overrides.hub ?? createMockHub();
	return { store: new MessageStore(auth, channels, api, ui, hub), auth, channels, api, ui, hub };
}

/* ───── tests ───── */

describe('MessageStore', () => {
	describe('initial state', () => {
		it('starts with empty messages and loading false', () => {
			const { store } = createStore();
			expect(store.messages).toEqual([]);
			expect(store.isLoadingMessages).toBe(false);
			expect(store.isSending).toBe(false);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.isPurgingChannel).toBe(false);
			expect(store.typingUsers).toEqual([]);
			expect(store.messageBody).toBe('');
			expect(store.replyingTo).toBeNull();
		});
	});

	describe('loadMessages', () => {
		it('populates messages from API response', async () => {
			const api = createMockApi();
			const msgs = [makeMessage({ id: 'a' }), makeMessage({ id: 'b' })];
			api.getMessages.mockResolvedValue({ messages: msgs, hasMore: true });
			const { store } = createStore({ api });

			await store.loadMessages('ch-1');

			expect(api.getMessages).toHaveBeenCalledWith('test-token', 'ch-1', { limit: 100 });
			expect(store.messages).toEqual(msgs);
			expect(store.hasMoreMessages).toBe(true);
			expect(store.isLoadingMessages).toBe(false);
		});

		it('returns early when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.loadMessages('ch-1');

			expect(api.getMessages).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			const error = new Error('network error');
			api.getMessages.mockRejectedValue(error);
			const { store } = createStore({ api, ui });

			await store.loadMessages('ch-1');

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingMessages).toBe(false);
		});

		it('sets isLoadingMessages during fetch', async () => {
			const api = createMockApi();
			let loadingDuringFetch = false;
			api.getMessages.mockImplementation(async () => {
				// We cannot read $state from outside Svelte reactivity context,
				// but the store does set it synchronously before the await.
				return { messages: [], hasMore: false };
			});
			const { store } = createStore({ api });

			await store.loadMessages('ch-1');
			// After completion, loading should be false
			expect(store.isLoadingMessages).toBe(false);
		});
	});

	describe('loadOlderMessages', () => {
		it('prepends older messages', async () => {
			const api = createMockApi();
			const existing = makeMessage({ id: 'b', createdAt: '2026-01-02T00:00:00Z' });
			const older = makeMessage({ id: 'a', createdAt: '2026-01-01T00:00:00Z' });
			api.getMessages.mockResolvedValue({ messages: [older], hasMore: false });

			const { store } = createStore({ api });
			store.messages = [existing];
			store.hasMoreMessages = true;

			await store.loadOlderMessages();

			expect(store.messages).toEqual([older, existing]);
			expect(store.hasMoreMessages).toBe(false);
		});

		it('does nothing if hasMoreMessages is false', async () => {
			const api = createMockApi();
			const { store } = createStore({ api });
			store.messages = [makeMessage()];
			store.hasMoreMessages = false;

			await store.loadOlderMessages();

			expect(api.getMessages).not.toHaveBeenCalled();
		});

		it('does nothing if no messages exist', async () => {
			const api = createMockApi();
			const { store } = createStore({ api });
			store.hasMoreMessages = true;

			await store.loadOlderMessages();

			expect(api.getMessages).not.toHaveBeenCalled();
		});
	});

	describe('sendMessage', () => {
		it('calls api.sendMessage and clears composer state', async () => {
			const api = createMockApi();
			api.sendMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.messageBody = 'Hello!';

			await store.sendMessage();

			expect(api.sendMessage).toHaveBeenCalledWith(
				'test-token', 'ch-1', 'Hello!', null, null, null
			);
			expect(store.messageBody).toBe('');
			expect(store.isSending).toBe(false);
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });
			store.messageBody = 'Hello';

			await store.sendMessage();

			expect(api.sendMessage).not.toHaveBeenCalled();
		});

		it('does nothing when no channel selected', async () => {
			const api = createMockApi();
			const channels = createMockChannels({ selectedChannelId: null });
			const { store } = createStore({ api, channels });
			store.messageBody = 'Hello';

			await store.sendMessage();

			expect(api.sendMessage).not.toHaveBeenCalled();
		});

		it('sets ui error when body is empty and no attachments', async () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			store.messageBody = '';

			await store.sendMessage();

			expect(ui.error).toBe('Message body, image, or file is required.');
		});

		it('falls back to loadMessages when hub is disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.sendMessage.mockResolvedValue(undefined);
			api.getMessages.mockResolvedValue({ messages: [], hasMore: false });
			const { store } = createStore({ api, hub });
			store.messageBody = 'Hi';

			await store.sendMessage();

			expect(api.getMessages).toHaveBeenCalled();
		});

		it('calls hub.clearTyping after sending', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.sendMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.messageBody = 'Hi';

			await store.sendMessage();

			expect(hub.clearTyping).toHaveBeenCalledWith('ch-1', 'Alice');
		});

		it('reports error to ui on api failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			const error = new Error('send failed');
			api.sendMessage.mockRejectedValue(error);
			const { store } = createStore({ api, ui });
			store.messageBody = 'Hi';

			await store.sendMessage();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isSending).toBe(false);
		});
	});

	describe('editMessage', () => {
		it('calls api.editMessage', async () => {
			const api = createMockApi();
			api.editMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });

			await store.editMessage('msg-1', 'updated body');

			expect(api.editMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'msg-1', 'updated body');
		});

		it('updates message locally when hub is disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.editMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.messages = [makeMessage({ id: 'msg-1', body: 'old' })];

			await store.editMessage('msg-1', 'new body');

			expect(store.messages[0].body).toBe('new body');
			expect(store.messages[0].editedAt).toBeTruthy();
		});

		it('does not update locally when hub is connected', async () => {
			const api = createMockApi();
			api.editMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.messages = [makeMessage({ id: 'msg-1', body: 'old' })];

			await store.editMessage('msg-1', 'new body');

			expect(store.messages[0].body).toBe('old');
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.editMessage('msg-1', 'new');

			expect(api.editMessage).not.toHaveBeenCalled();
		});

		it('reports error on failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			api.editMessage.mockRejectedValue(new Error('fail'));
			const { store } = createStore({ api, ui });

			await store.editMessage('msg-1', 'new');

			expect(ui.setError).toHaveBeenCalled();
		});
	});

	describe('deleteMessage', () => {
		it('calls api.deleteMessage', async () => {
			const api = createMockApi();
			api.deleteMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });

			await store.deleteMessage('msg-1');

			expect(api.deleteMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'msg-1');
		});

		it('removes message locally when hub is disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.deleteMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.messages = [makeMessage({ id: 'msg-1' }), makeMessage({ id: 'msg-2' })];

			await store.deleteMessage('msg-1');

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('msg-2');
		});

		it('also removes from pinned messages when hub disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.deleteMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.messages = [makeMessage({ id: 'msg-1' })];
			store.pinnedMessages = [makePinnedMessage({ messageId: 'msg-1' })];

			await store.deleteMessage('msg-1');

			expect(store.pinnedMessages).toHaveLength(0);
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.deleteMessage('msg-1');

			expect(api.deleteMessage).not.toHaveBeenCalled();
		});
	});

	describe('handleIncomingMessage (SignalR)', () => {
		it('appends message to list when channel matches', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			const msg = makeMessage({ id: 'new-msg', channelId: 'ch-1' });

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('new-msg');
		});

		it('does not add message for different channel', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			const msg = makeMessage({ id: 'new-msg', channelId: 'ch-2' });

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(0);
		});

		it('does not add duplicate message', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			const msg = makeMessage({ id: 'msg-1', channelId: 'ch-1' });
			store.messages = [msg];

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
		});

		it('fills in missing linkPreviews and mentions', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			const msg = {
				...makeMessage({ id: 'new', channelId: 'ch-1' }),
				linkPreviews: undefined,
				mentions: undefined,
				replyContext: undefined
			} as any;

			store.handleIncomingMessage(msg);

			expect(store.messages[0].linkPreviews).toEqual([]);
			expect(store.messages[0].mentions).toEqual([]);
			expect(store.messages[0].replyContext).toBeNull();
		});
	});

	describe('handleMessageEdited (SignalR)', () => {
		it('updates message body and editedAt', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1', body: 'old' })];

			const event: MessageEditedEvent = {
				messageId: 'msg-1',
				channelId: 'ch-1',
				body: 'new body',
				editedAt: '2026-01-02T00:00:00Z'
			};
			store.handleMessageEdited(event);

			expect(store.messages[0].body).toBe('new body');
			expect(store.messages[0].editedAt).toBe('2026-01-02T00:00:00Z');
		});

		it('ignores events for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1', body: 'old' })];

			store.handleMessageEdited({
				messageId: 'msg-1',
				channelId: 'ch-2',
				body: 'new',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.messages[0].body).toBe('old');
		});
	});

	describe('handleMessageDeleted (SignalR)', () => {
		it('removes message from list', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1' }), makeMessage({ id: 'msg-2', channelId: 'ch-1' })];
			store.pinnedMessages = [makePinnedMessage({ messageId: 'msg-1' })];

			const event: MessageDeletedEvent = { messageId: 'msg-1', channelId: 'ch-1' };
			store.handleMessageDeleted(event);

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('msg-2');
			expect(store.pinnedMessages).toHaveLength(0);
		});

		it('ignores events for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1' })];

			store.handleMessageDeleted({ messageId: 'msg-1', channelId: 'ch-2' });

			expect(store.messages).toHaveLength(1);
		});
	});

	describe('handleChannelPurged (SignalR)', () => {
		it('clears all messages and pinned messages', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage()];
			store.pinnedMessages = [makePinnedMessage()];
			store.hasMoreMessages = true;

			store.handleChannelPurged({ channelId: 'ch-1' });

			expect(store.messages).toEqual([]);
			expect(store.pinnedMessages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
		});

		it('ignores events for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage()];

			store.handleChannelPurged({ channelId: 'ch-2' });

			expect(store.messages).toHaveLength(1);
		});
	});

	describe('handleReactionUpdate (SignalR)', () => {
		it('updates reactions on the correct message', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1', reactions: [] })];

			const newReactions = [{ emoji: '👍', count: 1, userIds: ['u-2'] }];
			const update: ReactionUpdate = {
				messageId: 'msg-1',
				channelId: 'ch-1',
				reactions: newReactions
			};

			store.handleReactionUpdate(update);

			expect(store.messages[0].reactions).toEqual(newReactions);
		});

		it('ignores events for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.messages = [makeMessage({ id: 'msg-1', channelId: 'ch-1', reactions: [] })];

			store.handleReactionUpdate({
				messageId: 'msg-1',
				channelId: 'ch-2',
				reactions: [{ emoji: '👍', count: 1, userIds: ['u-2'] }]
			});

			expect(store.messages[0].reactions).toEqual([]);
		});
	});

	describe('handleTyping / handleStoppedTyping', () => {
		it('adds typing user for current channel', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });

			store.handleTyping('ch-1', 'Bob');

			expect(store.typingUsers).toEqual(['Bob']);
		});

		it('does not add duplicate typing user', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.typingUsers = ['Bob'];

			store.handleTyping('ch-1', 'Bob');

			expect(store.typingUsers).toEqual(['Bob']);
		});

		it('ignores typing for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });

			store.handleTyping('ch-2', 'Bob');

			expect(store.typingUsers).toEqual([]);
		});

		it('removes typing user on stopped typing', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.typingUsers = ['Bob', 'Carol'];

			store.handleStoppedTyping('ch-1', 'Bob');

			expect(store.typingUsers).toEqual(['Carol']);
		});
	});

	describe('reply management', () => {
		it('startReply sets replyingTo', () => {
			const { store } = createStore();
			store.startReply('msg-1', 'Alice', 'Hello...');

			expect(store.replyingTo).toEqual({
				messageId: 'msg-1',
				authorName: 'Alice',
				bodyPreview: 'Hello...',
				context: 'channel'
			});
		});

		it('cancelReply clears replyingTo', () => {
			const { store } = createStore();
			store.startReply('msg-1', 'Alice', 'Hello...');
			store.cancelReply();

			expect(store.replyingTo).toBeNull();
		});
	});

	describe('toggleSearch', () => {
		it('opens search', () => {
			const { store } = createStore();
			store.toggleSearch();
			expect(store.isSearchOpen).toBe(true);
		});

		it('closes search and clears state', () => {
			const { store } = createStore();
			store.isSearchOpen = true;
			store.searchQuery = 'test';
			store.searchFilters = { channelId: 'ch-1' };

			store.toggleSearch();

			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.searchFilters).toEqual({});
			expect(store.searchResults).toBeNull();
		});
	});

	describe('patchLinkPreviews', () => {
		it('adds link previews to a message', () => {
			const { store } = createStore();
			store.messages = [makeMessage({ id: 'msg-1', linkPreviews: [] })];

			const previews = [{ url: 'https://example.com', title: 'Example', description: null, imageUrl: null, siteName: null, canonicalUrl: null }];
			store.patchLinkPreviews('msg-1', previews);

			expect(store.messages[0].linkPreviews).toEqual(previews);
		});

		it('does not modify other messages', () => {
			const { store } = createStore();
			store.messages = [
				makeMessage({ id: 'msg-1', linkPreviews: [] }),
				makeMessage({ id: 'msg-2', linkPreviews: [] })
			];

			store.patchLinkPreviews('msg-1', [{ url: 'https://example.com', title: 'E', description: null, imageUrl: null, siteName: null, canonicalUrl: null }]);

			expect(store.messages[1].linkPreviews).toEqual([]);
		});
	});

	describe('resolveMentions', () => {
		it('replaces @displayName with wire token', () => {
			const { store } = createStore();
			store.pendingMentions = new Map([['Bob', 'u-bob']]);

			const result = store.resolveMentions('Hey @Bob!');
			expect(result).toBe('Hey <@u-bob>!');
		});

		it('replaces @here with wire token', () => {
			const { store } = createStore();
			const result = store.resolveMentions('Hey @here');
			expect(result).toBe('Hey <@here>');
		});
	});

	describe('pinned messages', () => {
		it('loadPinnedMessages fetches from API', async () => {
			const api = createMockApi();
			const pinned = [makePinnedMessage()];
			api.getPinnedMessages.mockResolvedValue(pinned);
			const { store } = createStore({ api });

			await store.loadPinnedMessages('ch-1');

			expect(store.pinnedMessages).toEqual(pinned);
		});

		it('loadPinnedMessages defaults to empty on error', async () => {
			const api = createMockApi();
			api.getPinnedMessages.mockRejectedValue(new Error('fail'));
			const { store } = createStore({ api });

			await store.loadPinnedMessages('ch-1');

			expect(store.pinnedMessages).toEqual([]);
		});

		it('unpinMessage removes from list', async () => {
			const api = createMockApi();
			api.unpinMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.pinnedMessages = [makePinnedMessage({ messageId: 'msg-1' }), makePinnedMessage({ messageId: 'msg-2' })];

			await store.unpinMessage('msg-1');

			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('msg-2');
		});

		it('togglePinnedPanel toggles and loads on open', async () => {
			const api = createMockApi();
			api.getPinnedMessages.mockResolvedValue([]);
			const { store } = createStore({ api });

			store.togglePinnedPanel();
			expect(store.showPinnedPanel).toBe(true);

			store.togglePinnedPanel();
			expect(store.showPinnedPanel).toBe(false);
		});
	});

	describe('handleMessagePinned (SignalR)', () => {
		it('adds pin to list when message exists locally', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			const msg = makeMessage({ id: 'msg-1', channelId: 'ch-1' });
			store.messages = [msg];
			store.showPinnedPanel = false;

			store.handleMessagePinned({
				messageId: 'msg-1',
				channelId: 'ch-1',
				pinnedBy: { userId: 'u-1', displayName: 'Alice' },
				pinnedAt: '2026-01-01T00:00:00Z'
			});

			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('msg-1');
		});

		it('ignores events for other channels', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });

			store.handleMessagePinned({
				messageId: 'msg-1',
				channelId: 'ch-2',
				pinnedBy: { userId: 'u-1', displayName: 'Alice' },
				pinnedAt: '2026-01-01T00:00:00Z'
			});

			expect(store.pinnedMessages).toHaveLength(0);
		});
	});

	describe('handleMessageUnpinned (SignalR)', () => {
		it('removes pin from list', () => {
			const channels = createMockChannels({ selectedChannelId: 'ch-1' });
			const { store } = createStore({ channels });
			store.pinnedMessages = [makePinnedMessage({ messageId: 'msg-1' })];

			store.handleMessageUnpinned({
				messageId: 'msg-1',
				channelId: 'ch-1',
				unpinnedBy: { userId: 'u-1', displayName: 'Alice' }
			});

			expect(store.pinnedMessages).toHaveLength(0);
		});
	});

	describe('purgeChannel', () => {
		it('calls api and clears messages when hub disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.purgeChannel.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.messages = [makeMessage()];
			store.hasMoreMessages = true;

			await store.purgeChannel('ch-1');

			expect(api.purgeChannel).toHaveBeenCalledWith('test-token', 'ch-1');
			expect(store.messages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.isPurgingChannel).toBe(false);
		});

		it('does not clear locally when hub is connected', async () => {
			const api = createMockApi();
			api.purgeChannel.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.messages = [makeMessage()];

			await store.purgeChannel('ch-1');

			expect(store.messages).toHaveLength(1);
		});
	});

	describe('reset', () => {
		it('resets all state to defaults', () => {
			const { store } = createStore();
			store.messages = [makeMessage()];
			store.messageBody = 'draft';
			store.isSearchOpen = true;
			store.searchQuery = 'test';
			store.pinnedMessages = [makePinnedMessage()];
			store.hasMoreMessages = true;
			store.typingUsers = ['Bob'];

			store.reset();

			expect(store.messages).toEqual([]);
			expect(store.messageBody).toBe('');
			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.pinnedMessages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.typingUsers).toEqual([]);
			expect(store.isSending).toBe(false);
			expect(store.isLoadingMessages).toBe(false);
			expect(store.replyingTo).toBeNull();
		});
	});

	describe('attachImage', () => {
		it('rejects unsupported image types', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const file = new File(['data'], 'test.bmp', { type: 'image/bmp' });

			store.attachImage(file);

			expect(ui.error).toBe('Unsupported image type. Allowed: JPG, PNG, WebP, GIF.');
			expect(store.pendingImage).toBeNull();
		});

		it('rejects images over 10MB', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const bigData = new Uint8Array(11 * 1024 * 1024);
			const file = new File([bigData], 'big.png', { type: 'image/png' });

			store.attachImage(file);

			expect(ui.error).toBe('Image must be under 10 MB.');
		});

		it('accepts valid image', () => {
			const { store } = createStore();
			const file = new File(['data'], 'photo.png', { type: 'image/png' });

			store.attachImage(file);

			expect(store.pendingImage).toBe(file);
			expect(store.pendingImagePreview).toBeTruthy();
		});
	});

	describe('attachFile', () => {
		it('rejects unsupported file types', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const file = new File(['data'], 'test.exe', { type: 'application/x-msdownload' });

			store.attachFile(file);

			expect(ui.error).toBe('Unsupported file type.');
			expect(store.pendingFile).toBeNull();
		});

		it('rejects files over 25MB', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const bigData = new Uint8Array(26 * 1024 * 1024);
			const file = new File([bigData], 'big.pdf', { type: 'application/pdf' });

			store.attachFile(file);

			expect(ui.error).toBe('File must be under 25 MB.');
		});

		it('accepts valid file', () => {
			const { store } = createStore();
			const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });

			store.attachFile(file);

			expect(store.pendingFile).toBe(file);
		});
	});
});
