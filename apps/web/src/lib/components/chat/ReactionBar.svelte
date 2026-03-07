<script lang="ts">
	import { onDestroy } from 'svelte';
	import type { Reaction, Member, CustomEmoji } from '$lib/types/index.js';

	let {
		reactions,
		currentUserId,
		onToggle,
		isPending = () => false,
		members = [],
		customEmojis = []
	}: {
		reactions: Reaction[];
		currentUserId: string | null;
		onToggle: (emoji: string) => void;
		isPending?: (emoji: string) => boolean;
		members?: Member[];
		customEmojis?: CustomEmoji[];
	} = $props();

	const CUSTOM_EMOJI_REGEX = /^:([a-zA-Z0-9_]{2,32}):$/;

	const customEmojiMap = $derived(new Map(customEmojis.map((e) => [e.name.toLowerCase(), e])));

	function getCustomEmoji(emoji: string): CustomEmoji | undefined {
		const match = CUSTOM_EMOJI_REGEX.exec(emoji);
		if (!match) return undefined;
		return customEmojiMap.get(match[1].toLowerCase());
	}

	let hoveredEmoji: string | null = $state(null);
	let hoverTimeout: ReturnType<typeof setTimeout> | null = $state(null);

	function showPopover(emoji: string, el: HTMLElement) {
		if (hoverTimeout) clearTimeout(hoverTimeout);
		hoverTimeout = setTimeout(() => {
			const rect = el.getBoundingClientRect();
			popoverFlipped = rect.top < POPOVER_FLIP_THRESHOLD_PX;
			hoveredEmoji = emoji;
		}, 250);
	}

	function hidePopover() {
		if (hoverTimeout) clearTimeout(hoverTimeout);
		hoverTimeout = null;
		hoveredEmoji = null;
	}

	let touchTimeout: ReturnType<typeof setTimeout> | null = $state(null);
	let touchTriggered = $state(false);
	let popoverFlipped = $state(false);

	/** Popover height (~80px) + buffer to avoid clipping at viewport top */
	const POPOVER_FLIP_THRESHOLD_PX = 80;

	function handleTouchStart(emoji: string, el: HTMLElement) {
		touchTriggered = false;
		touchTimeout = setTimeout(() => {
			touchTriggered = true;
			const rect = el.getBoundingClientRect();
			popoverFlipped = rect.top < POPOVER_FLIP_THRESHOLD_PX;
			hoveredEmoji = emoji;
		}, 500);
	}

	function handleTouchEnd(e: TouchEvent) {
		if (touchTimeout) {
			clearTimeout(touchTimeout);
			touchTimeout = null;
		}
		if (touchTriggered) {
			e.preventDefault();
			touchTriggered = false;
		}
	}

	function handleTouchMove() {
		if (touchTimeout) {
			clearTimeout(touchTimeout);
			touchTimeout = null;
		}
	}

	function handleBackdropTap() {
		hoveredEmoji = null;
	}

	onDestroy(() => {
		if (hoverTimeout) clearTimeout(hoverTimeout);
		if (touchTimeout) clearTimeout(touchTimeout);
	});

	function hasReacted(reaction: Reaction): boolean {
		return currentUserId !== null && reaction.userIds.includes(currentUserId);
	}

	const memberMap = $derived(new Map(members.map((m) => [m.userId, m.displayName])));

	function getReactorNames(reaction: Reaction): { names: string[]; remaining: number } {
		if (memberMap.size === 0) {
			return { names: [], remaining: 0 };
		}
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
	{#each reactions as reaction (reaction.emoji)}
		{@const custom = getCustomEmoji(reaction.emoji)}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="reaction-pill-wrapper"
			onmouseenter={(e) => showPopover(reaction.emoji, e.currentTarget)}
			onmouseleave={hidePopover}
			ontouchstart={(e) => handleTouchStart(reaction.emoji, e.currentTarget)}
			ontouchend={handleTouchEnd}
			ontouchmove={handleTouchMove}
			oncontextmenu={(e) => { if (touchTriggered) e.preventDefault(); }}
		>
			<button
				class="reaction-pill"
				class:reacted={hasReacted(reaction)}
				class:pending={isPending(reaction.emoji)}
				disabled={isPending(reaction.emoji)}
				onclick={() => { onToggle(reaction.emoji); hidePopover(); }}
				aria-describedby={hoveredEmoji === reaction.emoji ? `popover-${reactions.indexOf(reaction)}` : undefined}
			>
				<span class="reaction-emoji">
					{#if custom}
						<img src={custom.imageUrl} alt=":{custom.name}:" title=":{custom.name}:" class="custom-emoji-reaction" width="18" height="18" loading="lazy" />
					{:else}
						{reaction.emoji}
					{/if}
				</span>
				<span class="reaction-count">{reaction.count}</span>
			</button>

			{#if hoveredEmoji === reaction.emoji}
				{@const info = getReactorNames(reaction)}
				<div class="reaction-popover" class:flipped={popoverFlipped} role="tooltip" id="popover-{reactions.indexOf(reaction)}">
					<div class="popover-emoji">
						{#if custom}
							<img src={custom.imageUrl} alt=":{custom.name}:" title=":{custom.name}:" class="custom-emoji-popover" width="28" height="28" loading="lazy" />
						{:else}
							{reaction.emoji}
						{/if}
					</div>
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

	{#if hoveredEmoji !== null}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="popover-backdrop"
			onclick={handleBackdropTap}
			ontouchstart={handleBackdropTap}
		></div>
	{/if}
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
		-webkit-touch-callout: none;
		-webkit-user-select: none;
		user-select: none;
		touch-action: manipulation;
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
		display: inline-flex;
		align-items: center;
	}

	.custom-emoji-reaction {
		display: block;
		object-fit: contain;
	}

	.custom-emoji-popover {
		display: block;
		margin: 0 auto;
		object-fit: contain;
	}

	.reaction-count {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
	}

	.reaction-pill.reacted .reaction-count {
		color: var(--accent);
	}

	.reaction-pill.pending {
		cursor: wait;
		opacity: 0.7;
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

	.reaction-popover.flipped {
		bottom: unset;
		top: calc(100% + 8px);
		animation: popover-fade-in-flipped 150ms ease;
	}

	@keyframes popover-fade-in-flipped {
		from {
			opacity: 0;
			transform: translateX(-50%) translateY(-4px);
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

	.popover-backdrop {
		position: fixed;
		inset: 0;
		z-index: 99;
	}

	@media (hover: hover) {
		.popover-backdrop {
			pointer-events: none;
		}
	}
</style>
