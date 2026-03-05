<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let {
		userId,
		displayName,
		x,
		y,
		onclose
	}: {
		userId: string;
		displayName: string;
		x?: number;
		y?: number;
		onclose: () => void;
	} = $props();

	const app = getAppState();

	const isMobile = !window.matchMedia('(pointer: fine)').matches;

	let sheetEl = $state<HTMLDivElement | null>(null);

	$effect(() => {
		if (sheetEl) sheetEl.focus();
	});

	let sliderValue = $state(Math.round((app.userVolumes.get(userId) ?? 1.0) * 100));

	function handleVolumeChange(e: Event) {
		const val = parseInt((e.target as HTMLInputElement).value, 10);
		sliderValue = val;
		app.setUserVolume(userId, val / 100);
	}

	function handleReset() {
		sliderValue = 100;
		app.resetUserVolume(userId);
	}

	function handleBackdropClick(e: MouseEvent) {
		if (!(e.target as Element).closest('.action-sheet')) {
			onclose();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') onclose();
	}

	// Desktop positioning — clamp to viewport
	const menuWidth = 220;
	const menuHeight = 180;
	const clampedX = Math.min(x ?? 0, window.innerWidth - menuWidth - 8);
	const clampedY = Math.min(y ?? 0, window.innerHeight - menuHeight - 8);
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
	class="action-sheet-overlay"
	class:action-sheet-overlay--mobile={isMobile}
	onclick={handleBackdropClick}
	onkeydown={handleKeydown}
>
	<div
		class="action-sheet"
		class:action-sheet--mobile={isMobile}
		class:action-sheet--desktop={!isMobile}
		style={!isMobile ? `left: ${clampedX}px; top: ${clampedY}px;` : undefined}
		role="dialog"
		aria-label="{displayName} volume controls"
		tabindex="-1"
		bind:this={sheetEl}
	>
		<div class="action-sheet__header">{displayName}</div>
		<div class="action-sheet__section">
			<label class="volume-label" for="volume-{userId}">
				Volume
				<span class="volume-value">{sliderValue}%</span>
			</label>
			<input
				id="volume-{userId}"
				type="range"
				min="0"
				max="100"
				value={sliderValue}
				oninput={handleVolumeChange}
				class="volume-slider"
			/>
		</div>
		{#if sliderValue !== 100}
			<button class="action-sheet__item" onclick={handleReset}>
				Reset Volume
			</button>
		{/if}
	</div>
</div>

<style>
	.action-sheet-overlay {
		position: fixed;
		inset: 0;
		z-index: 100;
	}

	.action-sheet-overlay--mobile {
		background: rgba(0, 0, 0, 0.5);
	}

	/* ── Shared ── */
	.action-sheet {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
		z-index: 101;
	}

	.action-sheet__header {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-header);
		border-bottom: 1px solid var(--border);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.action-sheet__section {
		/* volume control wrapper */
	}

	.volume-label {
		display: flex;
		justify-content: space-between;
		font-size: 11px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.04em;
		margin-bottom: 6px;
	}

	.volume-value {
		color: var(--text-normal);
	}

	.volume-slider {
		width: 100%;
		-webkit-appearance: none;
		appearance: none;
		background: var(--bg-tertiary);
		border-radius: 2px;
		outline: none;
		cursor: pointer;
	}

	.action-sheet__item {
		display: block;
		width: 100%;
		background: none;
		border: none;
		border-radius: 4px;
		color: var(--text-muted);
		font-size: 13px;
		text-align: left;
		cursor: pointer;
		font-family: inherit;
	}

	.action-sheet__item:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	/* ── Desktop ── */
	.action-sheet--desktop {
		position: fixed;
		width: 220px;
		border-radius: 6px;
		padding: 8px;
	}

	.action-sheet--desktop .action-sheet__header {
		padding: 4px 4px 8px;
		margin-bottom: 8px;
	}

	.action-sheet--desktop .action-sheet__section {
		padding: 0 4px;
	}

	.action-sheet--desktop .volume-slider {
		height: 4px;
	}

	.action-sheet--desktop .volume-slider::-webkit-slider-thumb {
		-webkit-appearance: none;
		appearance: none;
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent);
		cursor: pointer;
	}

	.action-sheet--desktop .volume-slider::-moz-range-thumb {
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent);
		cursor: pointer;
		border: none;
	}

	.action-sheet--desktop .action-sheet__item {
		padding: 6px 4px;
		margin-top: 8px;
	}

	/* ── Mobile ── */
	.action-sheet--mobile {
		position: fixed;
		bottom: 0;
		left: 0;
		right: 0;
		border-radius: 12px 12px 0 0;
		border-bottom: none;
		padding: 12px;
		padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px));
		animation: slide-up 200ms ease-out;
	}

	.action-sheet--mobile .action-sheet__header {
		padding: 4px 4px 12px;
		margin-bottom: 12px;
		font-size: 14px;
	}

	.action-sheet--mobile .action-sheet__section {
		padding: 0 4px;
	}

	.action-sheet--mobile .volume-label {
		font-size: 12px;
		margin-bottom: 10px;
	}

	.action-sheet--mobile .volume-slider {
		height: 6px;
	}

	.action-sheet--mobile .volume-slider::-webkit-slider-thumb {
		-webkit-appearance: none;
		appearance: none;
		width: 24px;
		height: 24px;
		border-radius: 50%;
		background: var(--accent);
		cursor: pointer;
	}

	.action-sheet--mobile .volume-slider::-moz-range-thumb {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		background: var(--accent);
		cursor: pointer;
		border: none;
	}

	.action-sheet--mobile .action-sheet__item {
		padding: 12px 4px;
		margin-top: 8px;
		min-height: 44px;
		font-size: 15px;
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
