import { describe, it, expect, beforeEach, vi } from 'vitest';
import { DmStore } from './dm-store.svelte.js';
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
		getDmConversations: vi.fn().mockResolvedValue([]),
		getDmMessages: vi.fn().mockResolvedValue({ messages: [] }),
		sendDm: vi.fn().mockResolvedValue(undefined),
		createOrResumeDm: vi.fn().mockResolvedValue({ id: 'dm-new' }),
		closeDmConversation: vi.fn().mockResolvedValue(undefined),
		deleteDmMessage: vi.fn().mockResolvedValue(undefined),
		editDmMessage: vi.fn().mockResolvedValue(undefined),
		toggleDmReaction: vi.fn().mockResolvedValue({ reactions: [] }),
		uploadImage: vi.fn().mockResolvedValue({ imageUrl: 'https://img.url' }),
		uploadFile: vi.fn().mockResolvedValue({ fileUrl: 'url', fileName: 'f', fileSize: 100, fileContentType: 'text/plain' }),
		getDmPresence: vi.fn().mockResolvedValue([])
	} as any;
}

function mockHub() {
	return {
		joinDmChannel: vi.fn().mockResolvedValue(undefined),
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
		emitDmTyping: vi.fn(),
		clearDmTyping: vi.fn(),
		isConnected: true
	} as any;
}

