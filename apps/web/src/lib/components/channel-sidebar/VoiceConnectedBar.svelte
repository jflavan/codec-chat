<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const isInCall = $derived(!!app.activeCall);

	const label = $derived(
		isInCall
			? app.activeCall!.otherDisplayName
			: app.channels.find((c) => c.id === app.activeVoiceChannelId)?.name ?? 'Voice'
	);
</script>

<div class="voice-bar" class:transmitting={app.isPttActive} role="status" aria-label="Voice connected">
	<div class="voice-info">
		<span class="voice-label">
			{#if isInCall}
				In Call
			{:else if app.voiceInputMode === 'push-to-talk'}
				{app.isPttActive ? 'Transmitting' : 'Push to Talk'}
			{:else}
				Voice Connected
			{/if}
		</span>
		<span class="voice-channel-name">{isInCall ? `In call with ${label}` : `# ${label}`}</span>
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
				<!-- Headset with slash -->
				<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M12 1c-4.97 0-9 4.03-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2c0-3.87 3.13-7 7-7s7 3.13 7 7v2h-4v8h3c1.66 0 3-1.34 3-3v-7c0-4.97-4.03-9-9-9z"/>
					<line x1="3" y1="3" x2="21" y2="21" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/>
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
			onclick={() => isInCall ? app.endCall() : app.leaveVoiceChannel()}
			aria-label={isInCall ? 'End call' : 'Leave voice'}
			title={isInCall ? 'End call' : 'Leave voice'}
		>
			<!-- Phone hangup icon (Material Design call_end) -->
			<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08a.956.956 0 0 1-.01-1.36c3.36-3.13 7.53-4.96 11.72-4.96s8.36 1.83 11.72 4.96c.18.18.29.42.29.68 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28a11.27 11.27 0 0 0-2.67-1.85.996.996 0 0 1-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/>
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

	.voice-bar.transmitting .voice-label {
		color: var(--accent, #5865f2);
	}
</style>
