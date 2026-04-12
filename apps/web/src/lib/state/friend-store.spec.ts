import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

// Must import after mocking svelte
const { FriendStore } = await import('./friend-store.svelte.js');

type Friend = import('$lib/types/index.js').Friend;
type FriendRequest = import('$lib/types/index.js').FriendRequest;
type UserSearchResult = import('$lib/types/index.js').UserSearchResult;

function makeFriend(overrides: Partial<Friend> = {}): Friend {
	return {
		friendshipId: 'f-1',
		user: { id: 'u-1', displayName: 'Alice', avatarUrl: null },
		since: '2025-01-01T00:00:00Z',
		...overrides
	};
}

function makeRequest(overrides: Partial<FriendRequest> = {}): FriendRequest {
	return {
		id: 'r-1',
		requester: { id: 'u-2', displayName: 'Bob', avatarUrl: null },
		recipient: { id: 'u-me', displayName: 'Me', avatarUrl: null },
		status: 'Pending',
		createdAt: '2025-01-01T00:00:00Z',
		...overrides
	};
}

function createMocks() {
	const api = {
		getFriends: vi.fn().mockResolvedValue([]),
		getFriendRequests: vi.fn().mockResolvedValue([]),
		sendFriendRequest: vi.fn().mockResolvedValue(undefined),
		respondToFriendRequest: vi.fn().mockResolvedValue(undefined),
		cancelFriendRequest: vi.fn().mockResolvedValue(undefined),
		removeFriend: vi.fn().mockResolvedValue(undefined),
		searchUsers: vi.fn().mockResolvedValue([])
	} as any;

	const auth = { idToken: 'test-token' } as any;
	const ui = { setError: vi.fn() } as any;

	return { api, auth, ui };
}

