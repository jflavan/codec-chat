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

import { DmStore } from './dm-store.svelte';
import type { DmConversation, DirectMessage } from '$lib/types/index.js';

function makeMockAuth(overrides: Record<string, unknown> = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1', displayName: 'TestUser' } },
		effectiveDisplayName: 'TestUser',
		...overrides
	};
}

function makeMockApi() {
	return {
		getDmConversations: vi.fn().mockResolvedValue([]),
		getDmPresence: vi.fn().mockResolvedValue([]),
		getDmMessages: vi.fn().mockResolvedValue({ messages: [] }),
		sendDm: vi.fn().mockResolvedValue({}),
		createOrResumeDm: vi.fn().mockResolvedValue({ id: 'dm-1' }),
		closeDmConversation: vi.fn().mockResolvedValue(undefined),
		deleteDmMessage: vi.fn().mockResolvedValue(undefined),
		editDmMessage: vi.fn().mockResolvedValue(undefined),
		toggleDmReaction: vi.fn().mockResolvedValue({ reactions: [] }),
		uploadImage: vi.fn().mockResolvedValue({ imageUrl: 'http://img.test/1.png' }),
		uploadFile: vi.fn().mockResolvedValue({
			fileUrl: 'http://file.test/1.pdf',
			fileName: 'test.pdf',
			fileSize: 1024,
			fileContentType: 'application/pdf'
		})
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
		joinDmChannel: vi.fn().mockResolvedValue(undefined),
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
		emitDmTyping: vi.fn(),
		clearDmTyping: vi.fn(),
		isConnected: true
	};
}

function makeDmConversation(id: string, participantId = 'p-1'): DmConversation {
	return {
		id,
		participant: {
			id: participantId,
			displayName: 'Friend',
			avatarUrl: null,
			nickname: null
		},
		lastMessage: null,
		lastMessageAt: null,
		createdAt: '2026-01-01T00:00:00Z'
	} as DmConversation;
}

function makeDmMessage(id: string, dmChannelId: string, body = 'hello'): DirectMessage {
	return {
		id,
		dmChannelId,
		senderId: 'user-2',
		senderDisplayName: 'Friend',
		senderAvatarUrl: null,
		body,
		imageUrl: null,
		createdAt: '2026-01-01T00:00:00Z',
		editedAt: null,
		reactions: [],
		linkPreviews: [],
		replyContext: null,
		replyToDirectMessageId: null,
		fileUrl: null,
		fileName: null,
		fileSize: null,
		fileContentType: null,
		senderNickname: null
	} as unknown as DirectMessage;
}

