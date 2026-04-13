import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));
vi.mock('$app/environment', () => ({ browser: true }));

import { AnnouncementStore } from './announcement-store.svelte';
import type { Announcement } from './announcement-store.svelte';

function makeAnnouncement(overrides: Partial<Announcement> = {}): Announcement {
	return {
		id: 'ann-1',
		title: 'Test Announcement',
		body: 'This is a test announcement.',
		createdAt: '2026-01-01T00:00:00Z',
		expiresAt: null,
		...overrides
	};
}

describe('AnnouncementStore', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		localStorage.clear();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have empty announcements array', () => {
			const store = new AnnouncementStore();
			expect(store.announcements).toEqual([]);
		});

		it('should have empty dismissedIds set', () => {
			const store = new AnnouncementStore();
			expect(store.dismissedIds.size).toBe(0);
		});

		it('should have null activeAnnouncement', () => {
			const store = new AnnouncementStore();
			expect(store.activeAnnouncement).toBeNull();
		});
	});

	// --- setAnnouncements ---

	describe('setAnnouncements', () => {
		it('should update announcements list', () => {
			const store = new AnnouncementStore();
			const items = [makeAnnouncement(), makeAnnouncement({ id: 'ann-2', title: 'Second' })];
			store.setAnnouncements(items);
			expect(store.announcements).toHaveLength(2);
			expect(store.announcements[0].id).toBe('ann-1');
			expect(store.announcements[1].id).toBe('ann-2');
		});

		it('should replace previous announcements', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([makeAnnouncement()]);
			store.setAnnouncements([makeAnnouncement({ id: 'ann-new' })]);
			expect(store.announcements).toHaveLength(1);
			expect(store.announcements[0].id).toBe('ann-new');
		});
	});

	// --- activeAnnouncement ---

	describe('activeAnnouncement', () => {
		it('should return first non-dismissed announcement', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'ann-1' }),
				makeAnnouncement({ id: 'ann-2' })
			]);
			expect(store.activeAnnouncement?.id).toBe('ann-1');
		});

		it('should skip dismissed announcements', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([
				makeAnnouncement({ id: 'ann-1' }),
				makeAnnouncement({ id: 'ann-2' })
			]);
			store.dismiss('ann-1');
			expect(store.activeAnnouncement?.id).toBe('ann-2');
		});

		it('should return null when all announcements are dismissed', () => {
			const store = new AnnouncementStore();
			store.setAnnouncements([makeAnnouncement({ id: 'ann-1' })]);
			store.dismiss('ann-1');
			expect(store.activeAnnouncement).toBeNull();
		});

		it('should return null when announcements list is empty', () => {
			const store = new AnnouncementStore();
			expect(store.activeAnnouncement).toBeNull();
		});
	});

	// --- dismiss ---

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

		it('should persist multiple dismissed IDs', () => {
			const store = new AnnouncementStore();
			store.dismiss('ann-1');
			store.dismiss('ann-2');
			const stored = JSON.parse(localStorage.getItem('dismissed-announcements') ?? '[]');
			expect(stored).toContain('ann-1');
			expect(stored).toContain('ann-2');
		});

		it('should be idempotent for same id', () => {
			const store = new AnnouncementStore();
			store.dismiss('ann-1');
			store.dismiss('ann-1');
			expect(store.dismissedIds.size).toBe(1);
		});
	});

	// --- localStorage hydration ---

	describe('localStorage hydration', () => {
		it('should load previously dismissed IDs from localStorage', () => {
			localStorage.setItem('dismissed-announcements', JSON.stringify(['ann-old']));
			const store = new AnnouncementStore();
			expect(store.dismissedIds.has('ann-old')).toBe(true);
		});

		it('should not show hydrated dismissed announcement as active', () => {
			localStorage.setItem('dismissed-announcements', JSON.stringify(['ann-1']));
			const store = new AnnouncementStore();
			store.setAnnouncements([makeAnnouncement({ id: 'ann-1' })]);
			expect(store.activeAnnouncement).toBeNull();
		});

		it('should handle invalid JSON in localStorage gracefully', () => {
			localStorage.setItem('dismissed-announcements', 'not-valid-json');
			const store = new AnnouncementStore();
			expect(store.dismissedIds.size).toBe(0);
		});

		it('should handle missing localStorage key', () => {
			const store = new AnnouncementStore();
			expect(store.dismissedIds.size).toBe(0);
		});
	});
});
