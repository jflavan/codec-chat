import { sveltekit } from '@sveltejs/kit/vite';
import { SvelteKitPWA } from '@vite-pwa/sveltekit';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		sveltekit(),
		SvelteKitPWA({
			registerType: 'prompt',
			includeAssets: ['favicon.ico', 'apple-touch-icon.png'],
			manifest: {
				name: 'Codec — Real-Time Chat',
				short_name: 'Codec',
				description:
					'Codec is an open-source, real-time chat app inspired by Discord — built with SvelteKit and ASP.NET Core.',
				theme_color: '#050B07',
				background_color: '#050B07',
				display: 'standalone',
				scope: '/',
				start_url: '/',
				icons: [
					{
						src: 'pwa-192x192.png',
						sizes: '192x192',
						type: 'image/png'
					},
					{
						src: 'pwa-512x512.png',
						sizes: '512x512',
						type: 'image/png'
					},
					{
						src: 'pwa-512x512.png',
						sizes: '512x512',
						type: 'image/png',
						purpose: 'maskable'
					}
				]
			},
			workbox: {
				globPatterns: ['client/**/*.{js,css,ico,png,svg,webp,webmanifest}']
			}
		})
	],
	server: {
		port: 5174,
		strictPort: true
	}
});
