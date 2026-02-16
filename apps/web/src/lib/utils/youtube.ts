/**
 * Extracts a YouTube video ID from a URL, or returns null if the URL is not a
 * recognised YouTube link.
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

export function extractYouTubeVideoId(url: string): string | null {
	const match = YOUTUBE_REGEX.exec(url);
	return match?.[1] ?? null;
}

/** Build a privacy-enhanced embed URL for a YouTube video. */
export function youTubeEmbedUrl(videoId: string): string {
	return `https://www.youtube-nocookie.com/embed/${encodeURIComponent(videoId)}`;
}
