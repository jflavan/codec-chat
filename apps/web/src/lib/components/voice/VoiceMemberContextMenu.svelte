<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let {
		userId,
		displayName,
		x,
		y,
		onclose,
	}: {
		userId: string;
		displayName: string;
		x: number;
		y: number;
		onclose: () => void;
	} = $props();

	const app = getAppState();

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

	function handleClickOutside(e: MouseEvent) {
		if (!(e.target as Element).closest('.voice-context-menu')) {
			onclose();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') onclose();
	}

	// Clamp position to viewport
	const menuWidth = 200;
	const menuHeight = 120;
	const clampedX = $derived(Math.min(x, window.innerWidth - menuWidth - 8));
	const clampedY = $derived(Math.min(y, window.innerHeight - menuHeight - 8));
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="voice-context-overlay" onclick={handleClickOutside} onkeydown={handleKeydown}>
	<div
		class="voice-context-menu"
		style="left: {clampedX}px; top: {clampedY}px;"
		role="menu"
	>
		<div class="context-header">{displayName}</div>
		<div class="volume-control">
			<label class="volume-label">
				Volume
				<span class="volume-value">{sliderValue}%</span>
			</label>
			<input
				type="range"
				min="0"
				max="100"
				value={sliderValue}
				oninput={handleVolumeChange}
				class="volume-slider"
			/>
		</div>
		{#if sliderValue !== 100}
			<button class="reset-btn" onclick={handleReset} role="menuitem">
				Reset Volume
			</button>
		{/if}
	</div>
</div>

<style>
	.voice-context-overlay {
		position: fixed;
		inset: 0;
		z-index: 100;
	}

	.voice-context-menu {
		position: fixed;
		width: 200px;
		background: var(--bg-secondary, #2f3136);
		border: 1px solid var(--border);
		border-radius: 6px;
		padding: 8px;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
		z-index: 101;
	}

	.context-header {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-header);
		padding: 4px 4px 8px;
		border-bottom: 1px solid var(--border);
		margin-bottom: 8px;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.volume-control {
		padding: 0 4px;
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
		height: 4px;
		-webkit-appearance: none;
		appearance: none;
		background: var(--bg-tertiary);
		border-radius: 2px;
		outline: none;
		cursor: pointer;
	}

	.volume-slider::-webkit-slider-thumb {
		-webkit-appearance: none;
		appearance: none;
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent, #5865f2);
		cursor: pointer;
	}

	.volume-slider::-moz-range-thumb {
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent, #5865f2);
		cursor: pointer;
		border: none;
	}

	.reset-btn {
		display: block;
		width: 100%;
		padding: 6px 4px;
		margin-top: 8px;
		background: none;
		border: none;
		border-radius: 4px;
		color: var(--text-muted);
		font-size: 13px;
		text-align: left;
		cursor: pointer;
	}

	.reset-btn:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}
</style>
