import adapter from '@sveltejs/adapter-node';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	kit: {
		adapter: adapter({ out: 'build' }),
		csp: {
			mode: 'auto',
			directives: {
				'default-src': ['self'],
				'script-src': ['strict-dynamic'],
				'style-src': ['self', 'unsafe-inline'],
				'connect-src': ['self'],
				'object-src': ['none'],
				'base-uri': ['self']
			}
		}
	}
};

export default config;
