<script lang="ts">
	let {
		authorName,
		bodyPreview,
		onCancel
	}: {
		authorName: string;
		bodyPreview: string;
		onCancel: () => void;
	} = $props();
</script>

<div class="reply-composer-bar" aria-label="Replying to {authorName}">
	<div class="reply-info">
		<span class="reply-label">Replying to <strong class="reply-target">{authorName}</strong></span>
		<span class="reply-preview">{bodyPreview}</span>
	</div>
	<button
		class="reply-cancel"
		type="button"
		onclick={onCancel}
		aria-label="Cancel reply"
	>
		<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
			<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
		</svg>
	</button>
</div>

<style>
	.reply-composer-bar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		background: var(--bg-secondary);
		border-bottom: 1px solid var(--border);
		border-radius: 8px 8px 0 0;
		gap: 8px;
	}

	.reply-info {
		display: flex;
		flex-direction: column;
		gap: 2px;
		min-width: 0;
	}

	.reply-label {
		font-size: 13px;
		color: var(--text-muted);
	}

	.reply-target {
		color: var(--accent);
		font-weight: 600;
	}

	.reply-preview {
		font-size: 13px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		max-width: 500px;
	}

	.reply-cancel {
		flex-shrink: 0;
		width: 28px;
		height: 28px;
		display: grid;
		place-items: center;
		border: none;
		border-radius: 4px;
		background: transparent;
		color: var(--text-muted);
		cursor: pointer;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.reply-cancel:hover {
		color: var(--text-normal);
		background: var(--bg-message-hover);
	}
</style>
