import { env } from '$env/dynamic/public';
import type { PaginatedResponse, AdminStats, AdminUser, AdminServer, Report, AdminAction, SystemAnnouncement } from '$lib/types/models';

const BASE = env.PUBLIC_API_BASE_URL || 'http://localhost:5050';

function getHeaders(): HeadersInit {
	const token = typeof localStorage !== 'undefined' ? localStorage.getItem('admin_token') : null;
	const headers: HeadersInit = { 'Content-Type': 'application/json' };
	if (token) headers['Authorization'] = `Bearer ${token}`;
	return headers;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
	const res = await fetch(`${BASE}${path}`, { headers: getHeaders(), ...options });
	if (!res.ok) {
		const body = await res.json().catch(() => ({}));
		throw new Error(body.detail || body.error || `HTTP ${res.status}`);
	}
	const text = await res.text();
	return text ? JSON.parse(text) : undefined;
}

export const adminApi = {
	getStats: () => request<AdminStats>('/admin/stats'),
	getUsers: (params: string) => request<PaginatedResponse<AdminUser>>(`/admin/users?${params}`),
	getUser: (id: string) => request<any>(`/admin/users/${id}`),
	disableUser: (id: string, reason: string) => request<void>(`/admin/users/${id}/disable`, { method: 'POST', body: JSON.stringify({ reason }) }),
	enableUser: (id: string) => request<void>(`/admin/users/${id}/enable`, { method: 'POST' }),
	forceLogout: (id: string) => request<void>(`/admin/users/${id}/force-logout`, { method: 'POST' }),
	resetPassword: (id: string) => request<void>(`/admin/users/${id}/reset-password`, { method: 'POST' }),
	setGlobalAdmin: (id: string, isGlobalAdmin: boolean) => request<void>(`/admin/users/${id}/global-admin`, { method: 'PUT', body: JSON.stringify({ isGlobalAdmin }) }),
	getServers: (params: string) => request<PaginatedResponse<AdminServer>>(`/admin/servers?${params}`),
	getServer: (id: string) => request<any>(`/admin/servers/${id}`),
	quarantineServer: (id: string, reason: string) => request<void>(`/admin/servers/${id}/quarantine`, { method: 'POST', body: JSON.stringify({ reason }) }),
	unquarantineServer: (id: string) => request<void>(`/admin/servers/${id}/unquarantine`, { method: 'POST' }),
	deleteServer: (id: string, reason: string) => request<void>(`/admin/servers/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) }),
	transferOwnership: (id: string, newOwnerUserId: string) => request<void>(`/admin/servers/${id}/transfer-ownership`, { method: 'PUT', body: JSON.stringify({ newOwnerUserId }) }),
	getReports: (params: string) => request<PaginatedResponse<Report>>(`/admin/reports?${params}`),
	getReport: (id: string) => request<any>(`/admin/reports/${id}`),
	updateReport: (id: string, data: any) => request<void>(`/admin/reports/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
	searchMessages: (params: string) => request<PaginatedResponse<any>>(`/admin/messages/search?${params}`),
	getAdminActions: (params: string) => request<PaginatedResponse<AdminAction>>(`/admin/actions?${params}`),
	getConnections: () => request<{ activeUsers: number }>('/admin/connections'),
	getAnnouncements: () => request<SystemAnnouncement[]>('/admin/announcements'),
	createAnnouncement: (data: { title: string; body: string; expiresAt?: string }) => request<{ id: string }>('/admin/announcements', { method: 'POST', body: JSON.stringify(data) }),
	updateAnnouncement: (id: string, data: any) => request<void>(`/admin/announcements/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
	deleteAnnouncement: (id: string) => request<void>(`/admin/announcements/${id}`, { method: 'DELETE' }),
	login: (email: string, password: string, recaptchaToken?: string) => request<{ accessToken: string; refreshToken: string }>('/auth/login', { method: 'POST', body: JSON.stringify({ email, password, recaptchaToken }) }),
	googleAuth: (idToken: string) => request<{ accessToken: string; refreshToken: string }>('/auth/google', { method: 'POST', body: JSON.stringify({ credential: idToken }) }),
	getMe: () => request<any>('/me'),
};
