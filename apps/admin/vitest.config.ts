import { fileURLToPath } from 'url';
import path from 'path';
import { defineConfig } from 'vitest/config';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

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
			$lib: path.resolve(__dirname, 'src/lib'),
			'$env/dynamic/public': path.resolve(__dirname, 'src/test-env.ts')
		}
	}
});
