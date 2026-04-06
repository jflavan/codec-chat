import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('$env/dynamic/public', () => ({
	env: { PUBLIC_GIPHY_API_KEY: 'test-api-key' }
}));

import { getTrendingGifs, searchGifs } from './giphy.js';

function makeGiphyGif(id: string, title: string) {
	return {
		id,
		title,
		images: {
			original: { url: `https://giphy.com/original/${id}.gif`, width: '480', height: '360' },
			fixed_height: { url: `https://giphy.com/fixed/${id}.gif`, width: '200', height: '150' }
		}
	};
}

describe('giphy service', () => {
	let fetchSpy: ReturnType<typeof vi.fn>;

	beforeEach(() => {
		fetchSpy = vi.fn();
		vi.stubGlobal('fetch', fetchSpy);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	describe('getTrendingGifs', () => {
		it('fetches trending GIFs and maps results', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () =>
					Promise.resolve({
						data: [makeGiphyGif('1', 'Cat'), makeGiphyGif('2', 'Dog')]
					})
			});

			const result = await getTrendingGifs(25);

			expect(fetchSpy).toHaveBeenCalledOnce();
			const url = fetchSpy.mock.calls[0][0] as string;
			expect(url).toContain('/trending');
			expect(url).toContain('api_key=test-api-key');
			expect(url).toContain('rating=pg-13');

			expect(result).toHaveLength(2);
			expect(result[0]).toEqual({
				id: '1',
				title: 'Cat',
				previewUrl: 'https://giphy.com/fixed/1.gif',
				previewWidth: 200,
				previewHeight: 150,
				originalUrl: 'https://giphy.com/original/1.gif'
			});
		});

		it('returns empty array on fetch failure', async () => {
			fetchSpy.mockResolvedValueOnce({ ok: false });

			const result = await getTrendingGifs();

			expect(result).toEqual([]);
		});
	});

	describe('searchGifs', () => {
		it('searches GIFs with query parameter', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () =>
					Promise.resolve({
						data: [makeGiphyGif('3', 'Funny cat')]
					})
			});

			const result = await searchGifs('cat');

			const url = fetchSpy.mock.calls[0][0] as string;
			expect(url).toContain('/search');
			expect(url).toContain('q=cat');
			expect(result).toHaveLength(1);
			expect(result[0].title).toBe('Funny cat');
		});

		it('falls back to trending when query is empty', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () =>
					Promise.resolve({
						data: [makeGiphyGif('4', 'Trending')]
					})
			});

			const result = await searchGifs('   ');

			const url = fetchSpy.mock.calls[0][0] as string;
			expect(url).toContain('/trending');
			expect(result).toHaveLength(1);
		});

		it('returns empty array on fetch failure', async () => {
			fetchSpy.mockResolvedValueOnce({ ok: false });

			const result = await searchGifs('cats');

			expect(result).toEqual([]);
		});
	});
});
