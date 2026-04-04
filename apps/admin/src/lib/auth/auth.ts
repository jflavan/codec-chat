import { adminApi } from '$lib/api/client';

export function getToken(): string | null {
	return typeof localStorage !== 'undefined' ? localStorage.getItem('admin_token') : null;
}

export function setToken(token: string): void {
	localStorage.setItem('admin_token', token);
}

export function clearToken(): void {
	localStorage.removeItem('admin_token');
	localStorage.removeItem('admin_refresh_token');
}

export async function verifyAdmin(): Promise<boolean> {
	try {
		const me = await adminApi.getMe();
		return me.user?.isGlobalAdmin === true;
	} catch {
		return false;
	}
}
