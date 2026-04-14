import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AuthStore } from './auth-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';

// Mock external modules
vi.mock('$lib/auth/google.js', () => ({
	initGoogleIdentity: vi.fn(),
	renderGoogleButton: vi.fn(),
	consumeRedirectCredential: vi.fn().mockReturnValue(null)
}));

vi.mock('$lib/auth/session.js', () => ({
	persistToken: vi.fn(),
	loadStoredToken: vi.fn().mockReturnValue(null),
	clearSession: vi.fn(),
	isTokenExpired: vi.fn().mockReturnValue(true),
	isSessionExpired: vi.fn().mockReturnValue(true),
	setAuthType: vi.fn(),
	getAuthType: vi.fn().mockReturnValue('google'),
	persistRefreshToken: vi.fn(),
	loadStoredRefreshToken: vi.fn().mockReturnValue(null),
	hasStoredAuthType: vi.fn().mockReturnValue(false)
}));

vi.mock('$lib/services/push-notifications.js', () => ({
	PushNotificationManager: class {
		isSupported = false;
		isSubscribed = vi.fn().mockResolvedValue(false);
		subscribe = vi.fn().mockResolvedValue(false);
		unsubscribe = vi.fn().mockResolvedValue(false);
	}
}));

vi.mock('$lib/utils/theme.js', () => ({
	applyTheme: vi.fn(),
	getTheme: vi.fn().mockReturnValue('dark')
}));

function mockApi() {
	return {
		getMe: vi.fn().mockResolvedValue({ user: { id: 'u1', displayName: 'Test', emailVerified: true } }),
		googleSignIn: vi.fn().mockResolvedValue({
			accessToken: 'new-token',
			refreshToken: 'new-refresh',
			isNewUser: false,
			needsLinking: false
		}),
		register: vi.fn().mockResolvedValue({}),
		login: vi.fn().mockResolvedValue({}),
		linkGoogle: vi.fn().mockResolvedValue({}),
		oauthCallback: vi.fn().mockResolvedValue({
			accessToken: 'oauth-token',
			refreshToken: 'oauth-refresh',
			isNewUser: false
		}),
		setNickname: vi.fn().mockResolvedValue({ nickname: 'Test', effectiveDisplayName: 'Test' }),
		removeNickname: vi.fn().mockResolvedValue({ nickname: null, effectiveDisplayName: 'Test' }),
		uploadAvatar: vi.fn().mockResolvedValue({ avatarUrl: 'https://avatar.url' }),
		deleteAvatar: vi.fn().mockResolvedValue({ avatarUrl: 'https://fallback.url' }),
		resendVerification: vi.fn().mockResolvedValue(undefined),
		refreshToken: vi.fn().mockResolvedValue({ accessToken: 'refreshed-token', refreshToken: 'new-refresh' }),
		setStatus: vi.fn().mockResolvedValue({ statusText: 'Busy', statusEmoji: null }),
		clearStatus: vi.fn().mockResolvedValue({ statusText: null, statusEmoji: null }),
		submitBugReport: vi.fn().mockResolvedValue({ issueUrl: 'https://github.com/issue/1' }),
		deleteAccount: vi.fn().mockResolvedValue(undefined),
		logout: vi.fn().mockResolvedValue(undefined)
	} as any;
}

