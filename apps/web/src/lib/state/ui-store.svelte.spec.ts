import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { UIStore } from './ui-store.svelte.js';
import { ApiError } from '$lib/api/client.js';
import { ReportType } from '$lib/types/models.js';

describe('UIStore', () => {
	let ui: UIStore;

	beforeEach(() => {
		ui = new UIStore();
	});

	describe('initial state', () => {
		it('starts with isInitialLoading true', () => {
			expect(ui.isInitialLoading).toBe(true);
		});

		it('starts with showFriendsPanel false', () => {
			expect(ui.showFriendsPanel).toBe(false);
		});

		it('starts with modals closed', () => {
			expect(ui.showCreateServer).toBe(false);
			expect(ui.showCreateChannel).toBe(false);
			expect(ui.settingsOpen).toBe(false);
			expect(ui.serverSettingsOpen).toBe(false);
			expect(ui.bugReportOpen).toBe(false);
			expect(ui.reportModal).toBe(null);
		});

		it('starts with no error', () => {
			expect(ui.error).toBe(null);
		});

		it('starts with isHubConnected false', () => {
			expect(ui.isHubConnected).toBe(false);
		});

		it('starts with empty form fields', () => {
			expect(ui.newServerName).toBe('');
			expect(ui.newChannelName).toBe('');
			expect(ui.newChannelType).toBe('text');
		});

		it('starts with friendsTab all', () => {
			expect(ui.friendsTab).toBe('all');
		});

		it('starts with no lightbox image', () => {
			expect(ui.lightboxImageUrl).toBe(null);
		});

		it('starts with empty pending reactions', () => {
			expect(ui.pendingReactionKeys.size).toBe(0);
		});
	});

	describe('openSettings / closeSettings', () => {
		it('opens settings with profile category', () => {
			ui.openSettings();
			expect(ui.settingsOpen).toBe(true);
			expect(ui.settingsCategory).toBe('profile');
		});

		it('closes settings', () => {
			ui.openSettings();
			ui.closeSettings();
			expect(ui.settingsOpen).toBe(false);
		});
	});

	describe('openServerSettings / closeServerSettings', () => {
		it('opens server settings with general category', () => {
			ui.openServerSettings();
			expect(ui.serverSettingsOpen).toBe(true);
			expect(ui.serverSettingsCategory).toBe('general');
		});

		it('closes server settings', () => {
			ui.openServerSettings();
			ui.closeServerSettings();
			expect(ui.serverSettingsOpen).toBe(false);
		});
	});

	describe('openReportModal / closeReportModal', () => {
		it('opens report modal with correct data', () => {
			ui.openReportModal(ReportType.User, 'user-123', 'BadUser');
			expect(ui.reportModal).toEqual({
				reportType: ReportType.User,
				targetId: 'user-123',
				targetName: 'BadUser'
			});
		});

		it('closes report modal', () => {
			ui.openReportModal(ReportType.User, 'user-123', 'BadUser');
			ui.closeReportModal();
			expect(ui.reportModal).toBe(null);
		});
	});

	describe('openDiscordWizard / closeDiscordWizard', () => {
		it('opens with default create mode', () => {
			ui.openDiscordWizard();
			expect(ui.discordWizardOpen).toBe(true);
			expect(ui.discordWizardMode).toBe('create');
			expect(ui.discordWizardServerId).toBe(null);
		});

		it('opens with existing mode and server id', () => {
			ui.openDiscordWizard('existing', 'srv-123');
			expect(ui.discordWizardMode).toBe('existing');
			expect(ui.discordWizardServerId).toBe('srv-123');
		});

		it('closes discord wizard', () => {
			ui.openDiscordWizard();
			ui.closeDiscordWizard();
			expect(ui.discordWizardOpen).toBe(false);
		});
	});

	describe('dismissAlphaNotification', () => {
		it('hides alpha notification', () => {
			ui.showAlphaNotification = true;
			ui.dismissAlphaNotification();
			expect(ui.showAlphaNotification).toBe(false);
		});
	});

	describe('openImagePreview / closeImagePreview', () => {
		it('sets lightbox URL', () => {
			ui.openImagePreview('https://example.com/image.png');
			expect(ui.lightboxImageUrl).toBe('https://example.com/image.png');
		});

		it('clears lightbox URL', () => {
			ui.openImagePreview('https://example.com/image.png');
			ui.closeImagePreview();
			expect(ui.lightboxImageUrl).toBe(null);
		});
	});

	describe('setError', () => {
		it('handles ApiError', () => {
			ui.setError(new ApiError(404, 'Not found'));
			expect(ui.error).toBe('Not found');
		});

		it('handles generic Error', () => {
			ui.setError(new Error('Something broke'));
			expect(ui.error).toBe('Something broke');
		});

		it('handles unknown error', () => {
			ui.setError('string error');
			expect(ui.error).toBe('An unexpected error occurred.');
		});

		it('handles null', () => {
			ui.setError(null);
			expect(ui.error).toBe('An unexpected error occurred.');
		});
	});

	describe('setTransientError', () => {
		beforeEach(() => {
			vi.useFakeTimers();
		});

		it('sets error and clears after duration', () => {
			ui.setTransientError('Temporary error', 1000);
			expect(ui.error).toBe('Temporary error');

			vi.advanceTimersByTime(1000);
			expect(ui.error).toBe(null);
		});

		it('does not clear if error changed', () => {
			ui.setTransientError('Temp', 1000);
			ui.error = 'Different error';

			vi.advanceTimersByTime(1000);
			expect(ui.error).toBe('Different error');
		});

		it('cancels previous timer on new call', () => {
			ui.setTransientError('First', 1000);
			ui.setTransientError('Second', 1000);

			vi.advanceTimersByTime(1000);
			expect(ui.error).toBe(null);
		});

		afterEach(() => {
			vi.useRealTimers();
		});
	});

	describe('resetNavigation', () => {
		it('resets navigation state', () => {
			ui.showFriendsPanel = true;
			ui.friendsTab = 'pending';
			ui.settingsOpen = true;
			ui.serverSettingsOpen = true;
			ui.mobileNavOpen = true;
			ui.mobileMembersOpen = true;
			ui.lightboxImageUrl = 'https://example.com/image.png';
			ui.error = 'Some error';

			ui.resetNavigation();

			expect(ui.showFriendsPanel).toBe(false);
			expect(ui.friendsTab).toBe('all');
			expect(ui.settingsOpen).toBe(false);
			expect(ui.serverSettingsOpen).toBe(false);
			expect(ui.mobileNavOpen).toBe(false);
			expect(ui.mobileMembersOpen).toBe(false);
			expect(ui.lightboxImageUrl).toBe(null);
			expect(ui.error).toBe(null);
		});
	});

	describe('reaction helpers', () => {
		it('reactionToggleKey creates correct key', () => {
			expect(UIStore.reactionToggleKey('msg-1', 'thumbsup')).toBe('msg-1:thumbsup');
		});

		it('isReactionPending returns false when not pending', () => {
			expect(ui.isReactionPending('msg-1', 'thumbsup')).toBe(false);
		});

		it('setReactionPending adds and removes keys', () => {
			const key = UIStore.reactionToggleKey('msg-1', 'thumbsup');

			ui.setReactionPending(key, true);
			expect(ui.isReactionPending('msg-1', 'thumbsup')).toBe(true);

			ui.setReactionPending(key, false);
			expect(ui.isReactionPending('msg-1', 'thumbsup')).toBe(false);
		});
	});

	describe('setTheme', () => {
		it('updates theme', () => {
			ui.setTheme('light');
			expect(ui.theme).toBe('light');
		});
	});
});
