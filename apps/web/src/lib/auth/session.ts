const TOKEN_KEY = 'codec_id_token';
const LOGIN_TS_KEY = 'codec_login_ts';
const MAX_SESSION_MS = 7 * 24 * 60 * 60 * 1000; // 1 week

/** Decode a JWT payload without verifying the signature (client-side only). */
function decodeJwtPayload(token: string): Record<string, unknown> | null {
	try {
		const base64 = token.split('.')[1];
		const json = atob(base64.replace(/-/g, '+').replace(/_/g, '/'));
		return JSON.parse(json);
	} catch {
		return null;
	}
}

export function isTokenExpired(token: string): boolean {
	const payload = decodeJwtPayload(token);
	if (!payload || typeof payload.exp !== 'number') return true;
	return Date.now() >= (payload.exp - 60) * 1000;
}

export function isSessionExpired(): boolean {
	const loginTs = localStorage.getItem(LOGIN_TS_KEY);
	if (!loginTs) return true;
	return Date.now() - Number(loginTs) > MAX_SESSION_MS;
}

export function persistToken(token: string): void {
	localStorage.setItem(TOKEN_KEY, token);
	if (!localStorage.getItem(LOGIN_TS_KEY)) {
		localStorage.setItem(LOGIN_TS_KEY, String(Date.now()));
	}
}

export function loadStoredToken(): string | null {
	return localStorage.getItem(TOKEN_KEY);
}

export function clearSession(): void {
	localStorage.removeItem(TOKEN_KEY);
	localStorage.removeItem(LOGIN_TS_KEY);
}
