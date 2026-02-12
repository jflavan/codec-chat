/* eslint-disable @typescript-eslint/no-explicit-any */

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
			callback: (response: { credential: string }) => onCredential(response.credential)
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
