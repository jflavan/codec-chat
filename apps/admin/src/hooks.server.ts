import { env } from '$env/dynamic/public';
import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
	const response = await resolve(event);

	const apiBase = env.PUBLIC_API_BASE_URL ?? '';
	const wsBase = apiBase.replace(/^http/, 'ws');

	const csp = [
		"default-src 'self'",
		`script-src 'self' https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/`,
		`style-src 'self' 'unsafe-inline'`,
		`img-src 'self' data: blob: https://lh3.googleusercontent.com ${apiBase}`,
		`connect-src 'self' ${apiBase} ${wsBase} https://www.google.com`,
		`frame-src https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/`,
		`object-src 'none'`,
		`base-uri 'self'`
	].join('; ');

	response.headers.set('Content-Security-Policy', csp);
	response.headers.set('X-Content-Type-Options', 'nosniff');
	response.headers.set('X-Frame-Options', 'DENY');
	response.headers.set('Referrer-Policy', 'strict-origin-when-cross-origin');
	response.headers.set('Permissions-Policy', 'microphone=(), camera=(), geolocation=(), payment=()');
	response.headers.set('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');

	return response;
};
