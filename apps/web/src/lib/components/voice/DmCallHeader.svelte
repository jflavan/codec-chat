<script lang="ts">
	import { onDestroy } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let elapsed = $state(0);
	let timer: ReturnType<typeof setInterval> | null = null;

	// Start/stop timer reactively based on call status.
	$effect(() => {
		if (app.activeCall?.status === 'active' && app.activeCall.answeredAt && !timer) {
			const start = new Date(app.activeCall.answeredAt).getTime();
			elapsed = Math.floor((Date.now() - start) / 1000);
			timer = setInterval(() => {
				elapsed = Math.floor((Date.now() - start) / 1000);
			}, 1000);
		}
		if (!app.activeCall && timer) {
			clearInterval(timer);
			timer = null;
			elapsed = 0;
		}
	});

	onDestroy(() => {
		if (timer) clearInterval(timer);
	});

	const formattedTime = $derived.by(() => {
		const m = Math.floor(elapsed / 60);
		const s = elapsed % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	});
</script>

{#if app.activeCall}
	<div class="dm-call-header" role="status" aria-label="Voice call in progress">
		<div class="call-info">
			{#if app.activeCall.otherAvatarUrl}
				<img class="call-avatar" src={app.activeCall.otherAvatarUrl} alt="" />
			{:else}
				<div class="call-avatar-placeholder">
					{app.activeCall.otherDisplayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<div class="call-details">
				<span class="call-name">{app.activeCall.otherDisplayName}</span>
				{#if app.activeCall.status === 'ringing'}
					<span class="call-status ringing">Calling...</span>
				{:else}
					<span class="call-status active">{formattedTime}</span>
				{/if}
			</div>
		</div>
		<div class="call-controls">
			{#if app.activeCall.status === 'active'}
				<button
					class="ctl-btn"
					class:active={app.isMuted}
					onclick={() => app.toggleMute()}
					aria-label={app.isMuted ? 'Unmute' : 'Mute'}
				>
					{#if app.isMuted}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/></svg>
					{:else}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/></svg>
					{/if}
				</button>
				<button
					class="ctl-btn"
					class:active={app.isDeafened}
					onclick={() => app.toggleDeafen()}
					aria-label={app.isDeafened ? 'Undeafen' : 'Deafen'}
				>
					{#if app.isDeafened}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1c-4.97 0-9 4.03-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2c0-3.87 3.13-7 7-7s7 3.13 7 7v2h-4v8h3c1.66 0 3-1.34 3-3v-7c0-4.97-4.03-9-9-9z"/><line x1="3" y1="3" x2="21" y2="21" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/></svg>
					{:else}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1c-4.97 0-9 4.03-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2c0-3.87 3.13-7 7-7s7 3.13 7 7v2h-4v8h3c1.66 0 3-1.34 3-3v-7c0-4.97-4.03-9-9-9z"/></svg>
					{/if}
				</button>
			{/if}
			<button
				class="ctl-btn end-btn"
				onclick={() => app.endCall()}
				aria-label={app.activeCall.status === 'ringing' ? 'Cancel call' : 'End call'}
			>
				<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08a.956.956 0 0 1-.01-1.36c3.36-3.13 7.53-4.96 11.72-4.96s8.36 1.83 11.72 4.96c.18.18.29.42.29.68 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28a11.27 11.27 0 0 0-2.67-1.85.996.996 0 0 1-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/></svg>
			</button>
		</div>
	</div>
{/if}

<style>
	.dm-call-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 16px;
		background: var(--bg-tertiary);
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}
	.call-info {
		display: flex;
		align-items: center;
		gap: 10px;
	}
	.call-avatar, .call-avatar-placeholder {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		object-fit: cover;
	}
	.call-avatar-placeholder {
		background: var(--bg-primary);
		display: grid;
		place-items: center;
		font-size: 14px;
		color: var(--text-muted);
		font-weight: 600;
	}
	.call-name {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
	}
	.call-details {
		display: flex;
		flex-direction: column;
	}
	.call-status {
		font-size: 12px;
	}
	.call-status.ringing {
		color: var(--warn);
		animation: pulse 1.5s ease-in-out infinite;
	}
	.call-status.active {
		color: var(--success);
		font-variant-numeric: tabular-nums;
	}
	.call-controls {
		display: flex;
		gap: 4px;
	}
	.ctl-btn {
		background: none;
		border: none;
		padding: 6px;
		cursor: pointer;
		color: var(--text-muted);
		border-radius: 4px;
		display: grid;
		place-items: center;
		min-width: 32px;
		min-height: 32px;
		transition: background 150ms ease, color 150ms ease;
	}
	.ctl-btn:hover { background: var(--bg-message-hover); color: var(--text-normal); }
	.ctl-btn.active { color: var(--danger); }
	.end-btn:hover { background: var(--danger); color: #fff; }
	@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
</style>
