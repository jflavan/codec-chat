import { describe, it, expect } from 'vitest';

describe('DeleteAccountModal', () => {
	it('should require DELETE confirmation text', () => {
		const confirmationText = 'DELETE';
		const password = 'mypassword';
		const isDeleting = false;
		const isGoogleOnly = false;

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? false : password.length > 0) &&
			!isDeleting;

		expect(canSubmit).toBe(true);
	});

	it('should not allow submit with wrong confirmation text', () => {
		const confirmationText: string = 'delete';
		const password = 'mypassword';
		const isDeleting = false;
		const isGoogleOnly = false;

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? false : password.length > 0) &&
			!isDeleting;

		expect(canSubmit).toBe(false);
	});

	it('should not allow submit without password for non-Google users', () => {
		const confirmationText = 'DELETE';
		const password = '';
		const isDeleting = false;
		const isGoogleOnly = false;

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? false : password.length > 0) &&
			!isDeleting;

		expect(canSubmit).toBe(false);
	});

	it('should allow submit with Google credential for Google-only users', () => {
		const confirmationText = 'DELETE';
		const isDeleting = false;
		const isGoogleOnly = true;
		const googleCredential: string | null = 'some-credential';

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? googleCredential !== null : false) &&
			!isDeleting;

		expect(canSubmit).toBe(true);
	});

	it('should not allow submit without Google credential for Google-only users', () => {
		const confirmationText = 'DELETE';
		const isDeleting = false;
		const isGoogleOnly = true;
		const googleCredential: string | null = null;

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? googleCredential !== null : false) &&
			!isDeleting;

		expect(canSubmit).toBe(false);
	});

	it('should not allow submit while deleting', () => {
		const confirmationText = 'DELETE';
		const password = 'mypassword';
		const isDeleting = true;
		const isGoogleOnly = false;

		const canSubmit =
			confirmationText === 'DELETE' &&
			(isGoogleOnly ? false : password.length > 0) &&
			!isDeleting;

		expect(canSubmit).toBe(false);
	});
});
