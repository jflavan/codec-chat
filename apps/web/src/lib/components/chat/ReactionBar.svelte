<script lang="ts">
	import type { Reaction, Member } from '$lib/types/index.js';

	let {
		reactions,
		currentUserId,
		onToggle,
		members = []
	}: {
		reactions: Reaction[];
		currentUserId: string | null;
		onToggle: (emoji: string) => void;
		members?: Member[];
	} = $props();

	let hoveredEmoji: string | null = $state(null);
	let hoverTimeout: ReturnType<typeof setTimeout> | null = $state(null);

	function showPopover(emoji: string) {
		if (hoverTimeout) clearTimeout(hoverTimeout);
		hoverTimeout = setTimeout(() => {
			hoveredEmoji = emoji;
		}, 250);
	}

	function hidePopover() {
		if (hoverTimeout) clearTimeout(hoverTimeout);
		hoverTimeout = null;
		hoveredEmoji = null;
	}

	function hasReacted(reaction: Reaction): boolean {
		return currentUserId !== null && reaction.userIds.includes(currentUserId);
	}

	function getReactorNames(reaction: Reaction): { names: string[]; remaining: number } {
		if (members.length === 0) {
			return { names: [], remaining: 0 };
		}
		const memberMap = new Map(members.map((m) => [m.userId, m.displayName]));
		const names = reaction.userIds
			.map((id) => memberMap.get(id))
			.filter((name): name is string => name !== undefined);
		const MAX_NAMES = 10;
		if (names.length <= MAX_NAMES) {
			return { names, remaining: 0 };
		}
		return { names: names.slice(0, MAX_NAMES), remaining: names.length - MAX_NAMES };
	}
</script>

<div class="reaction-bar">
	{#each reactions as reaction}
		<div
			class="reaction-pill-wrapper"
			onmouseenter={() => showPopover(reaction.emoji)}
			onmouseleave={hidePopover}
		>
			<button
				class="reaction-pill"
				class:reacted={hasReacted(reaction)}
				onclick={() => { onToggle(reaction.emoji); hidePopover(); }}
			>
				<span class="reaction-emoji">{reaction.emoji}</span>
				<span class="reaction-count">{reaction.count}</span>
			</button>

			{#if hoveredEmoji === reaction.emoji}
				{@const info = getReactorNames(reaction)}
				<div class="reaction-popover" role="tooltip">
					<div class="popover-emoji">{reaction.emoji}</div>
					{#if info.names.length > 0}
						<ul class="popover-names">
							{#each info.names as name}
								<li>{name}</li>
							{/each}
							{#if info.remaining > 0}
								<li class="popover-more">and {info.remaining} more</li>
							{/if}
						</ul>
					{:else}
						<div class="popover-fallback">
							{reaction.count} {reaction.count === 1 ? 'reaction' : 'reactions'}
						</div>
					{/if}
				</div>
			{/if}
		</div>
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

	.reaction-pill-wrapper {
		position: relative;
	}

	.reaction-popover {
		position: absolute;
		bottom: calc(100% + 8px);
		left: 50%;
		transform: translateX(-50%);
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 8px;
		padding: 8px 12px;
		min-width: 120px;
		max-width: 200px;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
		z-index: 100;
		pointer-events: auto;
		animation: popover-fade-in 150ms ease;
	}

	@keyframes popover-fade-in {
		from {
			opacity: 0;
			transform: translateX(-50%) translateY(4px);
		}
		to {
			opacity: 1;
			transform: translateX(-50%) translateY(0);
		}
	}

	.popover-emoji {
		font-size: 20px;
		text-align: center;
		margin-bottom: 4px;
	}

	.popover-names {
		list-style: none;
		margin: 0;
		padding: 0;
		font-size: 12px;
		color: var(--text-normal);
		line-height: 1.6;
	}

	.popover-more {
		color: var(--text-muted);
		font-style: italic;
	}

	.popover-fallback {
		font-size: 12px;
		color: var(--text-muted);
		text-align: center;
	}
</style>
