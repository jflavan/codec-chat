import { defineConfig } from 'vitest/config';

export default defineConfig({
	test: {
		include: ['src/**/*.{test,spec}.{js,ts}'],
		environment: 'jsdom',
		globals: true,
		coverage: {
			provider: 'v8',
			include: ['src/lib/**/*.ts'],
			exclude: [
				'src/lib/types/**',
				// .svelte.ts files use Svelte 5 runes ($state) which require the
				// Svelte compiler to process — excluded from instrumented coverage
				'src/lib/state/**'
			]
		}
	},
	resolve: {
		alias: {
			$lib: '/Users/highfiveghost/conductor/workspaces/codec-chat/davis/apps/admin/src/lib',
			'$env/dynamic/public':
				'/Users/highfiveghost/conductor/workspaces/codec-chat/davis/apps/admin/src/test-env.ts'
		}
	}
});
