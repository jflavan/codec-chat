import type {
	MemberServer,
	Channel,
	Message,
	PaginatedMessages,
	PaginatedDmMessages,
	Reaction,
	Member,
	UserProfile,
	Friend,
	FriendRequest,
	UserSearchResult,
	DmConversation,
	DirectMessage,
	ServerInvite
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
	private onUnauthorized?: () => Promise<string | null>;

	constructor(
		private readonly baseUrl: string,
		onUnauthorized?: () => Promise<string | null>
	) {
		this.onUnauthorized = onUnauthorized;
	}

	/* ───── helpers ───── */

	private headers(token: string, json = false): HeadersInit {
		const h: Record<string, string> = { Authorization: `Bearer ${token}` };
		if (json) h['Content-Type'] = 'application/json';
		return h;
	}

	private async request<T>(url: string, init: RequestInit): Promise<T> {
		let response = await fetch(url, init);

		// On 401, attempt a silent token refresh and retry the request once.
		if (response.status === 401 && this.onUnauthorized) {
			const freshToken = await this.onUnauthorized();
			if (freshToken) {
				const retryHeaders = new Headers(init.headers);
				retryHeaders.set('Authorization', `Bearer ${freshToken}`);
				response = await fetch(url, { ...init, headers: retryHeaders });
			}
		}

		if (!response.ok) {
			const body = await response.json().catch(() => null);
			throw new ApiError(response.status, body?.error);
		}
		return response.json() as Promise<T>;
	}

	private async requestVoid(url: string, init: RequestInit): Promise<void> {
		let response = await fetch(url, init);

		// On 401, attempt a silent token refresh and retry the request once.
		if (response.status === 401 && this.onUnauthorized) {
			const freshToken = await this.onUnauthorized();
			if (freshToken) {
				const retryHeaders = new Headers(init.headers);
				retryHeaders.set('Authorization', `Bearer ${freshToken}`);
				response = await fetch(url, { ...init, headers: retryHeaders });
			}
		}

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

	/** Set or update the current user's nickname. */
	async setNickname(
		token: string,
		nickname: string
	): Promise<{ nickname: string; effectiveDisplayName: string }> {
		return this.request(`${this.baseUrl}/me/nickname`, {
			method: 'PUT',
			headers: this.headers(token, true),
			body: JSON.stringify({ nickname })
		});
	}

	/** Remove the current user's nickname, reverting to the Google display name. */
	async removeNickname(
		token: string
	): Promise<{ nickname: null; effectiveDisplayName: string }> {
		return this.request(`${this.baseUrl}/me/nickname`, {
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

	createServer(token: string, name: string): Promise<{ id: string; name: string; role: string }> {
		return this.request(`${this.baseUrl}/servers`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ name })
		});
	}

	/** Delete a server (requires Owner role or global admin). */
	deleteServer(token: string, serverId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Delete a channel from a server (requires Owner/Admin role or global admin). */
	deleteChannel(token: string, serverId: string, channelId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels/${encodeURIComponent(channelId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
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

	updateServer(
		token: string,
		serverId: string,
		name: string
	): Promise<{ id: string; name: string }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}`, {
			method: 'PATCH',
			headers: this.headers(token, true),
			body: JSON.stringify({ name })
		});
	}

	updateChannel(
		token: string,
		serverId: string,
		channelId: string,
		name: string
	): Promise<{ id: string; name: string; serverId: string }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels/${encodeURIComponent(channelId)}`, {
			method: 'PATCH',
			headers: this.headers(token, true),
			body: JSON.stringify({ name })
		});
	}

	/* ───── Messages ───── */

	getMessages(token: string, channelId: string, options?: { before?: string; limit?: number }): Promise<PaginatedMessages> {
		const params = new URLSearchParams();
		if (options?.before) params.set('before', options.before);
		if (options?.limit) params.set('limit', String(options.limit));
		const qs = params.toString();
		const url = `${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages${qs ? `?${qs}` : ''}`;
		return this.request(url, {
			headers: this.headers(token)
		});
	}

	/** Upload a chat image. Returns the public URL for the uploaded image. */
	async uploadImage(token: string, file: File): Promise<{ imageUrl: string }> {
		const form = new FormData();
		form.append('file', file);
		return this.request(`${this.baseUrl}/uploads/images`, {
			method: 'POST',
			headers: { Authorization: `Bearer ${token}` },
			body: form
		});
	}

	sendMessage(token: string, channelId: string, body: string, imageUrl?: string | null, replyToMessageId?: string | null): Promise<Message> {
		return this.request(`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ body, imageUrl: imageUrl ?? null, replyToMessageId: replyToMessageId ?? null })
		});
	}

	deleteMessage(token: string, channelId: string, messageId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	editMessage(token: string, channelId: string, messageId: string, body: string): Promise<{ id: string; body: string; editedAt: string }> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ body })
			}
		);
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

	/* ───── Direct Messages ───── */

	createOrResumeDm(
		token: string,
		recipientUserId: string
	): Promise<{ id: string; participant: { id: string; displayName: string; avatarUrl?: string | null }; createdAt: string }> {
		return this.request(`${this.baseUrl}/dm/channels`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ recipientUserId })
		});
	}

	getDmConversations(token: string): Promise<DmConversation[]> {
		return this.request(`${this.baseUrl}/dm/channels`, {
			headers: this.headers(token)
		});
	}

	getDmMessages(token: string, channelId: string): Promise<PaginatedDmMessages> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}/messages`,
			{ headers: this.headers(token) }
		);
	}

	sendDm(token: string, channelId: string, body: string, imageUrl?: string | null, replyToDirectMessageId?: string | null): Promise<DirectMessage> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}/messages`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({ body, imageUrl: imageUrl ?? null, replyToDirectMessageId: replyToDirectMessageId ?? null })
			}
		);
	}

	deleteDmMessage(token: string, channelId: string, messageId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	editDmMessage(token: string, channelId: string, messageId: string, body: string): Promise<{ id: string; body: string; editedAt: string }> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ body })
			}
		);
	}

	closeDmConversation(token: string, channelId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── Server Moderation ───── */

	/** Kick a member from a server (requires Owner or Admin role). */
	kickMember(token: string, serverId: string, userId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── Server Invites ───── */

	/** Create an invite code for a server (requires Owner or Admin role). */
	createInvite(
		token: string,
		serverId: string,
		options?: { maxUses?: number | null; expiresInHours?: number | null }
	): Promise<ServerInvite> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/invites`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({
					maxUses: options?.maxUses ?? null,
					expiresInHours: options?.expiresInHours ?? null
				})
			}
		);
	}

	/** List active invites for a server (requires Owner or Admin role). */
	getInvites(token: string, serverId: string): Promise<ServerInvite[]> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/invites`,
			{ headers: this.headers(token) }
		);
	}

	/** Revoke an invite code (requires Owner or Admin role). */
	revokeInvite(token: string, serverId: string, inviteId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/invites/${encodeURIComponent(inviteId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Join a server using an invite code. */
	joinViaInvite(token: string, code: string): Promise<{ serverId: string; userId: string; role: string }> {
		return this.request(
			`${this.baseUrl}/invites/${encodeURIComponent(code)}`,
			{ method: 'POST', headers: this.headers(token) }
		);
	}
}
