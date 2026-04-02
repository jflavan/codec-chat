// apps/web/src/lib/state/friend-store.svelte.ts
import { getContext, setContext } from 'svelte';
import type { Friend, FriendRequest, UserSearchResult } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

const FRIEND_KEY = Symbol('friend-store');

export function createFriendStore(auth: AuthStore, api: ApiClient, ui: UIStore): FriendStore {
	const store = new FriendStore(auth, api, ui);
	setContext(FRIEND_KEY, store);
	return store;
}

export function getFriendStore(): FriendStore {
	return getContext<FriendStore>(FRIEND_KEY);
}

export class FriendStore {
	/* ───── $state fields ───── */
	friends = $state<Friend[]>([]);
	incomingRequests = $state<FriendRequest[]>([]);
	outgoingRequests = $state<FriendRequest[]>([]);
	userSearchResults = $state<UserSearchResult[]>([]);
	isLoadingFriends = $state(false);
	isSearchingUsers = $state(false);
	friendSearchQuery = $state('');

	constructor(
		private auth: AuthStore,
		private api: ApiClient,
		private ui: UIStore
	) {}

	/* ───── Methods ───── */

	async loadFriends(): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingFriends = true;
		try {
			this.friends = await this.api.getFriends(this.auth.idToken);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingFriends = false;
		}
	}

	async loadFriendRequests(): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingFriends = true;
		try {
			const [incoming, outgoing] = await Promise.all([
				this.api.getFriendRequests(this.auth.idToken, 'received'),
				this.api.getFriendRequests(this.auth.idToken, 'sent')
			]);
			this.incomingRequests = incoming;
			this.outgoingRequests = outgoing;
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingFriends = false;
		}
	}

	async sendFriendRequest(recipientUserId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.sendFriendRequest(this.auth.idToken, recipientUserId);
			await this.loadFriendRequests();
			// Refresh search results to update relationship status.
			if (this.friendSearchQuery.trim().length >= 2) {
				await this.searchUsers(this.friendSearchQuery);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async acceptFriendRequest(requestId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.respondToFriendRequest(this.auth.idToken, requestId, 'accept');
			await this.loadFriends();
			await this.loadFriendRequests();
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async declineFriendRequest(requestId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.respondToFriendRequest(this.auth.idToken, requestId, 'decline');
			await this.loadFriendRequests();
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async cancelFriendRequest(requestId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.cancelFriendRequest(this.auth.idToken, requestId);
			await this.loadFriendRequests();
			if (this.friendSearchQuery.trim().length >= 2) {
				await this.searchUsers(this.friendSearchQuery);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async removeFriend(friendshipId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.removeFriend(this.auth.idToken, friendshipId);
			await this.loadFriends();
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async searchUsers(query: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.friendSearchQuery = query;
		if (query.trim().length < 2) {
			this.userSearchResults = [];
			return;
		}
		this.isSearchingUsers = true;
		try {
			this.userSearchResults = await this.api.searchUsers(this.auth.idToken, query);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSearchingUsers = false;
		}
	}

	/** Reset all fields to initial values. */
	reset(): void {
		this.friends = [];
		this.incomingRequests = [];
		this.outgoingRequests = [];
		this.userSearchResults = [];
		this.isLoadingFriends = false;
		this.isSearchingUsers = false;
		this.friendSearchQuery = '';
	}
}
