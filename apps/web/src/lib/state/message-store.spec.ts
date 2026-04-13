import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));
vi.mock('$lib/utils/theme.js', () => ({ getTheme: () => 'dark', applyTheme: vi.fn() }));
vi.mock('$lib/api/client.js', () => ({
	ApiError: class ApiError extends Error {
		status: number;
		constructor(status: number, msg = `API error ${status}`) {
			super(msg);
			this.name = 'ApiError';
			this.status = status;
		}
	}
}));
vi.mock('$lib/types/models.js', () => ({
	ReportType: { User: 0, Message: 1, Server: 2 }
}));

import { MessageStore } from './message-store.svelte';
import type { Message, PinnedMessage } from '$lib/types/index.js';

function makeMockAuth(overrides: Record<string, unknown> = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1', displayName: 'TestUser' } },
		effectiveDisplayName: 'TestUser',
		...overrides
	};
}

function makeMockChannels(selectedChannelId: string | null = null) {
	return {
		selectedChannelId
	};
}

function makeMockApi() {
	return {
		getMessages: vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
		sendMessage: vi.fn().mockResolvedValue({}),
		editMessage: vi.fn().mockResolvedValue(undefined),
		deleteMessage: vi.fn().mockResolvedValue(undefined),
		toggleReaction: vi.fn().mockResolvedValue({ reactions: [] }),
		uploadImage: vi.fn().mockResolvedValue({ imageUrl: 'http://img.test/1.png' }),
		uploadFile: vi.fn().mockResolvedValue({
			fileUrl: 'http://file.test/1.pdf',
			fileName: 'test.pdf',
			fileSize: 1024,
			fileContentType: 'application/pdf'
		}),
		getPinnedMessages: vi.fn().mockResolvedValue([]),
		pinMessage: vi.fn().mockResolvedValue(undefined),
		unpinMessage: vi.fn().mockResolvedValue(undefined),
		purgeChannel: vi.fn().mockResolvedValue(undefined),
		searchServerMessages: vi.fn().mockResolvedValue({ messages: [], totalCount: 0, page: 1, pageSize: 25 }),
		searchDmMessages: vi.fn().mockResolvedValue({ messages: [], totalCount: 0, page: 1, pageSize: 25 }),
		getMessagesAround: vi.fn().mockResolvedValue({ messages: [], hasMoreBefore: false }),
		getDmMessagesAround: vi.fn().mockResolvedValue({ messages: [], hasMoreBefore: false })
	};
}

function makeMockUi() {
	return {
		error: null as string | null,
		setError: vi.fn(),
		mobileNavOpen: false,
		pendingReactionKeys: new Set<string>(),
		ignoredReactionUpdates: new Map<string, string[]>(),
		isReactionPending: vi.fn().mockReturnValue(false),
		setReactionPending: vi.fn(),
		userPresence: new Map()
	};
}

function makeMockHub() {
	return {
		emitTyping: vi.fn(),
		clearTyping: vi.fn(),
		isConnected: true
	};
}

function makeMessage(id: string, channelId: string, body = 'hello'): Message {
	return {
		id,
		channelId,
		userId: 'user-2',
		displayName: 'Friend',
		avatarUrl: null,
		body,
		imageUrl: null,
		createdAt: '2026-01-01T00:00:00Z',
		editedAt: null,
		reactions: [],
		linkPreviews: [],
		mentions: [],
		replyContext: null,
		replyToMessageId: null,
		fileUrl: null,
		fileName: null,
		fileSize: null,
		fileContentType: null,
		nickname: null
	} as unknown as Message;
}

function makePinnedMessage(messageId: string, channelId: string): PinnedMessage {
	return {
		messageId,
		channelId,
		pinnedBy: 'user-1',
		pinnedAt: '2026-01-01T00:00:00Z',
		message: makeMessage(messageId, channelId)
	} as unknown as PinnedMessage;
}

