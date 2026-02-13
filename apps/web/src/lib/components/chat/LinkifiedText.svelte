<script lang="ts">
	let { text }: { text: string } = $props();

	interface TextSegment {
		type: 'text' | 'link';
		value: string;
	}

	const URL_REGEX = /https?:\/\/[^\s<>"')\]},;]+/gi;

	const segments: TextSegment[] = $derived.by(() => {
		const result: TextSegment[] = [];
		let lastIndex = 0;

		for (const match of text.matchAll(URL_REGEX)) {
			const matchIndex = match.index;
			if (matchIndex > lastIndex) {
				result.push({ type: 'text', value: text.slice(lastIndex, matchIndex) });
			}
			result.push({ type: 'link', value: match[0] });
			lastIndex = matchIndex + match[0].length;
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
			rel="noopener noreferrer">{segment.value}</a>{:else}{segment.value}{/if}{/each}

<style>
	.message-link {
		color: var(--accent);
		text-decoration: none;
	}

	.message-link:hover {
		text-decoration: underline;
	}
</style>
