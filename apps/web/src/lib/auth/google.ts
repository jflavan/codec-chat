/* eslint-disable @typescript-eslint/no-explicit-any */

/** Pending resolver for a programmatic token refresh request. */
let pendingRefreshResolver: ((token: string) => void) | null = null;

/**
 * Load the Google Identity Services SDK and initialise the sign-in flow.
 *
 * Calls `onCredential` with the JWT `credential` string whenever the user
 * signs in (button click or silent One Tap).
 */
export function initGoogleIdentity(
	clientId: string,
	onCredential: (token: string) => void,
	opts?: { renderButtonId?: string; autoSelect?: boolean }
): void {
	const script = document.createElement('script');
	script.src = 'https://accounts.google.com/gsi/client';
	script.async = true;
	script.defer = true;

	script.onload = () => {
		const google = (window as unknown as { google?: any }).google;
		if (!google?.accounts?.id) return;

		google.accounts.id.initialize({
			client_id: clientId,
			auto_select: opts?.autoSelect ?? true,
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

		const buttonEl = opts?.renderButtonId
			? document.getElementById(opts.renderButtonId)
			: null;

		if (buttonEl) {
			google.accounts.id.renderButton(buttonEl, { theme: 'outline', size: 'large' });
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

	const el = document.getElementById(elementId);
	if (el) {
		google.accounts.id.renderButton(el, { theme: 'outline', size: 'large' });
	}
	google.accounts.id.prompt();
}
