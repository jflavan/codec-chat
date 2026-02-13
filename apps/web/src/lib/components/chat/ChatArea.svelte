<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import MessageFeed from './MessageFeed.svelte';
	import TypingIndicator from './TypingIndicator.svelte';
	import Composer from './Composer.svelte';

	const app = getAppState();

	let isDragOver = $state(false);
	let dragCounter = 0;

	function handleDragEnter(e: DragEvent): void {
		if (!e.dataTransfer?.types.includes('Files')) return;
		e.preventDefault();
		dragCounter++;
		isDragOver = true;
	}

	function handleDragOver(e: DragEvent): void {
		if (!e.dataTransfer?.types.includes('Files')) return;
		e.preventDefault();
		e.dataTransfer.dropEffect = 'copy';
	}

	function handleDragLeave(e: DragEvent): void {
		e.preventDefault();
		dragCounter--;
		if (dragCounter <= 0) {
			dragCounter = 0;
			isDragOver = false;
		}
	}

	function handleDrop(e: DragEvent): void {
		e.preventDefault();
		dragCounter = 0;
		isDragOver = false;
		if (!app.selectedChannelId) return;
		const file = e.dataTransfer?.files[0];
		if (file?.type.startsWith('image/')) {
			app.attachImage(file);
		}
	}
</script>

<main
	class="chat-main"
	aria-label="Chat"
	ondragenter={handleDragEnter}
	ondragover={handleDragOver}
	ondragleave={handleDragLeave}
	ondrop={handleDrop}
>
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

	{#if isDragOver && app.selectedChannelId}
		<div class="drop-overlay" aria-hidden="true">
			<div class="drop-overlay-content">
				<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M19 7v2.99s-1.99.01-2 0V7h-3s.01-1.99 0-2h3V2h2v3h3v2h-3zm-3 4V8h-3V5H5c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2v-8h-3zM5 19l3-4 2 3 3-4 4 5H5z"/>
				</svg>
				<span class="drop-overlay-text">Drop image to upload</span>
			</div>
		</div>
	{/if}

	<div class="chat-body">
		{#if app.error}
			<div class="error-banner" role="alert">{app.error}</div>
		{/if}

		<MessageFeed />
		<TypingIndicator />
		<Composer />
	</div>
</main>

<style>
	.chat-main {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
		position: relative;
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

	/* ───── Drop overlay ───── */

	.drop-overlay {
		position: absolute;
		inset: 0;
		z-index: 50;
		display: grid;
		place-items: center;
		background: rgba(0, 0, 0, 0.6);
		pointer-events: none;
	}

	.drop-overlay-content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		padding: 32px 48px;
		border-radius: 12px;
		border: 2px dashed var(--accent);
		background: var(--bg-secondary);
		color: var(--accent);
	}

	.drop-overlay-text {
		font-size: 18px;
		font-weight: 600;
		color: var(--text-header);
	}

	.chat-body {
		flex: 1;
		position: relative;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.error-banner {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		z-index: 10;
		padding: 10px 16px;
		background: var(--danger);
		color: var(--bg-tertiary);
		font-size: 14px;
		font-weight: 500;
		text-align: center;
		pointer-events: none;
		animation: banner-lifecycle 5s ease forwards;
	}

	@keyframes banner-lifecycle {
		0%   { opacity: 1; transform: translateY(0); }
		75%  { opacity: 1; transform: translateY(0); }
		100% { opacity: 0; transform: translateY(-8px); }
	}
</style>
