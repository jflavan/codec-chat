<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getVoiceStore } from '$lib/state/voice-store.svelte.js';
	import VideoTile from './VideoTile.svelte';

	const auth = getAuthStore();
	const voice = getVoiceStore();

	const hasAnyVideo = $derived(
		voice.isVideoEnabled ||
		voice.isScreenSharing ||
		voice.remoteVideoTracks.size > 0
	);

	type TileInfo = {
		key: string;
		track: MediaStreamTrack;
		label: string;
		displayName: string;
		isSelf: boolean;
	};

	const tiles = $derived.by(() => {
		const result: TileInfo[] = [];

		// Local camera
		if (voice.localVideoTrack && voice.isVideoEnabled) {
			result.push({
				key: 'self:video',
				track: voice.localVideoTrack,
				label: 'video',
				displayName: auth.effectiveDisplayName + ' (You)',
				isSelf: true,
			});
		}

		// Local screen
		if (voice.localScreenTrack && voice.isScreenSharing) {
			result.push({
				key: 'self:screen',
				track: voice.localScreenTrack,
				label: 'screen',
				displayName: auth.effectiveDisplayName + ' (Screen)',
				isSelf: true,
			});
		}

		// Remote tracks
		for (const [key, { track, label }] of voice.remoteVideoTracks) {
			const participantId = key.split(':')[0];
			const channelId = voice.activeVoiceChannelId;
			let displayName = 'Unknown';
			if (channelId) {
				const members = voice.voiceChannelMembers.get(channelId) ?? [];
				const member = members.find((m) => m.participantId === participantId);
				if (member) displayName = member.displayName;
			} else if (voice.activeCall) {
				displayName = voice.activeCall.otherDisplayName;
			}
			const suffix = label === 'screen' ? ' (Screen)' : '';
			result.push({
				key,
				track,
				label,
				displayName: displayName + suffix,
				isSelf: false,
			});
		}

		return result;
	});
</script>

{#if hasAnyVideo}
	<div class="video-grid" class:single={tiles.length === 1} class:dual={tiles.length === 2}>
		{#each tiles as tile (tile.key)}
			<VideoTile
				track={tile.track}
				displayName={tile.displayName}
				label={tile.label}
				isSelf={tile.isSelf}
			/>
		{/each}
	</div>
{/if}

<style>
	.video-grid {
		display: grid;
		gap: 4px;
		padding: 8px;
		width: 100%;
		max-height: 60vh;
		grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
	}

	.video-grid.single {
		grid-template-columns: 1fr;
		max-width: 640px;
		margin: 0 auto;
	}

	.video-grid.dual {
		grid-template-columns: 1fr 1fr;
	}
</style>
