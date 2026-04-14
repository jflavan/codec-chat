import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock $app/environment before importing the store
vi.mock('$app/environment', () => ({
	browser: true,
	dev: false,
	building: false
}));

import { AnnouncementStore } from './announcement-store.svelte.js';

describe('AnnouncementStore', () => {
	let store: AnnouncementStore;

	beforeEach(() => {
		localStorage.clear();
		store = new AnnouncementStore();
	});

	describe('initial state', () => {
		it('starts with empty announcements', () => {
			expect(store.announcements).toEqual([]);
		});

		it('starts with empty dismissed set', () => {
			expect(store.dismissedIds.size).toBe(0);
		});
	});

	describe('setAnnouncements', () => {
		it('sets announcements list', () => {
			const items = [
				{ id: '1', title: 'Test', body: 'Body', createdAt: '2024-01-01', expiresAt: null }
			];
			store.setAnnouncements(items);
			expect(store.announcements).toEqual(items);
		});

		it('replaces existing announcements', () => {
			store.setAnnouncements([
				{ id: '1', title: 'First', body: '', createdAt: '', expiresAt: null }
			]);
			store.setAnnouncements([
				{ id: '2', title: 'Second', body: '', createdAt: '', expiresAt: null }
			]);
			expect(store.announcements).toHaveLength(1);
			expect(store.announcements[0].id).toBe('2');
		});
	});

	describe('dismiss', () => {
		it('adds id to dismissed set', () => {
			store.dismiss('ann-1');
			expect(store.dismissedIds.has('ann-1')).toBe(true);
		});

		it('persists to localStorage', () => {
			store.dismiss('ann-1');
			const stored = JSON.parse(localStorage.getItem('dismissed-announcements') ?? '[]');
			expect(stored).toContain('ann-1');
		});

		it('accumulates dismissed ids', () => {
			store.dismiss('ann-1');
			store.dismiss('ann-2');
			expect(store.dismissedIds.size).toBe(2);
		});
	});

	describe('activeAnnouncement (derived)', () => {
		it('returns null when no announcements', () => {
			expect(store.activeAnnouncement).toBe(null);
		});

		it('returns first non-dismissed announcement', () => {
			store.setAnnouncements([
				{ id: '1', title: 'First', body: '', createdAt: '', expiresAt: null },
				{ id: '2', title: 'Second', body: '', createdAt: '', expiresAt: null }
			]);
			expect(store.activeAnnouncement?.id).toBe('1');
		});

		it('skips dismissed announcements', () => {
			store.dismiss('1');
			store.setAnnouncements([
				{ id: '1', title: 'First', body: '', createdAt: '', expiresAt: null },
				{ id: '2', title: 'Second', body: '', createdAt: '', expiresAt: null }
			]);
			expect(store.activeAnnouncement?.id).toBe('2');
		});

		it('returns null when all dismissed', () => {
			store.dismiss('1');
			store.setAnnouncements([
				{ id: '1', title: 'First', body: '', createdAt: '', expiresAt: null }
			]);
			expect(store.activeAnnouncement).toBe(null);
		});
	});

	describe('localStorage restoration', () => {
		it('restores dismissed ids from localStorage', () => {
			localStorage.setItem('dismissed-announcements', JSON.stringify(['prev-1', 'prev-2']));
			const restored = new AnnouncementStore();
			expect(restored.dismissedIds.has('prev-1')).toBe(true);
			expect(restored.dismissedIds.has('prev-2')).toBe(true);
		});

		it('handles corrupt localStorage gracefully', () => {
			localStorage.setItem('dismissed-announcements', 'not-json');
			const restored = new AnnouncementStore();
			expect(restored.dismissedIds.size).toBe(0);
		});
	});
});
