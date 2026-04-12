import { describe, it, expect, beforeEach, vi } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

vi.mock('svelte/reactivity', () => {
	return {
		SvelteMap: Map
	};
});

vi.mock('$lib/utils/theme.js', () => ({
	getTheme: vi.fn().mockReturnValue('phosphor'),
	applyTheme: vi.fn(),
	THEMES: [
		{ id: 'phosphor', name: 'Phosphor Green' },
		{ id: 'midnight', name: 'Midnight' },
		{ id: 'ember', name: 'Ember' },
		{ id: 'light', name: 'Light' }
	]
}));

vi.mock('$lib/types/models.js', () => ({
	ReportType: { User: 0, Message: 1, Server: 2 }
}));

import { UIStore } from './ui-store.svelte';
import { applyTheme } from '$lib/utils/theme.js';

describe('UIStore', () => {
	let store: UIStore;

	beforeEach(() => {
		vi.clearAllMocks();
		store = new UIStore();
	});

	describe('initial state', () => {
		it('should start with isInitialLoading true', () => {
			expect(store.isInitialLoading).toBe(true);
		});

		it('should start with no error', () => {
			expect(store.error).toBeNull();
		});

		it('should start with isHubConnected false', () => {
			expect(store.isHubConnected).toBe(false);
		});

		it('should start with showFriendsPanel false', () => {
			expect(store.showFriendsPanel).toBe(false);
		});

		it('should start with modals closed', () => {
			expect(store.showCreateServer).toBe(false);
			expect(store.showCreateChannel).toBe(false);
			expect(store.settingsOpen).toBe(false);
			expect(store.serverSettingsOpen).toBe(false);
			expect(store.bugReportOpen).toBe(false);
			expect(store.reportModal).toBeNull();
		});

		it('should start with theme from getTheme()', () => {
			expect(store.theme).toBe('phosphor');
		});

		it('should start with friendsTab as all', () => {
			expect(store.friendsTab).toBe('all');
		});

		it('should start with empty form fields', () => {
			expect(store.newServerName).toBe('');
			expect(store.newChannelName).toBe('');
			expect(store.newChannelType).toBe('text');
		});

		it('should start with lightbox closed', () => {
			expect(store.lightboxImageUrl).toBeNull();
		});

		it('should start with mobile panels closed', () => {
			expect(store.mobileNavOpen).toBe(false);
			expect(store.mobileMembersOpen).toBe(false);
		});
	});

	describe('openSettings / closeSettings', () => {
		it('should open settings with profile category', () => {
			store.openSettings();
			expect(store.settingsOpen).toBe(true);
			expect(store.settingsCategory).toBe('profile');
		});

		it('should close settings', () => {
			store.openSettings();
			store.closeSettings();
			expect(store.settingsOpen).toBe(false);
		});
	});

	describe('setTheme', () => {
		it('should update theme and call applyTheme', () => {
			store.setTheme('midnight');
			expect(store.theme).toBe('midnight');
			expect(applyTheme).toHaveBeenCalledWith('midnight');
		});
	});

	describe('openServerSettings / closeServerSettings', () => {
		it('should open server settings with general category', () => {
			store.openServerSettings();
			expect(store.serverSettingsOpen).toBe(true);
			expect(store.serverSettingsCategory).toBe('general');
		});

		it('should close server settings', () => {
			store.openServerSettings();
			store.closeServerSettings();
			expect(store.serverSettingsOpen).toBe(false);
		});
	});

	describe('openReportModal / closeReportModal', () => {
		it('should open report modal with given data', () => {
			store.openReportModal(0, 'user-123', 'TestUser');
			expect(store.reportModal).toEqual({
				reportType: 0,
				targetId: 'user-123',
				targetName: 'TestUser'
			});
		});

		it('should close report modal', () => {
			store.openReportModal(0, 'user-123', 'TestUser');
			store.closeReportModal();
			expect(store.reportModal).toBeNull();
		});
	});

	describe('dismissAlphaNotification', () => {
		it('should set showAlphaNotification to false', () => {
			store.showAlphaNotification = true;
			store.dismissAlphaNotification();
			expect(store.showAlphaNotification).toBe(false);
		});
	});

	describe('openImagePreview / closeImagePreview', () => {
		it('should set lightbox URL', () => {
			store.openImagePreview('https://example.com/img.png');
			expect(store.lightboxImageUrl).toBe('https://example.com/img.png');
		});

		it('should clear lightbox URL', () => {
			store.openImagePreview('https://example.com/img.png');
			store.closeImagePreview();
			expect(store.lightboxImageUrl).toBeNull();
		});
	});

	describe('setError', () => {
		it('should set error message from Error', () => {
			store.setError(new Error('something went wrong'));
			expect(store.error).toBe('something went wrong');
		});

		it('should set generic message for non-Error values', () => {
			store.setError('string error');
			expect(store.error).toBe('An unexpected error occurred.');
		});

		it('should set generic message for null', () => {
			store.setError(null);
			expect(store.error).toBe('An unexpected error occurred.');
		});
	});

	describe('setTransientError', () => {
		beforeEach(() => {
			vi.useFakeTimers();
		});

		afterEach(() => {
			vi.useRealTimers();
		});

		it('should set error message', () => {
			store.setTransientError('transient problem');
			expect(store.error).toBe('transient problem');
		});

		it('should clear error after duration', () => {
			store.setTransientError('transient problem', 3000);
			expect(store.error).toBe('transient problem');

			vi.advanceTimersByTime(3000);
			expect(store.error).toBeNull();
		});

		it('should not clear error if message changed', () => {
			store.setTransientError('first', 3000);
			store.error = 'different error';

			vi.advanceTimersByTime(3000);
			expect(store.error).toBe('different error');
		});

		it('should cancel previous timer when called again', () => {
			store.setTransientError('first', 3000);
			store.setTransientError('second', 3000);

			vi.advanceTimersByTime(3000);
			expect(store.error).toBeNull();
		});
	});

	describe('resetNavigation', () => {
		it('should reset all navigation state', () => {
			store.showFriendsPanel = true;
			store.friendsTab = 'pending';
			store.settingsOpen = true;
			store.serverSettingsOpen = true;
			store.mobileNavOpen = true;
			store.mobileMembersOpen = true;
			store.lightboxImageUrl = 'https://example.com/img.png';
			store.error = 'some error';

			store.resetNavigation();

			expect(store.showFriendsPanel).toBe(false);
			expect(store.friendsTab).toBe('all');
			expect(store.settingsOpen).toBe(false);
			expect(store.serverSettingsOpen).toBe(false);
			expect(store.mobileNavOpen).toBe(false);
			expect(store.mobileMembersOpen).toBe(false);
			expect(store.lightboxImageUrl).toBeNull();
			expect(store.error).toBeNull();
		});
	});

	describe('reaction helpers', () => {
		it('should generate correct reaction toggle key', () => {
			expect(UIStore.reactionToggleKey('msg-1', '👍')).toBe('msg-1:👍');
		});

		it('should track pending reactions', () => {
			expect(store.isReactionPending('msg-1', '👍')).toBe(false);
			store.setReactionPending('msg-1:👍', true);
			expect(store.isReactionPending('msg-1', '👍')).toBe(true);
		});

		it('should remove pending reaction', () => {
			store.setReactionPending('msg-1:👍', true);
			store.setReactionPending('msg-1:👍', false);
			expect(store.isReactionPending('msg-1', '👍')).toBe(false);
		});
	});
});
