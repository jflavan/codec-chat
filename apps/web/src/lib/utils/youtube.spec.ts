import { describe, it, expect } from 'vitest';
import { extractYouTubeVideoId, extractYouTubeUrls } from './youtube';

describe('extractYouTubeVideoId', () => {
	it('extracts from standard watch URL', () => {
		expect(extractYouTubeVideoId('https://www.youtube.com/watch?v=dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('extracts from short URL', () => {
		expect(extractYouTubeVideoId('https://youtu.be/dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('extracts from embed URL', () => {
		expect(extractYouTubeVideoId('https://www.youtube.com/embed/dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('extracts from shorts URL', () => {
		expect(extractYouTubeVideoId('https://youtube.com/shorts/dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('extracts from live URL', () => {
		expect(extractYouTubeVideoId('https://www.youtube.com/live/dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('extracts from mobile URL', () => {
		expect(extractYouTubeVideoId('https://m.youtube.com/watch?v=dQw4w9WgXcQ')).toBe('dQw4w9WgXcQ');
	});

	it('handles extra query params', () => {
		expect(extractYouTubeVideoId('https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42')).toBe('dQw4w9WgXcQ');
	});

	it('returns null for non-YouTube URL', () => {
		expect(extractYouTubeVideoId('https://example.com/watch?v=abc12345678')).toBeNull();
	});

	it('returns null for invalid URL', () => {
		expect(extractYouTubeVideoId('not a url')).toBeNull();
	});

	it('returns null for empty string', () => {
		expect(extractYouTubeVideoId('')).toBeNull();
	});
});

describe('extractYouTubeUrls', () => {
	it('finds YouTube URLs in text', () => {
		const text = 'Check this: https://youtu.be/dQw4w9WgXcQ and more';
		const results = extractYouTubeUrls(text);
		expect(results).toHaveLength(1);
		expect(results[0].videoId).toBe('dQw4w9WgXcQ');
	});

	it('finds multiple YouTube URLs', () => {
		const text = 'https://youtu.be/abc12345678 and https://youtube.com/watch?v=def12345678';
		const results = extractYouTubeUrls(text);
		expect(results).toHaveLength(2);
	});

	it('deduplicates by video ID', () => {
		const text = 'https://youtu.be/dQw4w9WgXcQ and https://www.youtube.com/watch?v=dQw4w9WgXcQ';
		const results = extractYouTubeUrls(text);
		expect(results).toHaveLength(1);
	});

	it('returns empty for text without YouTube URLs', () => {
		expect(extractYouTubeUrls('Hello world')).toHaveLength(0);
	});

	it('returns empty for empty string', () => {
		expect(extractYouTubeUrls('')).toHaveLength(0);
	});
});
