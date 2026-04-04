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
				'script-src': ['strict-dynamic'],
				'style-src': ['self', 'unsafe-inline', 'https://fonts.googleapis.com', 'https://accounts.google.com'],
				'font-src': ['self', 'https://fonts.gstatic.com'],
				'connect-src': ['self'],
				'object-src': ['none'],
				'base-uri': ['self'],
				'form-action': ['self', 'https://accounts.google.com'],
				'worker-src': ['self']
			}
		}
	}
};

export default config;
