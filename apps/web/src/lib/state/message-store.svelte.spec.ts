import { describe, it, expect, beforeEach, vi } from 'vitest';
import { MessageStore } from './message-store.svelte.js';
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

function mockChannels(overrides = {}) {
	return {
		selectedChannelId: 'ch-1',
		channels: [],
		...overrides
	} as any;
}

function mockApi() {
	return {
		getMessages: vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
		sendMessage: vi.fn().mockResolvedValue(undefined),
		editMessage: vi.fn().mockResolvedValue(undefined),
		deleteMessage: vi.fn().mockResolvedValue(undefined),
		uploadImage: vi.fn().mockResolvedValue({ imageUrl: 'https://img.url' }),
		uploadFile: vi.fn().mockResolvedValue({ fileUrl: 'url', fileName: 'f', fileSize: 100, fileContentType: 'text/plain' }),
		toggleReaction: vi.fn().mockResolvedValue({ reactions: [] }),
		getPinnedMessages: vi.fn().mockResolvedValue([]),
		pinMessage: vi.fn().mockResolvedValue(undefined),
		unpinMessage: vi.fn().mockResolvedValue(undefined),
		purgeChannel: vi.fn().mockResolvedValue(undefined),
		searchServerMessages: vi.fn().mockResolvedValue({ messages: [], totalCount: 0 }),
		searchDmMessages: vi.fn().mockResolvedValue({ messages: [], totalCount: 0 }),
		getMessagesAround: vi.fn().mockResolvedValue({ messages: [], hasMoreBefore: false }),
		getDmMessagesAround: vi.fn().mockResolvedValue({ messages: [] })
	} as any;
}

function mockHub() {
	return {
		emitTyping: vi.fn(),
		clearTyping: vi.fn(),
		isConnected: true
	} as any;
}

