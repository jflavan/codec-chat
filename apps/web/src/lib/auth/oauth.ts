import { env } from '$env/dynamic/public';

export type OAuthProvider = 'github' | 'discord';

const API_BASE = env.PUBLIC_API_BASE_URL ?? 'http://localhost:5050';

/** Cached OAuth config from the API. */
let configCache: { github: { clientId: string; enabled: boolean }; discord: { clientId: string; enabled: boolean } } | null = null;

export async function getOAuthConfig(): Promise<typeof configCache> {
	if (configCache) return configCache;
	try {
		const res = await fetch(`${API_BASE}/auth/oauth/config`);
		if (res.ok) {
			configCache = await res.json();
		}
	} catch {
		// Silently fail — OAuth buttons just won't show
	}
	return configCache;
}

/** Build the GitHub OAuth authorization URL. */
export function getGitHubAuthUrl(clientId: string): string {
	const redirectUri = `${window.location.origin}/auth/callback/github`;
	const params = new URLSearchParams({
		client_id: clientId,
		redirect_uri: redirectUri,
		scope: 'read:user user:email',
		state: generateState()
	});
	return `https://github.com/login/oauth/authorize?${params}`;
}

/** Build the Discord OAuth authorization URL. */
export function getDiscordAuthUrl(clientId: string): string {
	const redirectUri = `${window.location.origin}/auth/callback/discord`;
	const params = new URLSearchParams({
		client_id: clientId,
		redirect_uri: redirectUri,
		response_type: 'code',
		scope: 'identify email',
		state: generateState()
	});
	return `https://discord.com/oauth2/authorize?${params}`;
}

/** Generate and store a random state parameter for CSRF protection. */
function generateState(): string {
	const array = new Uint8Array(32);
	crypto.getRandomValues(array);
	const state = Array.from(array, (b) => b.toString(16).padStart(2, '0')).join('');
	sessionStorage.setItem('oauth_state', state);
	return state;
}

/** Validate the returned state parameter. */
export function validateState(state: string): boolean {
	const stored = sessionStorage.getItem('oauth_state');
	sessionStorage.removeItem('oauth_state');
	return stored === state;
}
