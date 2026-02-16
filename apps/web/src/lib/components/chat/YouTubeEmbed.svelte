<script lang="ts">
	import Youtube from 'svelte-youtube-embed';

	let { videoId }: { videoId: string } = $props();
</script>

<aside class="youtube-embed">
	<Youtube id={videoId} />
</aside>

<style>
	.youtube-embed {
		max-width: 520px;
		margin-top: 8px;
		border-radius: 8px;
		overflow: hidden;
	}
</style>
