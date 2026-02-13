<script lang="ts">
	let { text }: { text: string } = $props();

	interface FormatSegment {
		type: 'text' | 'bold' | 'italic';
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

		return parts.length > 0 ? parts : [{ type: 'text', value: text }];
	});
</script>

{#each segments as segment}{#if segment.type === 'bold'}<strong class="fmt-bold">{segment.value}</strong>{:else if segment.type === 'italic'}<em class="fmt-italic">{segment.value}</em>{:else}{segment.value}{/if}{/each}

<style>
	.fmt-bold {
		font-weight: 700;
	}

	.fmt-italic {
		font-style: italic;
	}
</style>
