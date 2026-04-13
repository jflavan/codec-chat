import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));

// Must import after mock
const { FriendStore } = await import('./friend-store.svelte.js');

/* ───── helpers ───── */

function makeMockAuth(overrides: Record<string, unknown> = {}) {
	return { idToken: 'test-token', me: null, ...overrides };
}

function makeMockApi() {
	return {
		getFriends: vi.fn().mockResolvedValue([]),
		getFriendRequests: vi.fn().mockResolvedValue([]),
		sendFriendRequest: vi.fn().mockResolvedValue(undefined),
		respondToFriendRequest: vi.fn().mockResolvedValue(undefined),
		cancelFriendRequest: vi.fn().mockResolvedValue(undefined),
		removeFriend: vi.fn().mockResolvedValue(undefined),
		searchUsers: vi.fn().mockResolvedValue([])
	};
}

function makeMockUi() {
	return { error: null, setError: vi.fn() };
}

type MockAuth = ReturnType<typeof makeMockAuth>;
type MockApi = ReturnType<typeof makeMockApi>;
type MockUi = ReturnType<typeof makeMockUi>;

function createStore(
	authOverrides: Record<string, unknown> = {},
	apiOverrides: Record<string, unknown> = {},
	uiOverrides: Record<string, unknown> = {}
) {
	const auth = { ...makeMockAuth(), ...authOverrides };
	const api = { ...makeMockApi(), ...apiOverrides };
	const ui = { ...makeMockUi(), ...uiOverrides };
	const store = new FriendStore(auth as any, api as any, ui as any);
	return { store, auth, api, ui };
}

/* ───── tests ───── */

