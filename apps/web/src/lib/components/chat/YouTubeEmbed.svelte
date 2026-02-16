<script lang="ts">
	import Youtube from 'svelte-youtube-embed';

	let { videoId }: { videoId: string } = $props();
</script>

<aside class="youtube-embed">
	<Youtube id={videoId} thumbnail={undefined} play_button={undefined} />
</aside>

<style>
	.youtube-embed {
		max-width: 520px;
		margin-top: 8px;
		border-radius: 8px;
		border-left: 3px solid var(--danger);
		overflow: hidden;
	}
</style>
