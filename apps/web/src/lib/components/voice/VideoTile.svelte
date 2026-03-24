<script lang="ts">
	let { track, displayName, label, isSelf }: {
		track: MediaStreamTrack;
		displayName: string;
		label: string;
		isSelf: boolean;
	} = $props();

	let videoEl: HTMLVideoElement | undefined = $state();

	$effect(() => {
		if (videoEl && track) {
			const stream = new MediaStream([track]);
			videoEl.srcObject = stream;
			videoEl.play().catch(() => {});

			return () => {
				videoEl!.srcObject = null;
			};
		}
	});
</script>

<div class="video-tile" class:screen={label === 'screen'}>
	<video
		bind:this={videoEl}
		autoplay
		playsinline
		muted={isSelf}
	></video>
	<span class="name-badge">
		{#if label === 'screen'}
			<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" width="14" height="14">
				<path d="M2 4.25A2.25 2.25 0 0 1 4.25 2h11.5A2.25 2.25 0 0 1 18 4.25v8.5A2.25 2.25 0 0 1 15.75 15H4.25A2.25 2.25 0 0 1 2 12.75v-8.5ZM4.25 3.5a.75.75 0 0 0-.75.75v8.5c0 .414.336.75.75.75h11.5a.75.75 0 0 0 .75-.75v-8.5a.75.75 0 0 0-.75-.75H4.25Z" />
				<path d="M10 17a.75.75 0 0 1 .75-.75h.5a.75.75 0 0 1 0 1.5h-.5A.75.75 0 0 1 10 17ZM7.25 16.25a.75.75 0 0 0 0 1.5h.5a.75.75 0 0 0 0-1.5h-.5ZM11.75 16.25a.75.75 0 0 0 0 1.5h.5a.75.75 0 0 0 0-1.5h-.5Z" />
			</svg>
		{/if}
		{displayName}
	</span>
</div>

<style>
	.video-tile {
		position: relative;
		background: var(--bg-tertiary, #1e1f22);
		border-radius: 8px;
		overflow: hidden;
		aspect-ratio: 16 / 9;
	}

	.video-tile.screen {
		aspect-ratio: auto;
	}

	video {
		width: 100%;
		height: 100%;
		object-fit: contain;
		display: block;
	}

	.name-badge {
		position: absolute;
		bottom: 6px;
		left: 6px;
		background: rgba(0, 0, 0, 0.6);
		color: #fff;
		font-size: 12px;
		padding: 2px 8px;
		border-radius: 4px;
		display: flex;
		align-items: center;
		gap: 4px;
		max-width: calc(100% - 12px);
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
</style>
