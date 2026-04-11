<script lang="ts">
	import type { SearchResult } from '$lib/types/models.js';
	import { formatTime } from '$lib/utils/format.js';

	let { result, query, onJump }: {
		result: SearchResult;
		query: string;
		onJump: (messageId: string, channelId: string, isDm: boolean) => void;
	} = $props();

	function highlightMatches(text: string, q: string): string {
		if (!q) return escapeHtml(text);
		const escaped = q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
		// Escape HTML first, then highlight
		const safe = escapeHtml(text);
		return safe.replace(new RegExp(`(${escaped})`, 'gi'), '<mark>$1</mark>');
	}

	function escapeHtml(str: string): string {
		return str
			.replace(/&/g, '&amp;')
			.replace(/</g, '&lt;')
			.replace(/>/g, '&gt;')
			.replace(/"/g, '&quot;');
	}

	const isDm = $derived(!!result.dmChannelId);
	const channelId = $derived(result.channelId ?? result.dmChannelId ?? '');
</script>

<button
	class="search-result-item"
	onclick={() => onJump(result.id, channelId, isDm)}
	type="button"
>
	<div class="result-header">
		<div class="result-author-row">
			{#if result.authorAvatarUrl}
				<img class="result-avatar" src={result.authorAvatarUrl} alt="" />
			{:else}
				<div class="result-avatar-placeholder" aria-hidden="true">
					{result.authorName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<span class="result-author">{result.authorName}</span>
			<time class="result-time">{formatTime(result.createdAt)}</time>
		</div>
		{#if result.channelName}
			<span class="result-channel">#{result.channelName}</span>
		{/if}
	</div>
	{#if result.body}
		<p class="result-body">{@html highlightMatches(result.body, query)}</p>
	{/if}
	{#if result.imageUrl}
		<span class="result-attachment">Image attached</span>
	{/if}
</button>

<style>
	.search-result-item {
		display: block;
		width: 100%;
		text-align: left;
		padding: 10px 12px;
		border: none;
		background: none;
		border-radius: 4px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
	}

	.search-result-item:hover {
		background: var(--bg-message-hover);
	}

	.result-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 6px;
		margin-bottom: 4px;
	}

	.result-author-row {
		display: flex;
		align-items: center;
		gap: 6px;
		min-width: 0;
	}

	.result-avatar {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.result-avatar-placeholder {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 10px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.result-author {
		font-size: 13px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.result-time {
		font-size: 11px;
		color: var(--text-muted);
		white-space: nowrap;
		flex-shrink: 0;
	}

	.result-channel {
		font-size: 11px;
		color: var(--text-muted);
		background: var(--bg-tertiary);
		padding: 1px 6px;
		border-radius: 3px;
		white-space: nowrap;
		flex-shrink: 0;
	}

	.result-body {
		margin: 0;
		font-size: 13px;
		color: var(--text-normal);
		line-height: 1.4;
		overflow: hidden;
		display: -webkit-box;
		-webkit-line-clamp: 3;
		line-clamp: 3;
		-webkit-box-orient: vertical;
		word-break: break-word;
	}

	.result-body :global(mark) {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.25);
		color: var(--accent);
		padding: 0 1px;
		border-radius: 2px;
	}

	.result-attachment {
		font-size: 11px;
		color: var(--text-muted);
		font-style: italic;
		margin-top: 2px;
		display: block;
	}
</style>
