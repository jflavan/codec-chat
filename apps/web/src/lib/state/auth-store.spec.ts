import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { UserProfile, AuthResponse } from '$lib/types/index.js';

// ── Mocks ──────────────────────────────────────────────────────────────────

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn(),
	tick: vi.fn().mockResolvedValue(undefined)
}));

const mockPersistToken = vi.fn();
const mockLoadStoredToken = vi.fn().mockReturnValue(null);
const mockClearSession = vi.fn();
const mockIsTokenExpired = vi.fn().mockReturnValue(false);
const mockIsSessionExpired = vi.fn().mockReturnValue(true);
const mockSetAuthType = vi.fn();
const mockGetAuthType = vi.fn().mockReturnValue('google');
const mockPersistRefreshToken = vi.fn();
const mockLoadStoredRefreshToken = vi.fn().mockReturnValue(null);
const mockHasStoredAuthType = vi.fn().mockReturnValue(false);

vi.mock('$lib/auth/session.js', () => ({
	persistToken: (...args: unknown[]) => mockPersistToken(...args),
	loadStoredToken: (...args: unknown[]) => mockLoadStoredToken(...args),
	clearSession: (...args: unknown[]) => mockClearSession(...args),
	isTokenExpired: (...args: unknown[]) => mockIsTokenExpired(...args),
	isSessionExpired: (...args: unknown[]) => mockIsSessionExpired(...args),
	setAuthType: (...args: unknown[]) => mockSetAuthType(...args),
	getAuthType: (...args: unknown[]) => mockGetAuthType(...args),
	persistRefreshToken: (...args: unknown[]) => mockPersistRefreshToken(...args),
	loadStoredRefreshToken: (...args: unknown[]) => mockLoadStoredRefreshToken(...args),
	hasStoredAuthType: (...args: unknown[]) => mockHasStoredAuthType(...args)
}));