describe('AuthStore', () => {
	let store: AuthStore;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;

	beforeEach(() => {
		vi.clearAllMocks();
		api = mockApi();
		ui = new UIStore();
		store = new AuthStore(api, ui, 'test-client-id');
	});

	describe('initial state', () => {
		it('starts signed out', () => {
			expect(store.idToken).toBe(null);
			expect(store.me).toBe(null);
			expect(store.status).toBe('Signed out');
			expect(store.isSignedIn).toBe(false);
		});

		it('starts with no pending states', () => {
			expect(store.needsNickname).toBe(false);
			expect(store.needsLinking).toBe(false);
			expect(store.isLoadingMe).toBe(false);
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('defaults to google auth type', () => {
			expect(store.authType).toBe('google');
		});
	});

	describe('handleCredential', () => {
		it('signs in with Google credential', async () => {
			store.onSignedIn = vi.fn().mockResolvedValue(undefined);

			await store.handleCredential('google-token');

			expect(api.googleSignIn).toHaveBeenCalledWith('google-token');
			expect(store.idToken).toBe('new-token');
			expect(store.status).toBe('Signed in');
			expect(store.isSignedIn).toBe(true);
		});

		it('sets needsLinking when account linking required', async () => {
			api.googleSignIn.mockResolvedValue({
				needsLinking: true,
				email: 'user@example.com'
			});

			await store.handleCredential('google-token');

			expect(store.needsLinking).toBe(true);
			expect(store.linkingEmail).toBe('user@example.com');
			expect(store.pendingGoogleCredential).toBe('google-token');
		});

		it('sets needsNickname for new users', async () => {
			api.googleSignIn.mockResolvedValue({
				accessToken: 'token',
				refreshToken: 'refresh',
				isNewUser: true,
				needsLinking: false
			});

			await store.handleCredential('google-token');

			expect(store.needsNickname).toBe(true);
		});
	});

	describe('handleLocalAuth', () => {
		it('signs in with local auth', async () => {
			store.onSignedIn = vi.fn().mockResolvedValue(undefined);
			const response = {
				accessToken: 'local-token',
				refreshToken: 'local-refresh',
				user: { emailVerified: true }
			};

			await store.handleLocalAuth(response as any);

			expect(store.idToken).toBe('local-token');
			expect(store.authType).toBe('local');
			expect(store.emailVerified).toBe(true);
		});

		it('stops at verification for unverified email', async () => {
			store.onSignedIn = vi.fn();
			const response = {
				accessToken: 'local-token',
				refreshToken: 'local-refresh',
				user: { emailVerified: false }
			};

			await store.handleLocalAuth(response as any);

			expect(store.emailVerified).toBe(false);
			expect(store.onSignedIn).not.toHaveBeenCalled();
		});
	});

	describe('handleOAuthCallback', () => {
		it('signs in with OAuth provider', async () => {
			store.onSignedIn = vi.fn().mockResolvedValue(undefined);

			await store.handleOAuthCallback('github', 'auth-code');

			expect(api.oauthCallback).toHaveBeenCalledWith('github', 'auth-code');
			expect(store.idToken).toBe('oauth-token');
			expect(store.authType).toBe('github');
		});
	});

	describe('handleLinkGoogleSuccess', () => {
		it('clears linking state and signs in', async () => {
			store.needsLinking = true;
			store.linkingEmail = 'user@example.com';
			store.pendingGoogleCredential = 'google-token';
			store.onSignedIn = vi.fn().mockResolvedValue(undefined);

			const response = {
				accessToken: 'linked-token',
				refreshToken: 'linked-refresh'
			};

			await store.handleLinkGoogleSuccess(response as any);

			expect(store.needsLinking).toBe(false);
			expect(store.linkingEmail).toBe('');
			expect(store.pendingGoogleCredential).toBe('');
			expect(store.idToken).toBe('linked-token');
		});
	});

	describe('confirmNickname', () => {
		it('sets nickname and completes sign-in', async () => {
			store.idToken = 'token';
			store.needsNickname = true;
			store.onSignedIn = vi.fn().mockResolvedValue(undefined);

			await store.confirmNickname('MyNick');

			expect(api.setNickname).toHaveBeenCalledWith('token', 'MyNick');
			expect(store.needsNickname).toBe(false);
		});

		it('does nothing without token', async () => {
			store.idToken = null;
			await store.confirmNickname('MyNick');
			expect(api.setNickname).not.toHaveBeenCalled();
		});
	});

	describe('refreshAccessToken', () => {
		it('refreshes token using stored refresh token', async () => {
			const { loadStoredRefreshToken } = await import('$lib/auth/session.js');
			vi.mocked(loadStoredRefreshToken).mockReturnValue('stored-refresh');

			const result = await store.refreshAccessToken();

			expect(result).toBe(true);
			expect(store.idToken).toBe('refreshed-token');
		});

		it('returns false when no refresh token stored', async () => {
			const { loadStoredRefreshToken } = await import('$lib/auth/session.js');
			vi.mocked(loadStoredRefreshToken).mockReturnValue(null);

			const result = await store.refreshAccessToken();

			expect(result).toBe(false);
		});

		it('clears session on refresh failure', async () => {
			const { loadStoredRefreshToken, clearSession } = await import('$lib/auth/session.js');
			vi.mocked(loadStoredRefreshToken).mockReturnValue('bad-refresh');
			api.refreshToken.mockRejectedValue(new Error('invalid'));

			const result = await store.refreshAccessToken();

			expect(result).toBe(false);
			expect(clearSession).toHaveBeenCalled();
		});
	});

	describe('refreshToken (deduplicated)', () => {
		it('deduplicates concurrent refresh calls', async () => {
			const { loadStoredRefreshToken } = await import('$lib/auth/session.js');
			vi.mocked(loadStoredRefreshToken).mockReturnValue('refresh');

			const p1 = store.refreshToken();
			const p2 = store.refreshToken();

			const [r1, r2] = await Promise.all([p1, p2]);

			expect(r1).toBe(r2);
			// Only one actual API call
			expect(api.refreshToken).toHaveBeenCalledTimes(1);
		});
	});

	describe('loadMe', () => {
		it('loads user profile', async () => {
			store.idToken = 'token';

			await store.loadMe();

			expect(api.getMe).toHaveBeenCalledWith('token');
			expect(store.me).toBeDefined();
			expect(store.isLoadingMe).toBe(false);
		});

		it('does nothing without token', async () => {
			store.idToken = null;
			await store.loadMe();
			expect(api.getMe).not.toHaveBeenCalled();
		});

		it('sets error on failure', async () => {
			store.idToken = 'token';
			api.getMe.mockRejectedValue(new Error('Unauthorized'));

			await store.loadMe();

			expect(ui.error).toBe('Unauthorized');
			expect(store.isLoadingMe).toBe(false);
		});
	});

	describe('uploadAvatar', () => {
		it('uploads avatar and updates profile', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', avatarUrl: null } } as any;
			const file = new File(['data'], 'avatar.png', { type: 'image/png' });

			await store.uploadAvatar(file);

			expect(api.uploadAvatar).toHaveBeenCalledWith('token', file);
			expect(store.me!.user.avatarUrl).toBe('https://avatar.url');
			expect(store.isUploadingAvatar).toBe(false);
		});
	});

	describe('deleteAvatar', () => {
		it('deletes avatar and updates profile', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', avatarUrl: 'https://old.url' } } as any;

			await store.deleteAvatar();

			expect(store.me!.user.avatarUrl).toBe('https://fallback.url');
		});
	});

	describe('setNickname', () => {
		it('sets nickname and updates profile', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', nickname: null, effectiveDisplayName: 'Old' } } as any;

			await store.setNickname('NewNick');

			expect(api.setNickname).toHaveBeenCalledWith('token', 'NewNick');
			expect(store.me!.user.nickname).toBe('Test');
		});
	});

	describe('removeNickname', () => {
		it('removes nickname and updates profile', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', nickname: 'MyNick' } } as any;

			await store.removeNickname();

			expect(api.removeNickname).toHaveBeenCalledWith('token');
			expect(store.me!.user.nickname).toBe(null);
		});
	});

	describe('setStatus / clearStatus', () => {
		it('sets custom status', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', statusText: null, statusEmoji: null } } as any;

			await store.setStatus('Busy', null);

			expect(store.me!.user.statusText).toBe('Busy');
		});

		it('clears status', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1', statusText: 'Busy', statusEmoji: null } } as any;

			await store.clearStatus();

			expect(store.me!.user.statusText).toBe(null);
		});
	});

	describe('submitBugReport', () => {
		it('submits bug report', async () => {
			store.idToken = 'token';

			const result = await store.submitBugReport('Bug', 'Description', 'UA', '/page');

			expect(result.issueUrl).toBe('https://github.com/issue/1');
		});

		it('throws when not authenticated', async () => {
			store.idToken = null;
			await expect(store.submitBugReport('Bug', 'Desc', 'UA', '/')).rejects.toThrow('Not authenticated');
		});
	});

	describe('register / login delegates', () => {
		it('register delegates to API', async () => {
			await store.register('a@b.com', 'pass', 'nick');
			expect(api.register).toHaveBeenCalledWith('a@b.com', 'pass', 'nick', undefined);
		});

		it('login delegates to API', async () => {
			await store.login('a@b.com', 'pass');
			expect(api.login).toHaveBeenCalledWith('a@b.com', 'pass', undefined);
		});
	});

	describe('signOut', () => {
		it('clears all auth state', async () => {
			store.idToken = 'token';
			store.me = { user: { id: 'u1' } } as any;
			store.onSignedOut = vi.fn().mockResolvedValue(undefined);

			await store.signOut();

			expect(store.idToken).toBe(null);
			expect(store.me).toBe(null);
			expect(store.status).toBe('Signed out');
			expect(store.isSignedIn).toBe(false);
			expect(store.onSignedOut).toHaveBeenCalled();
		});
	});
});
