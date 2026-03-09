<script lang="ts">
	import type { CustomEmoji } from '$lib/types/index.js';
	import { CUSTOM_EMOJI_GLOBAL_REGEX } from '$lib/utils/emoji-regex.js';

	let { text, customEmojis = [] }: { text: string; customEmojis?: CustomEmoji[] } = $props();

	interface FormatSegment {
		type: 'text' | 'bold' | 'italic' | 'custom-emoji';
		value: string;
	}

	const FORMAT_REGEX = /(\*\*(.+?)\*\*|\*(.+?)\*|_(.+?)_)/g;

	const segments: FormatSegment[] = $derived.by(() => {
		const parts: FormatSegment[] = [];
		let lastIndex = 0;

		for (const match of text.matchAll(FORMAT_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				parts.push({ type: 'text', value: text.slice(lastIndex, matchIndex) });
			}

			if (match[2] !== undefined) {
				parts.push({ type: 'bold', value: match[0] });
			} else if (match[3] !== undefined) {
				parts.push({ type: 'bold', value: match[0] });
			} else if (match[4] !== undefined) {
				parts.push({ type: 'italic', value: match[0] });
			}

			lastIndex = matchIndex + match[0].length;
		}

		if (lastIndex < text.length) {
			parts.push({ type: 'text', value: text.slice(lastIndex) });
		}

		const formatted = parts.length > 0 ? parts : [{ type: 'text' as const, value: text }];

		// Parse custom emoji shortcodes in text segments
		if (customEmojis.length === 0) return formatted;
		const emojiNames = new Set(customEmojis.map((e) => e.name.toLowerCase()));

		const result: FormatSegment[] = [];
		for (const seg of formatted) {
			if (seg.type !== 'text') {
				result.push(seg);
				continue;
			}

			let segLastIndex = 0;
			for (const match of seg.value.matchAll(CUSTOM_EMOJI_GLOBAL_REGEX)) {
				const name = match[1].toLowerCase();
				if (!emojiNames.has(name)) continue;

				const matchIndex = match.index;
				if (matchIndex > segLastIndex) {
					result.push({ type: 'text', value: seg.value.slice(segLastIndex, matchIndex) });
				}
				result.push({ type: 'custom-emoji', value: match[0] });
				segLastIndex = matchIndex + match[0].length;
			}

			if (segLastIndex === 0) {
				result.push(seg);
			} else if (segLastIndex < seg.value.length) {
				result.push({ type: 'text', value: seg.value.slice(segLastIndex) });
			}
		}
		return result;
	});
</script>

{#each segments as segment}{#if segment.type === 'bold'}<strong class="fmt-bold">{segment.value}</strong>{:else if segment.type === 'italic'}<em class="fmt-italic">{segment.value}</em>{:else if segment.type === 'custom-emoji'}<span class="fmt-emoji">{segment.value}</span>{:else}{segment.value}{/if}{/each}

<style>
	.fmt-bold {
		font-weight: 700;
	}

	.fmt-italic {
		font-style: italic;
	}

	.fmt-emoji {
		color: var(--accent);
	}
</style>
