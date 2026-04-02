import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
	isTokenExpired,
	isSessionExpired,
	persistToken,
	loadStoredToken,
	clearSession,
	getAuthType,
	setAuthType,
	hasStoredAuthType,
	persistRefreshToken,
	loadStoredRefreshToken
} from './session';

// Global localStorage mock setup
const store: Record<string, string> = {};
const mockStorage = {
	getItem: (key: string) => store[key] ?? null,
	setItem: (key: string, value: string) => {
		store[key] = value;
	},
	removeItem: (key: string) => {
		delete store[key];
	},
	clear: () => {
		Object.keys(store).forEach((key) => delete store[key]);
	},
	length: 0,
	key: () => null
};

vi.stubGlobal('localStorage', mockStorage);

// Helper to create a fake JWT with a given exp
function fakeJwt(payload: Record<string, unknown>): string {
	const header = btoa(JSON.stringify({ alg: 'none' }));
	const body = btoa(JSON.stringify(payload));
	return `${header}.${body}.signature`;
}

describe('isTokenExpired', () => {
	it('returns true for expired token', () => {
		const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) - 120 });
		expect(isTokenExpired(token)).toBe(true);
	});

	it('returns false for valid token (expires in 1 hour)', () => {
		const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600 });
		expect(isTokenExpired(token)).toBe(false);
	});

	it('returns true for token expiring within 60s buffer', () => {
		const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 30 });
		expect(isTokenExpired(token)).toBe(true);
	});

	it('returns true for malformed token', () => {
		expect(isTokenExpired('not.a.jwt')).toBe(true);
	});

	it('returns true for token without exp', () => {
		const token = fakeJwt({ sub: '123' });
		expect(isTokenExpired(token)).toBe(true);
	});
});

describe('isSessionExpired', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns true when no login timestamp', () => {
		expect(isSessionExpired()).toBe(true);
	});

	it('returns false for recent login', () => {
		localStorage.setItem('codec_login_ts', String(Date.now()));
		expect(isSessionExpired()).toBe(false);
	});

	it('returns true for login older than 7 days', () => {
		const eightDaysAgo = Date.now() - 8 * 24 * 60 * 60 * 1000;
		localStorage.setItem('codec_login_ts', String(eightDaysAgo));
		expect(isSessionExpired()).toBe(true);
	});
});

describe('persistToken', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('stores token in localStorage', () => {
		persistToken('my-token');
		expect(localStorage.getItem('codec_id_token')).toBe('my-token');
	});

	it('sets login timestamp only once', () => {
		persistToken('token-1');
		const firstTs = localStorage.getItem('codec_login_ts');
		expect(firstTs).toBeTruthy();

		persistToken('token-2');
		expect(localStorage.getItem('codec_login_ts')).toBe(firstTs);
	});
});

describe('loadStoredToken', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns null when no token stored', () => {
		expect(loadStoredToken()).toBeNull();
	});

	it('returns stored token', () => {
		localStorage.setItem('codec_id_token', 'stored-token');
		expect(loadStoredToken()).toBe('stored-token');
	});
});

describe('clearSession', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('removes all keys', () => {
		localStorage.setItem('codec_id_token', 'token');
		localStorage.setItem('codec_login_ts', '12345');
		localStorage.setItem('codec_auth_type', 'google');
		localStorage.setItem('codec_refresh_token', 'refresh');

		clearSession();

		expect(localStorage.getItem('codec_id_token')).toBeNull();
		expect(localStorage.getItem('codec_login_ts')).toBeNull();
		expect(localStorage.getItem('codec_auth_type')).toBeNull();
		expect(localStorage.getItem('codec_refresh_token')).toBeNull();
	});
});

describe('getAuthType', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns google as default', () => {
		expect(getAuthType()).toBe('google');
	});

	it('returns stored auth type', () => {
		localStorage.setItem('codec_auth_type', 'local');
		expect(getAuthType()).toBe('local');
	});
});

describe('hasStoredAuthType', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns false when no auth type stored', () => {
		expect(hasStoredAuthType()).toBe(false);
	});

	it('returns true when auth type is stored', () => {
		localStorage.setItem('codec_auth_type', 'google');
		expect(hasStoredAuthType()).toBe(true);
	});
});

describe('setAuthType', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('stores auth type', () => {
		setAuthType('local');
		expect(localStorage.getItem('codec_auth_type')).toBe('local');
	});
});

describe('persistRefreshToken', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('stores refresh token', () => {
		persistRefreshToken('my-refresh-token');
		expect(localStorage.getItem('codec_refresh_token')).toBe('my-refresh-token');
	});
});

describe('loadStoredRefreshToken', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns null when no refresh token stored', () => {
		expect(loadStoredRefreshToken()).toBeNull();
	});

	it('returns stored refresh token', () => {
		localStorage.setItem('codec_refresh_token', 'stored-refresh-token');
		expect(loadStoredRefreshToken()).toBe('stored-refresh-token');
	});
});
