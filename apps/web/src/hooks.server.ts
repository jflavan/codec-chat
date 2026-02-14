import { env } from '$env/dynamic/public';
import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
	const response = await resolve(event);

	const apiBase = env.PUBLIC_API_BASE_URL ?? '';
	const wsBase = apiBase.replace(/^http/, 'ws');

	const csp = [
		"default-src 'self'",
		`script-src 'self' https://accounts.google.com`,
		`style-src 'self' 'unsafe-inline' https://fonts.googleapis.com`,
		`font-src 'self' https://fonts.gstatic.com`,
		`img-src 'self' data: blob: https://lh3.googleusercontent.com https://*.blob.core.windows.net ${apiBase}`,
		`connect-src 'self' ${apiBase} ${wsBase} https://accounts.google.com`,
		`frame-src https://accounts.google.com`,
		"object-src 'none'",
		"base-uri 'self'"
	].join('; ');

	response.headers.set('Content-Security-Policy', csp);
	response.headers.set('X-Content-Type-Options', 'nosniff');
	response.headers.set('X-Frame-Options', 'DENY');
	response.headers.set('Referrer-Policy', 'strict-origin-when-cross-origin');

	return response;
};
