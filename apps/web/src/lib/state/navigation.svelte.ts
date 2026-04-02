// apps/web/src/lib/state/navigation.svelte.ts
//
// Cross-store navigation orchestration functions extracted from AppState.
// These coordinate multiple stores during view transitions.

import type { ChatHubService } from '$lib/services/chat-hub.js';
import type { UIStore } from './ui-store.svelte.js';
import type { ServerStore } from './server-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import type { MessageStore } from './message-store.svelte.js';
import type { FriendStore } from './friend-store.svelte.js';
import type { DmStore } from './dm-store.svelte.js';

/** Navigate to the friends panel (Home view). */
export function goHome(
	ui: UIStore,
	servers: ServerStore,
	channels: ChannelStore,
	messages: MessageStore,
	friends: FriendStore,
	dms: DmStore
): void {
	ui.showFriendsPanel = true;
	ui.mobileNavOpen = false;
	servers.selectedServerId = null;
	channels.selectedChannelId = null;
	channels.channels = [];
	messages.messages = [];
	messages.hasMoreMessages = false;
	servers.members = [];
	servers.customEmojis = [];
	friends.loadFriends();
	friends.loadFriendRequests();
	dms.loadDmConversations();
}

/** Navigate to a server view, cleaning up DM state. */
export async function selectServer(
	serverId: string,
	ui: UIStore,
	servers: ServerStore,
	channels: ChannelStore,
	dms: DmStore,
	hub: ChatHubService
): Promise<void> {
	ui.showFriendsPanel = false;
	ui.mobileNavOpen = false;
	servers.selectedServerId = serverId;

	// Clean up active DM state so incoming DMs correctly increment unread badges.
	if (dms.activeDmChannelId) {
		await hub.leaveDmChannel(dms.activeDmChannelId);
		dms.activeDmChannelId = null;
		dms.dmMessages = [];
		dms.dmTypingUsers = [];
	}

	await channels.loadChannels(serverId);
	await servers.loadMembers(serverId);
	await servers.loadCustomEmojis(serverId);
	servers.loadServerPresence(serverId);
	servers.loadCategories();
	servers.loadNotificationPreferences();
}
