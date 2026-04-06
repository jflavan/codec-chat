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
		'img-src': [
			"'self'",
			'data:',
			'blob:',
			'https://lh3.googleusercontent.com',
			'https://*.blob.core.windows.net',
			'https://opengraph.githubassets.com',
			'https://*.githubusercontent.com',
			'https://*.twimg.com',
			'https://*.fbcdn.net',
			'https://*.cloudfront.net',
			'https://*.imgur.com',
			'https://*.wp.com',
			'https://cdn.discordapp.com',
			'https://avatars.githubusercontent.com',
			'https://*.giphy.com',
			apiBase
		].join(' '),
		'connect-src': `'self' ${apiBase} ${wsBase} https://accounts.google.com https://www.google.com https://play.google.com https://www.youtube.com https://fonts.googleapis.com https://fonts.gstatic.com https://github.com https://discord.com https://api.giphy.com`,
		'frame-src': 'https://accounts.google.com https://www.google.com https://www.youtube-nocookie.com https://github.com https://discord.com'
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
	response.headers.set('Permissions-Policy', 'microphone=(self), camera=(self), geolocation=(), payment=(), usb=(), bluetooth=()');
	response.headers.set('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');

	return response;
};
