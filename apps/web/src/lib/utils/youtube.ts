/**
 * Extracts a YouTube video ID from a URL, or returns null if the URL is not a
 * recognized YouTube link.
 *
 * Supported formats:
 *  - https://www.youtube.com/watch?v=VIDEO_ID
 *  - https://youtube.com/watch?v=VIDEO_ID
 *  - https://m.youtube.com/watch?v=VIDEO_ID
 *  - https://youtu.be/VIDEO_ID
 *  - https://www.youtube.com/embed/VIDEO_ID
 *  - https://youtube.com/shorts/VIDEO_ID
 *  - https://www.youtube.com/live/VIDEO_ID
 */

const YOUTUBE_REGEX =
	/^https?:\/\/(?:www\.|m\.)?(?:youtube\.com\/(?:watch\?.*v=|embed\/|shorts\/|live\/)|youtu\.be\/)([\w-]{11})(?:[?&#].*)?$/i;

/** Inline variant – matches YouTube URLs that appear inside a longer string. */
const YOUTUBE_INLINE_REGEX =
	/https?:\/\/(?:www\.|m\.)?(?:youtube\.com\/(?:watch\?[^\s<>"']*v=|embed\/|shorts\/|live\/)|youtu\.be\/)([\w-]{11})(?:[?&#][^\s<>"']*)*/gi;

export function extractYouTubeVideoId(url: string): string | null {
	const match = YOUTUBE_REGEX.exec(url);
	return match?.[1] ?? null;
}

/**
 * Extracts all YouTube video entries (URL + video ID) found in arbitrary text,
 * deduplicating by video ID.
 */
export function extractYouTubeUrls(text: string): { url: string; videoId: string }[] {
	const seen = new Set<string>();
	const results: { url: string; videoId: string }[] = [];
	for (const match of text.matchAll(YOUTUBE_INLINE_REGEX)) {
		const videoId = match[1];
		if (!seen.has(videoId)) {
			seen.add(videoId);
			results.push({ url: match[0], videoId });
		}
	}
	return results;
}


