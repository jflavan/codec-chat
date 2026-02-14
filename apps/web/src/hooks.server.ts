import { env } from '$env/dynamic/public';
import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
	const response = await resolve(event);

	const apiBase = env.PUBLIC_API_BASE_URL ?? '';
	const wsBase = apiBase.replace(/^http/, 'ws');

	// SvelteKit manages the core CSP header (including nonces for inline scripts)
	// via svelte.config.js. Here we append the dynamic directives that depend on
	// runtime environment variables.
	const existing = response.headers.get('Content-Security-Policy') ?? '';
	const dynamicDirectives: Record<string, string> = {
		'img-src': `'self' data: blob: https://lh3.googleusercontent.com https://*.blob.core.windows.net ${apiBase}`,
		'connect-src': `'self' ${apiBase} ${wsBase} https://accounts.google.com`,
		'frame-src': 'https://accounts.google.com'
	};

	let csp = existing;
	for (const [directive, value] of Object.entries(dynamicDirectives)) {
		if (csp.includes(directive)) {
			csp = csp.replace(new RegExp(`${directive}\\s[^;]*`), `${directive} ${value}`);
		} else {
			csp = csp ? `${csp}; ${directive} ${value}` : `${directive} ${value}`;
		}
	}

	response.headers.set('Content-Security-Policy', csp);
	response.headers.set('X-Content-Type-Options', 'nosniff');
	response.headers.set('X-Frame-Options', 'DENY');
	response.headers.set('Referrer-Policy', 'strict-origin-when-cross-origin');

	return response;
};
