// Push notification handlers for the Workbox-generated service worker.
// Imported via workbox.importScripts in the vite-pwa config.

self.addEventListener('push', function (event) {
	if (!event.data) return;

	var payload;
	try {
		payload = event.data.json();
	} catch (e) {
		payload = { type: 'generic', title: 'Codec', body: event.data.text() };
	}

	var options = {
		body: payload.body,
		icon: payload.icon || '/pwa-192x192.png',
		badge: '/pwa-192x192.png',
		tag: payload.tag,
		data: { url: payload.url || '/' },
		renotify: !!payload.tag
	};

	event.waitUntil(self.registration.showNotification(payload.title, options));
});

self.addEventListener('notificationclick', function (event) {
	event.notification.close();

	var url = (event.notification.data && event.notification.data.url) || '/';

	event.waitUntil(
		self.clients
			.matchAll({ type: 'window', includeUncontrolled: true })
			.then(function (windowClients) {
				for (var i = 0; i < windowClients.length; i++) {
					var client = windowClients[i];
					if (new URL(client.url).origin === self.location.origin) {
						client.focus();
						return;
					}
				}
				return self.clients.openWindow(url);
			})
	);
});
