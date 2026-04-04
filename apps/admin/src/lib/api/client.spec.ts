import { describe, it, expect, vi, beforeEach } from 'vitest';

// ---------------------------------------------------------------------------
// Mock $env/dynamic/public — resolved via vitest.config.ts alias
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Mock localStorage
// ---------------------------------------------------------------------------
const mockStorage: Record<string, string> = {};
vi.stubGlobal('localStorage', {
	getItem: (key: string) => mockStorage[key] ?? null,
	setItem: (key: string, value: string) => {
		mockStorage[key] = value;
	},
	removeItem: (key: string) => {
		delete mockStorage[key];
	}
});

// ---------------------------------------------------------------------------
// Import after globals are stubbed
// ---------------------------------------------------------------------------
import { adminApi } from './client';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function makeResponse(body: unknown, ok = true, status = 200): Response {
	return {
		ok,
		status,
		text: () => Promise.resolve(JSON.stringify(body)),
		json: () => Promise.resolve(body)
	} as unknown as Response;
}

function makeEmptyResponse(ok = true, status = 204): Response {
	return {
		ok,
		status,
		text: () => Promise.resolve(''),
		json: () => Promise.resolve({})
	} as unknown as Response;
}

function makeErrorResponse(status: number, body: unknown): Response {
	return {
		ok: false,
		status,
		text: () => Promise.resolve(JSON.stringify(body)),
		json: () => Promise.resolve(body)
	} as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe('adminApi', () => {
	let fetchMock: ReturnType<typeof vi.fn>;

	beforeEach(() => {
		// Reset storage
		for (const key of Object.keys(mockStorage)) delete mockStorage[key];

		fetchMock = vi.fn();
		vi.stubGlobal('fetch', fetchMock);
	});

	// -----------------------------------------------------------------------
	// Authorization header
	// -----------------------------------------------------------------------
	describe('authorization header', () => {
		it('sends Bearer token when admin_token is in localStorage', async () => {
			mockStorage['admin_token'] = 'my-jwt';
			fetchMock.mockResolvedValue(makeResponse({ totalUsers: 5 }));

			await adminApi.getStats();

			const [, init] = fetchMock.mock.calls[0];
			expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer my-jwt');
		});

		it('omits Authorization header when no token is stored', async () => {
			fetchMock.mockResolvedValue(makeResponse({ totalUsers: 5 }));

			await adminApi.getStats();

			const [, init] = fetchMock.mock.calls[0];
			expect((init.headers as Record<string, string>)['Authorization']).toBeUndefined();
		});

		it('always sends Content-Type application/json', async () => {
			fetchMock.mockResolvedValue(makeResponse({}));

			await adminApi.getStats();

			const [, init] = fetchMock.mock.calls[0];
			expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
		});
	});

	// -----------------------------------------------------------------------
	// Error handling
	// -----------------------------------------------------------------------
	describe('error handling', () => {
		it('throws with detail from error body', async () => {
			fetchMock.mockResolvedValue(makeErrorResponse(400, { detail: 'Bad input' }));

			await expect(adminApi.getStats()).rejects.toThrow('Bad input');
		});

		it('throws with error field from error body', async () => {
			fetchMock.mockResolvedValue(makeErrorResponse(401, { error: 'Unauthorized' }));

			await expect(adminApi.getStats()).rejects.toThrow('Unauthorized');
		});

		it('throws generic HTTP message when body has no detail or error', async () => {
			fetchMock.mockResolvedValue(makeErrorResponse(500, {}));

			await expect(adminApi.getStats()).rejects.toThrow('HTTP 500');
		});
	});

	// -----------------------------------------------------------------------
	// Stats
	// -----------------------------------------------------------------------
	describe('getStats', () => {
		it('calls GET /admin/stats', async () => {
			const stats = { users: { total: 10 }, live: { activeConnections: 1 } };
			fetchMock.mockResolvedValue(makeResponse(stats));

			const result = await adminApi.getStats();

			expect(fetchMock).toHaveBeenCalledOnce();
			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/stats');
			expect(result).toEqual(stats);
		});
	});

	// -----------------------------------------------------------------------
	// Users
	// -----------------------------------------------------------------------
	describe('getUsers', () => {
		it('calls GET /admin/users with query params', async () => {
			const page = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
			fetchMock.mockResolvedValue(makeResponse(page));

			await adminApi.getUsers('page=1&pageSize=20');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users?page=1&pageSize=20');
		});
	});

	describe('getUser', () => {
		it('calls GET /admin/users/{id}', async () => {
			fetchMock.mockResolvedValue(makeResponse({ id: 'abc' }));

			await adminApi.getUser('abc');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc');
		});
	});

	describe('disableUser', () => {
		it('calls POST /admin/users/{id}/disable with reason', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.disableUser('abc', 'spam');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc/disable');
			expect(init.method).toBe('POST');
			expect(JSON.parse(init.body)).toEqual({ reason: 'spam' });
		});
	});

	describe('enableUser', () => {
		it('calls POST /admin/users/{id}/enable', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.enableUser('abc');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc/enable');
			expect(init.method).toBe('POST');
		});
	});

	describe('forceLogout', () => {
		it('calls POST /admin/users/{id}/force-logout', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.forceLogout('abc');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc/force-logout');
			expect(init.method).toBe('POST');
		});
	});

	describe('resetPassword', () => {
		it('calls POST /admin/users/{id}/reset-password', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.resetPassword('abc');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc/reset-password');
			expect(init.method).toBe('POST');
		});
	});

	describe('setGlobalAdmin', () => {
		it('calls PUT /admin/users/{id}/global-admin with isGlobalAdmin flag', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.setGlobalAdmin('abc', true);

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/users/abc/global-admin');
			expect(init.method).toBe('PUT');
			expect(JSON.parse(init.body)).toEqual({ isGlobalAdmin: true });
		});
	});

	// -----------------------------------------------------------------------
	// Servers
	// -----------------------------------------------------------------------
	describe('getServers', () => {
		it('calls GET /admin/servers with query params', async () => {
			const page = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
			fetchMock.mockResolvedValue(makeResponse(page));

			await adminApi.getServers('page=1');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers?page=1');
		});
	});

	describe('getServer', () => {
		it('calls GET /admin/servers/{id}', async () => {
			fetchMock.mockResolvedValue(makeResponse({ id: 'srv-1' }));

			await adminApi.getServer('srv-1');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers/srv-1');
		});
	});

	describe('quarantineServer', () => {
		it('calls POST /admin/servers/{id}/quarantine with reason', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.quarantineServer('srv-1', 'abuse');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers/srv-1/quarantine');
			expect(init.method).toBe('POST');
			expect(JSON.parse(init.body)).toEqual({ reason: 'abuse' });
		});
	});

	describe('unquarantineServer', () => {
		it('calls POST /admin/servers/{id}/unquarantine', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.unquarantineServer('srv-1');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers/srv-1/unquarantine');
			expect(init.method).toBe('POST');
		});
	});

	describe('deleteServer', () => {
		it('calls DELETE /admin/servers/{id} with reason', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.deleteServer('srv-1', 'tos violation');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers/srv-1');
			expect(init.method).toBe('DELETE');
			expect(JSON.parse(init.body)).toEqual({ reason: 'tos violation' });
		});
	});

	describe('transferOwnership', () => {
		it('calls PUT /admin/servers/{id}/transfer-ownership with newOwnerUserId', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.transferOwnership('srv-1', 'user-999');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/servers/srv-1/transfer-ownership');
			expect(init.method).toBe('PUT');
			expect(JSON.parse(init.body)).toEqual({ newOwnerUserId: 'user-999' });
		});
	});

	// -----------------------------------------------------------------------
	// Reports
	// -----------------------------------------------------------------------
	describe('getReports', () => {
		it('calls GET /admin/reports with params', async () => {
			const page = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
			fetchMock.mockResolvedValue(makeResponse(page));

			await adminApi.getReports('status=open');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/reports?status=open');
		});
	});

	describe('getReport', () => {
		it('calls GET /admin/reports/{id}', async () => {
			fetchMock.mockResolvedValue(makeResponse({ id: 'r1' }));

			await adminApi.getReport('r1');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/reports/r1');
		});
	});

	describe('updateReport', () => {
		it('calls PUT /admin/reports/{id} with data', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.updateReport('r1', { status: 2 });

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/reports/r1');
			expect(init.method).toBe('PUT');
			expect(JSON.parse(init.body)).toEqual({ status: 2 });
		});
	});

	// -----------------------------------------------------------------------
	// Messages
	// -----------------------------------------------------------------------
	describe('searchMessages', () => {
		it('calls GET /admin/messages/search with params', async () => {
			const page = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
			fetchMock.mockResolvedValue(makeResponse(page));

			await adminApi.searchMessages('q=hello');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/messages/search?q=hello');
		});
	});

	// -----------------------------------------------------------------------
	// Admin actions / connections
	// -----------------------------------------------------------------------
	describe('getAdminActions', () => {
		it('calls GET /admin/actions with params', async () => {
			const page = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
			fetchMock.mockResolvedValue(makeResponse(page));

			await adminApi.getAdminActions('page=1');

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/actions?page=1');
		});
	});

	describe('getConnections', () => {
		it('calls GET /admin/connections', async () => {
			fetchMock.mockResolvedValue(makeResponse({ activeUsers: 42 }));

			const result = await adminApi.getConnections();

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/connections');
			expect(result).toEqual({ activeUsers: 42 });
		});
	});

	// -----------------------------------------------------------------------
	// Announcements
	// -----------------------------------------------------------------------
	describe('getAnnouncements', () => {
		it('calls GET /admin/announcements', async () => {
			fetchMock.mockResolvedValue(makeResponse([]));

			await adminApi.getAnnouncements();

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/announcements');
		});
	});

	describe('createAnnouncement', () => {
		it('calls POST /admin/announcements with data and returns id', async () => {
			fetchMock.mockResolvedValue(makeResponse({ id: 'ann-1' }));

			const result = await adminApi.createAnnouncement({
				title: 'Maintenance',
				body: 'Scheduled downtime',
				expiresAt: '2026-05-01T00:00:00Z'
			});

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/announcements');
			expect(init.method).toBe('POST');
			expect(JSON.parse(init.body)).toEqual({
				title: 'Maintenance',
				body: 'Scheduled downtime',
				expiresAt: '2026-05-01T00:00:00Z'
			});
			expect(result).toEqual({ id: 'ann-1' });
		});

		it('creates announcement without optional expiresAt', async () => {
			fetchMock.mockResolvedValue(makeResponse({ id: 'ann-2' }));

			await adminApi.createAnnouncement({ title: 'Hello', body: 'World' });

			const [, init] = fetchMock.mock.calls[0];
			const body = JSON.parse(init.body);
			expect(body.expiresAt).toBeUndefined();
		});
	});

	describe('updateAnnouncement', () => {
		it('calls PUT /admin/announcements/{id} with data', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.updateAnnouncement('ann-1', { isActive: false });

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/announcements/ann-1');
			expect(init.method).toBe('PUT');
			expect(JSON.parse(init.body)).toEqual({ isActive: false });
		});
	});

	describe('deleteAnnouncement', () => {
		it('calls DELETE /admin/announcements/{id}', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			await adminApi.deleteAnnouncement('ann-1');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/admin/announcements/ann-1');
			expect(init.method).toBe('DELETE');
		});
	});

	// -----------------------------------------------------------------------
	// Auth
	// -----------------------------------------------------------------------
	describe('login', () => {
		it('calls POST /auth/login with email and password', async () => {
			fetchMock.mockResolvedValue(
				makeResponse({ accessToken: 'tok', refreshToken: 'rtok' })
			);

			const result = await adminApi.login('admin@example.com', 'password123');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/auth/login');
			expect(init.method).toBe('POST');
			expect(JSON.parse(init.body)).toEqual({
				email: 'admin@example.com',
				password: 'password123',
				recaptchaToken: undefined
			});
			expect(result).toEqual({ accessToken: 'tok', refreshToken: 'rtok' });
		});

		it('includes recaptchaToken when provided', async () => {
			fetchMock.mockResolvedValue(
				makeResponse({ accessToken: 'tok', refreshToken: 'rtok' })
			);

			await adminApi.login('admin@example.com', 'pass', 'rctoken');

			const [, init] = fetchMock.mock.calls[0];
			expect(JSON.parse(init.body)).toEqual({
				email: 'admin@example.com',
				password: 'pass',
				recaptchaToken: 'rctoken'
			});
		});
	});

	describe('googleAuth', () => {
		it('calls POST /auth/google with credential', async () => {
			fetchMock.mockResolvedValue(
				makeResponse({ accessToken: 'tok', refreshToken: 'rtok' })
			);

			await adminApi.googleAuth('google-id-token');

			const [url, init] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/auth/google');
			expect(init.method).toBe('POST');
			expect(JSON.parse(init.body)).toEqual({ credential: 'google-id-token' });
		});
	});

	describe('getMe', () => {
		it('calls GET /me', async () => {
			fetchMock.mockResolvedValue(makeResponse({ user: { id: 'u1', isGlobalAdmin: true } }));

			const result = await adminApi.getMe();

			const [url] = fetchMock.mock.calls[0];
			expect(url).toBe('http://test-api/me');
			expect(result).toEqual({ user: { id: 'u1', isGlobalAdmin: true } });
		});
	});

	// -----------------------------------------------------------------------
	// Empty response body handling
	// -----------------------------------------------------------------------
	describe('empty response body', () => {
		it('returns undefined for empty text body', async () => {
			fetchMock.mockResolvedValue(makeEmptyResponse());

			const result = await adminApi.enableUser('abc');

			expect(result).toBeUndefined();
		});
	});
});
