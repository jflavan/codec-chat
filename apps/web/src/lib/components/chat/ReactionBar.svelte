<script lang="ts">
	import type { Reaction } from '$lib/types/index.js';

	let {
		reactions,
		currentUserId,
		onToggle
	}: {
		reactions: Reaction[];
		currentUserId: string | null;
		onToggle: (emoji: string) => void;
	} = $props();

	function hasReacted(reaction: Reaction): boolean {
		return currentUserId !== null && reaction.userIds.includes(currentUserId);
	}
</script>

<div class="reaction-bar">
	{#each reactions as reaction}
		<button
			class="reaction-pill"
			class:reacted={hasReacted(reaction)}
			onclick={() => onToggle(reaction.emoji)}
			title="{reaction.count} {reaction.count === 1 ? 'reaction' : 'reactions'}"
		>
			<span class="reaction-emoji">{reaction.emoji}</span>
			<span class="reaction-count">{reaction.count}</span>
		</button>
	{/each}
</div>

<style>
	.reaction-bar {
		display: flex;
		flex-wrap: wrap;
		gap: 4px;
		margin-top: 4px;
		align-items: center;
	}

	.reaction-pill {
		display: inline-flex;
		align-items: center;
		gap: 4px;
		padding: 4px 10px;
		min-height: 36px;
		border-radius: 12px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		color: var(--text-normal);
		font-size: 13px;
		cursor: pointer;
		transition:
			background-color 150ms ease,
			border-color 150ms ease;
		line-height: 1.4;
	}

	.reaction-pill:hover {
		background: var(--bg-message-hover);
		border-color: var(--text-dim);
	}

	.reaction-pill.reacted {
		border-color: var(--accent);
		background: var(--mention-bg);
	}

	.reaction-emoji {
		font-size: 14px;
	}

	.reaction-count {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
	}

	.reaction-pill.reacted .reaction-count {
		color: var(--accent);
	}
</style>
