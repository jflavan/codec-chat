import { describe, it, expect, beforeEach, vi } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

vi.mock('$app/environment', () => ({
	browser: true
}));

import { AnnouncementStore, type Announcement } from './announcement-store.svelte';

function makeAnnouncement(overrides: Partial<Announcement> = {}): Announcement {
	return {
		id: 'ann-1',
		title: 'Test Announcement',
		body: 'This is a test.',
		createdAt: '2026-01-01T00:00:00Z',
		expiresAt: null,
		...overrides
	};
}

describe('AnnouncementStore', () => {
	beforeEach(() => {
		localStorage.clear();
		vi.clearAllMocks();
	});

	it('should initialize with empty announcements', () => {
		const store = new AnnouncementStore();
		expect(store.announcements).toEqual([]);
	});

	it('should initialize with no active announcement', () => {
		const store = new AnnouncementStore();
		expect(store.activeAnnouncement).toBeNull();
	});

	it('should load previously dismissed IDs from localStorage', () => {
		localStorage.setItem('dismissed-announcements', JSON.stringify(['old-1', 'old-2']));
		const store = new AnnouncementStore();
		expect(store.dismissedIds.has('old-1')).toBe(true);
		expect(store.dismissedIds.has('old-2')).toBe(true);
	});

	it('should handle malformed JSON in localStorage gracefully', () => {
		localStorage.setItem('dismissed-announcements', 'not-json');
		const store = new AnnouncementStore();
		expect(store.dismissedIds.size).toBe(0);
	});

	describe('setAnnouncements', () => {
		it('should set announcements array', () => {
			const store = new AnnouncementStore();
			const items = [makeAnnouncement({ id: 'a1' }), makeAnnouncement({ id: 'a2' })];
			store.setAnnouncements(items);
			expect(store.announcements).toHaveLength(2);
			expect(store.announcements[0].id).toBe('a1');
		});

		it('should update activeAnnouncement to first non-dismissed', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'a1' }),
				makeAnnouncement({ id: 'a2' })
			]);
			expect(store.activeAnnouncement?.id).toBe('a1');
		});

		it('should skip dismissed announcements for activeAnnouncement', () => {
			localStorage.setItem('dismissed-announcements', JSON.stringify(['a1']));
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'a1' }),
				makeAnnouncement({ id: 'a2' })
			]);
			expect(store.activeAnnouncement?.id).toBe('a2');
		});

		it('should return null activeAnnouncement when all are dismissed', () => {
			localStorage.setItem('dismissed-announcements', JSON.stringify(['a1', 'a2']));
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'a1' }),
				makeAnnouncement({ id: 'a2' })
			]);
			expect(store.activeAnnouncement).toBeNull();
		});
	});

	describe('dismiss', () => {
		it('should add id to dismissedIds', () => {
			const store = new AnnouncementStore();
			store.dismiss('ann-1');
			expect(store.dismissedIds.has('ann-1')).toBe(true);
		});

		it('should persist dismissed IDs to localStorage', () => {
			const store = new AnnouncementStore();
			store.dismiss('ann-1');
			const stored = JSON.parse(localStorage.getItem('dismissed-announcements') ?? '[]');
			expect(stored).toContain('ann-1');
		});

		it('should update activeAnnouncement after dismissal', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'a1' }),
				makeAnnouncement({ id: 'a2' })
			]);
			expect(store.activeAnnouncement?.id).toBe('a1');

			store.dismiss('a1');
			expect(store.activeAnnouncement?.id).toBe('a2');
		});

		it('should handle dismissing the same id twice', () => {
			const store = new AnnouncementStore();
			store.dismiss('ann-1');
			store.dismiss('ann-1');
			expect(store.dismissedIds.has('ann-1')).toBe(true);
			const stored = JSON.parse(localStorage.getItem('dismissed-announcements') ?? '[]');
			// Set deduplicates
			expect(stored.filter((id: string) => id === 'ann-1')).toHaveLength(1);
		});

		it('should accumulate multiple dismissed IDs', () => {
			const store = new AnnouncementStore();
			store.dismiss('a1');
			store.dismiss('a2');
			expect(store.dismissedIds.has('a1')).toBe(true);
			expect(store.dismissedIds.has('a2')).toBe(true);
		});
	});
});
