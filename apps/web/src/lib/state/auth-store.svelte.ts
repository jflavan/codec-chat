// apps/web/src/lib/state/auth-store.svelte.ts
import { tick } from 'svelte';
import type { UserProfile, AuthResponse } from '$lib/types/index.js';
import { ApiClient } from '$lib/api/client.js';
import {
	persistToken,
	loadStoredToken,
	clearSession as clearStoredSession,
	isTokenExpired,
	isSessionExpired,
	type AuthType,
	setAuthType,
	getAuthType,
	persistRefreshToken,
	loadStoredRefreshToken,
	hasStoredAuthType
} from '$lib/auth/session.js';
import {
	initGoogleIdentity,
	renderGoogleButton,
	consumeRedirectCredential
} from '$lib/auth/google.js';
import { PushNotificationManager } from '$lib/services/push-notifications.js';
import { applyTheme } from '$lib/utils/theme.js';
import type { UIStore } from './ui-store.svelte.js';

export class AuthStore {
	/* ───── $state fields ───── */
	idToken = $state<string | null>(null);
	me = $state<UserProfile | null>(null);
	status = $state('Signed out');
	authType = $state<AuthType>('google');
	needsNickname = $state(false);
	needsLinking = $state(false);
	linkingEmail = $state('');
	pendingGoogleCredential = $state('');
	emailVerified = $state(true);
	isLoadingMe = $state(false);
	isUploadingAvatar = $state(false);
	pushNotificationsEnabled = $state(false);
	pushNotificationsSupported = $state(false);

	/* ───── derived ───── */
	isSignedIn = $derived(Boolean(this.idToken));
	isGlobalAdmin = $derived(Boolean(this.me?.user.isGlobalAdmin));
	effectiveDisplayName = $derived(
		this.me?.user.effectiveDisplayName ?? this.me?.user.displayName ?? ''
	);

	/* ───── callbacks (wired by +page.svelte to avoid circular deps) ───── */
	onSignedIn: (() => Promise<void>) | null = null;
	onSignedOut: (() => Promise<void>) | null = null;
	onMembersChanged: (() => Promise<void>) | null = null;

	/* ───── private ───── */
	private refreshPromise: Promise<string | null> | null = null;
	private pushManager: PushNotificationManager;

	constructor(
		private readonly api: ApiClient,
		private readonly ui: UIStore,
		private readonly googleClientId: string
	) {
		this.pushManager = new PushNotificationManager(this.api, () => this.idToken);
		this.pushNotificationsSupported = this.pushManager.isSupported;
	}

	/* ═══════════════════ Deduplicated bootstrap ═══════════════════ */

	/**
	 * Single bootstrap sequence called after every successful auth.
	 * Loads profile, servers, friends, DMs, starts SignalR, etc.
	 */
	private async completeSignIn(): Promise<void> {
		this.ui.isInitialLoading = true;
		try {
			await this.loadMe();
			if (this.onSignedIn) {
				await this.onSignedIn();
			}
			this.ui.isInitialLoading = false;
			this.ui.showAlphaNotification = true;
			this.checkPushSubscription();
		} catch (e) {
			this.ui.isInitialLoading = false;
			throw e;
		}
	}

	/* ═══════════════════ Init ═══════════════════ */

	/** Bootstrap auth: restore session or show sign-in UI. */
	init(): void {
		applyTheme(this.ui.theme);
		this.authType = getAuthType();

		// Check for credential from Google redirect flow (Android PWA)
		const redirectCredential = consumeRedirectCredential();
		if (redirectCredential) {
			this.handleCredential(redirectCredential);
			return;
		}

		if (!isSessionExpired()) {
			const stored = loadStoredToken();
			if (stored && !isTokenExpired(stored)) {
				// Token still valid — restore session directly (skip token exchange)
				this.idToken = stored;
				this.status = 'Signed in';

				this.completeSignIn().catch(() => {
					this.ui.isInitialLoading = false;
					this.renderSignIn();
				});
				return;
			}
			// Token expired — try refresh for all auth types
			this.refreshAccessToken()
				.then(async (success) => {
					if (success && this.idToken) {
						this.status = 'Signed in';
						await this.completeSignIn();
					} else {
						this.ui.isInitialLoading = false;
						this.renderSignIn();
					}
				})
				.catch(() => {
					this.ui.isInitialLoading = false;
					this.renderSignIn();
				});
			return;
		}
		clearStoredSession();
		this.ui.isInitialLoading = false;
		this.renderSignIn();
	}

