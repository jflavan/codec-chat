<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { env } from '$env/dynamic/public';
	import {
		createUIStore,
		createAuthStore,
		createServerStore,
		createChannelStore,
		createMessageStore,
		createDmStore,
		createFriendStore,
		createVoiceStore,
		setupSignalR,
		goHome,
		selectServer
	} from '$lib/state/index.js';
	import type { AuthStore } from '$lib/state/index.js';
	import { ApiClient } from '$lib/api/client.js';
	import { ChatHubService } from '$lib/services/chat-hub.js';
	import { VoiceService } from '$lib/services/voice-service.js';
	import ServerSidebar from '$lib/components/server-sidebar/ServerSidebar.svelte';
	import ChannelSidebar from '$lib/components/channel-sidebar/ChannelSidebar.svelte';
	import ChatArea from '$lib/components/chat/ChatArea.svelte';
	import MembersSidebar from '$lib/components/members/MembersSidebar.svelte';
	import FriendsPanel from '$lib/components/friends/FriendsPanel.svelte';
	import HomeSidebar from '$lib/components/dm/HomeSidebar.svelte';
	import DmChatArea from '$lib/components/dm/DmChatArea.svelte';
	import UserSettingsModal from '$lib/components/settings/UserSettingsModal.svelte';
	import ServerSettingsModal from '$lib/components/server-settings/ServerSettingsModal.svelte';
	import ImagePreview from '$lib/components/chat/ImagePreview.svelte';
	import LoadingScreen from '$lib/components/LoadingScreen.svelte';
	import LoginScreen from '$lib/components/LoginScreen.svelte';
	import AlphaNotification from '$lib/components/AlphaNotification.svelte';
	import IncomingCallOverlay from '$lib/components/voice/IncomingCallOverlay.svelte';
	import BugReportModal from '$lib/components/settings/BugReportModal.svelte';