describe('DmStore', () => {
	let store: DmStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;
	let hub: ReturnType<typeof mockHub>;

	beforeEach(() => {
		auth = mockAuth();
		api = mockApi();
		ui = new UIStore();
		hub = mockHub();
		store = new DmStore(auth, api, ui, hub);
	});

	describe('initial state', () => {
		it('starts with empty state', () => {
			expect(store.dmConversations).toEqual([]);
			expect(store.dmMessages).toEqual([]);
			expect(store.activeDmChannelId).toBe(null);
			expect(store.dmTypingUsers).toEqual([]);
			expect(store.unreadDmCounts.size).toBe(0);
			expect(store.dmMessageBody).toBe('');
			expect(store.isLoadingDmConversations).toBe(false);
			expect(store.isLoadingDmMessages).toBe(false);
			expect(store.isSendingDm).toBe(false);
		});
	});

	describe('loadDmConversations', () => {
		it('loads conversations from API', async () => {
			const convos = [{ id: 'dm-1', participant: { userId: 'u2' } }];
			api.getDmConversations.mockResolvedValue(convos);

			await store.loadDmConversations();

			expect(api.getDmConversations).toHaveBeenCalledWith('test-token');
			expect(store.dmConversations).toEqual(convos);
		});

		it('does nothing when not authenticated', async () => {
			auth.idToken = null;
			await store.loadDmConversations();
			expect(api.getDmConversations).not.toHaveBeenCalled();
		});

		it('sets error on failure', async () => {
			api.getDmConversations.mockRejectedValue(new Error('Failed'));
			await store.loadDmConversations();
			expect(ui.error).toBe('Failed');
		});
	});

	describe('selectDmConversation', () => {
		it('selects conversation and loads messages', async () => {
			const msgs = { messages: [{ id: 'msg-1', body: 'Hello' }] };
			api.getDmMessages.mockResolvedValue(msgs);

			await store.selectDmConversation('dm-1');

			expect(store.activeDmChannelId).toBe('dm-1');
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-1');
			expect(store.dmMessages).toEqual(msgs.messages);
		});

		it('leaves previous DM channel', async () => {
			store.activeDmChannelId = 'dm-old';
			api.getDmMessages.mockResolvedValue({ messages: [] });

			await store.selectDmConversation('dm-new');

			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-old');
		});

		it('clears typing and reply state on switch', async () => {
			store.dmTypingUsers = ['Alice'];
			store.replyingTo = { messageId: 'm1', authorName: 'A', bodyPreview: 'B', context: 'dm' };
			api.getDmMessages.mockResolvedValue({ messages: [] });

			await store.selectDmConversation('dm-1');

			expect(store.dmTypingUsers).toEqual([]);
			expect(store.replyingTo).toBe(null);
		});

		it('clears unread count for selected conversation', async () => {
			store.unreadDmCounts = new Map([['dm-1', 3]]);
			api.getDmMessages.mockResolvedValue({ messages: [] });

			await store.selectDmConversation('dm-1');

			expect(store.unreadDmCounts.has('dm-1')).toBe(false);
		});
	});

	describe('sendDmMessage', () => {
		it('sends message and clears state', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessageBody = 'Hello!';

			await store.sendDmMessage();

			expect(api.sendDm).toHaveBeenCalledWith(
				'test-token', 'dm-1', 'Hello!', null, null, null
			);
			expect(store.dmMessageBody).toBe('');
		});

		it('does nothing without active channel', async () => {
			store.dmMessageBody = 'Hello';
			await store.sendDmMessage();
			expect(api.sendDm).not.toHaveBeenCalled();
		});

		it('shows error for empty message without attachments', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessageBody = '';

			await store.sendDmMessage();

			expect(ui.error).toBe('Message body, image, or file is required.');
		});

		it('clears reply state after sending', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessageBody = 'reply text';
			store.replyingTo = { messageId: 'msg-1', authorName: 'Alice', bodyPreview: 'hi', context: 'dm' };

			await store.sendDmMessage();

			expect(store.replyingTo).toBe(null);
		});
	});

	describe('closeDmConversation', () => {
		it('removes conversation from list', async () => {
			store.dmConversations = [
				{ id: 'dm-1' },
				{ id: 'dm-2' }
			] as any;

			await store.closeDmConversation('dm-1');

			expect(api.closeDmConversation).toHaveBeenCalledWith('test-token', 'dm-1');
			expect(store.dmConversations).toHaveLength(1);
			expect(store.dmConversations[0].id).toBe('dm-2');
		});

		it('clears active state if closing active conversation', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [{ id: 'msg-1' }] as any;
			store.dmConversations = [{ id: 'dm-1' }] as any;

			await store.closeDmConversation('dm-1');

			expect(store.activeDmChannelId).toBe(null);
			expect(store.dmMessages).toEqual([]);
			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-1');
		});
	});

	describe('deleteDmMessage', () => {
		it('calls API to delete message', async () => {
			store.activeDmChannelId = 'dm-1';

			await store.deleteDmMessage('msg-1');

			expect(api.deleteDmMessage).toHaveBeenCalledWith('test-token', 'dm-1', 'msg-1');
		});

		it('falls back to local removal when disconnected', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [
				{ id: 'msg-1', body: 'hi' },
				{ id: 'msg-2', body: 'bye' }
			] as any;
			hub.isConnected = false;

			await store.deleteDmMessage('msg-1');

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('msg-2');
		});
	});

	describe('editDmMessage', () => {
		it('calls API to edit message', async () => {
			store.activeDmChannelId = 'dm-1';

			await store.editDmMessage('msg-1', 'new body');

			expect(api.editDmMessage).toHaveBeenCalledWith('test-token', 'dm-1', 'msg-1', 'new body');
		});

		it('falls back to local update when disconnected', async () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [{ id: 'msg-1', body: 'old' }] as any;
			hub.isConnected = false;

			await store.editDmMessage('msg-1', 'new');

			expect(store.dmMessages[0].body).toBe('new');
			expect(store.dmMessages[0].editedAt).toBeDefined();
		});
	});

	describe('reply management', () => {
		it('startReply sets reply context', () => {
			store.startReply('msg-1', 'Alice', 'Hello...');

			expect(store.replyingTo).toEqual({
				messageId: 'msg-1',
				authorName: 'Alice',
				bodyPreview: 'Hello...',
				context: 'dm'
			});
		});

		it('cancelReply clears reply', () => {
			store.startReply('msg-1', 'Alice', 'Hello...');
			store.cancelReply();
			expect(store.replyingTo).toBe(null);
		});
	});

	describe('SignalR handlers', () => {
		it('handleIncomingDm adds message to active channel', () => {
			store.activeDmChannelId = 'dm-1';
			const msg = { id: 'msg-1', dmChannelId: 'dm-1', body: 'hi' } as any;

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
		});

		it('handleIncomingDm increments unread for inactive channel', () => {
			store.activeDmChannelId = 'dm-2';
			const msg = { id: 'msg-1', dmChannelId: 'dm-1', body: 'hi' } as any;

			store.handleIncomingDm(msg, true);

			expect(store.unreadDmCounts.get('dm-1')).toBe(1);
		});

		it('handleIncomingDm does not duplicate messages', () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [{ id: 'msg-1', dmChannelId: 'dm-1' }] as any;
			const msg = { id: 'msg-1', dmChannelId: 'dm-1', body: 'hi' } as any;

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
		});

		it('handleDmTyping adds typing user', () => {
			store.activeDmChannelId = 'dm-1';

			store.handleDmTyping('dm-1', 'Alice');

			expect(store.dmTypingUsers).toContain('Alice');
		});

		it('handleDmTyping ignores different channel', () => {
			store.activeDmChannelId = 'dm-1';

			store.handleDmTyping('dm-2', 'Alice');

			expect(store.dmTypingUsers).toEqual([]);
		});

		it('handleDmStoppedTyping removes user', () => {
			store.activeDmChannelId = 'dm-1';
			store.dmTypingUsers = ['Alice', 'Bob'];

			store.handleDmStoppedTyping('dm-1', 'Alice');

			expect(store.dmTypingUsers).toEqual(['Bob']);
		});

		it('handleDmMessageDeleted removes message', () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [
				{ id: 'msg-1' },
				{ id: 'msg-2' }
			] as any;

			store.handleDmMessageDeleted({ dmChannelId: 'dm-1', messageId: 'msg-1' });

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('msg-2');
		});

		it('handleDmMessageEdited updates message body', () => {
			store.activeDmChannelId = 'dm-1';
			store.dmMessages = [{ id: 'msg-1', body: 'old' }] as any;

			store.handleDmMessageEdited({
				dmChannelId: 'dm-1',
				messageId: 'msg-1',
				body: 'edited',
				editedAt: '2024-01-01T00:00:00Z'
			});

			expect(store.dmMessages[0].body).toBe('edited');
		});

		it('patchDmLinkPreviews updates previews', () => {
			store.dmMessages = [{ id: 'msg-1', linkPreviews: [] }] as any;
			const previews = [{ url: 'https://example.com', title: 'Example' }] as any;

			store.patchDmLinkPreviews('msg-1', previews);

			expect(store.dmMessages[0].linkPreviews).toEqual(previews);
		});
	});

	describe('reset', () => {
		it('resets all state to defaults', () => {
			store.dmConversations = [{ id: 'dm-1' }] as any;
			store.dmMessages = [{ id: 'msg-1' }] as any;
			store.activeDmChannelId = 'dm-1';
			store.dmTypingUsers = ['Alice'];
			store.unreadDmCounts = new Map([['dm-1', 3]]);
			store.dmMessageBody = 'draft';
			store.isSendingDm = true;

			store.reset();

			expect(store.dmConversations).toEqual([]);
			expect(store.dmMessages).toEqual([]);
			expect(store.activeDmChannelId).toBe(null);
			expect(store.dmTypingUsers).toEqual([]);
			expect(store.unreadDmCounts.size).toBe(0);
			expect(store.dmMessageBody).toBe('');
			expect(store.isSendingDm).toBe(false);
		});
	});
});
