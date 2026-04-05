import adapter from '@sveltejs/adapter-node';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	kit: {
		adapter: adapter({ out: 'build' }),
		csp: {
			mode: 'auto',
			directives: {
				'default-src': ['self'],
				'script-src': ['strict-dynamic', 'https://accounts.google.com/gsi/', 'https://www.google.com/recaptcha/', 'https://www.gstatic.com/recaptcha/'],
				'style-src': ['self', 'unsafe-inline', 'https://accounts.google.com'],
				'connect-src': ['self'],
				'object-src': ['none'],
				'base-uri': ['self']
			}
		}
	}
};

export default config;
