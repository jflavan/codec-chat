const TOKEN_KEY = 'codec_id_token';
const LOGIN_TS_KEY = 'codec_login_ts';
const AUTH_TYPE_KEY = 'codec_auth_type';
const REFRESH_TOKEN_KEY = 'codec_refresh_token';
const MAX_SESSION_MS = 7 * 24 * 60 * 60 * 1000; // 1 week

export type AuthType = 'google' | 'local';

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
	localStorage.removeItem(AUTH_TYPE_KEY);
	localStorage.removeItem(REFRESH_TOKEN_KEY);
}

export function getAuthType(): AuthType {
	return (localStorage.getItem(AUTH_TYPE_KEY) as AuthType) ?? 'google';
}

export function setAuthType(type: AuthType): void {
	localStorage.setItem(AUTH_TYPE_KEY, type);
}

export function persistRefreshToken(token: string): void {
	localStorage.setItem(REFRESH_TOKEN_KEY, token);
}

export function loadStoredRefreshToken(): string | null {
	return localStorage.getItem(REFRESH_TOKEN_KEY);
}
