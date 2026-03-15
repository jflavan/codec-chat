import { describe, it, expect, beforeEach } from 'vitest';
import { recordEmojiUse, getFrequentEmojis } from './emoji-frequency';

describe('emoji-frequency', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	it('getFrequentEmojis returns defaults when no data', () => {
		const result = getFrequentEmojis();
		expect(result).toHaveLength(8);
		expect(result[0]).toBe('👍');
	});

	it('recordEmojiUse stores emoji and getFrequentEmojis returns it', () => {
		recordEmojiUse('🎯');
		const result = getFrequentEmojis();
		expect(result[0]).toBe('🎯');
	});

	it('recordEmojiUse increments count', () => {
		recordEmojiUse('🎯');
		recordEmojiUse('🎯');
		recordEmojiUse('🔥');
		const result = getFrequentEmojis();
		expect(result[0]).toBe('🎯'); // count=2 > count=1
	});

	it('getFrequentEmojis pads with defaults when fewer than limit', () => {
		recordEmojiUse('🎯');
		const result = getFrequentEmojis(4);
		expect(result).toHaveLength(4);
		expect(result[0]).toBe('🎯');
		// Remaining should be defaults not including 🎯
		expect(result).not.toContain(undefined);
	});

	it('getFrequentEmojis respects limit', () => {
		for (let i = 0; i < 20; i++) {
			recordEmojiUse(`emoji-${i}`);
		}
		const result = getFrequentEmojis(5);
		expect(result).toHaveLength(5);
	});

	it('handles corrupted localStorage gracefully', () => {
		localStorage.setItem('codec_frequent_emojis', 'not-json');
		const result = getFrequentEmojis();
		expect(result).toHaveLength(8); // defaults
	});
});
