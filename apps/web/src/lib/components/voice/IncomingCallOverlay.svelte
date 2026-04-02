<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { getVoiceStore } from '$lib/state/voice-store.svelte.js';

	const voice = getVoiceStore();

	let ringCleanup: (() => void) | null = null;

	onMount(() => {
		// Play a simple ring tone using oscillator (no external audio file needed).
		try {
			const ctx = new AudioContext();
			const osc = ctx.createOscillator();
			const gain = ctx.createGain();
			osc.type = 'sine';
			osc.frequency.value = 440;
			gain.gain.value = 0.1;
			osc.connect(gain);
			gain.connect(ctx.destination);
			osc.start();
			// Pulse ring: 1s on, 1s off
			const interval = setInterval(() => {
				gain.gain.value = gain.gain.value > 0 ? 0 : 0.1;
			}, 1000);
			ringCleanup = () => {
				osc.stop();
				ctx.close();
				clearInterval(interval);
			};
		} catch {
			// Audio not available
		}
	});

	onDestroy(() => {
		ringCleanup?.();
	});

	function accept() {
		if (voice.incomingCall) {
			voice.acceptCall(voice.incomingCall.callId);
		}
	}

	function decline() {
		if (voice.incomingCall) {
			voice.declineCall(voice.incomingCall.callId);
		}
	}
</script>

{#if voice.incomingCall}
	<div class="call-overlay" role="alertdialog" aria-label="Incoming voice call">
		<div class="call-card">
			{#if voice.incomingCall.callerAvatarUrl}
				<img class="caller-avatar" src={voice.incomingCall.callerAvatarUrl} alt="" />
			{:else}
				<div class="caller-avatar-placeholder">
					{voice.incomingCall.callerDisplayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<div class="caller-name">{voice.incomingCall.callerDisplayName}</div>
			<div class="call-label">Incoming Voice Call</div>
			<div class="call-actions">
				<button class="call-btn accept-btn" onclick={accept} aria-label="Accept call">
					<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
						<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
					</svg>
				</button>
				<button class="call-btn decline-btn" onclick={decline} aria-label="Decline call">
					<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
						<path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08a.956.956 0 0 1-.01-1.36c3.36-3.13 7.53-4.96 11.72-4.96s8.36 1.83 11.72 4.96c.18.18.29.42.29.68 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28a11.27 11.27 0 0 0-2.67-1.85.996.996 0 0 1-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/>
					</svg>
				</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.call-overlay {
		position: fixed;
		inset: 0;
		z-index: 200;
		display: grid;
		place-items: center;
		background: rgba(0, 0, 0, 0.7);
	}
	.call-card {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 12px;
		padding: 32px 40px;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		min-width: 280px;
	}
	.caller-avatar, .caller-avatar-placeholder {
		width: 80px;
		height: 80px;
		border-radius: 50%;
		object-fit: cover;
	}
	.caller-avatar-placeholder {
		background: var(--bg-tertiary);
		display: grid;
		place-items: center;
		font-size: 32px;
		color: var(--text-muted);
		font-weight: 600;
	}
	.caller-name {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
	}
	.call-label {
		font-size: 13px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.05em;
		animation: pulse 2s ease-in-out infinite;
	}
	.call-actions {
		display: flex;
		gap: 24px;
		margin-top: 12px;
	}
	.call-btn {
		width: 56px;
		height: 56px;
		border-radius: 50%;
		border: none;
		cursor: pointer;
		display: grid;
		place-items: center;
		transition: filter 150ms ease;
	}
	.call-btn:hover { filter: brightness(1.15); }
	.accept-btn { background: var(--success); color: #000; }
	.decline-btn { background: var(--danger); color: #fff; }
	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50% { opacity: 0.5; }
	}
</style>
