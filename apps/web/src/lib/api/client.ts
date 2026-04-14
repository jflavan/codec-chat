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
	ServerInvite,
	VoiceChannelMember,
	ActiveCallResponse,
	CustomEmoji,
	SearchFilters,
	PaginatedSearchResults,
	AroundMessages,
	PinnedMessage,
	PresenceEntry,
	AuthResponse,
	TokenRefreshResponse,
	ChannelCategory,
	PaginatedAuditLog,
	NotificationPreferences,
	Webhook,
	WebhookDelivery,
	BannedMember,
	ChannelPermissionOverride,
	DiscordImport,
	DiscordUserMapping
} from '$lib/types/index.js';

export class ApiError extends Error {
	constructor(
		public readonly status: number,
		message?: string,
		public readonly data?: Record<string, unknown>
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

	/** Mutex: a single in-flight refresh promise shared by all concurrent 401 retries. */
	private refreshPromise: Promise<string | null> | null = null;

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

	/**
	 * Deduplicated token refresh: the first 401 triggers the actual refresh;
	 * concurrent 401s await the same promise instead of racing.
	 */
	private refreshTokenOnce(): Promise<string | null> {
		if (!this.refreshPromise) {
			this.refreshPromise = this.onUnauthorized!().finally(() => {
				this.refreshPromise = null;
			});
		}
		return this.refreshPromise;
	}

	private async retryWithFreshToken(url: string, init: RequestInit): Promise<Response> {
		const freshToken = await this.refreshTokenOnce();
		if (!freshToken) throw new ApiError(401, 'Token refresh failed');
		const retryHeaders = new Headers(init.headers);
		retryHeaders.set('Authorization', `Bearer ${freshToken}`);
		return fetch(url, { ...init, headers: retryHeaders });
	}

	private async request<T>(url: string, init: RequestInit): Promise<T> {
		let response = await fetch(url, init);

		// On 401, attempt a deduplicated silent token refresh and retry once.
		if (response.status === 401 && this.onUnauthorized) {
			response = await this.retryWithFreshToken(url, init);
		}

		if (!response.ok) {
			const body = await response.json().catch(() => null);
			const message = body?.error
				?? body?.detail
				?? (body?.errors ? Object.values(body.errors).flat().join('; ') : null);
			throw new ApiError(response.status, message ?? undefined, body ?? undefined);
		}
		return response.json() as Promise<T>;
	}

	private async requestVoid(url: string, init: RequestInit): Promise<void> {
		let response = await fetch(url, init);

		// On 401, attempt a deduplicated silent token refresh and retry once.
		if (response.status === 401 && this.onUnauthorized) {
			response = await this.retryWithFreshToken(url, init);
		}

		if (!response.ok) {
			const body = await response.json().catch(() => null);
			const message = body?.error
				?? body?.detail
				?? (body?.errors ? Object.values(body.errors).flat().join('; ') : null);
			throw new ApiError(response.status, message ?? undefined, body ?? undefined);
		}
	}

	/* ───── Auth (no 401 retry — these are unauthenticated endpoints) ───── */

	private async requestNoRetry<T>(url: string, init: RequestInit): Promise<T> {
		const response = await fetch(url, init);
		if (!response.ok) {
			const body = await response.json().catch(() => null);
			const message = body?.error
				?? body?.detail
				?? (body?.errors ? Object.values(body.errors).flat().join('; ') : null);
			throw new ApiError(response.status, message ?? undefined, body ?? undefined);
		}
		return response.json() as Promise<T>;
	}

	async register(email: string, password: string, nickname: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.requestNoRetry(`${this.baseUrl}/auth/register`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ email, password, nickname, recaptchaToken })
		});
	}

	async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.requestNoRetry(`${this.baseUrl}/auth/login`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ email, password, recaptchaToken })
		});
	}

	async googleSignIn(credential: string): Promise<AuthResponse> {
		return this.requestNoRetry(`${this.baseUrl}/auth/google`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ credential })
		});
	}

	async refreshToken(refreshToken: string): Promise<TokenRefreshResponse> {
		return this.requestNoRetry(`${this.baseUrl}/auth/refresh`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ refreshToken })
		});
	}

	async linkGoogle(email: string, password: string, googleCredential: string): Promise<AuthResponse> {
		return this.requestNoRetry(`${this.baseUrl}/auth/link-google`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ email, password, googleCredential })
		});
	}

	async logout(refreshToken: string): Promise<void> {
		await fetch(`${this.baseUrl}/auth/logout`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ refreshToken })
		}).catch(() => {});
	}

	async verifyEmail(token: string): Promise<{ message: string }> {
		return this.requestNoRetry(`${this.baseUrl}/auth/verify-email`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ token })
		});
	}

	async resendVerification(accessToken: string): Promise<{ message: string }> {
		return this.request(`${this.baseUrl}/auth/resend-verification`, {
			method: 'POST',
			headers: this.headers(accessToken)
		});
	}

	async oauthCallback(provider: 'github' | 'discord', code: string): Promise<AuthResponse & { isNewUser?: boolean }> {
		return this.requestNoRetry(`${this.baseUrl}/auth/oauth/${provider}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ code })
		});
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

	/** Permanently delete the authenticated user's account. */
	async deleteAccount(
		token: string,
		confirmationText: string,
		password?: string,
		googleCredential?: string
	): Promise<{ message: string }> {
		return this.request(`${this.baseUrl}/me`, {
			method: 'DELETE',
			headers: this.headers(token, true),
			body: JSON.stringify({ confirmationText, password, googleCredential })
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

	/** Set or update the current user's custom status message. */
	async setStatus(
		token: string,
		statusText?: string | null,
		statusEmoji?: string | null
	): Promise<{ statusText: string | null; statusEmoji: string | null }> {
		return this.request(`${this.baseUrl}/me/status`, {
			method: 'PUT',
			headers: this.headers(token, true),
			body: JSON.stringify({ statusText, statusEmoji })
		});
	}

	/** Clear the current user's custom status message. */
	async clearStatus(
		token: string
	): Promise<{ statusText: null; statusEmoji: null }> {
		return this.request(`${this.baseUrl}/me/status`, {
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

	/** Persist the user's custom server display order. */
	reorderServers(token: string, serverIds: string[]): Promise<void> {
		return this.requestVoid(`${this.baseUrl}/servers/reorder`, {
			method: 'PUT',
			headers: this.headers(token, true),
			body: JSON.stringify({ serverIds })
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
		name: string,
		type?: 'text' | 'voice'
	): Promise<Channel> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ name, type: type ?? 'text' })
		});
	}

	updateServer(
		token: string,
		serverId: string,
		data: { name?: string; description?: string }
	): Promise<{ id: string; name: string; iconUrl?: string | null }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}`, {
			method: 'PATCH',
			headers: this.headers(token, true),
			body: JSON.stringify(data)
		});
	}

	/** Upload or update a server icon image. */
	async uploadServerIcon(
		token: string,
		serverId: string,
		file: File
	): Promise<{ iconUrl: string }> {
		const form = new FormData();
		form.append('file', file);
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/icon`,
			{
				method: 'POST',
				headers: { Authorization: `Bearer ${token}` },
				body: form
			}
		);
	}

	/** Remove the server icon. */
	async deleteServerIcon(token: string, serverId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/icon`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	updateChannel(
		token: string,
		serverId: string,
		channelId: string,
		data: { name?: string; description?: string }
	): Promise<{ id: string; name: string; serverId: string }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels/${encodeURIComponent(channelId)}`, {
			method: 'PATCH',
			headers: this.headers(token, true),
			body: JSON.stringify(data)
		});
	}

	/* ───── Categories ───── */

	/** List all channel categories for a server. */
	getCategories(token: string, serverId: string): Promise<ChannelCategory[]> {
		return this.request<ChannelCategory[]>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/categories`,
			{ headers: this.headers(token) }
		);
	}

	/** Create a new channel category in a server. */
	createCategory(token: string, serverId: string, name: string): Promise<ChannelCategory> {
		return this.request<ChannelCategory>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/categories`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({ name })
			}
		);
	}

	/** Rename a channel category. */
	renameCategory(token: string, serverId: string, categoryId: string, name: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/categories/${encodeURIComponent(categoryId)}`,
			{
				method: 'PATCH',
				headers: this.headers(token, true),
				body: JSON.stringify({ name })
			}
		);
	}

	/** Delete a channel category. */
	deleteCategory(token: string, serverId: string, categoryId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/categories/${encodeURIComponent(categoryId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Update channel positions and category assignments in a server. */
	updateChannelOrder(
		token: string,
		serverId: string,
		channels: { channelId: string; categoryId?: string; position: number }[]
	): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channel-order`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ channels })
			}
		);
	}

	/** Update category positions in a server. */
	updateCategoryOrder(
		token: string,
		serverId: string,
		categories: { categoryId: string; position: number }[]
	): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/category-order`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ categories })
			}
		);
	}

	/* ───── Audit Log ───── */

	/** Get paginated audit log entries for a server. */
	getAuditLog(
		token: string,
		serverId: string,
		options?: { before?: string; limit?: number }
	): Promise<PaginatedAuditLog> {
		const params = new URLSearchParams();
		if (options?.before) params.set('before', options.before);
		if (options?.limit) params.set('limit', options.limit.toString());
		const query = params.toString();
		return this.request<PaginatedAuditLog>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/audit-log${query ? `?${query}` : ''}`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Notification Preferences ───── */

	/** Mute or unmute an entire server for the current user. */
	muteServer(token: string, serverId: string, isMuted: boolean): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/mute`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ isMuted })
			}
		);
	}

	/** Mute or unmute a specific channel for the current user. */
	muteChannel(token: string, serverId: string, channelId: string, isMuted: boolean): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/channels/${encodeURIComponent(channelId)}/mute`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ isMuted })
			}
		);
	}

	/** Get notification preferences for a server. */
	getNotificationPreferences(token: string, serverId: string): Promise<NotificationPreferences> {
		return this.request<NotificationPreferences>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/notification-preferences`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Push Subscriptions ───── */

	/** Get the server's VAPID public key for push subscriptions. */
	getVapidPublicKey(): Promise<{ publicKey: string }> {
		return this.request(`${this.baseUrl}/push-subscriptions/vapid-key`, {});
	}

	/** Register a push subscription with the server. */
	subscribePush(token: string, subscription: { endpoint: string; p256dh: string; auth: string }): Promise<void> {
		return this.request(`${this.baseUrl}/push-subscriptions`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify(subscription)
		});
	}

	/** Remove a push subscription from the server. */
	unsubscribePush(token: string, endpoint: string): Promise<void> {
		return this.request(`${this.baseUrl}/push-subscriptions`, {
			method: 'DELETE',
			headers: this.headers(token, true),
			body: JSON.stringify({ endpoint })
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

	/** Upload a general file attachment. Returns URL and metadata. */
	async uploadFile(token: string, file: File): Promise<{ fileUrl: string; fileName: string; fileSize: number; fileContentType: string }> {
		const form = new FormData();
		form.append('file', file);
		return this.request(`${this.baseUrl}/uploads/files`, {
			method: 'POST',
			headers: { Authorization: `Bearer ${token}` },
			body: form
		});
	}

	sendMessage(token: string, channelId: string, body: string, imageUrl?: string | null, replyToMessageId?: string | null, fileFields?: { fileUrl: string; fileName: string; fileSize: number; fileContentType: string } | null): Promise<Message> {
		return this.request(`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({
				body,
				imageUrl: imageUrl ?? null,
				fileUrl: fileFields?.fileUrl ?? null,
				fileName: fileFields?.fileName ?? null,
				fileSize: fileFields?.fileSize ?? null,
				fileContentType: fileFields?.fileContentType ?? null,
				replyToMessageId: replyToMessageId ?? null
			})
		});
	}

	deleteMessage(token: string, channelId: string, messageId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages/${encodeURIComponent(messageId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	purgeChannel(token: string, channelId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`,
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

	getPinnedMessages(token: string, channelId: string): Promise<PinnedMessage[]> {
		return this.request(`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/pins`, {
			headers: this.headers(token)
		});
	}

	pinMessage(token: string, channelId: string, messageId: string): Promise<PinnedMessage> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/pins/${encodeURIComponent(messageId)}`,
			{
				method: 'POST',
				headers: this.headers(token)
			}
		);
	}

	unpinMessage(token: string, channelId: string, messageId: string): Promise<void> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/pins/${encodeURIComponent(messageId)}`,
			{
				method: 'DELETE',
				headers: this.headers(token)
			}
		);
	}

	toggleDmReaction(
		token: string,
		dmChannelId: string,
		messageId: string,
		emoji: string
	): Promise<{ action: string; reactions: Reaction[] }> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(dmChannelId)}/messages/${encodeURIComponent(messageId)}/reactions`,
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

	sendDm(token: string, channelId: string, body: string, imageUrl?: string | null, replyToDirectMessageId?: string | null, fileFields?: { fileUrl: string; fileName: string; fileSize: number; fileContentType: string } | null): Promise<DirectMessage> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(channelId)}/messages`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({
					body,
					imageUrl: imageUrl ?? null,
					fileUrl: fileFields?.fileUrl ?? null,
					fileName: fileFields?.fileName ?? null,
					fileSize: fileFields?.fileSize ?? null,
					fileContentType: fileFields?.fileContentType ?? null,
					replyToDirectMessageId: replyToDirectMessageId ?? null
				})
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

	/** Change a member's role in a server (requires ManageRoles permission). */
	updateMemberRole(token: string, serverId: string, userId: string, role: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}/role`,
			{ method: 'PATCH', headers: this.headers(token, true), body: JSON.stringify({ role }) }
		);
	}

	/* ───── Server Bans ───── */

	/** Ban a member from a server (requires Owner or Admin role). */
	banMember(token: string, serverId: string, userId: string, options?: { reason?: string; deleteMessages?: boolean }): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/bans/${encodeURIComponent(userId)}`,
			{ method: 'POST', headers: this.headers(token, true), body: JSON.stringify({ reason: options?.reason, deleteMessages: options?.deleteMessages ?? false }) }
		);
	}

	/** Unban a user from a server (requires Owner or Admin role). */
	unbanMember(token: string, serverId: string, userId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/bans/${encodeURIComponent(userId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Get banned users for a server (requires Owner or Admin role). */
	getBans(token: string, serverId: string): Promise<BannedMember[]> {
		return this.request<BannedMember[]>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/bans`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Server Roles ───── */

	/** List all roles for a server. */
	getRoles(token: string, serverId: string): Promise<import('$lib/types/models.js').ServerRole[]> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/roles`,
			{ headers: this.headers(token) }
		);
	}

	/** Create a new role in a server. */
	createRole(
		token: string,
		serverId: string,
		name: string,
		options?: { color?: string; permissions?: number; isHoisted?: boolean; isMentionable?: boolean }
	): Promise<import('$lib/types/models.js').ServerRole> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/roles`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify({ name, ...options })
			}
		);
	}

	/** Update a role in a server. */
	updateRole(
		token: string,
		serverId: string,
		roleId: string,
		updates: { name?: string; color?: string | null; permissions?: number; isHoisted?: boolean; isMentionable?: boolean }
	): Promise<import('$lib/types/models.js').ServerRole> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/roles/${encodeURIComponent(roleId)}`,
			{
				method: 'PATCH',
				headers: this.headers(token, true),
				body: JSON.stringify(updates)
			}
		);
	}

	/** Delete a role from a server. */
	deleteRole(token: string, serverId: string, roleId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/roles/${encodeURIComponent(roleId)}`,
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

	/** Get a preview of an invite (server name, icon, member count). */
	getInvitePreview(token: string, code: string): Promise<{ serverName: string; serverIcon: string | null; memberCount: number }> {
		return this.request(
			`${this.baseUrl}/invites/${encodeURIComponent(code)}`,
			{ headers: this.headers(token) }
		);
	}

	/** Join a server using an invite code. */
	joinViaInvite(token: string, code: string): Promise<{ serverId: string; userId: string; role: string }> {
		return this.request(
			`${this.baseUrl}/invites/${encodeURIComponent(code)}`,
			{ method: 'POST', headers: this.headers(token) }
		);
	}

	/* ───── Webhooks ───── */

	/** List webhooks for a server (requires Owner or Admin role). */
	getWebhooks(token: string, serverId: string): Promise<Webhook[]> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/webhooks`,
			{ headers: this.headers(token) }
		);
	}

	/** Create a new webhook for a server (requires Owner or Admin role). */
	createWebhook(
		token: string,
		serverId: string,
		data: { name: string; url: string; secret?: string; eventTypes: string[] }
	): Promise<Webhook> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/webhooks`,
			{
				method: 'POST',
				headers: this.headers(token, true),
				body: JSON.stringify(data)
			}
		);
	}

	/** Update an existing webhook (requires Owner or Admin role). */
	updateWebhook(
		token: string,
		serverId: string,
		webhookId: string,
		data: { name?: string; url?: string; secret?: string; eventTypes?: string[]; isActive?: boolean }
	): Promise<Webhook> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/webhooks/${encodeURIComponent(webhookId)}`,
			{
				method: 'PATCH',
				headers: this.headers(token, true),
				body: JSON.stringify(data)
			}
		);
	}

	/** Delete a webhook (requires Owner or Admin role). */
	deleteWebhook(token: string, serverId: string, webhookId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/webhooks/${encodeURIComponent(webhookId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Get recent delivery logs for a webhook (requires Owner or Admin role). */
	getWebhookDeliveries(token: string, serverId: string, webhookId: string): Promise<WebhookDelivery[]> {
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/webhooks/${encodeURIComponent(webhookId)}/deliveries`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Custom Emojis ───── */

	/** List all custom emojis for a server. */
	getCustomEmojis(token: string, serverId: string): Promise<CustomEmoji[]> {
		return this.request<CustomEmoji[]>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/emojis`,
			{ headers: this.headers(token) }
		);
	}

	/** Upload a new custom emoji to a server. */
	async uploadCustomEmoji(token: string, serverId: string, name: string, file: File): Promise<CustomEmoji> {
		const form = new FormData();
		form.append('name', name);
		form.append('file', file);
		return this.request<CustomEmoji>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/emojis`,
			{ method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: form }
		);
	}

	/** Rename a custom emoji. */
	renameCustomEmoji(token: string, serverId: string, emojiId: string, name: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/emojis/${encodeURIComponent(emojiId)}`,
			{ method: 'PATCH', headers: this.headers(token, true), body: JSON.stringify({ name }) }
		);
	}

	/** Delete a custom emoji from a server. */
	deleteCustomEmoji(token: string, serverId: string, emojiId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/emojis/${encodeURIComponent(emojiId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── Voice ───── */

	/** Get current voice channel members. */
	getVoiceStates(token: string, channelId: string): Promise<VoiceChannelMember[]> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/voice-states`,
			{ headers: this.headers(token) }
		);
	}

	/** Update the current user's mute/deafen state. */
	updateVoiceState(token: string, isMuted: boolean, isDeafened: boolean): Promise<void> {
		return this.requestVoid(`${this.baseUrl}/voice/state`, {
			method: 'PATCH',
			headers: this.headers(token, true),
			body: JSON.stringify({ isMuted, isDeafened })
		});
	}

	/** Get the active or ringing call for the current user, or null. */
	async getActiveCall(token: string): Promise<ActiveCallResponse | null> {
		const response = await fetch(`${this.baseUrl}/voice/active-call`, {
			headers: this.headers(token)
		});
		if (response.status === 204) return null;
		if (!response.ok) {
			const body = await response.json().catch(() => null);
			const message = body?.error
				?? body?.detail
				?? (body?.errors ? Object.values(body.errors).flat().join('; ') : null);
			throw new ApiError(response.status, message ?? undefined, body ?? undefined);
		}
		return response.json() as Promise<ActiveCallResponse>;
	}

	/** Get a LiveKit access token for a voice room. */
	getLiveKitToken(token: string, roomName: string): Promise<{ token: string }> {
		return this.request(
			`${this.baseUrl}/voice/token?roomName=${encodeURIComponent(roomName)}`,
			{
				headers: this.headers(token)
			}
		);
	}

	/* ───── Bug Reports ───── */

	submitBugReport(
		token: string,
		title: string,
		description: string,
		userAgent: string,
		currentPage: string
	): Promise<{ issueUrl: string }> {
		return this.request(`${this.baseUrl}/issues`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ title, description, userAgent, currentPage })
		});
	}

	/* ───── Reports ───── */

	/** Submit an abuse report for a user, message, or server. */
	submitReport(
		token: string,
		data: { reportType: number; targetId: string; reason: string }
	): Promise<{ id: string }> {
		return this.request(`${this.baseUrl}/reports`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify(data)
		});
	}

	/* ───── Announcements ───── */

	/** Fetch active system announcements. */
	getActiveAnnouncements(
		token: string
	): Promise<Array<{ id: string; title: string; body: string; createdAt: string; expiresAt: string | null }>> {
		return this.request(`${this.baseUrl}/announcements/active`, {
			headers: this.headers(token)
		});
	}

	/* ───── Message Search ───── */

	/** Search messages within a server. */
	searchServerMessages(
		token: string,
		serverId: string,
		query: string,
		filters: SearchFilters = {}
	): Promise<PaginatedSearchResults> {
		const params = new URLSearchParams({ q: query });
		if (filters.channelId) params.set('channelId', filters.channelId);
		if (filters.authorId) params.set('authorId', filters.authorId);
		if (filters.before) params.set('before', filters.before);
		if (filters.after) params.set('after', filters.after);
		if (filters.has) params.set('has', filters.has);
		if (filters.page) params.set('page', String(filters.page));
		if (filters.pageSize) params.set('pageSize', String(filters.pageSize));
		return this.request(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/search?${params}`,
			{ headers: this.headers(token) }
		);
	}

	/** Search messages across the current user's DM conversations. */
	searchDmMessages(
		token: string,
		query: string,
		filters: SearchFilters = {}
	): Promise<PaginatedSearchResults> {
		const params = new URLSearchParams({ q: query });
		if (filters.channelId) params.set('channelId', filters.channelId);
		if (filters.authorId) params.set('authorId', filters.authorId);
		if (filters.before) params.set('before', filters.before);
		if (filters.after) params.set('after', filters.after);
		if (filters.has) params.set('has', filters.has);
		if (filters.page) params.set('page', String(filters.page));
		if (filters.pageSize) params.set('pageSize', String(filters.pageSize));
		return this.request(
			`${this.baseUrl}/dm/search?${params}`,
			{ headers: this.headers(token) }
		);
	}

	/** Get messages around a target message in a channel. */
	getMessagesAround(
		token: string,
		channelId: string,
		messageId: string,
		limit: number = 50
	): Promise<AroundMessages> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages?around=${encodeURIComponent(messageId)}&limit=${limit}`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Presence ───── */

	/** Get online/idle/offline presence for all members of a server. */
	getServerPresence(token: string, serverId: string): Promise<PresenceEntry[]> {
		return this.request<PresenceEntry[]>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/presence`,
			{ headers: this.headers(token) }
		);
	}

	/** Get presence for users the current user has DM conversations with. */
	getDmPresence(token: string): Promise<PresenceEntry[]> {
		return this.request<PresenceEntry[]>(
			`${this.baseUrl}/dm/presence`,
			{ headers: this.headers(token) }
		);
	}

	/** Get messages around a target message in a DM channel. */
	getDmMessagesAround(
		token: string,
		dmChannelId: string,
		messageId: string,
		limit: number = 50
	): Promise<AroundMessages> {
		return this.request(
			`${this.baseUrl}/dm/channels/${encodeURIComponent(dmChannelId)}/messages?around=${encodeURIComponent(messageId)}&limit=${limit}`,
			{ headers: this.headers(token) }
		);
	}

	/* ───── Member Roles ───── */

	/** Replace all roles assigned to a member. */
	setMemberRoles(token: string, serverId: string, userId: string, roleIds: string[]): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}/roles`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ roleIds })
			}
		);
	}

	/** Add a single role to a member. */
	addMemberRole(token: string, serverId: string, userId: string, roleId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}/roles/${encodeURIComponent(roleId)}`,
			{ method: 'POST', headers: this.headers(token) }
		);
	}

	/** Remove a single role from a member. */
	removeMemberRole(token: string, serverId: string, userId: string, roleId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}/roles/${encodeURIComponent(roleId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── Channel Permission Overrides ───── */

	/** Get all permission overrides for a channel. */
	getChannelOverrides(token: string, channelId: string): Promise<ChannelPermissionOverride[]> {
		return this.request(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/overrides`,
			{ headers: this.headers(token) }
		);
	}

	/** Set or update a permission override for a role in a channel. */
	setChannelOverride(token: string, channelId: string, roleId: string, allow: number, deny: number): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/overrides/${encodeURIComponent(roleId)}`,
			{
				method: 'PUT',
				headers: this.headers(token, true),
				body: JSON.stringify({ allow, deny })
			}
		);
	}

	/** Delete a permission override for a role in a channel. */
	deleteChannelOverride(token: string, channelId: string, roleId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/channels/${encodeURIComponent(channelId)}/overrides/${encodeURIComponent(roleId)}`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/* ───── Discord Import ───── */

	/** Start a new Discord import job for a server. */
	startDiscordImport(
		token: string,
		serverId: string,
		botToken: string,
		discordGuildId: string
	): Promise<{ id: string }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ botToken, discordGuildId })
		});
	}

	/** Get the current Discord import job status for a server. */
	getDiscordImportStatus(token: string, serverId: string): Promise<DiscordImport> {
		return this.request<DiscordImport>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`,
			{ headers: this.headers(token) }
		);
	}

	/** Resync (re-run) a Discord import for a server. */
	resyncDiscordImport(
		token: string,
		serverId: string,
		botToken: string,
		discordGuildId: string
	): Promise<{ id: string }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/resync`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ botToken, discordGuildId })
		});
	}

	/** Cancel an in-progress Discord import job. */
	cancelDiscordImport(token: string, serverId: string): Promise<void> {
		return this.requestVoid(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import`,
			{ method: 'DELETE', headers: this.headers(token) }
		);
	}

	/** Get Discord user mappings for identity claiming within a server. */
	getDiscordUserMappings(token: string, serverId: string): Promise<DiscordUserMapping[]> {
		return this.request<DiscordUserMapping[]>(
			`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/mappings`,
			{ headers: this.headers(token) }
		);
	}

	/** Claim a Discord identity for the current user within a server. */
	claimDiscordIdentity(token: string, serverId: string, discordUserId: string): Promise<{ claimed: boolean }> {
		return this.request(`${this.baseUrl}/servers/${encodeURIComponent(serverId)}/discord-import/claim`, {
			method: 'POST',
			headers: this.headers(token, true),
			body: JSON.stringify({ discordUserId })
		});
	}
}
