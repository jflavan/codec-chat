<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import MessageFeed from './MessageFeed.svelte';
	import TypingIndicator from './TypingIndicator.svelte';
	import Composer from './Composer.svelte';

	const app = getAppState();
</script>

<main class="chat-main" aria-label="Chat">
	<header class="chat-header">
		<div class="chat-header-left">
			<svg class="channel-hash" width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M5.88 21 7.1 14H3.5l.3-2h3.6l.9-5H4.8l.3-2h3.5L9.8 3h2l-1.2 2h5L16.8 3h2l-1.2 2H21l-.3 2h-3.5l-.9 5h3.5l-.3 2h-3.6L14.7 21h-2l1.2-7h-5L7.7 21h-2zm4.3-9h5l.9-5h-5l-.9 5z"/>
			</svg>
			<h1 class="chat-channel-name">
				{app.selectedChannelName ?? 'Select a channel'}
			</h1>
		</div>
	</header>

	{#if app.error}
		<div class="error-banner" role="alert">{app.error}</div>
	{/if}

	<MessageFeed />
	<TypingIndicator />
	<Composer />
</main>

<style>
	.chat-main {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.chat-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.chat-header-left {
		display: flex;
		align-items: center;
		gap: 6px;
	}

	.chat-channel-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.channel-hash {
		flex-shrink: 0;
		color: var(--text-muted);
		opacity: 0.7;
	}

	.error-banner {
		padding: 8px 16px;
		background: var(--danger);
		color: var(--bg-tertiary);
		font-size: 14px;
		flex-shrink: 0;
	}
</style>
