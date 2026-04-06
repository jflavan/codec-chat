import { env } from '$env/dynamic/public';

const GIPHY_API_BASE = 'https://api.giphy.com/v1/gifs';

/** Compact representation of a GIPHY GIF for display in the picker. */
export interface GiphyGif {
	id: string;
	title: string;
	/** Fixed-height preview URL for the grid. */
	previewUrl: string;
	previewWidth: number;
	previewHeight: number;
	/** Full-size URL sent as the message image. */
	originalUrl: string;
}

let warnedMissingKey = false;

function getApiKey(): string {
	const key = env.PUBLIC_GIPHY_API_KEY ?? '';
	if (!key && !warnedMissingKey) {
		console.warn('PUBLIC_GIPHY_API_KEY is not set — GIF search will not work.');
		warnedMissingKey = true;
	}
	return key;
}

function mapGifs(data: GiphyApiGif[]): GiphyGif[] {
	return data.map((g) => ({
		id: g.id,
		title: g.title,
		previewUrl: g.images.fixed_height.url,
		previewWidth: parseInt(g.images.fixed_height.width, 10),
		previewHeight: parseInt(g.images.fixed_height.height, 10),
		originalUrl: g.images.original.url
	}));
}

/** Fetch trending GIFs from GIPHY. */
export async function getTrendingGifs(limit = 25, offset = 0): Promise<GiphyGif[]> {
	const key = getApiKey();
	if (!key) return [];

	const params = new URLSearchParams({
		api_key: key,
		limit: String(limit),
		offset: String(offset),
		rating: 'pg-13'
	});

	const res = await fetch(`${GIPHY_API_BASE}/trending?${params}`);
	if (!res.ok) {
		console.error(`GIPHY trending request failed: ${res.status}`);
		return [];
	}

	const json: GiphyApiResponse = await res.json();
	return mapGifs(json.data);
}

/** Search GIFs on GIPHY. */
export async function searchGifs(query: string, limit = 25, offset = 0): Promise<GiphyGif[]> {
	const key = getApiKey();
	if (!key) return [];

	const trimmed = query.trim();
	if (!trimmed) return getTrendingGifs(limit, offset);

	const params = new URLSearchParams({
		api_key: key,
		q: trimmed,
		limit: String(limit),
		offset: String(offset),
		rating: 'pg-13'
	});

	const res = await fetch(`${GIPHY_API_BASE}/search?${params}`);
	if (!res.ok) {
		console.error(`GIPHY search request failed: ${res.status}`);
		return [];
	}

	const json: GiphyApiResponse = await res.json();
	return mapGifs(json.data);
}

/* ───── Internal GIPHY API response shapes ───── */

interface GiphyApiImage {
	url: string;
	width: string;
	height: string;
}

interface GiphyApiGif {
	id: string;
	title: string;
	images: {
		original: GiphyApiImage;
		fixed_height: GiphyApiImage;
	};
}

interface GiphyApiResponse {
	data: GiphyApiGif[];
}
