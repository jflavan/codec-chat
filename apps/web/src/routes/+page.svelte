<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { env } from '$env/dynamic/public';
	import { createAppState } from '$lib/state/app-state.svelte.js';
	import ServerSidebar from '$lib/components/server-sidebar/ServerSidebar.svelte';
	import ChannelSidebar from '$lib/components/channel-sidebar/ChannelSidebar.svelte';
	import ChatArea from '$lib/components/chat/ChatArea.svelte';
	import MembersSidebar from '$lib/components/members/MembersSidebar.svelte';
	import FriendsPanel from '$lib/components/friends/FriendsPanel.svelte';
	import HomeSidebar from '$lib/components/dm/HomeSidebar.svelte';
	import DmChatArea from '$lib/components/dm/DmChatArea.svelte';
	import UserSettingsModal from '$lib/components/settings/UserSettingsModal.svelte';
	import ImagePreview from '$lib/components/chat/ImagePreview.svelte';
	import LoadingScreen from '$lib/components/LoadingScreen.svelte';

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';
	const googleClientId = env.PUBLIC_GOOGLE_CLIENT_ID ?? '';

	const app = createAppState(apiBaseUrl, googleClientId);

	onMount(() => {
		if (!googleClientId) {
			app.error = 'Missing PUBLIC_GOOGLE_CLIENT_ID.';
			app.isInitialLoading = false;
			return;
		}
		if (!apiBaseUrl) {
			app.error = 'Missing PUBLIC_API_BASE_URL.';
			app.isInitialLoading = false;
			return;
		}
		app.init();
	});

	onDestroy(() => {
		app.destroy();
	});
</script>

<svelte:head>
	<title>Codec</title>
</svelte:head>

{#if app.isSignedIn && app.isInitialLoading}
	<LoadingScreen />
{/if}

{#if !app.isInitialLoading}
<div class="app-shell" class:home-mode={app.showFriendsPanel} class:dm-active={app.showFriendsPanel && app.activeDmChannelId}>
	<ServerSidebar />
	{#if app.showFriendsPanel}
		<HomeSidebar />
		{#if app.activeDmChannelId}
			<DmChatArea />
		{:else}
			<FriendsPanel />
		{/if}
	{:else}
		<ChannelSidebar />
		<ChatArea />
		<MembersSidebar />
	{/if}
</div>

{#if app.settingsOpen}
	<UserSettingsModal />
{/if}

<ImagePreview />
{/if}

<style>
	.app-shell {
		display: grid;
		grid-template-columns: 72px 240px minmax(0, 1fr) 240px;
		height: 100vh;
		overflow: hidden;
	}

	.app-shell.home-mode {
		grid-template-columns: 72px 240px minmax(0, 1fr);
	}

	@media (max-width: 1199px) {
		.app-shell {
			grid-template-columns: 72px 240px minmax(0, 1fr);
		}

		.app-shell:not(.home-mode) > :global(:nth-child(4)) {
			display: none;
		}
	}

	@media (max-width: 899px) {
		.app-shell {
			grid-template-columns: 1fr;
			grid-template-rows: 1fr;
		}

		.app-shell > :global(:nth-child(1)) {
			display: none;
		}

		.app-shell:not(.home-mode) > :global(:nth-child(2)),
		.app-shell:not(.home-mode) > :global(:nth-child(4)) {
			display: none;
		}

		.app-shell:not(.home-mode) > :global(:nth-child(3)) {
			height: 100vh;
		}

		.app-shell.home-mode:not(.dm-active) > :global(:nth-child(2)) {
			display: none;
		}

		.app-shell.home-mode.dm-active > :global(:nth-child(2)) {
			display: none;
		}
	}
</style>
