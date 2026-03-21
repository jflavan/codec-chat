<script lang="ts">
	import { onMount } from 'svelte';
	import type { CustomEmoji } from '$lib/types/index.js';
	import EmojiPicker from './EmojiPicker.svelte';
	import { getFrequentEmojis, recordEmojiUse } from '$lib/utils/emoji-frequency.js';
	import { isNearScrollTop } from '$lib/utils/dom.js';
	import { CUSTOM_EMOJI_EXACT_REGEX } from '$lib/utils/emoji-regex.js';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let {
		isOwnMessage,
		canDelete,
		canPin = false,
		isPinned = false,
		onReply,
		onReact,
		onEdit,
		onDelete,
		onPin = () => {},
		onUnpin = () => {},
		isReactionPending = () => false
	}: {
		isOwnMessage: boolean;
		canDelete: boolean;
		canPin?: boolean;
		isPinned?: boolean;
		onReply: () => void;
		onReact: (emoji: string) => void;
		onEdit: () => void;
		onDelete: () => void;
		onPin?: () => void;
		onUnpin?: () => void;
		isReactionPending?: (emoji: string) => boolean;
	} = $props();

	const app = getAppState();

	let showPicker = $state(false);
	let showFullPicker = $state(false);
	let isFlipped = $state(false);

	let actionBarEl: HTMLElement | undefined = $state(undefined);

	const FLIP_THRESHOLD_PX = 50;
	let cachedScrollParent: HTMLElement | null = null;

	const quickEmojis = getFrequentEmojis(8);

	const customEmojiMap = $derived(new Map(app.customEmojis.map((e) => [e.name.toLowerCase(), e])));

	function getCustomEmoji(emoji: string): CustomEmoji | undefined {
		const match = CUSTOM_EMOJI_EXACT_REGEX.exec(emoji);
		if (!match) return undefined;
		return customEmojiMap.get(match[1].toLowerCase());
	}

	function checkFlip() {
		const messageEl = actionBarEl?.closest('.message') as HTMLElement | null;
		if (!messageEl) return;
		const result = isNearScrollTop(messageEl, FLIP_THRESHOLD_PX, cachedScrollParent);
		cachedScrollParent = result.scrollParent;
		isFlipped = result.flipped;
	}

	function handleReact(emoji: string) {
		if (isReactionPending(emoji)) return;
		recordEmojiUse(emoji);
		onReact(emoji);
		showPicker = false;
		showFullPicker = false;
	}

	function closePicker() {
		showPicker = false;
	}

	onMount(() => {
		const messageEl = actionBarEl?.closest('.message') as HTMLElement | null;
		if (!messageEl) return;

		messageEl.addEventListener('mouseenter', checkFlip);
		messageEl.addEventListener('focusin', checkFlip);

		return () => {
			messageEl.removeEventListener('mouseenter', checkFlip);
			messageEl.removeEventListener('focusin', checkFlip);
		};
	});
</script>

<div
	bind:this={actionBarEl}
	class="message-actions"
	class:picker-open={showPicker || showFullPicker}
	class:flipped={isFlipped}
