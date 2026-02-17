import adapter from '@sveltejs/adapter-node';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	kit: {
		adapter: adapter({ out: 'build' }),
		serviceWorker: {
			register: false
		},
		csp: {
			mode: 'auto',
			directives: {
				'default-src': ['self'],
				'script-src': ['self', 'unsafe-inline', 'strict-dynamic', 'https://accounts.google.com'],
				'style-src': ['self', 'unsafe-inline', 'https://fonts.googleapis.com'],
				'font-src': ['self', 'https://fonts.gstatic.com'],
				'connect-src': ['self', 'https://fonts.googleapis.com', 'https://fonts.gstatic.com'],
				'object-src': ['none'],
				'base-uri': ['self'],
				'worker-src': ['self']
			}
		}
	}
};

export default config;
