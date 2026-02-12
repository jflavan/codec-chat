<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
</script>

{#if app.typingUsers.length > 0}
	<div class="typing-indicator" aria-live="polite">
		<span class="typing-dots" aria-hidden="true">
			<span class="dot"></span><span class="dot"></span><span class="dot"></span>
		</span>
		<span class="typing-text">
			{#if app.typingUsers.length === 1}
				<strong>{app.typingUsers[0]}</strong> is typing…
			{:else if app.typingUsers.length === 2}
				<strong>{app.typingUsers[0]}</strong> and <strong>{app.typingUsers[1]}</strong> are typing…
			{:else}
				Several people are typing…
			{/if}
		</span>
	</div>
{/if}

<style>
	.typing-indicator {
		flex-shrink: 0;
		padding: 2px 16px 4px;
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 13px;
		color: var(--text-muted);
		background: var(--bg-primary);
		min-height: 20px;
	}

	.typing-dots {
		display: inline-flex;
		gap: 3px;
		align-items: center;
	}

	.typing-dots .dot {
		width: 6px;
		height: 6px;
		border-radius: 50%;
		background: var(--text-muted);
		animation: typing-bounce 1.4s infinite ease-in-out both;
	}

	.typing-dots .dot:nth-child(1) { animation-delay: 0s; }
	.typing-dots .dot:nth-child(2) { animation-delay: 0.2s; }
	.typing-dots .dot:nth-child(3) { animation-delay: 0.4s; }

	@keyframes typing-bounce {
		0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
		40% { transform: scale(1); opacity: 1; }
	}

	.typing-text strong {
		color: var(--text-header);
		font-weight: 600;
	}
</style>
