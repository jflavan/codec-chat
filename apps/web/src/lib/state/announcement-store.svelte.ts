// apps/web/src/lib/state/announcement-store.svelte.ts
import { setContext } from 'svelte';
import { browser } from '$app/environment';

const ANNOUNCEMENT_KEY = Symbol('announcement-store');
const STORAGE_KEY = 'dismissed-announcements';

export interface Announcement {
	id: string;
	title: string;
	body: string;
	createdAt: string;
	expiresAt: string | null;
}

export function createAnnouncementStore(): AnnouncementStore {
	const store = new AnnouncementStore();
	setContext(ANNOUNCEMENT_KEY, store);
	return store;
}

export class AnnouncementStore {
	announcements = $state<Announcement[]>([]);
	dismissedIds = $state<Set<string>>(new Set(
		browser ? (() => { try { return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]'); } catch { return []; } })() : []
	));

	activeAnnouncement = $derived(
		this.announcements.find(a => !this.dismissedIds.has(a.id)) ?? null
	);

	setAnnouncements(items: Announcement[]): void {
		this.announcements = items;
	}

	dismiss(id: string): void {
		const next = new Set(this.dismissedIds);
		next.add(id);
		this.dismissedIds = next;
		if (browser) {
			localStorage.setItem(STORAGE_KEY, JSON.stringify([...next]));
		}
	}
}
