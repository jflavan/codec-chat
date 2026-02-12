import type {
	MemberServer,
	DiscoverServer,
	Channel,
	Message,
	Reaction,
	Member,
	UserProfile,
	Friend,
	FriendRequest,
	UserSearchResult
} from '$lib/types/index.js';

export class ApiError extends Error {
	constructor(
		public readonly status: number,
		message?: string
	) {
		super(message ?? `API error: ${status}`);
		this.name = 'ApiError';
	}
}

/**
 * Thin HTTP wrapper around the Codec REST API.
 *
 * Every method requires an `idToken` for the `Authorization: Bearer` header.
 * Throws `ApiError` on non-2xx responses.
 */
export class ApiClient {
	constructor(private readonly baseUrl: string) {}

	/* ───── helpers ───── */

	private headers(token: string, json = false): HeadersInit {
		const h: Record<string, string> = { Authorization: `Bearer ${token}` };
		if (json) h['Content-Type'] = 'application/json';
		return h;
	}

	private async request<T>(url: string, init: RequestInit): Promise<T> {
		const response = await fetch(url, init);
		if (!response.ok) {
			const body = await response.json().catch(() => null);
			throw new ApiError(response.status, body?.error);
		}
		return response.json() as Promise<T>;
	}

	private async requestVoid(url: string, init: RequestInit): Promise<void> {
		const response = await fetch(url, init);
		if (!response.ok) {
			const body = await response.json().catch(() => null);
			throw new ApiError(response.status, body?.error);
		}
	}

	/* ───── User ───── */

	getMe(token: string): Promise<UserProfile> {
		return this.request(`${this.baseUrl}/me`, {
			headers: this.headers(token)
		});
	}

	/** Upload a custom global avatar image. */
	async uploadAvatar(token: string, file: File): Promise<{ avatarUrl: string }> {
		const form = new FormData();
		form.append('file', file);
		return this.request(`${this.baseUrl}/me/avatar`, {
			method: 'POST',
			headers: { Authorization: `Bearer ${token}` },
			body: form
		});
	}

	/** Remove the custom global avatar, reverting to the Google profile picture. */
	async deleteAvatar(token: string): Promise<{ avatarUrl: string }> {
		return this.request(`${this.baseUrl}/me/avatar`, {
			method: 'DELETE',
			headers: this.headers(token)
		});
	}

	/** Upload a server-specific avatar image. */
	async uploadServerAvatar(
		token: string,
		serverId: string,
		file: File
	): Promise<{ avatarUrl: string }> {
		const form = new FormData();
		form.append('file', file);
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/avatar`,
			{
				method: 'POST',
				headers: { Authorization: `Bearer ${token}` },
				body: form
			}
		);
	}

	/** Remove the server-specific avatar, falling back to the global avatar. */
	async deleteServerAvatar(
		token: string,
		serverId: string
	): Promise<{ avatarUrl: string }> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/avatar`,
			{
				method: 'DELETE',
				headers: this.headers(token)
			}
		);
	}

	/* ───── Servers ───── */

	getServers(token: string): Promise<MemberServer[]> {
		return this.request(`${this.baseUrl}/servers`, {
			headers: this.headers(token)
		});
	}

	getDiscoverServers(token: string): Promise<DiscoverServer[]> {
		return this.request(`${this.baseUrl}/servers/discover`, {
			headers: this.headers(token)
		});
	}

	createServer(token: string, name: string): Promise<{ id: string; name: string; role: string }> {
		return this.request(`${this.baseUrl}/servers`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ name })
		});
	}

	joinServer(token: string, serverId: string): Promise<void> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/join`, {
			method: 'POST',
			headers: this.headers(token)
		});
	}

	getMembers(token: string, serverId: string): Promise<Member[]> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members`, {
			headers: this.headers(token)
		});
	}

	/* ───── Channels ───── */

	getChannels(token: string, serverId: string): Promise<Channel[]> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels`, {
			headers: this.headers(token)
		});
	}

	createChannel(
		token: string,
		serverId: string,
		name: string
	): Promise<Channel> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ name })
		});
	}

	/* ───── Messages ───── */

	getMessages(token: string, channelId: string): Promise<Message[]> {
		return this.request(`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`, {
			headers: this.headers(token)
		});
	}

	sendMessage(token: string, channelId: string, body: string): Promise<Message> {
		return this.request(`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ body })
		});
	}

	/* ───── Reactions ───── */

	toggleReaction(
		token: string,
		channelId: string,
		messageId: string,
		emoji: string
	): Promise<{ action: string; reactions: Reaction[] }> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}/reactions`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({ emoji })
			}
		);
	}

	/* ───── Friends ───── */

	getFriends(token: string): Promise<Friend[]> {
		return this.request(`${this.baseUrl}/friends`, {
			headers: this.headers(token)
		});
	}

	removeFriend(token: string, friendshipId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/friends/${encodeURIComponent(friendshipId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	getFriendRequests(token: string, direction: 'received' | 'sent'): Promise<FriendRequest[]> {
		return this.request(
			`${this.baseUrl}/friends/requests?direction=${encodeURIComponent(direction)}`,
			{ headers: this.headers(token) }
		);
	}

	sendFriendRequest(token: string, recipientUserId: string): Promise<FriendRequest> {
		return this.request(`${this.baseUrl}/friends/requests`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ recipientUserId })
		});
	}

	respondToFriendRequest(
		token: string,
		requestId: string,
		action: 'accept' | 'decline'
	): Promise<FriendRequest> {
		return this.request(
			`${this.baseUrl}/friends/requests/${encodeURIComponent(requestId)}`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ action })
			}
		);
	}

	cancelFriendRequest(token: string, requestId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/friends/requests/${encodeURIComponent(requestId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── User Search ───── */

	searchUsers(token: string, query: string): Promise<UserSearchResult[]> {
		return this.request(
			`${this.baseUrl}/users/search?q=${encodeURIComponent(query)}`,
			{ headers: this.headers(token) }
		);
	}
}
