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

	function closeMobileNav(): void {
		app.mobileNavOpen = false;
	}

	function closeMobileMembers(): void {
		app.mobileMembersOpen = false;
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			if (app.mobileMembersOpen) {
				app.mobileMembersOpen = false;
				e.stopPropagation();
			} else if (app.mobileNavOpen) {
				app.mobileNavOpen = false;
				e.stopPropagation();
			}
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

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

<!-- Mobile navigation drawer -->
{#if app.mobileNavOpen}
	<div class="mobile-drawer-backdrop" onclick={closeMobileNav} aria-hidden="true"></div>
	<div class="mobile-drawer" aria-label="Navigation">
		<div class="mobile-drawer-servers">
			<ServerSidebar />
		</div>
		<div class="mobile-drawer-channels">
			{#if app.showFriendsPanel}
				<HomeSidebar />
			{:else}
				<ChannelSidebar />
			{/if}
		</div>
	</div>
{/if}

<!-- Mobile members drawer -->
{#if app.mobileMembersOpen}
	<div class="mobile-drawer-backdrop" onclick={closeMobileMembers} aria-hidden="true"></div>
	<aside class="mobile-members-drawer" aria-label="Members">
		<MembersSidebar />
	</aside>
{/if}

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

	/* ───── Mobile: single-column layout ───── */

	@media (max-width: 899px) {
		.app-shell {
			grid-template-columns: 1fr;
			grid-template-rows: 1fr;
		}

		/* Hide server sidebar and channel/home sidebar from grid on mobile */
		.app-shell > :global(:nth-child(1)),
		.app-shell > :global(:nth-child(2)) {
			display: none;
		}

		/* Hide members sidebar from grid on mobile (accessed via drawer) */
		.app-shell:not(.home-mode) > :global(:nth-child(4)) {
			display: none;
		}

		/* Main content area fills viewport */
		.app-shell:not(.home-mode) > :global(:nth-child(3)),
		.app-shell.home-mode > :global(:nth-child(3)) {
			height: 100vh;
		}
	}

	/* ───── Mobile drawer overlay ───── */

	.mobile-drawer-backdrop {
		display: none;
	}

	.mobile-drawer {
		display: none;
	}

	.mobile-members-drawer {
		display: none;
	}

	@media (max-width: 899px) {
		.mobile-drawer-backdrop {
			display: block;
			position: fixed;
			inset: 0;
			background: rgba(0, 0, 0, 0.6);
			z-index: 60;
		}

		.mobile-drawer {
			display: flex;
			position: fixed;
			top: 0;
			left: 0;
			bottom: 0;
			width: 312px;
			max-width: 85vw;
			z-index: 61;
			animation: slide-in-left 200ms ease;
		}

		.mobile-drawer-servers {
			width: 72px;
			flex-shrink: 0;
			height: 100%;
			overflow: hidden;
		}

		.mobile-drawer-servers > :global(*) {
			height: 100%;
		}

		.mobile-drawer-channels {
			flex: 1;
			min-width: 0;
			height: 100%;
			overflow: hidden;
		}

		.mobile-drawer-channels > :global(*) {
			height: 100%;
		}

		.mobile-members-drawer {
			display: flex;
			position: fixed;
			top: 0;
			right: 0;
			bottom: 0;
			width: 260px;
			max-width: 80vw;
			z-index: 61;
			animation: slide-in-right 200ms ease;
		}

		.mobile-members-drawer > :global(*) {
			width: 100%;
			height: 100%;
		}
	}

	@keyframes slide-in-left {
		from { transform: translateX(-100%); }
		to { transform: translateX(0); }
	}

	@keyframes slide-in-right {
		from { transform: translateX(100%); }
		to { transform: translateX(0); }
	}

	@media (prefers-reduced-motion: reduce) {
		.mobile-drawer,
		.mobile-members-drawer {
			animation: none;
		}
	}
</style>
