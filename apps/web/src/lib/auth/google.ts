/* eslint-disable @typescript-eslint/no-explicit-any */

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
				onCredential(response.credential);
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
