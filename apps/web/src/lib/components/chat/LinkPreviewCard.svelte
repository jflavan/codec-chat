<script lang="ts">
	import type { LinkPreview } from '$lib/types/index.js';

	let { preview }: { preview: LinkPreview } = $props();

	const href = $derived(preview.canonicalUrl ?? preview.url);
	const displayDescription = $derived(
		preview.description && preview.description.length > 300
			? preview.description.slice(0, 300) + '…'
			: preview.description
	);
</script>

{#if preview.title}
	<aside class="link-preview-card">
		<div class="preview-text">
			{#if preview.siteName}
				<span class="preview-site-name">{preview.siteName}</span>
			{/if}
			<a
				class="preview-title"
				{href}
				target="_blank"
				rel="noopener noreferrer"
			>
				{preview.title}
			</a>
			{#if displayDescription}
				<p class="preview-description">{displayDescription}</p>
			{/if}
		</div>
		{#if preview.imageUrl}
			<a {href} target="_blank" rel="noopener noreferrer" class="preview-thumbnail-link">
				<img
					class="preview-thumbnail"
					src={preview.imageUrl}
					alt=""
					loading="lazy"
				/>
			</a>
		{/if}
	</aside>
{/if}

<style>
	.link-preview-card {
		display: flex;
		gap: 12px;
		max-width: 520px;
		margin-top: 8px;
		padding: 12px;
		border-radius: 8px;
		border: 1px solid var(--border);
		border-left: 3px solid var(--accent);
		background: var(--bg-secondary);
		overflow: hidden;
	}

	.preview-text {
		flex: 1;
		min-width: 0;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.preview-site-name {
		font-size: 12px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.02em;
	}

	.preview-title {
		font-size: 15px;
		font-weight: 600;
		color: var(--accent);
		text-decoration: none;
		overflow: hidden;
		text-overflow: ellipsis;
		display: -webkit-box;
		-webkit-line-clamp: 2;
		-webkit-box-orient: vertical;
	}

	.preview-title:hover {
		text-decoration: underline;
	}

	.preview-description {
		margin: 2px 0 0;
		font-size: 13px;
		color: var(--text-normal);
		line-height: 1.4;
		overflow: hidden;
		display: -webkit-box;
		-webkit-line-clamp: 3;
		-webkit-box-orient: vertical;
	}

	.preview-thumbnail-link {
		flex-shrink: 0;
		align-self: center;
	}

	.preview-thumbnail {
		width: 80px;
		height: 80px;
		border-radius: 4px;
		object-fit: cover;
		display: block;
	}

	/* Responsive: stack thumbnail above text on narrow widths */
	@media (max-width: 599px) {
		.link-preview-card {
			flex-direction: column-reverse;
		}

		.preview-thumbnail-link {
			align-self: stretch;
		}

		.preview-thumbnail {
			width: 100%;
			height: auto;
			max-height: 160px;
			object-fit: cover;
			border-radius: 4px;
		}
	}
</style>
