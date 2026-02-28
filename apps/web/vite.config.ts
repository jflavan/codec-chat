import { sveltekit } from '@sveltejs/kit/vite';
import { SvelteKitPWA } from '@vite-pwa/sveltekit';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		sveltekit(),
		SvelteKitPWA({
			registerType: 'prompt',
			includeAssets: ['favicon.ico', 'favicon-16x16.png', 'favicon-32x32.png', 'apple-touch-icon.png', 'og-image.png'],
			manifest: {
				name: 'Codec — Real-Time Chat',
				short_name: 'Codec',
				description:
					'Codec is an open-source, real-time chat app inspired by Discord — built with SvelteKit and ASP.NET Core.',
				theme_color: '#050B07',
				background_color: '#050B07',
				display: 'standalone',
				display_override: ['standalone', 'minimal-ui'],
				scope: '/',
				start_url: '/',
				id: '/',
				lang: 'en',
				dir: 'ltr',
				orientation: 'any',
				categories: ['social', 'communication'],
				prefer_related_applications: false,
				related_applications: [],
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
					},
					{
						src: 'android-chrome-192x192.png',
						sizes: '192x192',
						type: 'image/png'
					},
					{
						src: 'android-chrome-512x512.png',
						sizes: '512x512',
						type: 'image/png'
					}
				],
				screenshots: [
					{
						src: 'screenshots/desktop-wide.png',
						sizes: '1280x720',
						type: 'image/png',
						form_factor: 'wide',
						label: 'Codec desktop chat view with servers, channels, and messages'
					},
					{
						src: 'screenshots/mobile-narrow.png',
						sizes: '390x1024',
						type: 'image/png',
						form_factor: 'narrow',
						label: 'Codec mobile chat view'
					}
				],
				shortcuts: [
					{
						name: 'Direct Messages',
						short_name: 'DMs',
						url: '/',
						description: 'Open your direct messages',
						icons: [{ src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' }]
					}
				],
				launch_handler: {
					client_mode: 'navigate-existing'
				},
				edge_side_panel: {
					preferred_width: 400
				},
				share_target: {
					action: '/',
					method: 'GET',
					enctype: 'application/x-www-form-urlencoded',
					params: {
						title: 'title',
						text: 'text',
						url: 'url'
					}
				},
				protocol_handlers: [
					{
						protocol: 'web+codec',
						url: '/%s'
					}
				],
				handle_links: 'preferred'
			} as Record<string, unknown>,
			workbox: {
				globPatterns: ['client/**/*.{html,js,css,ico,png,svg,webp,webmanifest}'],
				cleanupOutdatedCaches: true,
				runtimeCaching: [
					{
						urlPattern: /^https:\/\/fonts\.googleapis\.com\/.*/i,
						handler: 'CacheFirst',
						options: {
							cacheName: 'google-fonts-cache',
							expiration: {
								maxEntries: 10,
								maxAgeSeconds: 60 * 60 * 24 * 365
							},
							cacheableResponse: {
								statuses: [0, 200]
							}
						}
					},
					{
						urlPattern: /^https:\/\/fonts\.gstatic\.com\/.*/i,
						handler: 'CacheFirst',
						options: {
							cacheName: 'gstatic-fonts-cache',
							expiration: {
								maxEntries: 10,
								maxAgeSeconds: 60 * 60 * 24 * 365
							},
							cacheableResponse: {
								statuses: [0, 200]
							}
						}
					}
				]
			}
		})
	],
	server: {
		port: 5174,
		strictPort: true
	}
});
