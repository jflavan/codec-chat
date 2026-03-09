/* eslint-disable @typescript-eslint/no-explicit-any */

/** Pending resolver for a programmatic token refresh request. */
let pendingRefreshResolver: ((token: string) => void) | null = null;

/**
 * Detect whether the app is running as a standalone PWA on Android.
 *
 * In this mode, Google's default popup-based sign-in flow breaks because
 * the popup opens in a separate Chrome Custom Tab that cannot communicate
 * back to the PWA's isolated browsing context via `window.opener`.
 */
function isAndroidStandalonePwa(): boolean {
	const isStandalone = window.matchMedia('(display-mode: standalone)').matches;
	const isAndroid = /Android/i.test(navigator.userAgent);
	return isStandalone && isAndroid;
}

/**
 * Read and immediately clear the Google credential cookie set by the
 * `/auth/callback` redirect handler.
 */
export function consumeRedirectCredential(): string | null {
	const match = document.cookie.match(/(?:^|;\s*)_google_credential=([^;]+)/);
	if (!match) return null;
	// Clear the cookie immediately
	document.cookie = '_google_credential=; path=/; max-age=0; expires=Thu, 01 Jan 1970 00:00:00 GMT';
	return decodeURIComponent(match[1]);
}

/**
 * Load the Google Identity Services SDK and initialise the sign-in flow.
 *
 * Calls `onCredential` with the JWT `credential` string whenever the user
 * signs in (button click or silent One Tap).
 *
 * On Android standalone PWAs, uses `ux_mode: 'redirect'` to avoid the
 * broken popup flow. The redirect posts back to `/auth/callback` which
 * stashes the credential in a cookie and redirects to `/`.
 */
export function initGoogleIdentity(
	clientId: string,
	onCredential: (token: string) => void,
	opts?: { renderButtonId?: string; renderButtonIds?: string[]; autoSelect?: boolean }
): void {
	const script = document.createElement('script');
	script.src = 'https://accounts.google.com/gsi/client';
	script.async = true;
	script.defer = true;

	script.onload = () => {
		const google = (window as unknown as { google?: any }).google;
		if (!google?.accounts?.id) return;

		const useRedirect = isAndroidStandalonePwa();
		const loginUri = useRedirect ? `${window.location.origin}/auth/callback` : undefined;

		google.accounts.id.initialize({
			client_id: clientId,
			auto_select: opts?.autoSelect ?? true,
			ux_mode: useRedirect ? 'redirect' : 'popup',
			login_uri: loginUri,
			callback: (response: { credential: string }) => {
				// During a programmatic refresh, resolve the pending promise
				// instead of calling onCredential to avoid re-initializing the app.
				if (pendingRefreshResolver) {
					pendingRefreshResolver(response.credential);
					pendingRefreshResolver = null;
				} else {
					onCredential(response.credential);
				}
			}
		});

		const ids = opts?.renderButtonIds ?? (opts?.renderButtonId ? [opts.renderButtonId] : []);
		let rendered = false;
		for (const id of ids) {
			const el = document.getElementById(id);
			if (el) {
				const buttonOpts: Record<string, unknown> = { theme: 'outline', size: 'large' };
				if (useRedirect) {
					buttonOpts.ux_mode = 'redirect';
					buttonOpts.login_uri = loginUri;
				}
				google.accounts.id.renderButton(el, buttonOpts);
				rendered = true;
			}
		}
		if (rendered) {
			google.accounts.id.prompt();
		}
	};

	document.head.appendChild(script);
}

/**
 * Attempt to silently obtain a fresh Google ID token via One Tap.
 *
 * Resolves with the new JWT credential string on success.
 * Rejects if silent re-authentication is unavailable (e.g. third-party
 * cookies blocked, user signed out of Google, or the prompt times out).
 */
export function requestFreshToken(): Promise<string> {
	return new Promise((resolve, reject) => {
		const google = (window as unknown as { google?: any }).google;
		if (!google?.accounts?.id) {
			reject(new Error('Google Identity Services not loaded'));
			return;
		}

		let settled = false;

		const timeout = setTimeout(() => {
			if (!settled) {
				settled = true;
				pendingRefreshResolver = null;
				reject(new Error('Token refresh timed out'));
			}
		}, 10_000);

		pendingRefreshResolver = (token: string) => {
			if (!settled) {
				settled = true;
				clearTimeout(timeout);
				resolve(token);
			}
		};

		google.accounts.id.prompt((notification: any) => {
			if (!settled && (notification.isNotDisplayed() || notification.isSkippedMoment())) {
				settled = true;
				clearTimeout(timeout);
				pendingRefreshResolver = null;
				reject(new Error('Silent re-authentication not available'));
			}
		});
	});
}

/** Re-render the sign-in button and trigger One Tap after sign-out. */
export function renderGoogleButton(elementId: string): void {
	const google = (window as unknown as { google?: any }).google;
	if (!google?.accounts?.id) return;

	google.accounts.id.disableAutoSelect();

	const useRedirect = isAndroidStandalonePwa();
	const el = document.getElementById(elementId);
	if (el) {
		const buttonOpts: Record<string, unknown> = { theme: 'outline', size: 'large' };
		if (useRedirect) {
			buttonOpts.ux_mode = 'redirect';
			buttonOpts.login_uri = `${window.location.origin}/auth/callback`;
		}
		google.accounts.id.renderButton(el, buttonOpts);
	}
	google.accounts.id.prompt();
}