describe('MessageStore', () => {
	let store: MessageStore;
	let auth: ReturnType<typeof mockAuth>;
	let channels: ReturnType<typeof mockChannels>;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		auth = mockAuth();
		channels = mockChannels();
		api = mockApi();
		ui = new UIStore();
		hub = mockHub();
		store = new MessageStore(auth, channels, api, ui, hub);
	});

	describe('initial state', () => {
		it('starts with empty messages', () => {
			expect(store.messages).toEqual([]);
		});

		it('starts with loading flags false', () => {
			expect(store.isSending).toBe(false);
			expect(store.isLoadingMessages).toBe(false);
			expect(store.isLoadingOlderMessages).toBe(false);
		});

		it('starts with search closed', () => {
			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
		});

		it('starts with no pinned messages', () => {
			expect(store.pinnedMessages).toEqual([]);
			expect(store.showPinnedPanel).toBe(false);
		});
	});

	describe('loadMessages', () => {
		it('loads messages from API', async () => {
			const msgs = { messages: [{ id: 'msg-1', body: 'Hi' }], hasMore: true };
			api.getMessages.mockResolvedValue(msgs);

			await store.loadMessages('ch-1');

			expect(api.getMessages).toHaveBeenCalledWith('test-token', 'ch-1', { limit: 100 });
			expect(store.messages).toEqual(msgs.messages);
			expect(store.hasMoreMessages).toBe(true);
		});

		it('does nothing when not authenticated', async () => {
			auth.idToken = null;
			await store.loadMessages('ch-1');
			expect(api.getMessages).not.toHaveBeenCalled();
		});

		it('discards stale responses', async () => {
			let resolve1: (v: any) => void;
			let resolve2: (v: any) => void;
			api.getMessages
				.mockReturnValueOnce(new Promise(r => { resolve1 = r; }))
				.mockReturnValueOnce(new Promise(r => { resolve2 = r; }));

			const p1 = store.loadMessages('ch-1');
			const p2 = store.loadMessages('ch-2');

			resolve2!({ messages: [{ id: 'msg-2' }], hasMore: false });
			await p2;

			resolve1!({ messages: [{ id: 'msg-1' }], hasMore: false });
			await p1;

			// Only the second (latest) load should be kept
			expect(store.messages).toEqual([{ id: 'msg-2' }]);
		});
	});

	describe('loadOlderMessages', () => {
		it('loads older messages and prepends', async () => {
			store.messages = [{ id: 'msg-2', createdAt: '2024-01-02' }] as any;
			store.hasMoreMessages = true;
			api.getMessages.mockResolvedValue({
				messages: [{ id: 'msg-1', createdAt: '2024-01-01' }],
				hasMore: false
			});

			await store.loadOlderMessages();

			expect(store.messages).toHaveLength(2);
			expect(store.messages[0].id).toBe('msg-1');
			expect(store.hasMoreMessages).toBe(false);
		});

		it('does nothing when no more messages', async () => {
			store.hasMoreMessages = false;
			await store.loadOlderMessages();
			expect(api.getMessages).not.toHaveBeenCalled();
		});

		it('does nothing when already loading', async () => {
			store.hasMoreMessages = true;
			store.messages = [{ id: 'msg-1', createdAt: '2024-01-01' }] as any;
			store.isLoadingOlderMessages = true;
			await store.loadOlderMessages();
			expect(api.getMessages).not.toHaveBeenCalled();
		});
	});

	describe('sendMessage', () => {
		it('sends message and clears body', async () => {
			store.messageBody = 'Hello!';

			await store.sendMessage();

			expect(api.sendMessage).toHaveBeenCalledWith(
				'test-token', 'ch-1', 'Hello!', null, null, null
			);
			expect(store.messageBody).toBe('');
		});

		it('shows error for empty message without attachments', async () => {
			store.messageBody = '';
			await store.sendMessage();
			expect(ui.error).toBe('Message body, image, or file is required.');
		});

		it('clears reply state after sending', async () => {
			store.messageBody = 'reply text';
			store.replyingTo = { messageId: 'msg-1', authorName: 'Alice', bodyPreview: 'hi', context: 'channel' };

			await store.sendMessage();

			expect(store.replyingTo).toBe(null);
		});

		it('does nothing without token', async () => {
			auth.idToken = null;
			store.messageBody = 'test';
			await store.sendMessage();
			expect(api.sendMessage).not.toHaveBeenCalled();
		});
	});

	describe('resolveMentions', () => {
		it('converts @here to wire token', () => {
			expect(store.resolveMentions('Hello @here')).toBe('Hello <@here>');
		});

		it('converts user mentions to wire tokens', () => {
			store.pendingMentions = new Map([['Alice', 'user-123']]);
			expect(store.resolveMentions('Hey @Alice')).toBe('Hey <@user-123>');
		});

		it('handles multiple mentions', () => {
			store.pendingMentions = new Map([
				['Alice', 'user-1'],
				['Bob', 'user-2']
			]);
			expect(store.resolveMentions('@Alice and @Bob')).toBe('<@user-1> and <@user-2>');
		});
	});

	describe('reply management', () => {
		it('startReply sets reply context', () => {
			store.startReply('msg-1', 'Alice', 'Hello...');
			expect(store.replyingTo).toEqual({
				messageId: 'msg-1',
				authorName: 'Alice',
				bodyPreview: 'Hello...',
				context: 'channel'
			});
		});

		it('cancelReply clears reply', () => {
			store.startReply('msg-1', 'Alice', 'Hello...');
			store.cancelReply();
			expect(store.replyingTo).toBe(null);
		});
	});

	describe('editMessage', () => {
		it('calls API to edit', async () => {
			await store.editMessage('msg-1', 'new body');
			expect(api.editMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'msg-1', 'new body');
		});

		it('falls back to local update when disconnected', async () => {
			hub.isConnected = false;
			store.messages = [{ id: 'msg-1', body: 'old' }] as any;

			await store.editMessage('msg-1', 'new');

			expect(store.messages[0].body).toBe('new');
		});
	});

	describe('deleteMessage', () => {
		it('calls API to delete', async () => {
			await store.deleteMessage('msg-1');
			expect(api.deleteMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'msg-1');
		});

		it('falls back to local removal when disconnected', async () => {
			hub.isConnected = false;
			store.messages = [{ id: 'msg-1' }, { id: 'msg-2' }] as any;

			await store.deleteMessage('msg-1');

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('msg-2');
		});
	});

	describe('pinned messages', () => {
		it('loadPinnedMessages fetches from API', async () => {
			const pinned = [{ messageId: 'msg-1', channelId: 'ch-1' }];
			api.getPinnedMessages.mockResolvedValue(pinned);

			await store.loadPinnedMessages('ch-1');

			expect(store.pinnedMessages).toEqual(pinned);
		});

		it('pinMessage calls API', async () => {
			await store.pinMessage('msg-1');
			expect(api.pinMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'msg-1');
		});

		it('unpinMessage removes from local list', async () => {
			store.pinnedMessages = [
				{ messageId: 'msg-1' },
				{ messageId: 'msg-2' }
			] as any;

			await store.unpinMessage('msg-1');

			expect(store.pinnedMessages).toHaveLength(1);
		});

		it('togglePinnedPanel toggles and loads', () => {
			api.getPinnedMessages.mockResolvedValue([]);

			store.togglePinnedPanel();

			expect(store.showPinnedPanel).toBe(true);
			expect(api.getPinnedMessages).toHaveBeenCalled();
		});

		it('togglePinnedPanel closes', () => {
			store.showPinnedPanel = true;

			store.togglePinnedPanel();

			expect(store.showPinnedPanel).toBe(false);
		});
	});

	describe('purgeChannel', () => {
		it('calls API', async () => {
			await store.purgeChannel('ch-1');
			expect(api.purgeChannel).toHaveBeenCalledWith('test-token', 'ch-1');
		});

		it('clears messages locally when disconnected', async () => {
			hub.isConnected = false;
			store.messages = [{ id: 'msg-1' }] as any;
			store.hasMoreMessages = true;

			await store.purgeChannel('ch-1');

			expect(store.messages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
		});
	});

	describe('search', () => {
		it('toggleSearch opens and closes', () => {
			store.toggleSearch();
			expect(store.isSearchOpen).toBe(true);

			store.toggleSearch();
			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.searchResults).toBe(null);
		});

		it('searchMessages queries server', async () => {
			store.getSelectedServerId = () => 'srv-1';
			const results = { messages: [{ id: 'msg-1' }], totalCount: 1 };
			api.searchServerMessages.mockResolvedValue(results);

			await store.searchMessages('hello');

			expect(api.searchServerMessages).toHaveBeenCalledWith('test-token', 'srv-1', 'hello', {});
			expect(store.searchResults).toEqual(results);
		});

		it('searchMessages returns null for short query', async () => {
			await store.searchMessages('a');
			expect(store.searchResults).toBe(null);
		});
	});

	describe('SignalR handlers', () => {
		it('handleIncomingMessage adds to current channel', () => {
			const msg = { id: 'msg-1', channelId: 'ch-1', body: 'hi' } as any;

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
		});

		it('handleIncomingMessage ignores different channel', () => {
			const msg = { id: 'msg-1', channelId: 'ch-2', body: 'hi' } as any;

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(0);
		});

		it('handleIncomingMessage does not duplicate', () => {
			store.messages = [{ id: 'msg-1', channelId: 'ch-1' }] as any;
			const msg = { id: 'msg-1', channelId: 'ch-1', body: 'hi' } as any;

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
		});

		it('handleTyping adds user', () => {
			store.handleTyping('ch-1', 'Alice');
			expect(store.typingUsers).toContain('Alice');
		});

		it('handleTyping ignores different channel', () => {
			store.handleTyping('ch-2', 'Alice');
			expect(store.typingUsers).toEqual([]);
		});

		it('handleTyping does not duplicate', () => {
			store.typingUsers = ['Alice'];
			store.handleTyping('ch-1', 'Alice');
			expect(store.typingUsers).toEqual(['Alice']);
		});

		it('handleStoppedTyping removes user', () => {
			store.typingUsers = ['Alice', 'Bob'];
			store.handleStoppedTyping('ch-1', 'Alice');
			expect(store.typingUsers).toEqual(['Bob']);
		});

		it('handleMessageDeleted removes message', () => {
			store.messages = [{ id: 'msg-1' }, { id: 'msg-2' }] as any;

			store.handleMessageDeleted({ channelId: 'ch-1', messageId: 'msg-1' });

			expect(store.messages).toHaveLength(1);
		});

		it('handleChannelPurged clears all messages', () => {
			store.messages = [{ id: 'msg-1' }] as any;
			store.hasMoreMessages = true;
			store.pinnedMessages = [{ messageId: 'msg-1' }] as any;

			store.handleChannelPurged({ channelId: 'ch-1' });

			expect(store.messages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.pinnedMessages).toEqual([]);
		});

		it('handleMessageEdited updates message', () => {
			store.messages = [{ id: 'msg-1', body: 'old' }] as any;

			store.handleMessageEdited({
				channelId: 'ch-1',
				messageId: 'msg-1',
				body: 'new',
				editedAt: '2024-01-01T00:00:00Z'
			});

			expect(store.messages[0].body).toBe('new');
		});

		it('handleMessageUnpinned removes from pinned list', () => {
			store.pinnedMessages = [
				{ messageId: 'msg-1' },
				{ messageId: 'msg-2' }
			] as any;

			store.handleMessageUnpinned({ channelId: 'ch-1', messageId: 'msg-1', unpinnedBy: { userId: 'u-1', displayName: 'User' } });

			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('msg-2');
		});

		it('patchLinkPreviews updates previews on a message', () => {
			store.messages = [{ id: 'msg-1', linkPreviews: [] }] as any;
			const previews = [{ url: 'https://example.com', title: 'Ex' }] as any;

			store.patchLinkPreviews('msg-1', previews);

			expect(store.messages[0].linkPreviews).toEqual(previews);
		});
	});

	describe('reset', () => {
		it('resets all state', () => {
			store.messages = [{ id: 'msg-1' }] as any;
			store.messageBody = 'draft';
			store.isSending = true;
			store.isSearchOpen = true;
			store.pinnedMessages = [{ messageId: 'msg-1' }] as any;

			store.reset();

			expect(store.messages).toEqual([]);
			expect(store.messageBody).toBe('');
			expect(store.isSending).toBe(false);
			expect(store.isSearchOpen).toBe(false);
			expect(store.pinnedMessages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
		});
	});
});
