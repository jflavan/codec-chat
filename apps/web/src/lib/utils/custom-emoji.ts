import type { CustomEmoji } from '$lib/types/models';

/**
 * Replace :emoji_name: patterns with <img> tags for matching custom emojis.
 * Returns HTML string with custom emojis rendered as inline images.
 */
export function renderCustomEmojis(text: string, customEmojis: CustomEmoji[]): string {
	if (customEmojis.length === 0) return text;

	const emojiMap = new Map(customEmojis.map((e) => [e.name.toLowerCase(), e]));

	return text.replace(/:([a-zA-Z0-9_]{2,32}):/g, (match, name) => {
		const emoji = emojiMap.get(name.toLowerCase());
		if (!emoji) return match;
		return `<img src="${emoji.imageUrl}" alt=":${emoji.name}:" title=":${emoji.name}:" class="custom-emoji-inline" width="20" height="20" loading="lazy" />`;
	});
}