import ReportModal from '$lib/components/report/ReportModal.svelte';
	import NicknameModal from '$lib/components/NicknameModal.svelte';
	import LinkAccountModal from '$lib/components/LinkAccountModal.svelte';
	import VerificationGate from '$lib/components/VerificationGate.svelte';

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';
	const googleClientId = env.PUBLIC_GOOGLE_CLIENT_ID ?? '';

	/* ───── Create stores in dependency order ───── */
	const ui = createUIStore();

	let authRef: AuthStore;
	const api = new ApiClient(apiBaseUrl, () => authRef.refreshToken());
	const hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);

	const auth = authRef = createAuthStore(api, ui, googleClientId);
	const servers = createServerStore(auth, api, ui, hub);
	const channels = createChannelStore(auth, api, ui, hub);
	const messages = createMessageStore(auth, channels, api, ui, hub);
	const dms = createDmStore(auth, api, ui, hub);
	const friends = createFriendStore(auth, api, ui);
	const voice = createVoiceStore(auth, api, ui, hub, new VoiceService());

	/* ───── Wire cross-store callbacks ───── */
	const reconnectTimerRef = { current: null as ReturnType<typeof setTimeout> | null };

	auth.onSignedIn = async () => {
		await Promise.all([
			servers.loadServers(),
			friends.loadFriends(),
			friends.loadFriendRequests(),
			dms.loadDmConversations(),
			setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui, reconnectTimerRef)
		]);

		// Load channels/members/emojis for the auto-selected first server
		if (servers.selectedServerId) {
			await selectServer(servers.selectedServerId, ui, servers, channels, dms, hub);
		}
	};

	auth.onSignedOut = async () => {
		await hub.stop();
		servers.reset();
		channels.reset();
		messages.reset();
		dms.reset();
		friends.reset();
		voice.reset();
		ui.resetNavigation();
	};

	auth.onMembersChanged = async () => {
		if (servers.selectedServerId) {
			await servers.loadMembers(servers.selectedServerId);
		}
	};

	// ChannelStore callbacks
	channels.getSelectedServerId = () => servers.selectedServerId;
	channels.onLoadMessages = (channelId) => messages.loadMessages(channelId);
	channels.onLoadVoiceStates = () => voice.loadAllVoiceStates(channels.channels);
	channels.onLoadPinnedMessages = (channelId) => messages.loadPinnedMessages(channelId);
	channels.onChannelSwitch = () => {
		messages.typingUsers = [];
		messages.pendingMentions = new Map();
		messages.replyingTo = null;
		messages.pinnedMessages = [];
		messages.showPinnedPanel = false;
	};

	// ServerStore callbacks
	servers.onGoHome = () => goHome(ui, servers, channels, messages, friends, dms);
	servers.onSelectServer = (serverId) => selectServer(serverId, ui, servers, channels, dms, hub);

	// MessageStore callbacks
	messages.getSelectedServerId = () => servers.selectedServerId;
	messages.getActiveDmChannelId = () => dms.activeDmChannelId;
	messages.onSelectChannel = (channelId) => channels.selectChannel(channelId);
	messages.onSelectDmConversation = (channelId) => dms.selectDmConversation(channelId);
	messages.onSetDmMessages = (msgs) => { dms.dmMessages = msgs; };

	/* ───── Lifecycle ───── */
	onMount(() => {
		if (!googleClientId) {
			ui.error = 'Missing PUBLIC_GOOGLE_CLIENT_ID.';
			ui.isInitialLoading = false;
			return;
		}
		if (!apiBaseUrl) {
			ui.error = 'Missing PUBLIC_API_BASE_URL.';
			ui.isInitialLoading = false;
			return;
		}
		auth.init();

		const handleBeforeUnload = () => voice.teardownVoiceSync();
		window.addEventListener('beforeunload', handleBeforeUnload);
		return () => window.removeEventListener('beforeunload', handleBeforeUnload);
	});

	onDestroy(() => voice.destroy());

	function closeMobileNav(): void {
		ui.mobileNavOpen = false;
	}

	function closeMobileMembers(): void {
		ui.mobileMembersOpen = false;
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			if (ui.mobileMembersOpen) {
				ui.mobileMembersOpen = false;
				e.stopPropagation();
			} else if (ui.mobileNavOpen) {
				ui.mobileNavOpen = false;
				e.stopPropagation();
			}
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

<svelte:head>
	<title>Codec</title>
</svelte:head>

{#if auth.isSignedIn && ui.isInitialLoading}
	<LoadingScreen />
{/if}

{#if !ui.isInitialLoading}
<div class="app-shell" class:home-mode={ui.showFriendsPanel} class:dm-active={ui.showFriendsPanel && dms.activeDmChannelId}>
	<ServerSidebar />
	{#if ui.showFriendsPanel}
		<HomeSidebar />
		{#if dms.activeDmChannelId}
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
{#if ui.mobileNavOpen}
	<div class="mobile-drawer-backdrop" onclick={closeMobileNav} aria-hidden="true"></div>
	<div class="mobile-drawer" aria-label="Navigation">
		<div class="mobile-drawer-servers">
			<ServerSidebar />
		</div>
		<div class="mobile-drawer-channels">
			{#if ui.showFriendsPanel}
				<HomeSidebar />
			{:else}
				<ChannelSidebar />
			{/if}
		</div>
	</div>
{/if}

<!-- Mobile members drawer -->
{#if ui.mobileMembersOpen}
	<div class="mobile-drawer-backdrop" onclick={closeMobileMembers} aria-hidden="true"></div>
	<aside class="mobile-members-drawer" aria-label="Members">
		<MembersSidebar />
	</aside>
{/if}

{#if ui.settingsOpen}
	<UserSettingsModal />
{/if}

{#if ui.serverSettingsOpen}
	<ServerSettingsModal />
{/if}

{#if ui.bugReportOpen}
	<BugReportModal />
{/if}

{#if ui.reportModal}
	<ReportModal />
{/if}

<ImagePreview />
<AlphaNotification />
{#if voice.incomingCall}
	<IncomingCallOverlay />
{/if}

{#if !auth.isSignedIn}
	<LoginScreen />
{/if}

{#if auth.isSignedIn && !auth.emailVerified}
	<VerificationGate />
{/if}

{#if auth.needsNickname}
	<NicknameModal />
{/if}

{#if auth.needsLinking}
	<LinkAccountModal />
{/if}
{/if}

<style>
	.app-shell {
		display: grid;
		grid-template-columns: 72px 240px minmax(0, 1fr) 240px;
		height: 100vh;
		height: 100dvh;
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
		.app-shell,
		.app-shell.home-mode {
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
			height: 100dvh;
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
			overscroll-behavior: contain;
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
			overscroll-behavior: contain;
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
