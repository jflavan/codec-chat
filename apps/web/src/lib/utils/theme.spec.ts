import { describe, it, expect, beforeEach } from 'vitest';
import { THEMES, getTheme, applyTheme } from './theme';

describe('THEMES', () => {
	it('has 4 themes', () => {
		expect(THEMES).toHaveLength(4);
	});

	it('includes phosphor as first theme', () => {
		expect(THEMES[0].id).toBe('phosphor');
	});
});

describe('getTheme', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('returns phosphor by default', () => {
		expect(getTheme()).toBe('phosphor');
	});

	it('returns stored theme', () => {
		localStorage.setItem('codec-theme', 'midnight');
		expect(getTheme()).toBe('midnight');
	});

	it('returns phosphor for invalid stored value', () => {
		localStorage.setItem('codec-theme', 'invalid-theme');
		expect(getTheme()).toBe('phosphor');
	});
});

describe('applyTheme', () => {
	beforeEach(() => {
		localStorage.clear();
		delete document.documentElement.dataset.theme;
	});

	it('removes data-theme for phosphor', () => {
		document.documentElement.dataset.theme = 'midnight';
		applyTheme('phosphor');
		expect(document.documentElement.dataset.theme).toBeUndefined();
	});

	it('sets data-theme for non-phosphor theme', () => {
		applyTheme('midnight');
		expect(document.documentElement.dataset.theme).toBe('midnight');
	});

	it('persists to localStorage', () => {
		applyTheme('ember');
		expect(localStorage.getItem('codec-theme')).toBe('ember');
	});

	it('sets meta theme-color', () => {
		const meta = document.createElement('meta');
		meta.setAttribute('name', 'theme-color');
		meta.setAttribute('content', '');
		document.head.appendChild(meta);

		applyTheme('light');
		expect(meta.getAttribute('content')).toBe('#E8E8ED');

		document.head.removeChild(meta);
	});
});