vi.mock('$lib/auth/google.js', () => ({
	initGoogleIdentity: vi.fn(),
	renderGoogleButton: vi.fn(),
	consumeRedirectCredential: vi.fn().mockReturnValue(null)
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

import { AuthStore } from './auth-store.svelte.js';

// ── Helpers ────────────────────────────────────────────────────────────────

function makeApi(): Record<string, ReturnType<typeof vi.fn>> {
	return {
		googleSignIn: vi.fn(),
		register: vi.fn(),
		login: vi.fn(),
		linkGoogle: vi.fn(),
		oauthCallback: vi.fn(),
		resendVerification: vi.fn(),
		getMe: vi.fn(),
		setNickname: vi.fn(),
		refreshToken: vi.fn(),
		logout: vi.fn(),
		deleteAccount: vi.fn(),
		submitBugReport: vi.fn(),
		uploadAvatar: vi.fn(),
		deleteAvatar: vi.fn(),
		removeNickname: vi.fn(),
		setStatus: vi.fn(),
		clearStatus: vi.fn()
	};
}

function makeUi() {
	return {
		isInitialLoading: false,
		isHubConnected: false,
		error: null as string | null,
		showAlphaNotification: false,
		theme: 'dark',
		setError: vi.fn()
	};
}

function makeProfile(overrides: Partial<UserProfile['user']> = {}): UserProfile {
	return {
		user: {
			id: 'u1',
			displayName: 'Test User',
			effectiveDisplayName: 'Test User',
			avatarUrl: 'https://example.com/avatar.png',
			isGlobalAdmin: false,
			emailVerified: true,
			...overrides
		}
	};
}

function makeAuthResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
	return {
		accessToken: 'access-token-123',
		refreshToken: 'refresh-token-456',
		user: {
			id: 'u1',
			displayName: 'Test User',
			effectiveDisplayName: 'Test User',
			emailVerified: true
		},
		...overrides
	};
}

// ── Tests ──────────────────────────────────────────────────────────────────

describe('AuthStore', () => {
	let api: ReturnType<typeof makeApi>;
	let ui: ReturnType<typeof makeUi>;
	let store: AuthStore;

	beforeEach(() => {
		vi.clearAllMocks();
		api = makeApi();
		ui = makeUi();
		store = new AuthStore(api as never, ui as never, 'google-client-id');
	});

	// ── Constructor & defaults ──

	describe('constructor', () => {
		it('initializes with correct defaults', () => {
			expect(store.idToken).toBeNull();
			expect(store.me).toBeNull();
			expect(store.status).toBe('Signed out');
			expect(store.needsNickname).toBe(false);
			expect(store.needsLinking).toBe(false);
			expect(store.emailVerified).toBe(true);
		});
	});

	// ── Derived state ──

	describe('isSignedIn', () => {
		it('returns false when idToken is null', () => {
			expect(store.isSignedIn).toBe(false);
		});

		it('returns true when idToken is set', () => {
			store.idToken = 'some-token';
			expect(store.isSignedIn).toBe(true);
		});
	});

	describe('isGlobalAdmin', () => {
		it('returns false when me is null', () => {
			expect(store.isGlobalAdmin).toBe(false);
		});

		it('returns true when me.user.isGlobalAdmin is true', () => {
			store.me = makeProfile({ isGlobalAdmin: true });
			expect(store.isGlobalAdmin).toBe(true);
		});

		it('returns false when me.user.isGlobalAdmin is false', () => {
			store.me = makeProfile({ isGlobalAdmin: false });
			expect(store.isGlobalAdmin).toBe(false);
		});
	});

	// ── handleCredential ──

	describe('handleCredential', () => {
		it('sets idToken and calls persistToken on success', async () => {
			const response = makeAuthResponse();
			api.googleSignIn.mockResolvedValue(response);
			api.getMe.mockResolvedValue(makeProfile());

			await store.handleCredential('google-token');

			expect(store.idToken).toBe('access-token-123');
			expect(store.status).toBe('Signed in');
			expect(mockPersistToken).toHaveBeenCalledWith('access-token-123');
			expect(mockPersistRefreshToken).toHaveBeenCalledWith('refresh-token-456');
			expect(mockSetAuthType).toHaveBeenCalledWith('google');
		});

		it('sets needsNickname when response.isNewUser is true', async () => {
			const response = makeAuthResponse({ isNewUser: true });
			api.googleSignIn.mockResolvedValue(response);

			await store.handleCredential('google-token');

			expect(store.needsNickname).toBe(true);
			expect(ui.isInitialLoading).toBe(false);
		});

		it('sets needsLinking and linkingEmail when response.needsLinking is true', async () => {
			const response = makeAuthResponse({
				needsLinking: true,
				email: 'user@test.com'
			});
			api.googleSignIn.mockResolvedValue(response);

			await store.handleCredential('google-token');

			expect(store.needsLinking).toBe(true);
			expect(store.linkingEmail).toBe('user@test.com');
			expect(store.pendingGoogleCredential).toBe('google-token');
			expect(ui.isInitialLoading).toBe(false);
		});

		it('calls onSignedIn callback on success', async () => {
			const response = makeAuthResponse();
			api.googleSignIn.mockResolvedValue(response);
			api.getMe.mockResolvedValue(makeProfile());
			const onSignedIn = vi.fn().mockResolvedValue(undefined);
			store.onSignedIn = onSignedIn;

			await store.handleCredential('google-token');

			expect(onSignedIn).toHaveBeenCalled();
		});

		it('does not call onSignedIn when needsLinking', async () => {
			const response = makeAuthResponse({ needsLinking: true });
			api.googleSignIn.mockResolvedValue(response);
			const onSignedIn = vi.fn();
			store.onSignedIn = onSignedIn;

			await store.handleCredential('google-token');

			expect(onSignedIn).not.toHaveBeenCalled();
		});
	});

	// ── handleLocalAuth ──

	describe('handleLocalAuth', () => {
		it('sets idToken, status, and authType on success', async () => {
			const response = makeAuthResponse();
			api.getMe.mockResolvedValue(makeProfile());

			await store.handleLocalAuth(response);

			expect(store.idToken).toBe('access-token-123');
			expect(store.status).toBe('Signed in');
			expect(store.authType).toBe('local');
			expect(mockSetAuthType).toHaveBeenCalledWith('local');
		});

		it('does not call onSignedIn when emailVerified is false', async () => {
			const response = makeAuthResponse({
				user: {
					id: 'u1',
					displayName: 'Test',
					effectiveDisplayName: 'Test',
					emailVerified: false
				}
			});
			const onSignedIn = vi.fn();
			store.onSignedIn = onSignedIn;

			await store.handleLocalAuth(response);

			expect(store.emailVerified).toBe(false);
			expect(onSignedIn).not.toHaveBeenCalled();
			expect(ui.isInitialLoading).toBe(false);
		});

		it('calls onSignedIn when emailVerified is true', async () => {
			const response = makeAuthResponse();
			api.getMe.mockResolvedValue(makeProfile());
			const onSignedIn = vi.fn().mockResolvedValue(undefined);
			store.onSignedIn = onSignedIn;

			await store.handleLocalAuth(response);

			expect(onSignedIn).toHaveBeenCalled();
		});
	});

	// ── handleOAuthCallback ──

	describe('handleOAuthCallback', () => {
		it('sets idToken and authType for github', async () => {
			const response = makeAuthResponse();
			api.oauthCallback.mockResolvedValue(response);
			api.getMe.mockResolvedValue(makeProfile());

			await store.handleOAuthCallback('github', 'auth-code');

			expect(store.idToken).toBe('access-token-123');
			expect(store.authType).toBe('github');
			expect(mockSetAuthType).toHaveBeenCalledWith('github');
			expect(api.oauthCallback).toHaveBeenCalledWith('github', 'auth-code');
		});

		it('sets needsNickname when isNewUser', async () => {
			const response = makeAuthResponse({ isNewUser: true });
			api.oauthCallback.mockResolvedValue(response);

			await store.handleOAuthCallback('discord', 'auth-code');

			expect(store.needsNickname).toBe(true);
		});
	});

	// ── signOut ──

	describe('signOut', () => {
		it('calls onSignedOut and clears state', async () => {
			store.idToken = 'some-token';
			store.me = makeProfile();
			store.status = 'Signed in';
			const onSignedOut = vi.fn().mockResolvedValue(undefined);
			store.onSignedOut = onSignedOut;

			await store.signOut();

			expect(onSignedOut).toHaveBeenCalled();
			expect(store.idToken).toBeNull();
			expect(store.me).toBeNull();
			expect(store.status).toBe('Signed out');
			expect(mockClearSession).toHaveBeenCalled();
		});

		it('calls api.logout with refresh token', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh');

			await store.signOut();

			expect(api.logout).toHaveBeenCalledWith('stored-refresh');
		});

		it('clears state even if logout fails', async () => {
			store.idToken = 'some-token';
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh');
			api.logout.mockRejectedValue(new Error('network error'));

			await store.signOut();

			expect(store.idToken).toBeNull();
			expect(mockClearSession).toHaveBeenCalled();
		});
	});

	// ── loadMe ──

	describe('loadMe', () => {
		it('calls api.getMe and sets me on success', async () => {
			store.idToken = 'test-token';
			const profile = makeProfile();
			api.getMe.mockResolvedValue(profile);

			await store.loadMe();

			expect(api.getMe).toHaveBeenCalledWith('test-token');
			expect(store.me).toEqual(profile);
			expect(store.isLoadingMe).toBe(false);
		});

		it('sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			const error = new Error('Failed to load');
			api.getMe.mockRejectedValue(error);

			await store.loadMe();

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingMe).toBe(false);
		});

		it('does nothing when idToken is null', async () => {
			store.idToken = null;

			await store.loadMe();

			expect(api.getMe).not.toHaveBeenCalled();
		});
	});

	// ── uploadAvatar ──

	describe('uploadAvatar', () => {
		it('calls api.uploadAvatar and updates me.user.avatarUrl', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			api.uploadAvatar.mockResolvedValue({ avatarUrl: 'https://new-avatar.png' });

			const file = new File([''], 'avatar.png');
			await store.uploadAvatar(file);

			expect(api.uploadAvatar).toHaveBeenCalledWith('test-token', file);
			expect(store.me?.user.avatarUrl).toBe('https://new-avatar.png');
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('calls onMembersChanged after upload', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			api.uploadAvatar.mockResolvedValue({ avatarUrl: 'https://new.png' });
			const onMembersChanged = vi.fn().mockResolvedValue(undefined);
			store.onMembersChanged = onMembersChanged;

			await store.uploadAvatar(new File([''], 'a.png'));

			expect(onMembersChanged).toHaveBeenCalled();
		});

		it('does nothing when idToken is null', async () => {
			store.idToken = null;

			await store.uploadAvatar(new File([''], 'a.png'));

			expect(api.uploadAvatar).not.toHaveBeenCalled();
		});
	});

	// ── deleteAvatar ──

	describe('deleteAvatar', () => {
		it('calls api.deleteAvatar and updates me.user.avatarUrl', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile({ avatarUrl: 'https://old.png' });
			api.deleteAvatar.mockResolvedValue({ avatarUrl: 'https://google-pic.png' });

			await store.deleteAvatar();

			expect(api.deleteAvatar).toHaveBeenCalledWith('test-token');
			expect(store.me?.user.avatarUrl).toBe('https://google-pic.png');
		});
	});

	// ── setNickname ──

	describe('setNickname', () => {
		it('calls api.setNickname and updates me', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			api.setNickname.mockResolvedValue({
				nickname: 'NewNick',
				effectiveDisplayName: 'NewNick'
			});

			await store.setNickname('NewNick');

			expect(api.setNickname).toHaveBeenCalledWith('test-token', 'NewNick');
			expect(store.me?.user.nickname).toBe('NewNick');
			expect(store.me?.user.effectiveDisplayName).toBe('NewNick');
		});
	});

	// ── removeNickname ──

	describe('removeNickname', () => {
		it('calls api.removeNickname and updates me', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile({ nickname: 'OldNick' });
			api.removeNickname.mockResolvedValue({
				nickname: null,
				effectiveDisplayName: 'Test User'
			});

			await store.removeNickname();

			expect(api.removeNickname).toHaveBeenCalledWith('test-token');
			expect(store.me?.user.nickname).toBeNull();
		});
	});

	// ── setStatus ──

	describe('setStatus', () => {
		it('calls api.setStatus and updates me', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			api.setStatus.mockResolvedValue({
				statusText: 'Busy',
				statusEmoji: '🔴'
			});

			await store.setStatus('Busy', '🔴');

			expect(api.setStatus).toHaveBeenCalledWith('test-token', 'Busy', '🔴');
			expect(store.me?.user.statusText).toBe('Busy');
			expect(store.me?.user.statusEmoji).toBe('🔴');
		});
	});

	// ── clearStatus ──

	describe('clearStatus', () => {
		it('calls api.clearStatus and updates me', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile({ statusText: 'Busy', statusEmoji: '🔴' });
			api.clearStatus.mockResolvedValue({
				statusText: null,
				statusEmoji: null
			});

			await store.clearStatus();

			expect(api.clearStatus).toHaveBeenCalledWith('test-token');
			expect(store.me?.user.statusText).toBeNull();
			expect(store.me?.user.statusEmoji).toBeNull();
		});
	});

	// ── register / login ──

	describe('register', () => {
		it('delegates to api.register', async () => {
			const response = makeAuthResponse();
			api.register.mockResolvedValue(response);

			const result = await store.register('a@b.com', 'pass', 'nick', 'recaptcha');

			expect(api.register).toHaveBeenCalledWith('a@b.com', 'pass', 'nick', 'recaptcha');
			expect(result).toEqual(response);
		});
	});

	describe('login', () => {
		it('delegates to api.login', async () => {
			const response = makeAuthResponse();
			api.login.mockResolvedValue(response);

			const result = await store.login('a@b.com', 'pass', 'recaptcha');

			expect(api.login).toHaveBeenCalledWith('a@b.com', 'pass', 'recaptcha');
			expect(result).toEqual(response);
		});
	});

	// ── refreshAccessToken ──

	describe('refreshAccessToken', () => {
		it('returns false when no refresh token', async () => {
			mockLoadStoredRefreshToken.mockReturnValue(null);

			const result = await store.refreshAccessToken();

			expect(result).toBe(false);
			expect(api.refreshToken).not.toHaveBeenCalled();
		});

		it('calls api.refreshToken and sets idToken on success', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh');
			api.refreshToken.mockResolvedValue({
				accessToken: 'new-access',
				refreshToken: 'new-refresh'
			});

			const result = await store.refreshAccessToken();

			expect(result).toBe(true);
			expect(store.idToken).toBe('new-access');
			expect(mockPersistToken).toHaveBeenCalledWith('new-access');
			expect(mockPersistRefreshToken).toHaveBeenCalledWith('new-refresh');
		});

		it('clears session and returns false on failure', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh');
			api.refreshToken.mockRejectedValue(new Error('expired'));

			const result = await store.refreshAccessToken();

			expect(result).toBe(false);
			expect(mockClearSession).toHaveBeenCalled();
		});
	});

	// ── refreshToken (deduplicated) ──

	describe('refreshToken', () => {
		it('deduplicates concurrent calls', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh');
			api.refreshToken.mockResolvedValue({
				accessToken: 'new-access',
				refreshToken: 'new-refresh'
			});

			const [r1, r2] = await Promise.all([store.refreshToken(), store.refreshToken()]);

			expect(api.refreshToken).toHaveBeenCalledTimes(1);
			expect(r1).toBe('new-access');
			expect(r2).toBe('new-access');
		});
	});

	// ── confirmNickname ──

	describe('confirmNickname', () => {
		it('calls api.setNickname and sets needsNickname to false', async () => {
			store.idToken = 'test-token';
			store.needsNickname = true;
			api.setNickname.mockResolvedValue(undefined);
			api.getMe.mockResolvedValue(makeProfile());

			await store.confirmNickname('MyNick');

			expect(api.setNickname).toHaveBeenCalledWith('test-token', 'MyNick');
			expect(store.needsNickname).toBe(false);
		});
	});

	// ── resendVerification ──

	describe('resendVerification', () => {
		it('calls api.resendVerification', async () => {
			store.idToken = 'test-token';
			api.resendVerification.mockResolvedValue(undefined);

			await store.resendVerification();

			expect(api.resendVerification).toHaveBeenCalledWith('test-token');
		});

		it('does nothing when not authenticated', async () => {
			store.idToken = null;

			await store.resendVerification();

			expect(api.resendVerification).not.toHaveBeenCalled();
		});
	});

	// ── deleteAccount ──

	describe('deleteAccount', () => {
		it('calls api.deleteAccount and clears state', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const onSignedOut = vi.fn().mockResolvedValue(undefined);
			store.onSignedOut = onSignedOut;
			api.deleteAccount.mockResolvedValue(undefined);

			await store.deleteAccount('password123');

			expect(api.deleteAccount).toHaveBeenCalledWith(
				'test-token',
				'DELETE',
				'password123',
				undefined
			);
			expect(onSignedOut).toHaveBeenCalled();
			expect(store.idToken).toBeNull();
			expect(store.me).toBeNull();
			expect(store.status).toBe('Signed out');
			expect(mockClearSession).toHaveBeenCalled();
		});

		it('does nothing when not authenticated', async () => {
			store.idToken = null;

			await store.deleteAccount();

			expect(api.deleteAccount).not.toHaveBeenCalled();
		});
	});

	// ── submitBugReport ──

	describe('submitBugReport', () => {
		it('throws when not authenticated', async () => {
			store.idToken = null;

			await expect(
				store.submitBugReport('title', 'desc', 'ua', '/page')
			).rejects.toThrow('Not authenticated');
		});

		it('calls api.submitBugReport when authenticated', async () => {
			store.idToken = 'test-token';
			api.submitBugReport.mockResolvedValue({ issueUrl: 'https://github.com/issue/1' });

			const result = await store.submitBugReport('Bug', 'Desc', 'Chrome', '/home');

			expect(api.submitBugReport).toHaveBeenCalledWith(
				'test-token',
				'Bug',
				'Desc',
				'Chrome',
				'/home'
			);
			expect(result).toEqual({ issueUrl: 'https://github.com/issue/1' });
		});
	});

	// ── Error handling paths ──

	describe('error handling', () => {
		it('uploadAvatar sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Upload failed');
			api.uploadAvatar.mockRejectedValue(error);

			await store.uploadAvatar(new File([''], 'a.png'));

			expect(ui.setError).toHaveBeenCalledWith(error);
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('deleteAvatar sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Delete failed');
			api.deleteAvatar.mockRejectedValue(error);

			await store.deleteAvatar();

			expect(ui.setError).toHaveBeenCalledWith(error);
		});

		it('setNickname sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Nickname failed');
			api.setNickname.mockRejectedValue(error);

			await store.setNickname('Bad');

			expect(ui.setError).toHaveBeenCalledWith(error);
		});

		it('setStatus sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Status failed');
			api.setStatus.mockRejectedValue(error);

			await store.setStatus('Busy');

			expect(ui.setError).toHaveBeenCalledWith(error);
		});

		it('clearStatus sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Clear failed');
			api.clearStatus.mockRejectedValue(error);

			await store.clearStatus();

			expect(ui.setError).toHaveBeenCalledWith(error);
		});

		it('removeNickname sets ui.error on failure', async () => {
			store.idToken = 'test-token';
			store.me = makeProfile();
			const error = new Error('Remove failed');
			api.removeNickname.mockRejectedValue(error);

			await store.removeNickname();

			expect(ui.setError).toHaveBeenCalledWith(error);
		});
	});
});