	private renderSignIn(): void {
		initGoogleIdentity(this.googleClientId, (token) => this.handleCredential(token), {
			renderButtonIds: ['google-button', 'login-google-button'],
			autoSelect: hasStoredAuthType() && this.authType === 'google'
		});
	}

	/* ═══════════════════ Auth methods ═══════════════════ */

	async handleCredential(token: string): Promise<void> {
		this.ui.isInitialLoading = true;

		try {
			const response = await this.api.googleSignIn(token);

			// Account linking needed — user has an existing email/password account
			if (response.needsLinking) {
				this.needsLinking = true;
				this.linkingEmail = response.email ?? '';
				this.pendingGoogleCredential = token;
				this.ui.isInitialLoading = false;
				return;
			}

			// Store backend-issued tokens
			this.idToken = response.accessToken;
			this.status = 'Signed in';
			persistToken(response.accessToken);
			persistRefreshToken(response.refreshToken);
			setAuthType('google');
			this.authType = 'google';

			// New Google user needs to set a nickname
			if (response.isNewUser) {
				this.needsNickname = true;
				this.ui.isInitialLoading = false;
				return;
			}

			await this.completeSignIn();
		} catch {
			this.ui.isInitialLoading = false;
			this.renderSignIn();
		}
	}

	async register(
		email: string,
		password: string,
		nickname: string,
		recaptchaToken?: string
	): Promise<AuthResponse> {
		return this.api.register(email, password, nickname, recaptchaToken);
	}

