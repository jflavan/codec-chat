<script lang="ts">
	import type { LinkPreview } from '$lib/types/index.js';
	import { youTubeEmbedUrl } from '$lib/utils/youtube.js';

	let { preview, videoId, url }: { preview?: LinkPreview; videoId: string; url?: string } =
		$props();

	const embedSrc = $derived(youTubeEmbedUrl(videoId));
	const href = $derived(preview?.canonicalUrl ?? preview?.url ?? url ?? '');
	const title = $derived(preview?.title ?? 'YouTube video');
</script>

<aside class="youtube-embed">
	{#if preview?.siteName}
		<span class="embed-site-name">{preview.siteName}</span>
	{/if}
	{#if href}
		<a class="embed-title" {href} target="_blank" rel="noopener noreferrer">
			{title}
		</a>
	{/if}
	<div class="embed-player">
		<iframe
			src={embedSrc}
			title={title}

			allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
			allowfullscreen
			loading="lazy"
			referrerpolicy="no-referrer"
		></iframe>
	</div>
</aside>

<style>
	.youtube-embed {
		max-width: 520px;
		margin-top: 8px;
		padding: 12px;
		border-radius: 8px;
		border: 1px solid var(--border);
		border-left: 3px solid #ff0000;
		background: var(--bg-secondary);
		display: flex;
		flex-direction: column;
		gap: 4px;
		overflow: hidden;
	}

	.embed-site-name {
		font-size: 12px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.02em;
	}

	.embed-title {
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

	.embed-title:hover {
		text-decoration: underline;
	}

	.embed-player {
		position: relative;
		width: 100%;
		padding-bottom: 56.25%; /* 16:9 aspect ratio */
		margin-top: 4px;
		border-radius: 4px;
		overflow: hidden;
		background: #000;
	}

	.embed-player iframe {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
		border: none;
		border-radius: 4px;
	}
</style>
