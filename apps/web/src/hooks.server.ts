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
	// Open Graph images can originate from any HTTPS domain, so we allow https:
	// as a scheme-source. Specific CDNs are listed for documentation clarity, but
	// https: already covers them.
	const dynamicDirectives: Record<string, string> = {
		'img-src': [
			"'self'",
			'data:',
			'blob:',
			'https:',
			'https://lh3.googleusercontent.com',
			'https://*.blob.core.windows.net',
			'https://opengraph.githubassets.com',
			'https://*.githubusercontent.com',
			'https://*.twimg.com',
			'https://*.fbcdn.net',
			'https://*.cloudfront.net',
			'https://*.imgur.com',
			'https://*.wp.com',
			apiBase
		].join(' '),
		'connect-src': `'self' ${apiBase} ${wsBase} https://accounts.google.com https://www.youtube.com https://fonts.googleapis.com https://fonts.gstatic.com`,
		'frame-src': 'https://accounts.google.com https://www.youtube-nocookie.com'
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
	response.headers.set('Permissions-Policy', 'microphone=(self), camera=()');

	return response;
};
