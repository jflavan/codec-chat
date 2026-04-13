import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { formatTime, formatMessageTimestamp, formatDateSeparator, isDifferentDay } from './format';

describe('formatTime', () => {
	it('formats a valid ISO string', () => {
		const result = formatTime('2024-01-15T14:30:00Z');
		expect(result).toBeTruthy();
		expect(result).toContain(':');
	});

	it('returns empty string for invalid date', () => {
		expect(formatTime('not-a-date')).toBe('');
	});

	it('returns empty string for empty input', () => {
		expect(formatTime('')).toBe('');
	});

	it('caches results for same input', () => {
		const iso = '2024-06-15T10:00:00Z';
		const first = formatTime(iso);
		const second = formatTime(iso);
		expect(first).toBe(second);
	});

	it('handles different valid dates', () => {
		const r1 = formatTime('2024-01-01T00:00:00Z');
		const r2 = formatTime('2024-01-01T12:00:00Z');
		expect(r1).toBeTruthy();
		expect(r2).toBeTruthy();
	});
});

describe('formatMessageTimestamp', () => {
	beforeEach(() => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date('2026-04-13T15:00:00Z'));
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('shows only time for today', () => {
		const result = formatMessageTimestamp('2026-04-13T10:30:00Z');
		expect(result).toContain(':');
		expect(result).not.toContain('Yesterday');
		expect(result).not.toContain('2026');
	});

	it('shows "Yesterday at" for yesterday', () => {
		const result = formatMessageTimestamp('2026-04-12T10:30:00Z');
		expect(result).toMatch(/^Yesterday at .+/);
	});

	it('shows full date and time for older messages', () => {
		const result = formatMessageTimestamp('2026-04-01T10:30:00Z');
		expect(result).toContain('2026');
		expect(result).toContain(':');
	});

	it('returns empty string for invalid date', () => {
		expect(formatMessageTimestamp('garbage')).toBe('');
	});

	it('caches results for same input', () => {
		const iso = '2026-04-13T09:00:00Z';
		const first = formatMessageTimestamp(iso);
		const second = formatMessageTimestamp(iso);
		expect(first).toBe(second);
	});
});

describe('formatDateSeparator', () => {
	beforeEach(() => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date('2026-04-13T15:00:00Z'));
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('returns "Today" for today', () => {
		expect(formatDateSeparator('2026-04-13T08:00:00Z')).toBe('Today');
	});

	it('returns "Yesterday" for yesterday', () => {
		expect(formatDateSeparator('2026-04-12T20:00:00Z')).toBe('Yesterday');
	});

	it('returns month and day for earlier this year', () => {
		const result = formatDateSeparator('2026-01-15T12:00:00Z');
		expect(result).toContain('January');
		expect(result).toContain('15');
		expect(result).not.toContain('2026');
	});

	it('includes year for a different year', () => {
		const result = formatDateSeparator('2025-07-04T12:00:00Z');
		expect(result).toContain('July');
		expect(result).toContain('4');
		expect(result).toContain('2025');
	});

	it('returns empty string for invalid date', () => {
		expect(formatDateSeparator('nope')).toBe('');
	});
});

describe('isDifferentDay', () => {
	it('returns false for same day', () => {
		expect(isDifferentDay('2026-04-13T08:00:00Z', '2026-04-13T22:00:00Z')).toBe(false);
	});

	it('returns true for different days', () => {
		expect(isDifferentDay('2026-04-12T12:00:00Z', '2026-04-13T12:00:00Z')).toBe(true);
	});

	it('returns true for different months', () => {
		expect(isDifferentDay('2026-03-15T12:00:00Z', '2026-04-15T12:00:00Z')).toBe(true);
	});

	it('returns true for different years', () => {
		expect(isDifferentDay('2025-06-15T12:00:00Z', '2026-06-15T12:00:00Z')).toBe(true);
	});

	it('returns false for identical timestamps', () => {
		expect(isDifferentDay('2026-04-13T15:00:00Z', '2026-04-13T15:00:00Z')).toBe(false);
	});
});
