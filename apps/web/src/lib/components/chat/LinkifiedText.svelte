<script lang="ts">
	import type { Mention } from '$lib/types/index.js';

	let { text, mentions = [] }: { text: string; mentions?: Mention[] } = $props();

	interface TextSegment {
		type: 'text' | 'link' | 'mention';
		value: string;
		displayName?: string;
	}

	const COMBINED_REGEX = /(<@here>|<@[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}>|https?:\/\/[^\s<>"')\]},;]+)/gi;
	const MENTION_REGEX = /^<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>$/i;
	const HERE_REGEX = /^<@here>$/i;

	const segments: TextSegment[] = $derived.by(() => {
		const result: TextSegment[] = [];
		let lastIndex = 0;

		for (const match of text.matchAll(COMBINED_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				result.push({ type: 'text', value: text.slice(lastIndex, matchIndex) });
			}

			const token = match[0];
			if (HERE_REGEX.test(token)) {
				result.push({
					type: 'mention',
					value: 'here',
					displayName: 'here'
				});
			} else if (token.match(MENTION_REGEX)) {
				const mentionMatch = token.match(MENTION_REGEX)!;
				const userId = mentionMatch[1].toLowerCase();
				const resolved = mentions.find((m) => m.userId.toLowerCase() === userId);
				result.push({
					type: 'mention',
					value: userId,
					displayName: resolved?.displayName ?? 'Unknown User'
				});
			} else {
				result.push({ type: 'link', value: token });
			}

			lastIndex = matchIndex + token.length;
		}

		if (lastIndex < text.length) {
			result.push({ type: 'text', value: text.slice(lastIndex) });
		}

		if (result.length === 0) {
			result.push({ type: 'text', value: text });
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
			title={segment.displayName}>@{segment.displayName}</span>{:else}{segment.value}{/if}{/each}

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
</style>