describe('FriendStore', () => {
	let store: InstanceType<typeof FriendStore>;
	let api: ReturnType<typeof createMocks>['api'];
	let auth: ReturnType<typeof createMocks>['auth'];
	let ui: ReturnType<typeof createMocks>['ui'];

	beforeEach(() => {
		vi.clearAllMocks();
		const mocks = createMocks();
		api = mocks.api;
		auth = mocks.auth;
		ui = mocks.ui;
		store = new FriendStore(auth, api, ui);
	});

	describe('initial state', () => {
		it('has empty friends list', () => {
			expect(store.friends).toEqual([]);
		});

		it('has empty incoming requests', () => {
			expect(store.incomingRequests).toEqual([]);
		});

		it('has empty outgoing requests', () => {
			expect(store.outgoingRequests).toEqual([]);
		});

		it('has empty search results', () => {
			expect(store.userSearchResults).toEqual([]);
		});

		it('is not loading', () => {
			expect(store.isLoadingFriends).toBe(false);
			expect(store.isSearchingUsers).toBe(false);
		});

		it('has empty search query', () => {
			expect(store.friendSearchQuery).toBe('');
		});
	});

	describe('loadFriends', () => {
		it('loads friends from api', async () => {
			const friends = [makeFriend()];
			api.getFriends.mockResolvedValue(friends);

			await store.loadFriends();

			expect(api.getFriends).toHaveBeenCalledWith('test-token');
			expect(store.friends).toEqual(friends);
		});

		it('sets isLoadingFriends during load', async () => {
			let resolvePromise: (v: any) => void;
			api.getFriends.mockReturnValue(new Promise((r) => (resolvePromise = r)));

			const promise = store.loadFriends();
			expect(store.isLoadingFriends).toBe(true);

			resolvePromise!([]);
			await promise;
			expect(store.isLoadingFriends).toBe(false);
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.loadFriends();

			expect(api.getFriends).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure and resets loading', async () => {
			const err = new Error('network error');
			api.getFriends.mockRejectedValue(err);

			await store.loadFriends();

			expect(ui.setError).toHaveBeenCalledWith(err);
			expect(store.isLoadingFriends).toBe(false);
		});
	});

	describe('loadFriendRequests', () => {
		it('loads incoming and outgoing requests', async () => {
			const incoming = [makeRequest({ id: 'r-in' })];
			const outgoing = [makeRequest({ id: 'r-out', status: 'Pending' })];
			api.getFriendRequests
				.mockResolvedValueOnce(incoming)
				.mockResolvedValueOnce(outgoing);

			await store.loadFriendRequests();

			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'received');
			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'sent');
			expect(store.incomingRequests).toEqual(incoming);
			expect(store.outgoingRequests).toEqual(outgoing);
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.loadFriendRequests();

			expect(api.getFriendRequests).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const err = new Error('fail');
			api.getFriendRequests.mockRejectedValue(err);

			await store.loadFriendRequests();

			expect(ui.setError).toHaveBeenCalledWith(err);
			expect(store.isLoadingFriends).toBe(false);
		});
	});

	describe('sendFriendRequest', () => {
		it('calls api and reloads requests', async () => {
			await store.sendFriendRequest('u-target');

			expect(api.sendFriendRequest).toHaveBeenCalledWith('test-token', 'u-target');
			// loadFriendRequests is called after
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('refreshes search results if query is long enough', async () => {
			store.friendSearchQuery = 'alice';
			const searchResults: UserSearchResult[] = [
				{ id: 'u-1', displayName: 'Alice', relationshipStatus: 'pending' }
			];
			api.searchUsers.mockResolvedValue(searchResults);

			await store.sendFriendRequest('u-1');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'alice');
		});

		it('does not refresh search results if query is too short', async () => {
			store.friendSearchQuery = 'a';

			await store.sendFriendRequest('u-1');

			expect(api.searchUsers).not.toHaveBeenCalled();
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.sendFriendRequest('u-target');

			expect(api.sendFriendRequest).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const err = new Error('fail');
			api.sendFriendRequest.mockRejectedValue(err);

			await store.sendFriendRequest('u-target');

			expect(ui.setError).toHaveBeenCalledWith(err);
		});
	});

	describe('acceptFriendRequest', () => {
		it('calls api with accept and reloads both friends and requests', async () => {
			await store.acceptFriendRequest('r-1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'r-1', 'accept');
			expect(api.getFriends).toHaveBeenCalled();
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.acceptFriendRequest('r-1');

			expect(api.respondToFriendRequest).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const err = new Error('fail');
			api.respondToFriendRequest.mockRejectedValue(err);

			await store.acceptFriendRequest('r-1');

			expect(ui.setError).toHaveBeenCalledWith(err);
		});
	});

	describe('declineFriendRequest', () => {
		it('calls api with decline and reloads requests', async () => {
			await store.declineFriendRequest('r-1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'r-1', 'decline');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.declineFriendRequest('r-1');

			expect(api.respondToFriendRequest).not.toHaveBeenCalled();
		});
	});

	describe('cancelFriendRequest', () => {
		it('calls api and reloads requests', async () => {
			await store.cancelFriendRequest('r-1');

			expect(api.cancelFriendRequest).toHaveBeenCalledWith('test-token', 'r-1');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('refreshes search results if query is long enough', async () => {
			store.friendSearchQuery = 'bob';

			await store.cancelFriendRequest('r-1');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'bob');
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.cancelFriendRequest('r-1');

			expect(api.cancelFriendRequest).not.toHaveBeenCalled();
		});
	});

	describe('removeFriend', () => {
		it('calls api and reloads friends', async () => {
			await store.removeFriend('f-1');

			expect(api.removeFriend).toHaveBeenCalledWith('test-token', 'f-1');
			expect(api.getFriends).toHaveBeenCalled();
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.removeFriend('f-1');

			expect(api.removeFriend).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const err = new Error('fail');
			api.removeFriend.mockRejectedValue(err);

			await store.removeFriend('f-1');

			expect(ui.setError).toHaveBeenCalledWith(err);
		});
	});

	describe('searchUsers', () => {
		it('searches users and stores results', async () => {
			const results: UserSearchResult[] = [
				{ id: 'u-1', displayName: 'Alice', relationshipStatus: 'none' }
			];
			api.searchUsers.mockResolvedValue(results);

			await store.searchUsers('alice');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'alice');
			expect(store.userSearchResults).toEqual(results);
			expect(store.friendSearchQuery).toBe('alice');
		});

		it('clears results when query is too short', async () => {
			store.userSearchResults = [
				{ id: 'u-1', displayName: 'Alice', relationshipStatus: 'none' }
			];

			await store.searchUsers('a');

			expect(api.searchUsers).not.toHaveBeenCalled();
			expect(store.userSearchResults).toEqual([]);
		});

		it('clears results for empty query', async () => {
			await store.searchUsers('');

			expect(api.searchUsers).not.toHaveBeenCalled();
			expect(store.userSearchResults).toEqual([]);
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.searchUsers('alice');

			expect(api.searchUsers).not.toHaveBeenCalled();
		});

		it('sets isSearchingUsers during search', async () => {
			let resolvePromise: (v: any) => void;
			api.searchUsers.mockReturnValue(new Promise((r) => (resolvePromise = r)));

			const promise = store.searchUsers('alice');
			expect(store.isSearchingUsers).toBe(true);

			resolvePromise!([]);
			await promise;
			expect(store.isSearchingUsers).toBe(false);
		});

		it('calls ui.setError on failure', async () => {
			const err = new Error('fail');
			api.searchUsers.mockRejectedValue(err);

			await store.searchUsers('alice');

			expect(ui.setError).toHaveBeenCalledWith(err);
			expect(store.isSearchingUsers).toBe(false);
		});
	});

	describe('reset', () => {
		it('resets all state to initial values', () => {
			store.friends = [makeFriend()];
			store.incomingRequests = [makeRequest()];
			store.outgoingRequests = [makeRequest({ id: 'r-2' })];
			store.userSearchResults = [
				{ id: 'u-1', displayName: 'Alice', relationshipStatus: 'none' }
			];
			store.isLoadingFriends = true;
			store.isSearchingUsers = true;
			store.friendSearchQuery = 'test';

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