	async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.api.login(email, password, recaptchaToken);
	}

	async linkGoogle(
		email: string,
		password: string,
		googleCredential: string
	): Promise<AuthResponse> {
		return this.api.linkGoogle(email, password, googleCredential);
	}

	async handleLinkGoogleSuccess(response: AuthResponse): Promise<void> {
		this.needsLinking = false;
		this.linkingEmail = '';
		this.pendingGoogleCredential = '';

		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType('google');
		this.authType = 'google';

		await this.completeSignIn();
	}

	async handleOAuthCallback(provider: 'github' | 'discord', code: string): Promise<void> {
		const response = await this.api.oauthCallback(provider, code);
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType(provider);
		this.authType = provider;

		if (response.isNewUser) {
			this.needsNickname = true;
			this.ui.isInitialLoading = false;
			return;
		}

		await this.completeSignIn();
	}

	async handleLocalAuth(response: AuthResponse): Promise<void> {
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType('local');
		this.authType = 'local';
		this.emailVerified = response.user.emailVerified ?? false;

		if (!this.emailVerified) {
			this.ui.isInitialLoading = false;
			return;
		}

		await this.completeSignIn();
	}

	async resendVerification(): Promise<void> {
		if (!this.idToken) return;
		await this.api.resendVerification(this.idToken);
	}

	async checkEmailVerified(): Promise<boolean> {
		if (!this.idToken) return false;
		try {
			const profile = await this.api.getMe(this.idToken);
			if (profile.user.emailVerified) {
				this.emailVerified = true;
				this.me = profile;
				await this.completeSignIn();
				return true;
			}
		} catch {
			// ignore
		}
		return false;
	}

	async confirmNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		await this.api.setNickname(this.idToken, nickname);
		this.needsNickname = false;
		await this.completeSignIn();
	}

	async refreshAccessToken(): Promise<boolean> {
		const refreshToken = loadStoredRefreshToken();
		if (!refreshToken) return false;

		try {
			const response = await this.api.refreshToken(refreshToken);
			this.idToken = response.accessToken;
			persistToken(response.accessToken);
			persistRefreshToken(response.refreshToken);
			return true;
		} catch {
			clearStoredSession();
			return false;
		}
	}

	/**
	 * Attempt to silently refresh the auth token.
	 * Concurrent calls are deduplicated so only one refresh runs at a time.
	 * Signs the user out if the refresh fails.
	 */
	async refreshToken(): Promise<string | null> {
		if (this.refreshPromise) return this.refreshPromise;

		this.refreshPromise = (async () => {
			try {
				const success = await this.refreshAccessToken();
				return success ? this.idToken : null;
			} catch {
				await this.signOut();
				return null;
			} finally {
				this.refreshPromise = null;
			}
		})();

		return this.refreshPromise;
	}

	async signOut(): Promise<void> {
		// onSignedOut stops hub + resets all stores FIRST
		if (this.onSignedOut) {
			await this.onSignedOut();
		}

		// Revoke refresh token server-side before clearing local state
		const refreshToken = loadStoredRefreshToken();
		if (refreshToken) {
			try {
				await this.api.logout(refreshToken);
			} catch {
				// Best-effort: proceed with local cleanup even if server revocation fails
			}
		}

		clearStoredSession();

		this.ui.isInitialLoading = false;
		this.ui.isHubConnected = false;
		this.idToken = null;
		this.me = null;
		this.status = 'Signed out';

		await tick();
		renderGoogleButton('google-button');
		renderGoogleButton('login-google-button');
	}

	/* ═══════════════════ Data loading ═══════════════════ */

	async loadMe(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		this.isLoadingMe = true;
		try {
			this.me = await this.api.getMe(this.idToken);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingMe = false;
		}
	}

	/* ═══════════════════ Bug Reports ═══════════════════ */

	async submitBugReport(
		title: string,
		description: string,
		userAgent: string,
		currentPage: string
	): Promise<{ issueUrl: string }> {
		if (!this.idToken) throw new Error('Not authenticated');
		return this.api.submitBugReport(this.idToken, title, description, userAgent, currentPage);
	}

	/* ═══════════════════ Avatar ═══════════════════ */

	/** Upload a custom global avatar and refresh the local profile. */
	async uploadAvatar(file: File): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.ui.error = null;
		try {
			const { avatarUrl } = await this.api.uploadAvatar(this.idToken, file);
			if (this.me) {
				this.me = {
					...this.me,
					user: { ...this.me.user, avatarUrl }
				};
			}
			// Refresh member list so the sidebar picks up the new avatar.
			if (this.onMembersChanged) {
				await this.onMembersChanged();
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	/** Remove the custom global avatar and revert to the Google profile picture. */
	async deleteAvatar(): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.ui.error = null;
		try {
			const { avatarUrl } = await this.api.deleteAvatar(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: { ...this.me.user, avatarUrl }
				};
			}
			if (this.onMembersChanged) {
				await this.onMembersChanged();
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	/* ═══════════════════ Nickname ═══════════════════ */

	/** Set or update the current user's nickname. */
	async setNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.setNickname(this.idToken, nickname);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						nickname: result.nickname,
						effectiveDisplayName: result.effectiveDisplayName
					}
				};
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Remove the current user's nickname, reverting to the Google display name. */
	async removeNickname(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.removeNickname(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						nickname: result.nickname,
						effectiveDisplayName: result.effectiveDisplayName
					}
				};
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Status ═══════════════════ */

	/** Set or update the current user's custom status message. */
	async setStatus(statusText?: string | null, statusEmoji?: string | null): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.setStatus(this.idToken, statusText, statusEmoji);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						statusText: result.statusText,
						statusEmoji: result.statusEmoji
					}
				};
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Clear the current user's custom status message. */
	async clearStatus(): Promise<void> {
		if (!this.idToken) return;
		this.ui.error = null;
		try {
			const result = await this.api.clearStatus(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						statusText: result.statusText,
						statusEmoji: result.statusEmoji
					}
				};
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Push Notifications ═══════════════════ */

	async checkPushSubscription(): Promise<void> {
		this.pushNotificationsEnabled = await this.pushManager.isSubscribed();
	}

	async enablePushNotifications(): Promise<boolean> {
		const success = await this.pushManager.subscribe();
		this.pushNotificationsEnabled = success;
		return success;
	}

	async disablePushNotifications(): Promise<boolean> {
		const success = await this.pushManager.unsubscribe();
		if (success) this.pushNotificationsEnabled = false;
		return success;
	}
}
