import { describe, it, expect, beforeEach, vi } from 'vitest';
import { FriendStore } from './friend-store.svelte.js';
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
		getFriends: vi.fn().mockResolvedValue([]),
		getFriendRequests: vi.fn().mockResolvedValue([]),
		sendFriendRequest: vi.fn().mockResolvedValue(undefined),
		respondToFriendRequest: vi.fn().mockResolvedValue(undefined),
		cancelFriendRequest: vi.fn().mockResolvedValue(undefined),
		removeFriend: vi.fn().mockResolvedValue(undefined),
		searchUsers: vi.fn().mockResolvedValue([])
	} as any;
}

describe('FriendStore', () => {
	let store: FriendStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;

	beforeEach(() => {
		auth = mockAuth();
		api = mockApi();
		ui = new UIStore();
		store = new FriendStore(auth, api, ui);
	});

	describe('initial state', () => {
		it('starts with empty lists', () => {
			expect(store.friends).toEqual([]);
			expect(store.incomingRequests).toEqual([]);
			expect(store.outgoingRequests).toEqual([]);
			expect(store.userSearchResults).toEqual([]);
		});

		it('starts with loading flags false', () => {
			expect(store.isLoadingFriends).toBe(false);
			expect(store.isSearchingUsers).toBe(false);
		});

		it('starts with empty search query', () => {
			expect(store.friendSearchQuery).toBe('');
		});
	});

	describe('loadFriends', () => {
		it('loads friends from API', async () => {
			const friends = [{ id: 'f1', userId: 'u1', displayName: 'Friend1' }];
			api.getFriends.mockResolvedValue(friends);

			await store.loadFriends();

			expect(api.getFriends).toHaveBeenCalledWith('test-token');
			expect(store.friends).toEqual(friends);
		});

		it('sets loading flag during load', async () => {
			let resolveApi: () => void;
			api.getFriends.mockReturnValue(new Promise(r => { resolveApi = r as any; }));

			const promise = store.loadFriends();
			expect(store.isLoadingFriends).toBe(true);

			resolveApi!();
			await promise;
			expect(store.isLoadingFriends).toBe(false);
		});

		it('does nothing when not authenticated', async () => {
			auth.idToken = null;
			await store.loadFriends();
			expect(api.getFriends).not.toHaveBeenCalled();
		});

		it('sets error on failure', async () => {
			api.getFriends.mockRejectedValue(new Error('Network error'));
			await store.loadFriends();
			expect(ui.error).toBe('Network error');
		});
	});

	describe('loadFriendRequests', () => {
		it('loads incoming and outgoing requests', async () => {
			const incoming = [{ id: 'r1' }];
			const outgoing = [{ id: 'r2' }];
			api.getFriendRequests
				.mockResolvedValueOnce(incoming)
				.mockResolvedValueOnce(outgoing);

			await store.loadFriendRequests();

			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'received');
			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'sent');
			expect(store.incomingRequests).toEqual(incoming);
			expect(store.outgoingRequests).toEqual(outgoing);
		});
	});

	describe('sendFriendRequest', () => {
		it('sends request and reloads', async () => {
			await store.sendFriendRequest('user-2');

			expect(api.sendFriendRequest).toHaveBeenCalledWith('test-token', 'user-2');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('refreshes search results if query active', async () => {
			store.friendSearchQuery = 'test';
			const searchResults = [{ userId: 'u2', displayName: 'Found' }];
			api.searchUsers.mockResolvedValue(searchResults);

			await store.sendFriendRequest('user-2');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'test');
		});

		it('does not refresh search if query too short', async () => {
			store.friendSearchQuery = 'a';
			await store.sendFriendRequest('user-2');
			expect(api.searchUsers).not.toHaveBeenCalled();
		});
	});

	describe('acceptFriendRequest', () => {
		it('accepts and reloads friends and requests', async () => {
			await store.acceptFriendRequest('req-1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'req-1', 'accept');
			expect(api.getFriends).toHaveBeenCalled();
			expect(api.getFriendRequests).toHaveBeenCalled();
		});
	});

	describe('declineFriendRequest', () => {
		it('declines and reloads requests', async () => {
			await store.declineFriendRequest('req-1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'req-1', 'decline');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});
	});

	describe('cancelFriendRequest', () => {
		it('cancels and reloads requests', async () => {
			await store.cancelFriendRequest('req-1');

			expect(api.cancelFriendRequest).toHaveBeenCalledWith('test-token', 'req-1');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});
	});

	describe('removeFriend', () => {
		it('removes and reloads friends', async () => {
			await store.removeFriend('friendship-1');

			expect(api.removeFriend).toHaveBeenCalledWith('test-token', 'friendship-1');
			expect(api.getFriends).toHaveBeenCalled();
		});
	});

	describe('searchUsers', () => {
		it('searches when query is long enough', async () => {
			const results = [{ userId: 'u1', displayName: 'User1' }];
			api.searchUsers.mockResolvedValue(results);

			await store.searchUsers('test');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'test');
			expect(store.userSearchResults).toEqual(results);
			expect(store.friendSearchQuery).toBe('test');
		});

		it('clears results when query too short', async () => {
			store.userSearchResults = [{ userId: 'u1' }] as any;
			await store.searchUsers('a');

			expect(api.searchUsers).not.toHaveBeenCalled();
			expect(store.userSearchResults).toEqual([]);
		});

		it('clears results for empty query', async () => {
			await store.searchUsers('');
			expect(store.userSearchResults).toEqual([]);
		});

		it('sets searching flag', async () => {
			let resolveApi: () => void;
			api.searchUsers.mockReturnValue(new Promise(r => { resolveApi = r as any; }));

			const promise = store.searchUsers('test');
			expect(store.isSearchingUsers).toBe(true);

			resolveApi!();
			await promise;
			expect(store.isSearchingUsers).toBe(false);
		});
	});

	describe('reset', () => {
		it('resets all state to defaults', () => {
			store.friends = [{ id: 'f1' }] as any;
			store.incomingRequests = [{ id: 'r1' }] as any;
			store.outgoingRequests = [{ id: 'r2' }] as any;
			store.userSearchResults = [{ userId: 'u1' }] as any;
			store.isLoadingFriends = true;
			store.isSearchingUsers = true;
			store.friendSearchQuery = 'query';

			store.reset();

			expect(store.friends).toEqual([]);
			expect(store.incomingRequests).toEqual([]);
			expect(store.outgoingRequests).toEqual([]);
			expect(store.userSearchResults).toEqual([]);
			expect(store.isLoadingFriends).toBe(false);
			expect(store.isSearchingUsers).toBe(false);
			expect(store.friendSearchQuery).toBe('');
		});
	});
});