describe('DmStore', () => {
	let store: DmStore;
	let mockAuth: ReturnType<typeof makeMockAuth>;
	let mockApi: ReturnType<typeof makeMockApi>;
	let mockUi: ReturnType<typeof makeMockUi>;
	let mockHub: ReturnType<typeof makeMockHub>;

	beforeEach(() => {
		vi.clearAllMocks();
		mockAuth = makeMockAuth();
		mockApi = makeMockApi();
		mockUi = makeMockUi();
		mockHub = makeMockHub();
		store = new DmStore(mockAuth as any, mockApi as any, mockUi as any, mockHub as any);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have empty dmConversations', () => {
			expect(store.dmConversations).toEqual([]);
		});

		it('should have empty dmMessages', () => {
			expect(store.dmMessages).toEqual([]);
		});

		it('should have null activeDmChannelId', () => {
			expect(store.activeDmChannelId).toBeNull();
		});

		it('should have empty dmTypingUsers', () => {
			expect(store.dmTypingUsers).toEqual([]);
		});

		it('should have empty unreadDmCounts', () => {
			expect(store.unreadDmCounts.size).toBe(0);
		});

		it('should have empty dmMessageBody', () => {
			expect(store.dmMessageBody).toBe('');
		});

		it('should not be loading', () => {
			expect(store.isLoadingDmConversations).toBe(false);
			expect(store.isLoadingDmMessages).toBe(false);
			expect(store.isSendingDm).toBe(false);
		});

		it('should have no pending attachments', () => {
			expect(store.pendingDmImage).toBeNull();
			expect(store.pendingDmImagePreview).toBeNull();
			expect(store.pendingDmFile).toBeNull();
		});

		it('should have null replyingTo', () => {
			expect(store.replyingTo).toBeNull();
		});
	});

	// --- loadDmConversations ---

	describe('loadDmConversations', () => {
		it('should load conversations from API', async () => {
			const convos = [makeDmConversation('dm-1'), makeDmConversation('dm-2')];
			mockApi.getDmConversations.mockResolvedValue(convos);

			await store.loadDmConversations();

			expect(mockApi.getDmConversations).toHaveBeenCalledWith('test-token');
			expect(store.dmConversations).toEqual(convos);
			expect(store.isLoadingDmConversations).toBe(false);
		});

		it('should call setError on API failure', async () => {
			const err = new Error('network');
			mockApi.getDmConversations.mockRejectedValue(err);

			await store.loadDmConversations();

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isLoadingDmConversations).toBe(false);
		});

		it('should not call API when idToken is missing', async () => {
			mockAuth.idToken = null;
			store = new DmStore(mockAuth as any, mockApi as any, mockUi as any, mockHub as any);

			await store.loadDmConversations();

			expect(mockApi.getDmConversations).not.toHaveBeenCalled();
		});

		it('should load DM presence after conversations', async () => {
			mockApi.getDmConversations.mockResolvedValue([]);
			mockApi.getDmPresence.mockResolvedValue([
				{ userId: 'u-1', status: 'online' }
			]);

			await store.loadDmConversations();

			expect(mockApi.getDmPresence).toHaveBeenCalledWith('test-token');
		});
	});

	// --- selectDmConversation ---

	describe('selectDmConversation', () => {
		it('should set activeDmChannelId and load messages', async () => {
			const msgs = [makeDmMessage('m-1', 'dm-1')];
			mockApi.getDmMessages.mockResolvedValue({ messages: msgs });

			await store.selectDmConversation('dm-1');

			expect(store.activeDmChannelId).toBe('dm-1');
			expect(mockHub.joinDmChannel).toHaveBeenCalledWith('dm-1');
			expect(store.dmMessages).toEqual(msgs);
		});

		it('should leave previous DM channel', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });

			await store.selectDmConversation('dm-1');
			await store.selectDmConversation('dm-2');

			expect(mockHub.leaveDmChannel).toHaveBeenCalledWith('dm-1');
			expect(mockHub.joinDmChannel).toHaveBeenCalledWith('dm-2');
		});

		it('should clear unread count for selected conversation', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			store.unreadDmCounts = new Map([['dm-1', 3]]);

			await store.selectDmConversation('dm-1');

			expect(store.unreadDmCounts.has('dm-1')).toBe(false);
		});

		it('should reset typing users and message body', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			store.dmTypingUsers = ['Alice'];
			store.dmMessageBody = 'draft';

			await store.selectDmConversation('dm-1');

			expect(store.dmTypingUsers).toEqual([]);
			expect(store.dmMessageBody).toBe('');
		});

		it('should clear replyingTo', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			store.replyingTo = { messageId: 'm-1', authorName: 'A', bodyPreview: 'x', context: 'dm' };

			await store.selectDmConversation('dm-1');

			expect(store.replyingTo).toBeNull();
		});
	});

	// --- sendDmMessage ---

	describe('sendDmMessage', () => {
		beforeEach(async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			vi.clearAllMocks();
		});

		it('should send a text message', async () => {
			store.dmMessageBody = 'Hello there';

			await store.sendDmMessage();

			expect(mockApi.sendDm).toHaveBeenCalledWith(
				'test-token', 'dm-1', 'Hello there', null, null, null
			);
			expect(store.dmMessageBody).toBe('');
			expect(store.isSendingDm).toBe(false);
		});

		it('should set error when body, image, and file are all empty', async () => {
			store.dmMessageBody = '';

			await store.sendDmMessage();

			expect(mockUi.error).toBe('Message body, image, or file is required.');
			expect(mockApi.sendDm).not.toHaveBeenCalled();
		});

		it('should not send when idToken is missing', async () => {
			mockAuth.idToken = null;
			store = new DmStore(mockAuth as any, mockApi as any, mockUi as any, mockHub as any);
			store.dmMessageBody = 'test';

			await store.sendDmMessage();

			expect(mockApi.sendDm).not.toHaveBeenCalled();
		});

		it('should call setError on API failure', async () => {
			store.dmMessageBody = 'test';
			const err = new Error('send failed');
			mockApi.sendDm.mockRejectedValue(err);

			await store.sendDmMessage();

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isSendingDm).toBe(false);
		});

		it('should clear DM typing after send', async () => {
			store.dmMessageBody = 'test';

			await store.sendDmMessage();

			expect(mockHub.clearDmTyping).toHaveBeenCalledWith('dm-1', 'TestUser');
		});

		it('should fall back to loadDmMessages when hub is disconnected', async () => {
			store.dmMessageBody = 'test';
			mockHub.isConnected = false;

			await store.sendDmMessage();

			expect(mockApi.getDmMessages).toHaveBeenCalledWith('test-token', 'dm-1');
		});

		it('should include replyToDirectMessageId when replying', async () => {
			store.dmMessageBody = 'reply text';
			store.replyingTo = { messageId: 'm-99', authorName: 'Bob', bodyPreview: 'hi', context: 'dm' };

			await store.sendDmMessage();

			expect(mockApi.sendDm).toHaveBeenCalledWith(
				'test-token', 'dm-1', 'reply text', null, 'm-99', null
			);
			expect(store.replyingTo).toBeNull();
		});
	});

	// --- sendDmGifMessage ---

	describe('sendDmGifMessage', () => {
		beforeEach(async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			vi.clearAllMocks();
		});

		it('should send GIF URL as image', async () => {
			await store.sendDmGifMessage('https://giphy.com/test.gif');

			expect(mockApi.sendDm).toHaveBeenCalledWith(
				'test-token', 'dm-1', '', 'https://giphy.com/test.gif', null, null
			);
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.sendDm.mockRejectedValue(err);

			await store.sendDmGifMessage('https://giphy.com/test.gif');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
			expect(store.isSendingDm).toBe(false);
		});
	});

	// --- openDmWithUser ---

	describe('openDmWithUser', () => {
		it('should create or resume DM then select it', async () => {
			mockApi.createOrResumeDm.mockResolvedValue({ id: 'dm-new' });
			mockApi.getDmConversations.mockResolvedValue([makeDmConversation('dm-new')]);
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });

			await store.openDmWithUser('user-2');

			expect(mockApi.createOrResumeDm).toHaveBeenCalledWith('test-token', 'user-2');
			expect(store.activeDmChannelId).toBe('dm-new');
		});

		it('should call setError on failure', async () => {
			const err = new Error('fail');
			mockApi.createOrResumeDm.mockRejectedValue(err);

			await store.openDmWithUser('user-2');

			expect(mockUi.setError).toHaveBeenCalledWith(err);
		});
	});

	// --- closeDmConversation ---

	describe('closeDmConversation', () => {
		it('should remove conversation from list', async () => {
			store.dmConversations = [makeDmConversation('dm-1'), makeDmConversation('dm-2')];

			await store.closeDmConversation('dm-1');

			expect(store.dmConversations).toHaveLength(1);
			expect(store.dmConversations[0].id).toBe('dm-2');
		});

		it('should clear active state when closing active conversation', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			store.dmConversations = [makeDmConversation('dm-1')];
			await store.selectDmConversation('dm-1');

			await store.closeDmConversation('dm-1');

			expect(store.activeDmChannelId).toBeNull();
			expect(store.dmMessages).toEqual([]);
			expect(mockHub.leaveDmChannel).toHaveBeenCalledWith('dm-1');
		});
	});

	// --- deleteDmMessage ---

	describe('deleteDmMessage', () => {
		beforeEach(async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			vi.clearAllMocks();
		});

		it('should call API to delete message', async () => {
			await store.deleteDmMessage('m-1');

			expect(mockApi.deleteDmMessage).toHaveBeenCalledWith('test-token', 'dm-1', 'm-1');
		});

		it('should locally remove message when hub disconnected', async () => {
			mockHub.isConnected = false;
			store.dmMessages = [makeDmMessage('m-1', 'dm-1'), makeDmMessage('m-2', 'dm-1')];

			await store.deleteDmMessage('m-1');

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('m-2');
		});
	});

	// --- editDmMessage ---

	describe('editDmMessage', () => {
		beforeEach(async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			vi.clearAllMocks();
		});

		it('should call API to edit message', async () => {
			await store.editDmMessage('m-1', 'edited body');

			expect(mockApi.editDmMessage).toHaveBeenCalledWith('test-token', 'dm-1', 'm-1', 'edited body');
		});

		it('should locally update message when hub disconnected', async () => {
			mockHub.isConnected = false;
			store.dmMessages = [makeDmMessage('m-1', 'dm-1', 'original')];

			await store.editDmMessage('m-1', 'edited');

			expect(store.dmMessages[0].body).toBe('edited');
			expect(store.dmMessages[0].editedAt).toBeTruthy();
		});
	});

	// --- File/Image Attachments ---

	describe('attachDmImage', () => {
		it('should reject unsupported image types', () => {
			const file = new File(['data'], 'test.bmp', { type: 'image/bmp' });
			store.attachDmImage(file);

			expect(mockUi.error).toBe('Unsupported image type. Allowed: JPG, PNG, WebP, GIF.');
			expect(store.pendingDmImage).toBeNull();
		});

		it('should reject images over 10 MB', () => {
			const file = new File([new ArrayBuffer(11 * 1024 * 1024)], 'big.png', { type: 'image/png' });
			store.attachDmImage(file);

			expect(mockUi.error).toBe('Image must be under 10 MB.');
		});

		it('should accept valid images', () => {
			const file = new File(['data'], 'test.png', { type: 'image/png' });
			store.attachDmImage(file);

			expect(store.pendingDmImage).toBe(file);
			expect(store.pendingDmImagePreview).toBeTruthy();
		});
	});

	describe('attachDmFile', () => {
		it('should reject unsupported file types', () => {
			const file = new File(['data'], 'test.exe', { type: 'application/octet-stream' });
			store.attachDmFile(file);

			expect(mockUi.error).toBe('Unsupported file type.');
			expect(store.pendingDmFile).toBeNull();
		});

		it('should reject files over 25 MB', () => {
			const file = new File([new ArrayBuffer(26 * 1024 * 1024)], 'big.pdf', { type: 'application/pdf' });
			store.attachDmFile(file);

			expect(mockUi.error).toBe('File must be under 25 MB.');
		});

		it('should accept valid files', () => {
			const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });
			store.attachDmFile(file);

			expect(store.pendingDmFile).toBe(file);
		});
	});

	// --- Replies ---

	describe('startReply / cancelReply', () => {
		it('should set replyingTo with dm context', () => {
			store.startReply('m-1', 'Alice', 'hello...');

			expect(store.replyingTo).toEqual({
				messageId: 'm-1',
				authorName: 'Alice',
				bodyPreview: 'hello...',
				context: 'dm'
			});
		});

		it('should clear replyingTo on cancel', () => {
			store.startReply('m-1', 'Alice', 'hello...');
			store.cancelReply();

			expect(store.replyingTo).toBeNull();
		});
	});

	// --- SignalR Handlers ---

	describe('handleIncomingDm', () => {
		it('should append message when viewing active DM', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			const msg = makeDmMessage('m-new', 'dm-1', 'new msg');

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('m-new');
		});

		it('should not duplicate existing messages', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [makeDmMessage('m-1', 'dm-1')] });
			await store.selectDmConversation('dm-1');
			const msg = makeDmMessage('m-1', 'dm-1');

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
		});

		it('should increment unread count for inactive DM', () => {
			const msg = makeDmMessage('m-1', 'dm-2');

			store.handleIncomingDm(msg, false);

			expect(store.unreadDmCounts.get('dm-2')).toBe(1);
		});

		it('should increment existing unread count', () => {
			store.unreadDmCounts = new Map([['dm-2', 2]]);
			const msg = makeDmMessage('m-1', 'dm-2');

			store.handleIncomingDm(msg, false);

			expect(store.unreadDmCounts.get('dm-2')).toBe(3);
		});
	});

	describe('handleDmTyping / handleDmStoppedTyping', () => {
		beforeEach(async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
		});

		it('should add user to typing list', () => {
			store.handleDmTyping('dm-1', 'Alice');

			expect(store.dmTypingUsers).toEqual(['Alice']);
		});

		it('should not duplicate typing users', () => {
			store.handleDmTyping('dm-1', 'Alice');
			store.handleDmTyping('dm-1', 'Alice');

			expect(store.dmTypingUsers).toEqual(['Alice']);
		});

		it('should ignore typing from other channels', () => {
			store.handleDmTyping('dm-other', 'Alice');

			expect(store.dmTypingUsers).toEqual([]);
		});

		it('should remove user on stopped typing', () => {
			store.handleDmTyping('dm-1', 'Alice');
			store.handleDmStoppedTyping('dm-1', 'Alice');

			expect(store.dmTypingUsers).toEqual([]);
		});
	});

	describe('handleDmMessageDeleted', () => {
		it('should remove message from active channel', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			store.dmMessages = [makeDmMessage('m-1', 'dm-1'), makeDmMessage('m-2', 'dm-1')];

			store.handleDmMessageDeleted({ dmChannelId: 'dm-1', messageId: 'm-1' });

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('m-2');
		});

		it('should not remove messages from other channels', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			store.dmMessages = [makeDmMessage('m-1', 'dm-1')];

			store.handleDmMessageDeleted({ dmChannelId: 'dm-other', messageId: 'm-1' });

			expect(store.dmMessages).toHaveLength(1);
		});
	});

	describe('handleDmMessageEdited', () => {
		it('should update message body and editedAt', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [] });
			await store.selectDmConversation('dm-1');
			store.dmMessages = [makeDmMessage('m-1', 'dm-1', 'original')];

			store.handleDmMessageEdited({
				dmChannelId: 'dm-1',
				messageId: 'm-1',
				body: 'edited',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.dmMessages[0].body).toBe('edited');
			expect(store.dmMessages[0].editedAt).toBe('2026-01-02T00:00:00Z');
		});
	});

	describe('patchDmLinkPreviews', () => {
		it('should add link previews to matching message', () => {
			store.dmMessages = [makeDmMessage('m-1', 'dm-1')];
			const previews = [{ url: 'https://example.com', title: 'Example', description: null, imageUrl: null }];

			store.patchDmLinkPreviews('m-1', previews as any);

			expect(store.dmMessages[0].linkPreviews).toEqual(previews);
		});
	});

	// --- Reset ---

	describe('reset', () => {
		it('should clear all state', async () => {
			mockApi.getDmMessages.mockResolvedValue({ messages: [makeDmMessage('m-1', 'dm-1')] });
			mockApi.getDmConversations.mockResolvedValue([makeDmConversation('dm-1')]);
			await store.loadDmConversations();
			await store.selectDmConversation('dm-1');
			store.dmMessageBody = 'draft';

			store.reset();

			expect(store.dmConversations).toEqual([]);
			expect(store.dmMessages).toEqual([]);
			expect(store.activeDmChannelId).toBeNull();
			expect(store.dmTypingUsers).toEqual([]);
			expect(store.unreadDmCounts.size).toBe(0);
			expect(store.dmMessageBody).toBe('');
			expect(store.isLoadingDmConversations).toBe(false);
			expect(store.isLoadingDmMessages).toBe(false);
			expect(store.isSendingDm).toBe(false);
			expect(store.pendingDmImage).toBeNull();
			expect(store.pendingDmFile).toBeNull();
			expect(store.replyingTo).toBeNull();
		});
	});
});
