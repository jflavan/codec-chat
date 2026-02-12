import type {
	MemberServer,
	DiscoverServer,
	Channel,
	Message,
	Member,
	UserProfile
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

	/* ───── User ───── */

	getMe(token: string): Promise<UserProfile> {
		return this.request(`${this.baseUrl}/me`, {
			headers: this.headers(token)
		});
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
}