>
	{#if canPin}
		<button
			class="action-btn"
			title={isPinned ? 'Unpin Message' : 'Pin Message'}
			aria-label={isPinned ? 'Unpin Message' : 'Pin Message'}
			onclick={isPinned ? onUnpin : onPin}
		>
			<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/>
			</svg>
		</button>
	{/if}
	<button
		class="action-btn"
		onclick={onReply}
		title="Reply"
		aria-label="Reply to message"
	>
		<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
			<path d="M6.598 2.152a.5.5 0 0 1 .052.707L3.354 6.5H11.5a4.5 4.5 0 0 1 0 9h-1a.5.5 0 0 1 0-1h1a3.5 3.5 0 1 0 0-7H3.354l3.296 3.641a.5.5 0 1 1-.74.672l-4.2-4.638a.5.5 0 0 1 0-.672l4.2-4.638a.5.5 0 0 1 .688-.053z"/>
		</svg>
	</button>
	<button
		class="action-btn"
		onclick={() => (showPicker = !showPicker)}
		title="Add reaction"
		aria-label="Add reaction"
	>
		<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
			<path
				d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"
			/>
		</svg>
	</button>
	{#if isOwnMessage}
		<button
			class="action-btn"
			onclick={onEdit}
			title="Edit message"
			aria-label="Edit message"
		>
			<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
				<path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168l10-10ZM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5Zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293l6.5-6.5Z"/>
			</svg>
		</button>
	{/if}
	{#if canDelete}
		<button
			class="action-btn action-btn-danger"
			onclick={onDelete}
			title="Delete message"
			aria-label="Delete message"
		>
			<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
				<path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6Z"/>
				<path d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1ZM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118ZM2.5 3h11V2h-11v1Z"/>
			</svg>
		</button>
	{/if}

	{#if showPicker}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="picker-backdrop" onclick={closePicker} onkeydown={closePicker}></div>
		<div class="emoji-picker" role="menu">
			{#each quickEmojis as emoji}
				{@const customEmoji = getCustomEmoji(emoji)}
				<button
					class="emoji-option"
					onclick={() => handleReact(emoji)}
					disabled={isReactionPending(emoji)}
					role="menuitem"
					aria-label="React with {customEmoji ? `:${customEmoji.name}:` : emoji}"
				>
					{#if customEmoji}
						<img src={customEmoji.imageUrl} alt=":{customEmoji.name}:" title=":{customEmoji.name}:" class="custom-emoji-quick" width="22" height="22" loading="lazy" />
					{:else}
						{emoji}
					{/if}
				</button>
			{/each}
			<button
				class="emoji-option emoji-more"
				onclick={() => { showPicker = false; showFullPicker = true; }}
				role="menuitem"
				aria-label="More emojis"
				title="More emojis"
			>
				<svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor">
					<path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"/>
				</svg>
			</button>
		</div>
	{/if}
	{#if showFullPicker}
		<EmojiPicker
			mode="reaction"
			onSelect={handleReact}
			onClose={() => { showFullPicker = false; }}
			customEmojis={app.customEmojis}
			flipped={isFlipped}
		/>
	{/if}
</div>

<style>
	.message-actions {
		position: absolute;
		top: -14px;
		right: 32px;
		z-index: 5;
		display: flex;
		align-items: center;
		opacity: 0;
		pointer-events: none;
		transition: opacity 120ms ease;
	}

	:global(.message:hover) > .message-actions,
	:global(.message:focus-within) > .message-actions,
	.message-actions.picker-open {
		opacity: 1;
		pointer-events: auto;
	}

	.message-actions.flipped {
		top: unset;
		bottom: -14px;
	}

	.action-btn {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 34px;
		height: 32px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		color: var(--text-dim);
		cursor: pointer;
		box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3);
		transition:
			color 120ms ease,
			background-color 120ms ease,
			border-color 120ms ease;
	}

	.action-btn:hover {
		color: var(--text-normal);
		background: var(--bg-message-hover);
		border-color: var(--text-dim);
	}

	.action-btn-danger:hover {
		color: var(--danger);
		background: rgba(var(--danger-rgb, 255, 59, 59), 0.1);
		border-color: var(--danger);
	}

	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 9;
	}

	.emoji-picker {
		position: absolute;
		bottom: calc(100% + 4px);
		right: 0;
		display: flex;
		gap: 2px;
		padding: 6px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
		z-index: 10;
	}

	.message-actions.flipped .emoji-picker {
		bottom: unset;
		top: calc(100% + 4px);
	}

	.emoji-option {
		width: 32px;
		height: 32px;
		display: grid;
		place-items: center;
		border: none;
		border-radius: 6px;
		background: transparent;
		font-size: 18px;
		cursor: pointer;
		transition: background-color 100ms ease;
	}

	.emoji-option:hover {
		background: var(--bg-message-hover);
	}

	.custom-emoji-quick {
		display: block;
		object-fit: contain;
	}

	.emoji-more {
		color: var(--text-dim);
	}
	.emoji-more:hover {
		color: var(--text-normal);
	}

	/* ───── Mobile adjustments ───── */

	@media (max-width: 768px) {
		:global(.message:focus-within) > .message-actions,
		.message-actions.picker-open {
			opacity: 1;
			pointer-events: auto;
		}

		.action-btn {
			min-width: 44px;
			min-height: 44px;
		}

		.emoji-option {
			min-width: 44px;
			min-height: 44px;
		}

		.emoji-picker {
			position: fixed;
			bottom: 0;
			left: 0;
			right: 0;
			top: unset;
			border-radius: 12px 12px 0 0;
			justify-content: center;
			padding: 12px;
			padding-bottom: calc(12px + env(safe-area-inset-bottom));
			z-index: 100;
			animation: slide-up 200ms ease;
		}

		.picker-backdrop {
			background: rgba(0, 0, 0, 0.5);
			z-index: 99;
		}
	}

	@keyframes slide-up {
		from {
			transform: translateY(100%);
		}
		to {
			transform: translateY(0);
		}
	}
</style>