describe('FriendStore', () => {
	describe('initial state', () => {
		it('has empty defaults', () => {
			const { store } = createStore();
			expect(store.friends).toEqual([]);
			expect(store.incomingRequests).toEqual([]);
			expect(store.outgoingRequests).toEqual([]);
			expect(store.userSearchResults).toEqual([]);
			expect(store.isLoadingFriends).toBe(false);
			expect(store.isSearchingUsers).toBe(false);
			expect(store.friendSearchQuery).toBe('');
		});
	});

	describe('loadFriends', () => {
		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.loadFriends();
			expect(api.getFriends).not.toHaveBeenCalled();
		});

		it('sets friends on success', async () => {
			const friends = [{ friendshipId: '1', user: { id: 'u1', displayName: 'Alice' }, since: '2024-01-01' }];
			const { store, api } = createStore();
			api.getFriends.mockResolvedValueOnce(friends);

			await store.loadFriends();

			expect(store.friends).toEqual(friends);
			expect(store.isLoadingFriends).toBe(false);
		});

		it('sets isLoadingFriends during load', async () => {
			const { store, api } = createStore();
			let capturedLoading = false;
			api.getFriends.mockImplementation(() => {
				capturedLoading = store.isLoadingFriends;
				return Promise.resolve([]);
			});

			await store.loadFriends();

			expect(capturedLoading).toBe(true);
			expect(store.isLoadingFriends).toBe(false);
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('Network error');
			const { store, api, ui } = createStore();
			api.getFriends.mockRejectedValueOnce(error);

			await store.loadFriends();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingFriends).toBe(false);
		});
	});

	describe('loadFriendRequests', () => {
		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.loadFriendRequests();
			expect(api.getFriendRequests).not.toHaveBeenCalled();
		});

		it('loads incoming and outgoing requests concurrently', async () => {
			const incoming = [{ id: 'r1', requester: { id: 'u1' }, recipient: { id: 'u2' }, status: 'pending', createdAt: '2024-01-01' }];
			const outgoing = [{ id: 'r2', requester: { id: 'u2' }, recipient: { id: 'u3' }, status: 'pending', createdAt: '2024-01-01' }];
			const { store, api } = createStore();
			api.getFriendRequests
				.mockResolvedValueOnce(incoming)
				.mockResolvedValueOnce(outgoing);

			await store.loadFriendRequests();

			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'received');
			expect(api.getFriendRequests).toHaveBeenCalledWith('test-token', 'sent');
			expect(store.incomingRequests).toEqual(incoming);
			expect(store.outgoingRequests).toEqual(outgoing);
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			api.getFriendRequests.mockRejectedValueOnce(error);

			await store.loadFriendRequests();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingFriends).toBe(false);
		});
	});

	describe('sendFriendRequest', () => {
		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.sendFriendRequest('u1');
			expect(api.sendFriendRequest).not.toHaveBeenCalled();
		});

		it('calls api and reloads requests', async () => {
			const { store, api } = createStore();
			api.getFriendRequests.mockResolvedValue([]);

			await store.sendFriendRequest('u1');

			expect(api.sendFriendRequest).toHaveBeenCalledWith('test-token', 'u1');
			// loadFriendRequests is called which calls getFriendRequests
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('refreshes search results when query is >= 2 chars', async () => {
			const { store, api } = createStore();
			api.getFriendRequests.mockResolvedValue([]);
			api.searchUsers.mockResolvedValue([]);
			store.friendSearchQuery = 'ab';

			await store.sendFriendRequest('u1');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'ab');
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			api.sendFriendRequest.mockRejectedValueOnce(error);

			await store.sendFriendRequest('u1');

			expect(ui.setError).toHaveBeenCalledWith(error);
		});
	});

	describe('acceptFriendRequest', () => {
		it('calls api with accept and reloads friends and requests', async () => {
			const { store, api } = createStore();
			api.getFriends.mockResolvedValue([]);
			api.getFriendRequests.mockResolvedValue([]);

			await store.acceptFriendRequest('r1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'r1', 'accept');
			expect(api.getFriends).toHaveBeenCalled();
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.acceptFriendRequest('r1');
			expect(api.respondToFriendRequest).not.toHaveBeenCalled();
		});
	});

	describe('declineFriendRequest', () => {
		it('calls api with decline and reloads requests', async () => {
			const { store, api } = createStore();
			api.getFriendRequests.mockResolvedValue([]);

			await store.declineFriendRequest('r1');

			expect(api.respondToFriendRequest).toHaveBeenCalledWith('test-token', 'r1', 'decline');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});
	});

	describe('cancelFriendRequest', () => {
		it('calls api and reloads requests', async () => {
			const { store, api } = createStore();
			api.getFriendRequests.mockResolvedValue([]);

			await store.cancelFriendRequest('r1');

			expect(api.cancelFriendRequest).toHaveBeenCalledWith('test-token', 'r1');
			expect(api.getFriendRequests).toHaveBeenCalled();
		});

		it('refreshes search results when query is >= 2 chars', async () => {
			const { store, api } = createStore();
			api.getFriendRequests.mockResolvedValue([]);
			api.searchUsers.mockResolvedValue([]);
			store.friendSearchQuery = 'ab';

			await store.cancelFriendRequest('r1');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'ab');
		});
	});

	describe('removeFriend', () => {
		it('calls api and reloads friends', async () => {
			const { store, api } = createStore();
			api.getFriends.mockResolvedValue([]);

			await store.removeFriend('f1');

			expect(api.removeFriend).toHaveBeenCalledWith('test-token', 'f1');
			expect(api.getFriends).toHaveBeenCalled();
		});

		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.removeFriend('f1');
			expect(api.removeFriend).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			api.removeFriend.mockRejectedValueOnce(error);

			await store.removeFriend('f1');

			expect(ui.setError).toHaveBeenCalledWith(error);
		});
	});

	describe('searchUsers', () => {
		it('clears results and does nothing for query < 2 chars', async () => {
			const { store, api } = createStore();
			store.userSearchResults = [{ id: '1', displayName: 'X', relationshipStatus: 'none' }];

			await store.searchUsers('a');

			expect(store.userSearchResults).toEqual([]);
			expect(api.searchUsers).not.toHaveBeenCalled();
		});

		it('calls api.searchUsers for query >= 2 chars', async () => {
			const results = [{ id: '1', displayName: 'Alice', relationshipStatus: 'none' }];
			const { store, api } = createStore();
			api.searchUsers.mockResolvedValueOnce(results);

			await store.searchUsers('al');

			expect(api.searchUsers).toHaveBeenCalledWith('test-token', 'al');
			expect(store.userSearchResults).toEqual(results);
			expect(store.isSearchingUsers).toBe(false);
		});

		it('sets isSearchingUsers during search', async () => {
			const { store, api } = createStore();
			let capturedSearching = false;
			api.searchUsers.mockImplementation(() => {
				capturedSearching = store.isSearchingUsers;
				return Promise.resolve([]);
			});

			await store.searchUsers('test');

			expect(capturedSearching).toBe(true);
			expect(store.isSearchingUsers).toBe(false);
		});

		it('does nothing when idToken is null', async () => {
			const { store, api } = createStore({ idToken: null });
			await store.searchUsers('test');
			expect(api.searchUsers).not.toHaveBeenCalled();
		});

		it('calls ui.setError on failure', async () => {
			const error = new Error('fail');
			const { store, api, ui } = createStore();
			api.searchUsers.mockRejectedValueOnce(error);

			await store.searchUsers('test');

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isSearchingUsers).toBe(false);
		});
	});

	describe('reset', () => {
		it('clears all state to defaults', () => {
			const { store } = createStore();
			store.friends = [{ friendshipId: '1', user: { id: 'u1', displayName: 'A' }, since: '2024' } as any];
			store.incomingRequests = [{ id: 'r1' } as any];
			store.outgoingRequests = [{ id: 'r2' } as any];
			store.userSearchResults = [{ id: 'u1' } as any];
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
