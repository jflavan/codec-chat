import { describe, it, expect } from 'vitest';
import { CUSTOM_EMOJI_EXACT_REGEX, CUSTOM_EMOJI_GLOBAL_REGEX } from './emoji-regex';

describe('CUSTOM_EMOJI_EXACT_REGEX', () => {
	it('matches valid shortcode', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':pepe:')).toBe(true);
	});

	it('captures name without colons', () => {
		const match = ':pepe:'.match(CUSTOM_EMOJI_EXACT_REGEX);
		expect(match?.[1]).toBe('pepe');
	});

	it('matches 2-char name (minimum)', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':ab:')).toBe(true);
	});

	it('matches 32-char name (maximum)', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':' + 'a'.repeat(32) + ':')).toBe(true);
	});

	it('rejects 1-char name', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':a:')).toBe(false);
	});

	it('rejects 33-char name', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':' + 'a'.repeat(33) + ':')).toBe(false);
	});

	it('allows underscores and digits', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':emoji_1:')).toBe(true);
	});

	it('rejects spaces', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':not valid:')).toBe(false);
	});

	it('rejects special characters', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test(':hello!:')).toBe(false);
	});

	it('does not match embedded in text', () => {
		expect(CUSTOM_EMOJI_EXACT_REGEX.test('hello :pepe: world')).toBe(false);
	});
});

describe('CUSTOM_EMOJI_GLOBAL_REGEX', () => {
	it('finds emojis in text', () => {
		const text = 'Hello :pepe: world :kek:';
		const matches = [...text.matchAll(CUSTOM_EMOJI_GLOBAL_REGEX)];
		expect(matches).toHaveLength(2);
		expect(matches[0][1]).toBe('pepe');
		expect(matches[1][1]).toBe('kek');
	});

	it('returns empty for text without emojis', () => {
		const matches = [...'Hello world'.matchAll(CUSTOM_EMOJI_GLOBAL_REGEX)];
		expect(matches).toHaveLength(0);
	});
});
