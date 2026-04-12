import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock svelte context APIs before importing the store
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

import { DmStore } from './dm-store.svelte.js';
import type { DirectMessage, DmConversation, DmParticipant } from '$lib/types/index.js';
import type { DmReactionUpdate, DmMessageDeletedEvent, DmMessageEditedEvent } from '$lib/services/chat-hub.js';

/* ───── helpers ───── */

function makeDirectMessage(overrides: Partial<DirectMessage> = {}): DirectMessage {
	return {
		id: 'dm-msg-1',
		dmChannelId: 'dm-ch-1',
		authorUserId: 'u-2',
		authorName: 'Bob',
		body: 'Hey there',
		createdAt: '2026-01-01T00:00:00Z',
		linkPreviews: [],
		replyContext: null,
		reactions: [],
		...overrides
	};
}

function makeDmConversation(overrides: Partial<DmConversation> = {}): DmConversation {
	return {
		id: 'dm-ch-1',
		participant: { id: 'u-2', displayName: 'Bob', avatarUrl: null },
		lastMessage: { authorName: 'Bob', body: 'Hey', createdAt: '2026-01-01T00:00:00Z' },
		sortDate: '2026-01-01T00:00:00Z',
		...overrides
	};
}

function createMockApi() {
	return {
		getDmConversations: vi.fn(),
		getDmPresence: vi.fn(),
		getDmMessages: vi.fn(),
		sendDm: vi.fn(),
		createOrResumeDm: vi.fn(),
		closeDmConversation: vi.fn(),
		uploadImage: vi.fn(),
		uploadFile: vi.fn(),
		deleteDmMessage: vi.fn(),
		editDmMessage: vi.fn(),
		toggleDmReaction: vi.fn()
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

function createMockUi() {
	return {
		error: null as string | null,
		setError: vi.fn(),
		mobileNavOpen: false,
		userPresence: new Map(),
		ignoredReactionUpdates: new Map<string, string[]>(),
		pendingReactionKeys: new Set<string>(),
		setReactionPending: vi.fn()
	} as any;
}

function createMockHub() {
	return {
		isConnected: true,
		joinDmChannel: vi.fn().mockResolvedValue(undefined),
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
		clearDmTyping: vi.fn(),
		emitDmTyping: vi.fn()
	} as any;
}

function createStore(overrides: {
	auth?: ReturnType<typeof createMockAuth>;
	api?: ReturnType<typeof createMockApi>;
	ui?: ReturnType<typeof createMockUi>;
	hub?: ReturnType<typeof createMockHub>;
} = {}) {
	const auth = overrides.auth ?? createMockAuth();
	const api = overrides.api ?? createMockApi();
	const ui = overrides.ui ?? createMockUi();
	const hub = overrides.hub ?? createMockHub();
	return { store: new DmStore(auth, api, ui, hub), auth, api, ui, hub };
}

/* ───── tests ───── */

describe('DmStore', () => {
	describe('initial state', () => {
		it('starts with empty conversations and messages', () => {
			const { store } = createStore();
			expect(store.dmConversations).toEqual([]);
			expect(store.dmMessages).toEqual([]);
			expect(store.activeDmChannelId).toBeNull();
			expect(store.dmTypingUsers).toEqual([]);
			expect(store.unreadDmCounts).toEqual(new Map());
			expect(store.dmMessageBody).toBe('');
			expect(store.isLoadingDmConversations).toBe(false);
			expect(store.isLoadingDmMessages).toBe(false);
			expect(store.isSendingDm).toBe(false);
			expect(store.replyingTo).toBeNull();
		});
	});

	describe('loadDmConversations', () => {
		it('populates conversations from API', async () => {
			const api = createMockApi();
			const convos = [makeDmConversation()];
			api.getDmConversations.mockResolvedValue(convos);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });

			await store.loadDmConversations();

			expect(api.getDmConversations).toHaveBeenCalledWith('test-token');
			expect(store.dmConversations).toEqual(convos);
			expect(store.isLoadingDmConversations).toBe(false);
		});

		it('returns early when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.loadDmConversations();

			expect(api.getDmConversations).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			const error = new Error('network error');
			api.getDmConversations.mockRejectedValue(error);
			const { store } = createStore({ api, ui });

			await store.loadDmConversations();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingDmConversations).toBe(false);
		});
	});

	describe('selectDmConversation', () => {
		it('sets active channel, joins hub, and loads messages', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.getDmMessages.mockResolvedValue({ messages: [makeDirectMessage()] });
			const { store } = createStore({ api, hub });

			await store.selectDmConversation('dm-ch-1');

			expect(store.activeDmChannelId).toBe('dm-ch-1');
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-ch-1');
			expect(api.getDmMessages).toHaveBeenCalledWith('test-token', 'dm-ch-1');
		});

		it('leaves previous channel before joining new one', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.getDmMessages.mockResolvedValue({ messages: [] });
			const { store } = createStore({ api, hub });
			store.activeDmChannelId = 'dm-ch-old';

			await store.selectDmConversation('dm-ch-new');

			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-ch-old');
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-ch-new');
		});

		it('clears unread count for selected conversation', async () => {
			const api = createMockApi();
			api.getDmMessages.mockResolvedValue({ messages: [] });
			const { store } = createStore({ api });
			store.unreadDmCounts = new Map([['dm-ch-1', 5]]);

			await store.selectDmConversation('dm-ch-1');

			expect(store.unreadDmCounts.has('dm-ch-1')).toBe(false);
		});

		it('resets typing users and composer', async () => {
			const api = createMockApi();
			api.getDmMessages.mockResolvedValue({ messages: [] });
			const { store } = createStore({ api });
			store.dmTypingUsers = ['Bob'];
			store.dmMessageBody = 'draft';
			store.replyingTo = { messageId: 'm', authorName: 'A', bodyPreview: 'B', context: 'dm' };

			await store.selectDmConversation('dm-ch-1');

			expect(store.dmTypingUsers).toEqual([]);
			expect(store.dmMessageBody).toBe('');
			expect(store.replyingTo).toBeNull();
		});
	});

	describe('loadDmMessages', () => {
		it('loads messages from API', async () => {
			const api = createMockApi();
			const msgs = [makeDirectMessage()];
			api.getDmMessages.mockResolvedValue({ messages: msgs });
			const { store } = createStore({ api });

			await store.loadDmMessages('dm-ch-1');

			expect(store.dmMessages).toEqual(msgs);
			expect(store.isLoadingDmMessages).toBe(false);
		});

		it('returns early when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.loadDmMessages('dm-ch-1');

			expect(api.getDmMessages).not.toHaveBeenCalled();
		});
	});

	describe('sendDmMessage', () => {
		it('sends message and clears composer', async () => {
			const api = createMockApi();
			api.sendDm.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = 'Hello!';

			await store.sendDmMessage();

			expect(api.sendDm).toHaveBeenCalledWith(
				'test-token', 'dm-ch-1', 'Hello!', null, null, null
			);
			expect(store.dmMessageBody).toBe('');
			expect(store.isSendingDm).toBe(false);
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = 'Hello';

			await store.sendDmMessage();

			expect(api.sendDm).not.toHaveBeenCalled();
		});

		it('does nothing when no active DM channel', async () => {
			const api = createMockApi();
			const { store } = createStore({ api });
			store.dmMessageBody = 'Hello';

			await store.sendDmMessage();

			expect(api.sendDm).not.toHaveBeenCalled();
		});

		it('sets ui error when body is empty and no attachments', async () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = '';

			await store.sendDmMessage();

			expect(ui.error).toBe('Message body, image, or file is required.');
		});

		it('clears typing after sending', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.sendDm.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = 'Hi';

			await store.sendDmMessage();

			expect(hub.clearDmTyping).toHaveBeenCalledWith('dm-ch-1', 'Alice');
		});

		it('falls back to loadDmMessages when hub disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.sendDm.mockResolvedValue(undefined);
			api.getDmMessages.mockResolvedValue({ messages: [] });
			const { store } = createStore({ api, hub });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = 'Hi';

			await store.sendDmMessage();

			expect(api.getDmMessages).toHaveBeenCalledWith('test-token', 'dm-ch-1');
		});

		it('reports error to ui on failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			api.sendDm.mockRejectedValue(new Error('send failed'));
			const { store } = createStore({ api, ui });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessageBody = 'Hi';

			await store.sendDmMessage();

			expect(ui.setError).toHaveBeenCalled();
			expect(store.isSendingDm).toBe(false);
		});
	});

	describe('openDmWithUser', () => {
		it('creates or resumes DM and selects it', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.createOrResumeDm.mockResolvedValue({ id: 'dm-ch-new' });
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			api.getDmMessages.mockResolvedValue({ messages: [] });
			const { store } = createStore({ api, hub });

			await store.openDmWithUser('u-3');

			expect(api.createOrResumeDm).toHaveBeenCalledWith('test-token', 'u-3');
			expect(api.getDmConversations).toHaveBeenCalled();
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-ch-new');
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.openDmWithUser('u-3');

			expect(api.createOrResumeDm).not.toHaveBeenCalled();
		});

		it('reports error on failure', async () => {
			const api = createMockApi();
			const ui = createMockUi();
			api.createOrResumeDm.mockRejectedValue(new Error('fail'));
			const { store } = createStore({ api, ui });

			await store.openDmWithUser('u-3');

			expect(ui.setError).toHaveBeenCalled();
		});
	});

	describe('closeDmConversation', () => {
		it('removes conversation and clears state if active', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.closeDmConversation.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.dmConversations = [makeDmConversation({ id: 'dm-ch-1' })];
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage()];

			await store.closeDmConversation('dm-ch-1');

			expect(store.dmConversations).toEqual([]);
			expect(store.activeDmChannelId).toBeNull();
			expect(store.dmMessages).toEqual([]);
			expect(hub.leaveDmChannel).toHaveBeenCalledWith('dm-ch-1');
		});

		it('removes conversation without clearing active when different channel', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			api.closeDmConversation.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.dmConversations = [
				makeDmConversation({ id: 'dm-ch-1' }),
				makeDmConversation({ id: 'dm-ch-2' })
			];
			store.activeDmChannelId = 'dm-ch-2';

			await store.closeDmConversation('dm-ch-1');

			expect(store.dmConversations).toHaveLength(1);
			expect(store.activeDmChannelId).toBe('dm-ch-2');
			expect(hub.leaveDmChannel).not.toHaveBeenCalled();
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.closeDmConversation('dm-ch-1');

			expect(api.closeDmConversation).not.toHaveBeenCalled();
		});
	});

	describe('deleteDmMessage', () => {
		it('calls API and removes locally when hub disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.deleteDmMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1' }), makeDirectMessage({ id: 'dm-msg-2' })];

			await store.deleteDmMessage('dm-msg-1');

			expect(api.deleteDmMessage).toHaveBeenCalledWith('test-token', 'dm-ch-1', 'dm-msg-1');
			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('dm-msg-2');
		});

		it('does not remove locally when hub is connected', async () => {
			const api = createMockApi();
			api.deleteDmMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1' })];

			await store.deleteDmMessage('dm-msg-1');

			expect(store.dmMessages).toHaveLength(1);
		});

		it('does nothing when not authenticated', async () => {
			const api = createMockApi();
			const auth = createMockAuth({ idToken: null });
			const { store } = createStore({ api, auth });

			await store.deleteDmMessage('dm-msg-1');

			expect(api.deleteDmMessage).not.toHaveBeenCalled();
		});
	});

	describe('editDmMessage', () => {
		it('calls API and updates locally when hub disconnected', async () => {
			const api = createMockApi();
			const hub = createMockHub();
			hub.isConnected = false;
			api.editDmMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api, hub });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', body: 'old' })];

			await store.editDmMessage('dm-msg-1', 'new body');

			expect(api.editDmMessage).toHaveBeenCalledWith('test-token', 'dm-ch-1', 'dm-msg-1', 'new body');
			expect(store.dmMessages[0].body).toBe('new body');
			expect(store.dmMessages[0].editedAt).toBeTruthy();
		});

		it('does not update locally when hub connected', async () => {
			const api = createMockApi();
			api.editDmMessage.mockResolvedValue(undefined);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', body: 'old' })];

			await store.editDmMessage('dm-msg-1', 'new body');

			expect(store.dmMessages[0].body).toBe('old');
		});
	});

	describe('handleIncomingDm (SignalR)', () => {
		it('appends message when viewing active DM channel', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';

			const msg = makeDirectMessage({ id: 'new-dm', dmChannelId: 'dm-ch-1' });
			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('new-dm');
		});

		it('does not duplicate messages', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';
			const msg = makeDirectMessage({ id: 'dm-msg-1', dmChannelId: 'dm-ch-1' });
			store.dmMessages = [msg];

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages).toHaveLength(1);
		});

		it('increments unread count when not viewing DMs', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.activeDmChannelId = null;

			const msg = makeDirectMessage({ dmChannelId: 'dm-ch-1' });
			store.handleIncomingDm(msg, false);

			expect(store.unreadDmCounts.get('dm-ch-1')).toBe(1);
		});

		it('increments unread count when viewing different DM channel', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-2';

			const msg = makeDirectMessage({ dmChannelId: 'dm-ch-1' });
			store.handleIncomingDm(msg, true);

			expect(store.unreadDmCounts.get('dm-ch-1')).toBe(1);
		});

		it('accumulates unread counts', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.unreadDmCounts = new Map([['dm-ch-1', 3]]);

			const msg = makeDirectMessage({ dmChannelId: 'dm-ch-1' });
			store.handleIncomingDm(msg, false);

			expect(store.unreadDmCounts.get('dm-ch-1')).toBe(4);
		});

		it('fills in missing linkPreviews and replyContext', () => {
			const api = createMockApi();
			api.getDmConversations.mockResolvedValue([]);
			api.getDmPresence.mockResolvedValue([]);
			const { store } = createStore({ api });
			store.activeDmChannelId = 'dm-ch-1';

			const msg = {
				...makeDirectMessage({ id: 'new', dmChannelId: 'dm-ch-1' }),
				linkPreviews: undefined,
				replyContext: undefined
			} as any;

			store.handleIncomingDm(msg, true);

			expect(store.dmMessages[0].linkPreviews).toEqual([]);
			expect(store.dmMessages[0].replyContext).toBeNull();
		});
	});

	describe('handleDmTyping / handleDmStoppedTyping', () => {
		it('adds typing user for active DM channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';

			store.handleDmTyping('dm-ch-1', 'Bob');

			expect(store.dmTypingUsers).toEqual(['Bob']);
		});

		it('does not add duplicate typing user', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmTypingUsers = ['Bob'];

			store.handleDmTyping('dm-ch-1', 'Bob');

			expect(store.dmTypingUsers).toEqual(['Bob']);
		});

		it('ignores typing for non-active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';

			store.handleDmTyping('dm-ch-2', 'Bob');

			expect(store.dmTypingUsers).toEqual([]);
		});

		it('removes typing user on stopped typing', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmTypingUsers = ['Bob', 'Carol'];

			store.handleDmStoppedTyping('dm-ch-1', 'Bob');

			expect(store.dmTypingUsers).toEqual(['Carol']);
		});

		it('ignores stopped typing for non-active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmTypingUsers = ['Bob'];

			store.handleDmStoppedTyping('dm-ch-2', 'Bob');

			expect(store.dmTypingUsers).toEqual(['Bob']);
		});
	});

	describe('handleDmMessageDeleted (SignalR)', () => {
		it('removes message from active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1' }), makeDirectMessage({ id: 'dm-msg-2' })];

			store.handleDmMessageDeleted({ messageId: 'dm-msg-1', dmChannelId: 'dm-ch-1' });

			expect(store.dmMessages).toHaveLength(1);
			expect(store.dmMessages[0].id).toBe('dm-msg-2');
		});

		it('ignores events for non-active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1' })];

			store.handleDmMessageDeleted({ messageId: 'dm-msg-1', dmChannelId: 'dm-ch-2' });

			expect(store.dmMessages).toHaveLength(1);
		});
	});

	describe('handleDmMessageEdited (SignalR)', () => {
		it('updates message body and editedAt', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', body: 'old' })];

			store.handleDmMessageEdited({
				messageId: 'dm-msg-1',
				dmChannelId: 'dm-ch-1',
				body: 'new body',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.dmMessages[0].body).toBe('new body');
			expect(store.dmMessages[0].editedAt).toBe('2026-01-02T00:00:00Z');
		});

		it('ignores events for non-active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', body: 'old' })];

			store.handleDmMessageEdited({
				messageId: 'dm-msg-1',
				dmChannelId: 'dm-ch-2',
				body: 'new',
				editedAt: '2026-01-02T00:00:00Z'
			});

			expect(store.dmMessages[0].body).toBe('old');
		});
	});

	describe('handleDmReactionUpdate (SignalR)', () => {
		it('updates reactions on correct message', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', reactions: [] })];

			const newReactions = [{ emoji: '👍', count: 1, userIds: ['u-2'] }];
			store.handleDmReactionUpdate({
				messageId: 'dm-msg-1',
				dmChannelId: 'dm-ch-1',
				reactions: newReactions
			});

			expect(store.dmMessages[0].reactions).toEqual(newReactions);
		});

		it('ignores events for non-active channel', () => {
			const { store } = createStore();
			store.activeDmChannelId = 'dm-ch-1';
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', reactions: [] })];

			store.handleDmReactionUpdate({
				messageId: 'dm-msg-1',
				dmChannelId: 'dm-ch-2',
				reactions: [{ emoji: '👍', count: 1, userIds: ['u-2'] }]
			});

			expect(store.dmMessages[0].reactions).toEqual([]);
		});
	});

	describe('patchDmLinkPreviews', () => {
		it('adds link previews to a message', () => {
			const { store } = createStore();
			store.dmMessages = [makeDirectMessage({ id: 'dm-msg-1', linkPreviews: [] })];

			const previews = [{ url: 'https://example.com', title: 'Example', description: null, imageUrl: null, siteName: null, canonicalUrl: null }];
			store.patchDmLinkPreviews('dm-msg-1', previews);

			expect(store.dmMessages[0].linkPreviews).toEqual(previews);
		});
	});

	describe('reply management', () => {
		it('startReply sets replyingTo with dm context', () => {
			const { store } = createStore();
			store.startReply('dm-msg-1', 'Bob', 'Hey...');

			expect(store.replyingTo).toEqual({
				messageId: 'dm-msg-1',
				authorName: 'Bob',
				bodyPreview: 'Hey...',
				context: 'dm'
			});
		});

		it('cancelReply clears replyingTo', () => {
			const { store } = createStore();
			store.startReply('dm-msg-1', 'Bob', 'Hey...');
			store.cancelReply();

			expect(store.replyingTo).toBeNull();
		});
	});

	describe('attachDmImage', () => {
		it('rejects unsupported image types', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const file = new File(['data'], 'test.bmp', { type: 'image/bmp' });

			store.attachDmImage(file);

			expect(ui.error).toBe('Unsupported image type. Allowed: JPG, PNG, WebP, GIF.');
			expect(store.pendingDmImage).toBeNull();
		});

		it('rejects images over 10MB', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const bigData = new Uint8Array(11 * 1024 * 1024);
			const file = new File([bigData], 'big.png', { type: 'image/png' });

			store.attachDmImage(file);

			expect(ui.error).toBe('Image must be under 10 MB.');
		});

		it('accepts valid image', () => {
			const { store } = createStore();
			const file = new File(['data'], 'photo.png', { type: 'image/png' });

			store.attachDmImage(file);

			expect(store.pendingDmImage).toBe(file);
			expect(store.pendingDmImagePreview).toBeTruthy();
		});
	});

	describe('attachDmFile', () => {
		it('rejects unsupported file types', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const file = new File(['data'], 'test.exe', { type: 'application/x-msdownload' });

			store.attachDmFile(file);

			expect(ui.error).toBe('Unsupported file type.');
			expect(store.pendingDmFile).toBeNull();
		});

		it('rejects files over 25MB', () => {
			const ui = createMockUi();
			const { store } = createStore({ ui });
			const bigData = new Uint8Array(26 * 1024 * 1024);
			const file = new File([bigData], 'big.pdf', { type: 'application/pdf' });

			store.attachDmFile(file);

			expect(ui.error).toBe('File must be under 25 MB.');
		});

		it('accepts valid file', () => {
			const { store } = createStore();
			const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });

			store.attachDmFile(file);

			expect(store.pendingDmFile).toBe(file);
		});
	});

	describe('reset', () => {
		it('resets all state to defaults', () => {
			const { store } = createStore();
			store.dmConversations = [makeDmConversation()];
			store.dmMessages = [makeDirectMessage()];
			store.activeDmChannelId = 'dm-ch-1';
			store.dmTypingUsers = ['Bob'];
			store.unreadDmCounts = new Map([['dm-ch-1', 5]]);
			store.dmMessageBody = 'draft';
			store.isSendingDm = true;

			store.reset();

			expect(store.dmConversations).toEqual([]);
			expect(store.dmMessages).toEqual([]);
			expect(store.activeDmChannelId).toBeNull();
			expect(store.dmTypingUsers).toEqual([]);
			expect(store.unreadDmCounts).toEqual(new Map());
			expect(store.dmMessageBody).toBe('');
			expect(store.isLoadingDmConversations).toBe(false);
			expect(store.isLoadingDmMessages).toBe(false);
			expect(store.isSendingDm).toBe(false);
			expect(store.replyingTo).toBeNull();
		});
	});
});
