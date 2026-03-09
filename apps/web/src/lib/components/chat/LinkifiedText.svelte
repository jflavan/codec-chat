<script lang="ts">
	import type { Mention, CustomEmoji } from '$lib/types/index.js';
	import { CUSTOM_EMOJI_GLOBAL_REGEX } from '$lib/utils/emoji-regex.js';

	let { text, mentions = [], customEmojis = [] }: { text: string; mentions?: Mention[]; customEmojis?: CustomEmoji[] } = $props();

	interface TextSegment {
		type: 'text' | 'link' | 'mention' | 'bold' | 'italic' | 'custom-emoji';
		value: string;
		displayName?: string;
		imageUrl?: string;
	}

	const COMBINED_REGEX = /(<@here>|<@[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}>|https?:\/\/[^\s<>"')\]},;]+)/gi;
	const MENTION_REGEX = /^<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>$/i;
	const HERE_REGEX = /^<@here>$/i;
	const FORMAT_REGEX = /(\*\*(.+?)\*\*|\*(.+?)\*|_(.+?)_)/g;

	function parseFormatting(value: string): TextSegment[] {
		const parts: TextSegment[] = [];
		let lastIndex = 0;

		for (const match of value.matchAll(FORMAT_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				parts.push({ type: 'text', value: value.slice(lastIndex, matchIndex) });
			}

			if (match[2] !== undefined) {
				parts.push({ type: 'bold', value: match[2] });
			} else if (match[3] !== undefined) {
				parts.push({ type: 'bold', value: match[3] });
			} else if (match[4] !== undefined) {
				parts.push({ type: 'italic', value: match[4] });
			}

			lastIndex = matchIndex + match[0].length;
		}

		if (lastIndex < value.length) {
			parts.push({ type: 'text', value: value.slice(lastIndex) });
		}

		return parts.length > 0 ? parts : [{ type: 'text', value }];
	}

	function parseCustomEmojis(segs: TextSegment[], emojiMap: Map<string, CustomEmoji>): TextSegment[] {
		if (emojiMap.size === 0) return segs;

		const result: TextSegment[] = [];
		for (const seg of segs) {
			if (seg.type !== 'text') {
				result.push(seg);
				continue;
			}

			let lastIndex = 0;
			for (const match of seg.value.matchAll(CUSTOM_EMOJI_GLOBAL_REGEX)) {
				const name = match[1].toLowerCase();
				const emoji = emojiMap.get(name);
				if (!emoji) continue;

				const matchIndex = match.index;
				if (matchIndex > lastIndex) {
					result.push({ type: 'text', value: seg.value.slice(lastIndex, matchIndex) });
				}
				result.push({
					type: 'custom-emoji',
					value: emoji.name,
					imageUrl: emoji.imageUrl
				});
				lastIndex = matchIndex + match[0].length;
			}

			if (lastIndex === 0) {
				result.push(seg);
			} else if (lastIndex < seg.value.length) {
				result.push({ type: 'text', value: seg.value.slice(lastIndex) });
			}
		}
		return result;
	}

	const segments: TextSegment[] = $derived.by(() => {
		const rawSegments: TextSegment[] = [];
		let lastIndex = 0;

		for (const match of text.matchAll(COMBINED_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				rawSegments.push({ type: 'text', value: text.slice(lastIndex, matchIndex) });
			}

			const token = match[0];
			const mentionMatch = MENTION_REGEX.exec(token);
			if (HERE_REGEX.test(token)) {
				rawSegments.push({
					type: 'mention',
					value: 'here',
					displayName: 'here'
				});
			} else if (mentionMatch) {
				const userId = mentionMatch[1].toLowerCase();
				const resolved = mentions.find((m) => m.userId.toLowerCase() === userId);
				rawSegments.push({
					type: 'mention',
					value: userId,
					displayName: resolved?.displayName ?? 'Unknown User'
				});
			} else {
				rawSegments.push({ type: 'link', value: token });
			}

			lastIndex = matchIndex + token.length;
		}

		if (lastIndex < text.length) {
			rawSegments.push({ type: 'text', value: text.slice(lastIndex) });
		}

		if (rawSegments.length === 0) {
			rawSegments.push({ type: 'text', value: text });
		}

		// Pass 1: Parse formatting (bold/italic) in text segments
		const formatted: TextSegment[] = [];
		for (const seg of rawSegments) {
			if (seg.type === 'text') {
				formatted.push(...parseFormatting(seg.value));
			} else {
				formatted.push(seg);
			}
		}

		// Pass 2: Parse custom emojis in remaining text segments
		const emojiMap = new Map(customEmojis.map((e) => [e.name.toLowerCase(), e]));
		return parseCustomEmojis(formatted, emojiMap);
	});
</script>

{#each segments as segment}{#if segment.type === 'link'}<a
			href={segment.value}
			class="message-link"
			target="_blank"
			rel="noopener noreferrer">{segment.value}</a>{:else if segment.type === 'mention'}<span
			class="mention-badge"
			title={segment.displayName}>@{segment.displayName}</span>{:else if segment.type === 'custom-emoji'}<img
			src={segment.imageUrl}
			alt=":{segment.value}:"
			title=":{segment.value}:"
			class="custom-emoji-inline"
			width="20"
			height="20"
			loading="lazy"
		/>{:else if segment.type === 'bold'}<strong class="format-bold">{segment.value}</strong>{:else if segment.type === 'italic'}<em class="format-italic">{segment.value}</em>{:else}{segment.value}{/if}{/each}

<style>
	.message-link {
		color: var(--accent);
		text-decoration: none;
	}

	.message-link:hover {
		text-decoration: underline;
	}

	.mention-badge {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.3);
		color: var(--accent);
		border-radius: 3px;
		padding: 0 2px;
		font-weight: 500;
		cursor: default;
	}

	.mention-badge:hover {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.5);
	}

	.format-bold {
		font-weight: 700;
		color: var(--text-header);
	}

	.format-italic {
		font-style: italic;
	}

	.custom-emoji-inline {
		display: inline;
		vertical-align: middle;
		margin: 0 1px;
	}
</style>
