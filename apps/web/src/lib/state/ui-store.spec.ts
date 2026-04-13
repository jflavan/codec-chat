import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));
vi.mock('$lib/utils/theme.js', () => ({ getTheme: () => 'dark', applyTheme: vi.fn() }));
vi.mock('$lib/api/client.js', () => ({
	ApiError: class ApiError extends Error {
		status: number;
		constructor(status: number, msg = `API error ${status}`) {
			super(msg);
			this.name = 'ApiError';
			this.status = status;
		}
	}
}));
vi.mock('$lib/types/models.js', () => ({
	ReportType: { User: 0, Message: 1, Server: 2 }
}));

import { UIStore } from './ui-store.svelte';
import { applyTheme } from '$lib/utils/theme.js';
import { ApiError } from '$lib/api/client.js';
import { ReportType } from '$lib/types/models.js';

describe('UIStore', () => {
	let store: UIStore;

	beforeEach(() => {
		vi.clearAllMocks();
		store = new UIStore();
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have isInitialLoading true', () => {
			expect(store.isInitialLoading).toBe(true);
		});

		it('should have showFriendsPanel false', () => {
			expect(store.showFriendsPanel).toBe(false);
		});

		it('should have showCreateServer false', () => {
			expect(store.showCreateServer).toBe(false);
		});

		it('should have showCreateChannel false', () => {
			expect(store.showCreateChannel).toBe(false);
		});

		it('should have settingsOpen false', () => {
			expect(store.settingsOpen).toBe(false);
		});

		it('should have settingsCategory as profile', () => {
			expect(store.settingsCategory).toBe('profile');
		});

		it('should have serverSettingsOpen false', () => {
			expect(store.serverSettingsOpen).toBe(false);
		});

		it('should have reportModal null', () => {
			expect(store.reportModal).toBeNull();
		});

		it('should have error null', () => {
			expect(store.error).toBeNull();
		});

		it('should have lightboxImageUrl null', () => {
			expect(store.lightboxImageUrl).toBeNull();
		});

		it('should have theme from getTheme()', () => {
			expect(store.theme).toBe('dark');
		});

		it('should have discordWizardOpen false', () => {
			expect(store.discordWizardOpen).toBe(false);
		});

		it('should have empty pendingReactionKeys', () => {
			expect(store.pendingReactionKeys.size).toBe(0);
		});
	});

	// --- Settings ---

	describe('openSettings', () => {
		it('should set settingsOpen to true and category to profile', () => {
			store.settingsCategory = 'account';
			store.openSettings();
			expect(store.settingsOpen).toBe(true);
			expect(store.settingsCategory).toBe('profile');
		});
	});

	describe('closeSettings', () => {
		it('should set settingsOpen to false', () => {
			store.settingsOpen = true;
			store.closeSettings();
			expect(store.settingsOpen).toBe(false);
		});
	});

	// --- Server Settings ---

	describe('openServerSettings', () => {
		it('should set serverSettingsOpen to true and category to general', () => {
			store.serverSettingsCategory = 'channels';
			store.openServerSettings();
			expect(store.serverSettingsOpen).toBe(true);
			expect(store.serverSettingsCategory).toBe('general');
		});
	});

	describe('closeServerSettings', () => {
		it('should set serverSettingsOpen to false', () => {
			store.serverSettingsOpen = true;
			store.closeServerSettings();
			expect(store.serverSettingsOpen).toBe(false);
		});
	});

	// --- Report Modal ---

	describe('openReportModal', () => {
		it('should set reportModal with given values', () => {
			store.openReportModal(ReportType.User, 'user-123', 'TestUser');
			expect(store.reportModal).toEqual({
				reportType: ReportType.User,
				targetId: 'user-123',
				targetName: 'TestUser'
			});
		});

		it('should support different report types', () => {
			store.openReportModal(ReportType.Message, 'msg-456', 'Bad message');
			expect(store.reportModal?.reportType).toBe(ReportType.Message);
		});
	});

	describe('closeReportModal', () => {
		it('should set reportModal to null', () => {
			store.openReportModal(ReportType.User, 'user-123', 'TestUser');
			store.closeReportModal();
			expect(store.reportModal).toBeNull();
		});
	});

	// --- Discord Wizard ---

	describe('openDiscordWizard', () => {
		it('should open wizard with default create mode', () => {
			store.openDiscordWizard();
			expect(store.discordWizardOpen).toBe(true);
			expect(store.discordWizardMode).toBe('create');
			expect(store.discordWizardServerId).toBeNull();
		});

		it('should open wizard with existing mode and serverId', () => {
			store.openDiscordWizard('existing', 'server-789');
			expect(store.discordWizardOpen).toBe(true);
			expect(store.discordWizardMode).toBe('existing');
			expect(store.discordWizardServerId).toBe('server-789');
		});
	});

	describe('closeDiscordWizard', () => {
		it('should close the wizard', () => {
			store.discordWizardOpen = true;
			store.closeDiscordWizard();
			expect(store.discordWizardOpen).toBe(false);
		});
	});

	// --- Alpha Notification ---

	describe('dismissAlphaNotification', () => {
		it('should set showAlphaNotification to false', () => {
			store.showAlphaNotification = true;
			store.dismissAlphaNotification();
			expect(store.showAlphaNotification).toBe(false);
		});
	});

	// --- Image Preview ---

	describe('openImagePreview', () => {
		it('should set lightboxImageUrl', () => {
			store.openImagePreview('https://example.com/image.png');
			expect(store.lightboxImageUrl).toBe('https://example.com/image.png');
		});
	});

	describe('closeImagePreview', () => {
		it('should set lightboxImageUrl to null', () => {
			store.lightboxImageUrl = 'https://example.com/image.png';
			store.closeImagePreview();
			expect(store.lightboxImageUrl).toBeNull();
		});
	});

	// --- Error handling ---

	describe('setError', () => {
		it('should set error message from Error instance', () => {
			store.setError(new Error('Something went wrong'));
			expect(store.error).toBe('Something went wrong');
		});

		it('should set error message from ApiError instance', () => {
			store.setError(new ApiError(404, 'Not found'));
			expect(store.error).toBe('Not found');
		});

		it('should set generic message for unknown error types', () => {
			store.setError('just a string');
			expect(store.error).toBe('An unexpected error occurred.');
		});

		it('should set generic message for null', () => {
			store.setError(null);
			expect(store.error).toBe('An unexpected error occurred.');
		});

		it('should set generic message for undefined', () => {
			store.setError(undefined);
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

		it('should set error message immediately', () => {
			store.setTransientError('Temporary error');
			expect(store.error).toBe('Temporary error');
		});

		it('should clear error after default timeout', () => {
			store.setTransientError('Temporary error');
			vi.advanceTimersByTime(5000);
			expect(store.error).toBeNull();
		});

		it('should clear error after custom timeout', () => {
			store.setTransientError('Quick error', 2000);
			vi.advanceTimersByTime(1999);
			expect(store.error).toBe('Quick error');
			vi.advanceTimersByTime(1);
			expect(store.error).toBeNull();
		});

		it('should replace previous transient error', () => {
			store.setTransientError('First error', 3000);
			store.setTransientError('Second error', 3000);
			expect(store.error).toBe('Second error');
			vi.advanceTimersByTime(3000);
			expect(store.error).toBeNull();
		});

		it('should not clear error if it was changed externally', () => {
			store.setTransientError('Transient', 2000);
			store.error = 'Permanent error';
			vi.advanceTimersByTime(2000);
			// The timer callback checks if error === message before clearing
			expect(store.error).toBe('Permanent error');
		});
	});

	// --- Theme ---

	describe('setTheme', () => {
		it('should update theme and call applyTheme', () => {
			store.setTheme('midnight');
			expect(store.theme).toBe('midnight');
			expect(applyTheme).toHaveBeenCalledWith('midnight');
		});
	});

	// --- Reset Navigation ---

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

	// --- Reaction helpers ---

	describe('reactionToggleKey', () => {
		it('should create a key from messageId and emoji', () => {
			expect(UIStore.reactionToggleKey('msg-1', '👍')).toBe('msg-1:👍');
		});
	});

	describe('isReactionPending', () => {
		it('should return false when no reactions are pending', () => {
			expect(store.isReactionPending('msg-1', '👍')).toBe(false);
		});

		it('should return true when reaction is pending', () => {
			store.setReactionPending('msg-1:👍', true);
			expect(store.isReactionPending('msg-1', '👍')).toBe(true);
		});
	});

	describe('setReactionPending', () => {
		it('should add key when pending is true', () => {
			store.setReactionPending('msg-1:👍', true);
			expect(store.pendingReactionKeys.has('msg-1:👍')).toBe(true);
		});

		it('should remove key when pending is false', () => {
			store.setReactionPending('msg-1:👍', true);
			store.setReactionPending('msg-1:👍', false);
			expect(store.pendingReactionKeys.has('msg-1:👍')).toBe(false);
		});
	});
});
