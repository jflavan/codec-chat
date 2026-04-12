import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { UserProfile, AuthResponse } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';

// Mock all external dependencies before importing the store
vi.mock('svelte', () => ({
	tick: vi.fn().mockResolvedValue(undefined),
	getContext: vi.fn(),
	setContext: vi.fn()
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

vi.mock('$lib/auth/google.js', () => ({
	initGoogleIdentity: vi.fn(),
	renderGoogleButton: vi.fn(),
	consumeRedirectCredential: vi.fn().mockReturnValue(null)
}));

vi.mock('$lib/services/push-notifications.js', () => ({
	PushNotificationManager: class MockPushNotificationManager {
		isSupported = false;
		isSubscribed = vi.fn().mockResolvedValue(false);
		subscribe = vi.fn().mockResolvedValue(false);
		unsubscribe = vi.fn().mockResolvedValue(false);
		constructor() {}
	}
}));

vi.mock('$lib/utils/theme.js', () => ({
	applyTheme: vi.fn()
}));

import { AuthStore } from './auth-store.svelte';
import type { UIStore } from './ui-store.svelte';
import {
	persistToken,
	clearSession,
	setAuthType,
	persistRefreshToken,
	loadStoredRefreshToken
} from '$lib/auth/session.js';

const mockPersistToken = vi.mocked(persistToken);
const mockClearSession = vi.mocked(clearSession);
const mockSetAuthType = vi.mocked(setAuthType);
const mockPersistRefreshToken = vi.mocked(persistRefreshToken);
const mockLoadStoredRefreshToken = vi.mocked(loadStoredRefreshToken);

function createMockApi(): ApiClient {
	return {
		googleSignIn: vi.fn(),
		login: vi.fn(),
		register: vi.fn(),
		logout: vi.fn(),
		refreshToken: vi.fn(),
		getMe: vi.fn(),
		uploadAvatar: vi.fn(),
		deleteAvatar: vi.fn(),
		setNickname: vi.fn(),
		removeNickname: vi.fn(),
		setStatus: vi.fn(),
		clearStatus: vi.fn(),
		resendVerification: vi.fn(),
		deleteAccount: vi.fn(),
		submitBugReport: vi.fn(),
		oauthCallback: vi.fn(),
		linkGoogle: vi.fn()
	} as unknown as ApiClient;
}

function createMockUi(): UIStore {
	return {
		isInitialLoading: false,
		isHubConnected: false,
		error: null,
		theme: 'phosphor',
		showAlphaNotification: false,
		setError: vi.fn()
	} as unknown as UIStore;
}

function makeUserProfile(overrides: Partial<UserProfile['user']> = {}): UserProfile {
	return {
		user: {
			id: 'user-1',
			email: 'test@example.com',
			displayName: 'Test User',
			nickname: null,
			effectiveDisplayName: 'Test User',
			avatarUrl: 'https://example.com/avatar.png',
			createdAt: '2026-01-01T00:00:00Z',
			emailVerified: true,
			isGlobalAdmin: false,
			statusText: null,
			statusEmoji: null,
			authProvider: 'google',
			...overrides
		},
		servers: []
	} as unknown as UserProfile;
}

function makeAuthResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
	return {
		accessToken: 'test-access-token',
		refreshToken: 'test-refresh-token',
		isNewUser: false,
		needsLinking: false,
		email: 'test@example.com',
		user: {
			id: 'user-1',
			email: 'test@example.com',
			displayName: 'Test User',
			nickname: null,
			effectiveDisplayName: 'Test User',
			avatarUrl: null,
			emailVerified: true,
			isGlobalAdmin: false
		},
		...overrides
	} as AuthResponse;
}

describe('AuthStore', () => {
	let store: AuthStore;
	let mockApi: ApiClient;
	let mockUi: UIStore;

	beforeEach(() => {
		vi.clearAllMocks();
		mockApi = createMockApi();
		mockUi = createMockUi();
		store = new AuthStore(mockApi, mockUi, 'test-client-id');
	});

	describe('initial state', () => {
		it('should have null idToken', () => {
			expect(store.idToken).toBeNull();
		});

		it('should have null me', () => {
			expect(store.me).toBeNull();
		});

		it('should have status Signed out', () => {
			expect(store.status).toBe('Signed out');
		});

		it('should have isSignedIn false', () => {
			expect(store.isSignedIn).toBe(false);
		});

		it('should have needsNickname false', () => {
			expect(store.needsNickname).toBe(false);
		});

		it('should have needsLinking false', () => {
			expect(store.needsLinking).toBe(false);
		});

		it('should have emailVerified true', () => {
			expect(store.emailVerified).toBe(true);
		});

		it('should have isUploadingAvatar false', () => {
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('should have isGlobalAdmin false when me is null', () => {
			expect(store.isGlobalAdmin).toBe(false);
		});

		it('should have empty effectiveDisplayName when me is null', () => {
			expect(store.effectiveDisplayName).toBe('');
		});
	});

	describe('handleCredential', () => {
		it('should set idToken and status on successful google sign-in', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.googleSignIn).mockResolvedValue(response);
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			await store.handleCredential('google-token');

			expect(store.idToken).toBe('test-access-token');
			expect(store.status).toBe('Signed in');
			expect(mockPersistToken).toHaveBeenCalledWith('test-access-token');
			expect(mockPersistRefreshToken).toHaveBeenCalledWith('test-refresh-token');
			expect(mockSetAuthType).toHaveBeenCalledWith('google');
		});

		it('should set needsLinking when response requires it', async () => {
			const response = makeAuthResponse({
				needsLinking: true,
				email: 'existing@example.com'
			});
			vi.mocked(mockApi.googleSignIn).mockResolvedValue(response);

			await store.handleCredential('google-token');

			expect(store.needsLinking).toBe(true);
			expect(store.linkingEmail).toBe('existing@example.com');
			expect(store.pendingGoogleCredential).toBe('google-token');
			expect(store.idToken).toBeNull();
		});

		it('should set needsNickname for new users', async () => {
			const response = makeAuthResponse({ isNewUser: true });
			vi.mocked(mockApi.googleSignIn).mockResolvedValue(response);

			await store.handleCredential('google-token');

			expect(store.needsNickname).toBe(true);
			expect(store.idToken).toBe('test-access-token');
			expect(store.status).toBe('Signed in');
		});

		it('should handle API error gracefully', async () => {
			vi.mocked(mockApi.googleSignIn).mockRejectedValue(new Error('network error'));

			await store.handleCredential('google-token');

			expect(store.idToken).toBeNull();
			expect(mockUi.isInitialLoading).toBe(false);
		});
	});

	describe('handleLocalAuth', () => {
		it('should set idToken and status on success', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			await store.handleLocalAuth(response);

			expect(store.idToken).toBe('test-access-token');
			expect(store.status).toBe('Signed in');
			expect(store.authType).toBe('local');
			expect(mockPersistToken).toHaveBeenCalledWith('test-access-token');
		});

		it('should not complete sign-in when email is not verified', async () => {
			const response = makeAuthResponse({
				user: {
					id: 'user-1',
					email: 'test@example.com',
					displayName: 'Test',
					nickname: null,
					effectiveDisplayName: 'Test',
					avatarUrl: null,
					emailVerified: false,
					isGlobalAdmin: false
				} as AuthResponse['user']
			});

			await store.handleLocalAuth(response);

			expect(store.emailVerified).toBe(false);
			expect(store.idToken).toBe('test-access-token');
			// getMe should not be called because completeSignIn is skipped
			expect(mockApi.getMe).not.toHaveBeenCalled();
		});

		it('should complete sign-in when email is verified', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			await store.handleLocalAuth(response);

			expect(store.emailVerified).toBe(true);
			expect(mockApi.getMe).toHaveBeenCalled();
		});
	});

	describe('handleOAuthCallback', () => {
		it('should set idToken and persist tokens', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.oauthCallback).mockResolvedValue(response);
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			await store.handleOAuthCallback('github', 'auth-code');

			expect(store.idToken).toBe('test-access-token');
			expect(store.status).toBe('Signed in');
			expect(mockPersistToken).toHaveBeenCalledWith('test-access-token');
			expect(mockSetAuthType).toHaveBeenCalledWith('github');
			expect(store.authType).toBe('github');
		});

		it('should set needsNickname for new OAuth users', async () => {
			const response = makeAuthResponse({ isNewUser: true });
			vi.mocked(mockApi.oauthCallback).mockResolvedValue(response);

			await store.handleOAuthCallback('discord', 'auth-code');

			expect(store.needsNickname).toBe(true);
		});
	});

	describe('signOut', () => {
		it('should call onSignedOut callback', async () => {
			const onSignedOut = vi.fn().mockResolvedValue(undefined);
			store.onSignedOut = onSignedOut;
			store.idToken = 'some-token';

			await store.signOut();

			expect(onSignedOut).toHaveBeenCalled();
		});

		it('should call api.logout with refresh token', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh-token');
			store.idToken = 'some-token';

			await store.signOut();

			expect(mockApi.logout).toHaveBeenCalledWith('stored-refresh-token');
		});

		it('should clear session and reset state', async () => {
			store.idToken = 'some-token';
			store.me = makeUserProfile();
			store.status = 'Signed in';

			await store.signOut();

			expect(mockClearSession).toHaveBeenCalled();
			expect(store.idToken).toBeNull();
			expect(store.me).toBeNull();
			expect(store.status).toBe('Signed out');
		});

		it('should handle logout API failure gracefully', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('stored-refresh-token');
			vi.mocked(mockApi.logout).mockRejectedValue(new Error('network'));
			store.idToken = 'some-token';

			await store.signOut();

			// Should still clear local state despite server error
			expect(mockClearSession).toHaveBeenCalled();
			expect(store.idToken).toBeNull();
		});
	});

	describe('loadMe', () => {
		it('should set me on success', async () => {
			const profile = makeUserProfile();
			vi.mocked(mockApi.getMe).mockResolvedValue(profile);
			store.idToken = 'valid-token';

			await store.loadMe();

			expect(store.me).toEqual(profile);
			expect(store.isLoadingMe).toBe(false);
		});

		it('should call ui.setError on failure', async () => {
			const error = new Error('failed to load');
			vi.mocked(mockApi.getMe).mockRejectedValue(error);
			store.idToken = 'valid-token';

			await store.loadMe();

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isLoadingMe).toBe(false);
		});

		it('should not call API when idToken is null', async () => {
			store.idToken = null;

			await store.loadMe();

			expect(mockApi.getMe).not.toHaveBeenCalled();
		});

		it('should set isLoadingMe during load', async () => {
			let loadingDuringCall = false;
			vi.mocked(mockApi.getMe).mockImplementation(async () => {
				loadingDuringCall = store.isLoadingMe;
				return makeUserProfile();
			});
			store.idToken = 'valid-token';

			await store.loadMe();

			expect(loadingDuringCall).toBe(true);
			expect(store.isLoadingMe).toBe(false);
		});
	});

	describe('refreshToken', () => {
		it('should deduplicate concurrent calls', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('refresh-token');
			vi.mocked(mockApi.refreshToken).mockResolvedValue({
				accessToken: 'new-token',
				refreshToken: 'new-refresh'
			});

			const promise1 = store.refreshToken();
			const promise2 = store.refreshToken();

			const [result1, result2] = await Promise.all([promise1, promise2]);

			// Both calls should return the same token value
			expect(result1).toBe('new-token');
			expect(result2).toBe('new-token');
			// But the underlying API should only be called once
			expect(mockApi.refreshToken).toHaveBeenCalledTimes(1);
		});

		it('should return new token on success', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('refresh-token');
			vi.mocked(mockApi.refreshToken).mockResolvedValue({
				accessToken: 'new-token',
				refreshToken: 'new-refresh'
			});

			const result = await store.refreshToken();

			expect(result).toBe('new-token');
			expect(store.idToken).toBe('new-token');
		});

		it('should return null when no refresh token stored', async () => {
			mockLoadStoredRefreshToken.mockReturnValue(null);

			const result = await store.refreshToken();

			expect(result).toBeNull();
		});

		it('should clear refreshPromise after completion', async () => {
			mockLoadStoredRefreshToken.mockReturnValue('refresh-token');
			vi.mocked(mockApi.refreshToken).mockResolvedValue({
				accessToken: 'new-token',
				refreshToken: 'new-refresh'
			});

			await store.refreshToken();
			// After resolution, a second call should make a new request
			const promise2 = store.refreshToken();
			await promise2;

			expect(mockApi.refreshToken).toHaveBeenCalledTimes(2);
		});
	});

	describe('uploadAvatar', () => {
		it('should update me.user.avatarUrl on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile();
			vi.mocked(mockApi.uploadAvatar).mockResolvedValue({
				avatarUrl: 'https://example.com/new-avatar.png'
			});

			await store.uploadAvatar(new File([''], 'avatar.png'));

			expect(store.me?.user.avatarUrl).toBe('https://example.com/new-avatar.png');
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('should set isUploadingAvatar during upload', async () => {
			let uploading = false;
			vi.mocked(mockApi.uploadAvatar).mockImplementation(async () => {
				uploading = store.isUploadingAvatar;
				return { avatarUrl: 'https://example.com/new.png' };
			});
			store.idToken = 'valid-token';
			store.me = makeUserProfile();

			await store.uploadAvatar(new File([''], 'avatar.png'));

			expect(uploading).toBe(true);
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('should call ui.setError on failure', async () => {
			const error = new Error('upload failed');
			vi.mocked(mockApi.uploadAvatar).mockRejectedValue(error);
			store.idToken = 'valid-token';
			store.me = makeUserProfile();

			await store.uploadAvatar(new File([''], 'avatar.png'));

			expect(mockUi.setError).toHaveBeenCalledWith(error);
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('should not call API when idToken is null', async () => {
			store.idToken = null;
			await store.uploadAvatar(new File([''], 'avatar.png'));
			expect(mockApi.uploadAvatar).not.toHaveBeenCalled();
		});

		it('should call onMembersChanged after successful upload', async () => {
			const onMembersChanged = vi.fn().mockResolvedValue(undefined);
			store.onMembersChanged = onMembersChanged;
			store.idToken = 'valid-token';
			store.me = makeUserProfile();
			vi.mocked(mockApi.uploadAvatar).mockResolvedValue({
				avatarUrl: 'https://example.com/new.png'
			});

			await store.uploadAvatar(new File([''], 'avatar.png'));

			expect(onMembersChanged).toHaveBeenCalled();
		});
	});

	describe('deleteAvatar', () => {
		it('should update me.user.avatarUrl on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile({ avatarUrl: 'https://example.com/custom.png' });
			vi.mocked(mockApi.deleteAvatar).mockResolvedValue({
				avatarUrl: 'https://example.com/default.png'
			});

			await store.deleteAvatar();

			expect(store.me?.user.avatarUrl).toBe('https://example.com/default.png');
			expect(store.isUploadingAvatar).toBe(false);
		});

		it('should call ui.setError on failure', async () => {
			const error = new Error('delete failed');
			vi.mocked(mockApi.deleteAvatar).mockRejectedValue(error);
			store.idToken = 'valid-token';
			store.me = makeUserProfile();

			await store.deleteAvatar();

			expect(mockUi.setError).toHaveBeenCalledWith(error);
		});
	});

	describe('setNickname', () => {
		it('should update me.user.nickname on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile();
			vi.mocked(mockApi.setNickname).mockResolvedValue({
				nickname: 'NewNick',
				effectiveDisplayName: 'NewNick'
			});

			await store.setNickname('NewNick');

			expect(store.me?.user.nickname).toBe('NewNick');
			expect(store.me?.user.effectiveDisplayName).toBe('NewNick');
		});

		it('should call ui.setError on failure', async () => {
			const error = new Error('nickname error');
			vi.mocked(mockApi.setNickname).mockRejectedValue(error);
			store.idToken = 'valid-token';
			store.me = makeUserProfile();

			await store.setNickname('Bad');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
		});

		it('should not call API when idToken is null', async () => {
			store.idToken = null;
			await store.setNickname('Nick');
			expect(mockApi.setNickname).not.toHaveBeenCalled();
		});
	});

	describe('setStatus', () => {
		it('should update me.user.statusText and statusEmoji on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile();
			vi.mocked(mockApi.setStatus).mockResolvedValue({
				statusText: 'Working',
				statusEmoji: '💻'
			});

			await store.setStatus('Working', '💻');

			expect(store.me?.user.statusText).toBe('Working');
			expect(store.me?.user.statusEmoji).toBe('💻');
		});

		it('should call ui.setError on failure', async () => {
			const error = new Error('status error');
			vi.mocked(mockApi.setStatus).mockRejectedValue(error);
			store.idToken = 'valid-token';
			store.me = makeUserProfile();

			await store.setStatus('Status');

			expect(mockUi.setError).toHaveBeenCalledWith(error);
		});
	});

	describe('clearStatus', () => {
		it('should clear status fields on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile({ statusText: 'Active', statusEmoji: '🟢' });
			vi.mocked(mockApi.clearStatus).mockResolvedValue({
				statusText: null,
				statusEmoji: null
			});

			await store.clearStatus();

			expect(store.me?.user.statusText).toBeNull();
			expect(store.me?.user.statusEmoji).toBeNull();
		});
	});

	describe('removeNickname', () => {
		it('should clear nickname on success', async () => {
			store.idToken = 'valid-token';
			store.me = makeUserProfile({ nickname: 'OldNick' });
			vi.mocked(mockApi.removeNickname).mockResolvedValue({
				nickname: null,
				effectiveDisplayName: 'Test User'
			});

			await store.removeNickname();

			expect(store.me?.user.nickname).toBeNull();
			expect(store.me?.user.effectiveDisplayName).toBe('Test User');
		});
	});

	describe('register / login delegates', () => {
		it('register should delegate to api.register', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.register).mockResolvedValue(response);

			const result = await store.register('a@b.com', 'pass', 'nick', 'recaptcha');

			expect(mockApi.register).toHaveBeenCalledWith('a@b.com', 'pass', 'nick', 'recaptcha');
			expect(result).toEqual(response);
		});

		it('login should delegate to api.login', async () => {
			const response = makeAuthResponse();
			vi.mocked(mockApi.login).mockResolvedValue(response);

			const result = await store.login('a@b.com', 'pass', 'recaptcha');

			expect(mockApi.login).toHaveBeenCalledWith('a@b.com', 'pass', 'recaptcha');
			expect(result).toEqual(response);
		});
	});

	describe('derived state', () => {
		it('isSignedIn should be true when idToken is set', () => {
			store.idToken = 'some-token';
			expect(store.isSignedIn).toBe(true);
		});

		it('isGlobalAdmin should reflect me.user.isGlobalAdmin', () => {
			store.me = makeUserProfile({ isGlobalAdmin: true });
			expect(store.isGlobalAdmin).toBe(true);
		});

		it('effectiveDisplayName should use me.user.effectiveDisplayName', () => {
			store.me = makeUserProfile({ effectiveDisplayName: 'Custom Name' });
			expect(store.effectiveDisplayName).toBe('Custom Name');
		});
	});

	describe('handleLinkGoogleSuccess', () => {
		it('should clear linking state and complete sign-in', async () => {
			store.needsLinking = true;
			store.linkingEmail = 'test@example.com';
			store.pendingGoogleCredential = 'cred';
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			const response = makeAuthResponse();
			await store.handleLinkGoogleSuccess(response);

			expect(store.needsLinking).toBe(false);
			expect(store.linkingEmail).toBe('');
			expect(store.pendingGoogleCredential).toBe('');
			expect(store.idToken).toBe('test-access-token');
			expect(store.authType).toBe('google');
		});
	});

	describe('confirmNickname', () => {
		it('should set nickname via API and complete sign-in', async () => {
			store.idToken = 'valid-token';
			store.needsNickname = true;
			vi.mocked(mockApi.setNickname).mockResolvedValue({
				nickname: 'MyNick',
				effectiveDisplayName: 'MyNick'
			});
			vi.mocked(mockApi.getMe).mockResolvedValue(makeUserProfile());

			await store.confirmNickname('MyNick');

			expect(mockApi.setNickname).toHaveBeenCalledWith('valid-token', 'MyNick');
			expect(store.needsNickname).toBe(false);
		});

		it('should not call API when idToken is null', async () => {
			store.idToken = null;
			await store.confirmNickname('Nick');
			expect(mockApi.setNickname).not.toHaveBeenCalled();
		});
	});

	describe('submitBugReport', () => {
		it('should delegate to api.submitBugReport', async () => {
			store.idToken = 'valid-token';
			vi.mocked(mockApi.submitBugReport).mockResolvedValue({
				issueUrl: 'https://github.com/issue/1'
			});

			const result = await store.submitBugReport('Bug', 'Description', 'Chrome', '/page');

			expect(mockApi.submitBugReport).toHaveBeenCalledWith(
				'valid-token',
				'Bug',
				'Description',
				'Chrome',
				'/page'
			);
			expect(result.issueUrl).toBe('https://github.com/issue/1');
		});

		it('should throw when not authenticated', async () => {
			store.idToken = null;
			await expect(
				store.submitBugReport('Bug', 'Desc', 'UA', '/page')
			).rejects.toThrow('Not authenticated');
		});
	});
});
