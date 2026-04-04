import { describe, it, expect, vi, beforeEach } from 'vitest';

// ---------------------------------------------------------------------------
// Mock $lib/api/client — verifyAdmin delegates to adminApi.getMe()
// ---------------------------------------------------------------------------
const mockGetMe = vi.fn<() => Promise<unknown>>();
vi.mock('$lib/api/client', () => ({
	adminApi: {
		getMe: () => mockGetMe()
	}
}));

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
// Import after mocks are set up
// ---------------------------------------------------------------------------
import { getToken, setToken, clearToken, verifyAdmin } from './auth';

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe('auth helpers', () => {
	beforeEach(() => {
		// Clear storage before each test
		for (const key of Object.keys(mockStorage)) delete mockStorage[key];
		mockGetMe.mockReset();
	});

	// -----------------------------------------------------------------------
	// getToken
	// -----------------------------------------------------------------------
	describe('getToken()', () => {
		it('returns null when no token is stored', () => {
			expect(getToken()).toBeNull();
		});

		it('returns the stored token', () => {
			mockStorage['admin_token'] = 'my-jwt';
			expect(getToken()).toBe('my-jwt');
		});
	});

	// -----------------------------------------------------------------------
	// setToken
	// -----------------------------------------------------------------------
	describe('setToken()', () => {
		it('stores the token in localStorage under admin_token', () => {
			setToken('new-jwt');
			expect(mockStorage['admin_token']).toBe('new-jwt');
		});

		it('overwrites an existing token', () => {
			mockStorage['admin_token'] = 'old-jwt';
			setToken('replacement-jwt');
			expect(mockStorage['admin_token']).toBe('replacement-jwt');
		});
	});

	// -----------------------------------------------------------------------
	// clearToken
	// -----------------------------------------------------------------------
	describe('clearToken()', () => {
		it('removes admin_token from localStorage', () => {
			mockStorage['admin_token'] = 'tok';
			clearToken();
			expect(mockStorage['admin_token']).toBeUndefined();
		});

		it('removes admin_refresh_token from localStorage', () => {
			mockStorage['admin_refresh_token'] = 'rtok';
			clearToken();
			expect(mockStorage['admin_refresh_token']).toBeUndefined();
		});

		it('does not throw when keys are not present', () => {
			expect(() => clearToken()).not.toThrow();
		});
	});

	// -----------------------------------------------------------------------
	// verifyAdmin
	// -----------------------------------------------------------------------
	describe('verifyAdmin()', () => {
		it('returns true when user is a global admin', async () => {
			mockGetMe.mockResolvedValue({ user: { id: 'u1', isGlobalAdmin: true } });

			const result = await verifyAdmin();

			expect(result).toBe(true);
		});

		it('returns false when user is not a global admin', async () => {
			mockGetMe.mockResolvedValue({ user: { id: 'u2', isGlobalAdmin: false } });

			const result = await verifyAdmin();

			expect(result).toBe(false);
		});

		it('returns false when user object is missing', async () => {
			mockGetMe.mockResolvedValue({});

			const result = await verifyAdmin();

			expect(result).toBe(false);
		});

		it('returns false when getMe throws', async () => {
			mockGetMe.mockRejectedValue(new Error('Network error'));

			const result = await verifyAdmin();

			expect(result).toBe(false);
		});

		it('returns false when getMe returns null', async () => {
			mockGetMe.mockResolvedValue(null);

			const result = await verifyAdmin();

			expect(result).toBe(false);
		});
	});
});
