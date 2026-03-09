import { error, redirect } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

/**
 * Google Identity Services redirect callback.
 *
 * When `ux_mode: 'redirect'` is used (Android standalone PWA), Google POSTs
 * the credential and a CSRF token here as form data. We validate the CSRF
 * token against the cookie Google sets, stash the credential in a short-lived
 * cookie, and redirect back to the app root where `init()` picks it up.
 */
export const POST: RequestHandler = async ({ request, cookies, url }) => {
	const form = await request.formData();
	const credential = form.get('credential');

	// Google sends g_csrf_token in both the POST body and a cookie.
	// Verify they match to prevent login CSRF attacks.
	const csrfTokenBody = form.get('g_csrf_token');
	const csrfTokenCookie = cookies.get('g_csrf_token');

	if (!csrfTokenBody || !csrfTokenCookie || csrfTokenBody !== csrfTokenCookie) {
		error(403, 'CSRF token mismatch');
	}

	if (typeof credential === 'string' && credential.length > 0) {
		const isSecure = url.protocol === 'https:';
		cookies.set('_google_credential', credential, {
			path: '/',
			httpOnly: false, // client JS needs to read this
			secure: isSecure,
			sameSite: 'lax',
			maxAge: 60 // expires in 60 seconds — just long enough for the redirect
		});
	}

	redirect(302, '/');
};
