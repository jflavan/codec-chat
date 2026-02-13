<script lang="ts">
	import type { Mention } from '$lib/types/index.js';

	let { text, mentions = [] }: { text: string; mentions?: Mention[] } = $props();

	interface TextSegment {
		type: 'text' | 'link' | 'mention' | 'bold' | 'italic';
		value: string;
		displayName?: string;
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

	const segments: TextSegment[] = $derived.by(() => {
		const rawSegments: TextSegment[] = [];
		let lastIndex = 0;

		for (const match of text.matchAll(COMBINED_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				rawSegments.push({ type: 'text', value: text.slice(lastIndex, matchIndex) });
			}

			const token = match[0];
			if (HERE_REGEX.test(token)) {
				rawSegments.push({
					type: 'mention',
					value: 'here',
					displayName: 'here'
				});
			} else if (token.match(MENTION_REGEX)) {
				const mentionMatch = token.match(MENTION_REGEX)!;
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

		const result: TextSegment[] = [];
		for (const seg of rawSegments) {
			if (seg.type === 'text') {
				result.push(...parseFormatting(seg.value));
			} else {
				result.push(seg);
			}
		}

		return result;
	});
</script>

{#each segments as segment}{#if segment.type === 'link'}<a
			href={segment.value}
			class="message-link"
			target="_blank"
			rel="noopener noreferrer">{segment.value}</a>{:else if segment.type === 'mention'}<span
			class="mention-badge"
			title={segment.displayName}>@{segment.displayName}</span>{:else if segment.type === 'bold'}<strong class="format-bold">{segment.value}</strong>{:else if segment.type === 'italic'}<em class="format-italic">{segment.value}</em>{:else}{segment.value}{/if}{/each}

<style>
	.message-link {
		color: var(--accent);
		text-decoration: none;
	}

	.message-link:hover {
		text-decoration: underline;
	}

	.mention-badge {
		background: rgba(88, 101, 242, 0.3);
		color: var(--accent);
		border-radius: 3px;
		padding: 0 2px;
		font-weight: 500;
		cursor: default;
	}

	.mention-badge:hover {
		background: rgba(88, 101, 242, 0.5);
	}

	.format-bold {
		font-weight: 700;
		color: var(--text-header);
	}

	.format-italic {
		font-style: italic;
	}
</style>