describe('MessageStore', () => {
	let store: MessageStore;
	let mockAuth: ReturnType<typeof makeMockAuth>;
	let mockChannels: ReturnType<typeof makeMockChannels>;
	let mockApi: ReturnType<typeof makeMockApi>;
	let mockUi: ReturnType<typeof makeMockUi>;
	let mockHub: ReturnType<typeof makeMockHub>;

	beforeEach(() => {
		vi.clearAllMocks();
		mockAuth = makeMockAuth();
		mockChannels = makeMockChannels('ch-1');
		mockApi = makeMockApi();
		mockUi = makeMockUi();
		mockHub = makeMockHub();
		store = new MessageStore(
			mockAuth as any,
			mockChannels as any,
			mockApi as any,
			mockUi as any,
			mockHub as any
		);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have empty messages', () => {
			expect(store.messages).toEqual([]);
		});

		it('should have empty typingUsers', () => {
			expect(store.typingUsers).toEqual([]);
		});

		it('should have empty messageBody', () => {
			expect(store.messageBody).toBe('');
		});

		it('should not be loading or sending', () => {
			expect(store.isLoadingMessages).toBe(false);
			expect(store.isLoadingOlderMessages).toBe(false);
			expect(store.isSending).toBe(false);
			expect(store.hasMoreMessages).toBe(false);
		});

		it('should have no pending attachments', () => {
			expect(store.pendingImage).toBeNull();
			expect(store.pendingImagePreview).toBeNull();
			expect(store.pendingFile).toBeNull();
		});

		it('should have no replyingTo', () => {
			expect(store.replyingTo).toBeNull();
		});

		it('should have search closed', () => {
			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.searchResults).toBeNull();
		});

		it('should have empty pinned messages', () => {
			expect(store.pinnedMessages).toEqual([]);
			expect(store.showPinnedPanel).toBe(false);
		});
	});

	// --- loadMessages ---

	describe('loadMessages', () => {
		it('should load messages from API', async () => {
			const msgs = [makeMessage('m-1', 'ch-1'), makeMessage('m-2', 'ch-1')];
			mockApi.getMessages.mockResolvedValue({ messages: msgs, hasMore: true });

			await store.loadMessages('ch-1');

			expect(mockApi.getMessages).toHaveBeenCalledWith('test-token', 'ch-1', { limit: 100 });
			expect(store.messages).toEqual(msgs);
			expect(store.hasMoreMessages).toBe(true);
			expect(store.isLoadingMessages).toBe(false);
		});

		it('should call setError on API failure', async () => {
			const err = new Error('network');
			mockApi.getMessages.mockRejectedValue(err);

			await store.loadMessages('ch-1');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isLoadingMessages).toBe(false);
		});

		it('should not call API when idToken is missing', async () => {
			mockAuth.idToken = null;
			store = new MessageStore(mockAuth as any, mockChannels as any, mockApi as any, mockUi as any, mockHub as any);

			await store.loadMessages('ch-1');

			expect(mockApi.getMessages).not.toHaveBeenCalled();
		});
	});

	// --- loadOlderMessages ---

	describe('loadOlderMessages', () => {
		it('should prepend older messages', async () => {
			store.messages = [makeMessage('m-2', 'ch-1')];
			store.hasMoreMessages = true;
			const olderMsgs = [makeMessage('m-1', 'ch-1')];
			mockApi.getMessages.mockResolvedValue({ messages: olderMsgs, hasMore: false });

			await store.loadOlderMessages();

			expect(store.messages).toHaveLength(2);
			expect(store.messages[0].id).toBe('m-1');
			expect(store.messages[1].id).toBe('m-2');
			expect(store.hasMoreMessages).toBe(false);
		});

		it('should not load when hasMoreMessages is false', async () => {
			store.messages = [makeMessage('m-1', 'ch-1')];
			store.hasMoreMessages = false;

			await store.loadOlderMessages();

			expect(mockApi.getMessages).not.toHaveBeenCalled();
		});

		it('should not load when no messages exist', async () => {
			store.hasMoreMessages = true;

			await store.loadOlderMessages();

			expect(mockApi.getMessages).not.toHaveBeenCalled();
		});

		it('should not load when selectedChannelId is null', async () => {
			mockChannels.selectedChannelId = null;
			store.messages = [makeMessage('m-1', 'ch-1')];
			store.hasMoreMessages = true;

			await store.loadOlderMessages();

			expect(mockApi.getMessages).not.toHaveBeenCalled();
		});
	});

	// --- sendMessage ---

	describe('sendMessage', () => {
		it('should send a text message', async () => {
			store.messageBody = 'Hello world';

			await store.sendMessage();

			expect(mockApi.sendMessage).toHaveBeenCalledWith(
				'test-token', 'ch-1', 'Hello world', null, null, null
			);
			expect(store.messageBody).toBe('');
			expect(store.isSending).toBe(false);
		});

		it('should set error when body, image, and file are all empty', async () => {
			store.messageBody = '';

			await store.sendMessage();

			expect(mockUi.error).toBe('Message body, image, or file is required.');
			expect(mockApi.sendMessage).not.toHaveBeenCalled();
		});

		it('should not send when idToken is missing', async () => {
			mockAuth.idToken = null;
			store = new MessageStore(mockAuth as any, mockChannels as any, mockApi as any, mockUi as any, mockHub as any);
			store.messageBody = 'test';

			await store.sendMessage();

			expect(mockApi.sendMessage).not.toHaveBeenCalled();
		});

		it('should not send when selectedChannelId is null', async () => {
			mockChannels.selectedChannelId = null;
			store.messageBody = 'test';

			await store.sendMessage();

			expect(mockApi.sendMessage).not.toHaveBeenCalled();
		});

		it('should call setError on API failure', async () => {
			store.messageBody = 'test';
			const err = new Error('send failed');
			mockApi.sendMessage.mockRejectedValue(err);

			await store.sendMessage();

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isSending).toBe(false);
		});

		it('should clear typing after send', async () => {
			store.messageBody = 'test';

			await store.sendMessage();

			expect(mockHub.clearTyping).toHaveBeenCalledWith('ch-1', 'TestUser');
		});

		it('should fall back to loadMessages when hub is disconnected', async () => {
			store.messageBody = 'test';
			mockHub.isConnected = false;

			await store.sendMessage();

			expect(mockApi.getMessages).toHaveBeenCalledWith('test-token', 'ch-1', { limit: 100 });
		});

		it('should include replyToMessageId when replying', async () => {
			store.messageBody = 'reply text';
			store.replyingTo = { messageId: 'm-99', authorName: 'Bob', bodyPreview: 'hi', context: 'channel' };

			await store.sendMessage();

			expect(mockApi.sendMessage).toHaveBeenCalledWith(
				'test-token', 'ch-1', 'reply text', null, 'm-99', null
			);
			expect(store.replyingTo).toBeNull();
		});

		it('should clear pendingMentions after send', async () => {
			store.messageBody = 'test';
			store.pendingMentions = new Map([['Alice', 'user-2']]);

			await store.sendMessage();

			expect(store.pendingMentions.size).toBe(0);
		});
	});

	// --- resolveMentions ---

	describe('resolveMentions', () => {
		it('should convert @here to wire token', () => {
			const result = store.resolveMentions('hello @here');
			expect(result).toBe('hello <@here>');
		});

		it('should convert user mentions to wire tokens', () => {
			store.pendingMentions = new Map([['Alice', 'user-2']]);
			const result = store.resolveMentions('hey @Alice check this');
			expect(result).toBe('hey <@user-2> check this');
		});

		it('should handle multiple @here occurrences', () => {
			const result = store.resolveMentions('@here and @here');
			expect(result).toBe('<@here> and <@here>');
		});
	});

	// --- sendGifMessage ---

	describe('sendGifMessage', () => {
		it('should send GIF URL as image', async () => {
			await store.sendGifMessage('https://giphy.com/test.gif');

			expect(mockApi.sendMessage).toHaveBeenCalledWith(
				'test-token', 'ch-1', '', 'https://giphy.com/test.gif', null, null
			);
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.sendMessage.mockRejectedValue(err);

			await store.sendGifMessage('https://giphy.com/test.gif');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isSending).toBe(false);
		});
	});

	// --- Image Attachments ---

	describe('attachImage', () => {
		it('should reject unsupported image types', () => {
			const file = new File(['data'], 'test.bmp', { type: 'image/bmp' });
			store.attachImage(file);

			expect(mockUi.error).toBe('Unsupported image type. Allowed: JPG, PNG, WebP, GIF.');
			expect(store.pendingImage).toBeNull();
		});

		it('should reject images over 10 MB', () => {
			const file = new File([new ArrayBuffer(11 * 1024 * 1024)], 'big.png', { type: 'image/png' });
			store.attachImage(file);

			expect(mockUi.error).toBe('Image must be under 10 MB.');
		});

		it('should accept valid images', () => {
			const file = new File(['data'], 'test.png', { type: 'image/png' });
			store.attachImage(file);

			expect(store.pendingImage).toBe(file);
			expect(store.pendingImagePreview).toBeTruthy();
		});
	});

	// --- File Attachments ---

	describe('attachFile', () => {
		it('should reject unsupported file types', () => {
			const file = new File(['data'], 'test.exe', { type: 'application/octet-stream' });
			store.attachFile(file);

			expect(mockUi.error).toBe('Unsupported file type.');
			expect(store.pendingFile).toBeNull();
		});

		it('should reject files over 25 MB', () => {
			const file = new File([new ArrayBuffer(26 * 1024 * 1024)], 'big.pdf', { type: 'application/pdf' });
			store.attachFile(file);

			expect(mockUi.error).toBe('File must be under 25 MB.');
		});

		it('should accept valid files', () => {
			const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });
			store.attachFile(file);

			expect(store.pendingFile).toBe(file);
		});
	});

	// --- Reply ---

	describe('startReply / cancelReply', () => {
		it('should set replyingTo with channel context', () => {
			store.startReply('m-1', 'Alice', 'hello...');

			expect(store.replyingTo).toEqual({
				messageId: 'm-1',
				authorName: 'Alice',
				bodyPreview: 'hello...',
				context: 'channel'
			});
		});

		it('should clear replyingTo on cancel', () => {
			store.startReply('m-1', 'Alice', 'hello...');
			store.cancelReply();

			expect(store.replyingTo).toBeNull();
		});
	});

	// --- editMessage ---

	describe('editMessage', () => {
		it('should call API to edit message', async () => {
			await store.editMessage('m-1', 'edited body');

			expect(mockApi.editMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'm-1', 'edited body');
		});

		it('should locally update message when hub disconnected', async () => {
			mockHub.isConnected = false;
			store.messages = [makeMessage('m-1', 'ch-1', 'original')];

			await store.editMessage('m-1', 'edited');

			expect(store.messages[0].body).toBe('edited');
			expect(store.messages[0].editedAt).toBeTruthy();
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.editMessage.mockRejectedValue(err);

			await store.editMessage('m-1', 'edited');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
		});
	});

	// --- deleteMessage ---

	describe('deleteMessage', () => {
		it('should call API to delete message', async () => {
			await store.deleteMessage('m-1');

			expect(mockApi.deleteMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'm-1');
		});

		it('should locally remove message when hub disconnected', async () => {
			mockHub.isConnected = false;
			store.messages = [makeMessage('m-1', 'ch-1'), makeMessage('m-2', 'ch-1')];

			await store.deleteMessage('m-1');

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('m-2');
		});

		it('should also remove from pinned messages when hub disconnected', async () => {
			mockHub.isConnected = false;
			store.messages = [makeMessage('m-1', 'ch-1')];
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1')];

			await store.deleteMessage('m-1');

			expect(store.pinnedMessages).toHaveLength(0);
		});
	});

	// --- Pinned Messages ---

	describe('loadPinnedMessages', () => {
		it('should load pinned messages for selected channel', async () => {
			const pinned = [makePinnedMessage('m-1', 'ch-1')];
			mockApi.getPinnedMessages.mockResolvedValue(pinned);

			await store.loadPinnedMessages();

			expect(mockApi.getPinnedMessages).toHaveBeenCalledWith('test-token', 'ch-1');
			expect(store.pinnedMessages).toEqual(pinned);
		});

		it('should load for a specific channel', async () => {
			const pinned = [makePinnedMessage('m-1', 'ch-2')];
			mockApi.getPinnedMessages.mockResolvedValue(pinned);

			await store.loadPinnedMessages('ch-2');

			expect(mockApi.getPinnedMessages).toHaveBeenCalledWith('test-token', 'ch-2');
		});

		it('should set empty array on failure', async () => {
			mockApi.getPinnedMessages.mockRejectedValue(new Error('fail'));

			await store.loadPinnedMessages();

			expect(store.pinnedMessages).toEqual([]);
		});
	});

	describe('pinMessage', () => {
		it('should call API to pin message', async () => {
			await store.pinMessage('m-1');

			expect(mockApi.pinMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'm-1');
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.pinMessage.mockRejectedValue(err);

			await store.pinMessage('m-1');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
		});
	});

	describe('unpinMessage', () => {
		it('should call API and remove from local list', async () => {
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1'), makePinnedMessage('m-2', 'ch-1')];

			await store.unpinMessage('m-1');

			expect(mockApi.unpinMessage).toHaveBeenCalledWith('test-token', 'ch-1', 'm-1');
			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('m-2');
		});
	});

	describe('togglePinnedPanel', () => {
		it('should toggle showPinnedPanel', () => {
			expect(store.showPinnedPanel).toBe(false);
			store.togglePinnedPanel();
			expect(store.showPinnedPanel).toBe(true);
		});

		it('should load pinned messages when opening', () => {
			mockApi.getPinnedMessages.mockResolvedValue([]);
			store.togglePinnedPanel();

			expect(mockApi.getPinnedMessages).toHaveBeenCalled();
		});
	});

	// --- Purge ---

	describe('purgeChannel', () => {
		it('should call API to purge', async () => {
			await store.purgeChannel('ch-1');

			expect(mockApi.purgeChannel).toHaveBeenCalledWith('test-token', 'ch-1');
			expect(store.isPurgingChannel).toBe(false);
		});

		it('should clear messages locally when hub disconnected and channel is selected', async () => {
			mockHub.isConnected = false;
			store.messages = [makeMessage('m-1', 'ch-1')];

			await store.purgeChannel('ch-1');

			expect(store.messages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.purgeChannel.mockRejectedValue(err);

			await store.purgeChannel('ch-1');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isPurgingChannel).toBe(false);
		});
	});

	// --- Search ---

	describe('toggleSearch', () => {
		it('should open search', () => {
			store.toggleSearch();
			expect(store.isSearchOpen).toBe(true);
		});

		it('should clear search state when closing', () => {
			store.isSearchOpen = true;
			store.searchQuery = 'test';
			store.searchResults = { messages: [], totalCount: 0, page: 1, pageSize: 25 } as any;

			store.toggleSearch();

			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.searchResults).toBeNull();
		});
	});

	describe('searchMessages', () => {
		it('should search server messages when server is selected', async () => {
			store.getSelectedServerId = () => 'srv-1';
			const results = { messages: [], totalCount: 0, page: 1, pageSize: 25 };
			mockApi.searchServerMessages.mockResolvedValue(results);

			await store.searchMessages('hello');

			expect(mockApi.searchServerMessages).toHaveBeenCalledWith('test-token', 'srv-1', 'hello', {});
			expect(store.searchResults).toEqual(results);
			expect(store.isSearching).toBe(false);
		});

		it('should search DM messages when DM channel is active', async () => {
			store.getSelectedServerId = () => null;
			store.getActiveDmChannelId = () => 'dm-1';
			const results = { messages: [], totalCount: 0, page: 1, pageSize: 25 };
			mockApi.searchDmMessages.mockResolvedValue(results);

			await store.searchMessages('hello');

			expect(mockApi.searchDmMessages).toHaveBeenCalledWith('test-token', 'hello', {});
		});

		it('should clear results for queries shorter than 2 chars', async () => {
			await store.searchMessages('a');

			expect(store.searchResults).toBeNull();
			expect(mockApi.searchServerMessages).not.toHaveBeenCalled();
		});

		it('should call setError on failure', async () => {
			store.getSelectedServerId = () => 'srv-1';
			const err = new Error('fail');
			mockApi.searchServerMessages.mockRejectedValue(err);

			await store.searchMessages('hello world');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isSearching).toBe(false);
		});
	});

	// --- SignalR Handlers ---

	describe('handleIncomingMessage', () => {
		it('should append message for active channel', () => {
			const msg = makeMessage('m-new', 'ch-1', 'new msg');

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('m-new');
		});

		it('should not duplicate existing messages', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];
			const msg = makeMessage('m-1', 'ch-1');

			store.handleIncomingMessage(msg);

			expect(store.messages).toHaveLength(1);
		});

		it('should not append messages for other channels', () => {
			const msg = makeMessage('m-1', 'ch-other');

			store.handleIncomingMessage(msg);

			expect(store.messages).toEqual([]);
		});
	});

	describe('handleTyping / handleStoppedTyping', () => {
		it('should add user to typing list', () => {
			store.handleTyping('ch-1', 'Alice');

			expect(store.typingUsers).toEqual(['Alice']);
		});

		it('should not duplicate typing users', () => {
			store.handleTyping('ch-1', 'Alice');
			store.handleTyping('ch-1', 'Alice');

			expect(store.typingUsers).toEqual(['Alice']);
		});

		it('should ignore typing from other channels', () => {
			store.handleTyping('ch-other', 'Alice');

			expect(store.typingUsers).toEqual([]);
		});

		it('should remove user on stopped typing', () => {
			store.handleTyping('ch-1', 'Alice');
			store.handleStoppedTyping('ch-1', 'Alice');

			expect(store.typingUsers).toEqual([]);
		});

		it('should ignore stopped typing from other channels', () => {
			store.handleTyping('ch-1', 'Alice');
			store.handleStoppedTyping('ch-other', 'Alice');

			expect(store.typingUsers).toEqual(['Alice']);
		});
	});

	describe('handleMessageDeleted', () => {
		it('should remove message from active channel', () => {
			store.messages = [makeMessage('m-1', 'ch-1'), makeMessage('m-2', 'ch-1')];

			store.handleMessageDeleted({ channelId: 'ch-1', messageId: 'm-1' });

			expect(store.messages).toHaveLength(1);
			expect(store.messages[0].id).toBe('m-2');
		});

		it('should also remove from pinned messages', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1')];

			store.handleMessageDeleted({ channelId: 'ch-1', messageId: 'm-1' });

			expect(store.pinnedMessages).toHaveLength(0);
		});

		it('should not remove messages from other channels', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];

			store.handleMessageDeleted({ channelId: 'ch-other', messageId: 'm-1' });

			expect(store.messages).toHaveLength(1);
		});
	});

	describe('handleChannelPurged', () => {
		it('should clear all messages for active channel', () => {
			store.messages = [makeMessage('m-1', 'ch-1'), makeMessage('m-2', 'ch-1')];
			store.hasMoreMessages = true;
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1')];

			store.handleChannelPurged({ channelId: 'ch-1' });

			expect(store.messages).toEqual([]);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.pinnedMessages).toEqual([]);
		});

		it('should not clear for other channels', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];

			store.handleChannelPurged({ channelId: 'ch-other' });

			expect(store.messages).toHaveLength(1);
		});
	});

	describe('handleMessageEdited', () => {
		it('should update message body and editedAt', () => {
			store.messages = [makeMessage('m-1', 'ch-1', 'original')];

			store.handleMessageEdited({
				channelId: 'ch-1',
				messageId: 'm-1',
				body: 'edited',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.messages[0].body).toBe('edited');
			expect(store.messages[0].editedAt).toBe('2026-01-02T00:00:00Z');
		});

		it('should not update for other channels', () => {
			store.messages = [makeMessage('m-1', 'ch-1', 'original')];

			store.handleMessageEdited({
				channelId: 'ch-other',
				messageId: 'm-1',
				body: 'edited',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.messages[0].body).toBe('original');
		});
	});

	describe('handleMessagePinned', () => {
		it('should add to pinned list when message exists locally', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];

			store.handleMessagePinned({
				channelId: 'ch-1',
				messageId: 'm-1',
				pinnedBy: 'user-1',
				pinnedAt: '2026-01-01T00:00:00Z'
			});

			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('m-1');
		});

		it('should not add for other channels', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];

			store.handleMessagePinned({
				channelId: 'ch-other',
				messageId: 'm-1',
				pinnedBy: 'user-1',
				pinnedAt: '2026-01-01T00:00:00Z'
			});

			expect(store.pinnedMessages).toHaveLength(0);
		});
	});

	describe('handleMessageUnpinned', () => {
		it('should remove from pinned list', () => {
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1'), makePinnedMessage('m-2', 'ch-1')];

			store.handleMessageUnpinned({ channelId: 'ch-1', messageId: 'm-1' });

			expect(store.pinnedMessages).toHaveLength(1);
			expect(store.pinnedMessages[0].messageId).toBe('m-2');
		});
	});

	describe('patchLinkPreviews', () => {
		it('should add link previews to matching message', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];
			const previews = [{ url: 'https://example.com', title: 'Example', description: null, imageUrl: null }];

			store.patchLinkPreviews('m-1', previews as any);

			expect(store.messages[0].linkPreviews).toEqual(previews);
		});
	});

	// --- handleComposerInput ---

	describe('handleComposerInput', () => {
		it('should emit typing when channel and auth exist', () => {
			store.handleComposerInput();

			expect(mockHub.emitTyping).toHaveBeenCalledWith('ch-1', 'TestUser');
		});

		it('should not emit when no channel selected', () => {
			mockChannels.selectedChannelId = null;

			store.handleComposerInput();

			expect(mockHub.emitTyping).not.toHaveBeenCalled();
		});
	});

	// --- Reset ---

	describe('reset', () => {
		it('should clear all state', () => {
			store.messages = [makeMessage('m-1', 'ch-1')];
			store.messageBody = 'draft';
			store.typingUsers = ['Alice'];
			store.isSearchOpen = true;
			store.searchQuery = 'test';
			store.pinnedMessages = [makePinnedMessage('m-1', 'ch-1')];
			store.showPinnedPanel = true;
			store.isSending = true;

			store.reset();

			expect(store.messages).toEqual([]);
			expect(store.messageBody).toBe('');
			expect(store.typingUsers).toEqual([]);
			expect(store.isSearchOpen).toBe(false);
			expect(store.searchQuery).toBe('');
			expect(store.searchResults).toBeNull();
			expect(store.pinnedMessages).toEqual([]);
			expect(store.showPinnedPanel).toBe(false);
			expect(store.isSending).toBe(false);
			expect(store.isLoadingMessages).toBe(false);
			expect(store.hasMoreMessages).toBe(false);
			expect(store.pendingImage).toBeNull();
			expect(store.pendingFile).toBeNull();
			expect(store.replyingTo).toBeNull();
			expect(store.isPurgingChannel).toBe(false);
			expect(store.highlightedMessageId).toBeNull();
		});
	});
});
