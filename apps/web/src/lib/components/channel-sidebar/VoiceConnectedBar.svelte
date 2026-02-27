<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const channelName = $derived(
		app.channels.find((c) => c.id === app.activeVoiceChannelId)?.name ?? 'Voice'
	);
</script>

<div class="voice-bar" role="status" aria-label="Voice connected">
	<div class="voice-info">
		<span class="voice-label">Voice Connected</span>
		<span class="voice-channel-name"># {channelName}</span>
	</div>
	<div class="voice-controls">
		<button
			class="voice-btn"
			class:active={app.isMuted}
			onclick={() => app.toggleMute()}
			aria-label={app.isMuted ? 'Unmute' : 'Mute'}
			title={app.isMuted ? 'Unmute' : 'Mute'}
		>
			{#if app.isMuted}
				<!-- Mic off -->
				<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/>
				</svg>
			{:else}
				<!-- Mic on -->
				<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/>
				</svg>
			{/if}
		</button>

		<button
			class="voice-btn"
			class:active={app.isDeafened}
			onclick={() => app.toggleDeafen()}
			aria-label={app.isDeafened ? 'Undeafen' : 'Deafen'}
			title={app.isDeafened ? 'Undeafen' : 'Deafen'}
		>
			{#if app.isDeafened}
				<!-- Headset off -->
				<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M12 1C7.03 1 3 5.03 3 10v3c0 1.1.9 2 2 2h1v-5H5c0-3.87 3.13-7 7-7s7 3.13 7 7h-1v5h1c1.1 0 2-.9 2-2v-3c0-4.97-4.03-9-9-9zm-1 14h2v1h-2zm5.5 1.5h-1.01L16 21H8l-.49-4.5H6.5c-.28 0-.5-.22-.5-.5v-4c0-.28.22-.5.5-.5h11c.28 0 .5.22.5.5v4c0 .28-.22.5-.5.5z"/>
				</svg>
			{:else}
				<!-- Headset on -->
				<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M12 1c-4.97 0-9 4.03-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2c0-3.87 3.13-7 7-7s7 3.13 7 7v2h-4v8h3c1.66 0 3-1.34 3-3v-7c0-4.97-4.03-9-9-9z"/>
				</svg>
			{/if}
		</button>

		<button
			class="voice-btn leave-btn"
			onclick={() => app.leaveVoiceChannel()}
			aria-label="Leave voice"
			title="Leave voice"
		>
			<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 9v-2l-2-2H8L6 7v2l-4 4 1.41 1.41L6 9.83V18h2v-4h8v4h2V9.83l2.59 2.58L22 11l-6-2z"/>
			</svg>
		</button>
	</div>
</div>

<style>
	.voice-bar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 6px 10px;
		background: var(--bg-tertiary, #1e2124);
		border-top: 1px solid var(--border);
		flex-shrink: 0;
		gap: 8px;
	}

	.voice-info {
		display: flex;
		flex-direction: column;
		overflow: hidden;
		flex: 1;
		min-width: 0;
	}

	.voice-label {
		font-size: 11px;
		font-weight: 600;
		color: var(--success, #43b581);
		text-transform: uppercase;
		letter-spacing: 0.04em;
		line-height: 1.2;
	}

	.voice-channel-name {
		font-size: 12px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		line-height: 1.3;
	}

	.voice-controls {
		display: flex;
		align-items: center;
		gap: 2px;
		flex-shrink: 0;
	}

	.voice-btn {
		background: none;
		border: none;
		padding: 5px;
		cursor: pointer;
		color: var(--text-muted);
		border-radius: 4px;
		display: grid;
		place-items: center;
		min-width: 28px;
		min-height: 28px;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.voice-btn:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.voice-btn.active {
		color: var(--danger, #f04747);
	}

	.leave-btn:hover {
		background: var(--danger, #f04747);
		color: #fff;
	}
</style>
