import { ApiClient } from '$lib/api/client.js';

/**
 * Manages Web Push API subscriptions.
 *
 * Handles requesting notification permission, subscribing to push via the
 * service worker, and syncing the subscription with the API server.
 */
export class PushNotificationManager {
	private vapidPublicKey: string | null = null;

	constructor(
		private api: ApiClient,
		private getToken: () => string | null
	) {}

	/** Whether the browser supports push notifications. */
	get isSupported(): boolean {
		return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
	}

	/** Current notification permission state. */
	get permission(): NotificationPermission {
		return this.isSupported ? Notification.permission : 'denied';
	}

	/** Whether the user has granted notification permission. */
	get isPermissionGranted(): boolean {
		return this.permission === 'granted';
	}

	/** Request notification permission from the user. */
	async requestPermission(): Promise<NotificationPermission> {
		if (!this.isSupported) return 'denied';
		return Notification.requestPermission();
	}

	/** Subscribe to push notifications. Returns true if successful. */
	async subscribe(): Promise<boolean> {
		const token = this.getToken();
		if (!this.isSupported || !token) return false;

		const permission = await this.requestPermission();
		if (permission !== 'granted') return false;

		try {
			const registration = await navigator.serviceWorker.ready;

			// Get VAPID public key if we don't have it yet.
			if (!this.vapidPublicKey) {
				const { publicKey } = await this.api.getVapidPublicKey();
				this.vapidPublicKey = publicKey;
			}

			const applicationServerKey = urlBase64ToUint8Array(this.vapidPublicKey);

			const subscription = await registration.pushManager.subscribe({
				userVisibleOnly: true,
				applicationServerKey: applicationServerKey.buffer as ArrayBuffer
			});

			const json = subscription.toJSON();
			if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) {
				return false;
			}

			await this.api.subscribePush(token, {
				endpoint: json.endpoint,
				p256dh: json.keys.p256dh,
				auth: json.keys.auth
			});

			return true;
		} catch (e) {
			console.error('Failed to subscribe to push notifications:', e);
			return false;
		}
	}

	/** Unsubscribe from push notifications. */
	async unsubscribe(): Promise<boolean> {
		const token = this.getToken();
		if (!this.isSupported || !token) return false;

		try {
			const registration = await navigator.serviceWorker.ready;
			const subscription = await registration.pushManager.getSubscription();

			if (subscription) {
				const endpoint = subscription.endpoint;
				await subscription.unsubscribe();
				await this.api.unsubscribePush(token, endpoint);
			}

			return true;
		} catch (e) {
			console.error('Failed to unsubscribe from push notifications:', e);
			return false;
		}
	}

	/** Check if the user currently has an active push subscription. */
	async isSubscribed(): Promise<boolean> {
		if (!this.isSupported) return false;

		try {
			const registration = await navigator.serviceWorker.ready;
			const subscription = await registration.pushManager.getSubscription();
			return subscription !== null;
		} catch {
			return false;
		}
	}
}

/** Convert a URL-safe base64 string to a Uint8Array (for applicationServerKey). */
function urlBase64ToUint8Array(base64String: string): Uint8Array {
	const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
	const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
	const rawData = atob(base64);
	const outputArray = new Uint8Array(rawData.length);
	for (let i = 0; i < rawData.length; ++i) {
		outputArray[i] = rawData.charCodeAt(i);
	}
	return outputArray;
}
