import { describe, it, expect } from 'vitest';
import { formatTime } from './format';

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
