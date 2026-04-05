// apps/web/src/lib/state/signalr.svelte.ts
//
// SignalR orchestration: wires ChatHubService callbacks to store handler methods.
// Extracted from AppState.startSignalR() to decouple hub lifecycle from state classes.

import type { ChatHubService } from '$lib/services/chat-hub.js';
import { isTokenExpired } from '$lib/auth/session.js';
import { goHome } from './navigation.svelte.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { ServerStore } from './server-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import type { MessageStore } from './message-store.svelte.js';
import type { DmStore } from './dm-store.svelte.js';
import type { FriendStore } from './friend-store.svelte.js';
import type { VoiceStore } from './voice-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

export async function setupSignalR(
	hub: ChatHubService,
	auth: AuthStore,
	servers: ServerStore,
	channels: ChannelStore,
	messages: MessageStore,
	dms: DmStore,
	friends: FriendStore,
	voice: VoiceStore,
	ui: UIStore
): Promise<void> {
	await hub.start(
		async () => {
			if (auth.idToken && !isTokenExpired(auth.idToken)) return auth.idToken;
			const fresh = await auth.refreshToken();
			return fresh ?? '';
		},
		{
			/* ─── Connection lifecycle ─── */

			onReconnecting: () => {
				ui.isHubConnected = false;

				// Tear down voice — SignalR group membership is lost, so the voice
				// session cannot recover without a full re-join.
				if (voice.activeVoiceChannelId || voice.activeCall) {
					voice.teardownOnDisconnect();
					ui.setTransientError('Voice disconnected due to network interruption.');
				}
			},

			onReconnected: async () => {
				ui.isHubConnected = true;

				// Re-join SignalR groups lost during disconnection.
				if (servers.selectedServerId) {
					await hub.joinServer(servers.selectedServerId).catch(() => {});
				}
				if (channels.selectedChannelId) {
					await hub.joinChannel(channels.selectedChannelId).catch(() => {});
					// Reload messages to catch anything missed while disconnected.
					await messages.loadMessages(channels.selectedChannelId).catch(() => {});
				}
				if (dms.activeDmChannelId) {
					await hub.joinDmChannel(dms.activeDmChannelId).catch(() => {});
				}
			},

			onClose: () => {
				ui.isHubConnected = false;
				if (voice.activeVoiceChannelId || voice.activeCall) {
					voice.teardownOnDisconnect();
				}
				// The hub service handles restart internally — no page reload needed.
			},

			/* ─── Channel messages ─── */

			onMessage: (msg) => {
				messages.handleIncomingMessage(msg);
			},

			onUserTyping: (channelId, displayName) => {
				messages.handleTyping(channelId, displayName);
			},

			onUserStoppedTyping: (channelId, displayName) => {
				messages.handleStoppedTyping(channelId, displayName);
			},

			onReactionUpdated: (update) => {
				messages.handleReactionUpdate(update);
			},

			onMessageDeleted: (event) => {
				messages.handleMessageDeleted(event);
			},

			onChannelPurged: (event) => {
				messages.handleChannelPurged(event);
			},

			onMessageEdited: (event) => {
				messages.handleMessageEdited(event);
			},

			onMessagePinned: (event) => {
				messages.handleMessagePinned(event);
			},

			onMessageUnpinned: (event) => {
				messages.handleMessageUnpinned(event);
			},

			/* ─── Link previews (cross-store: channel + DM) ─── */

			onLinkPreviewsReady: (event) => {
				if (event.channelId && event.channelId === channels.selectedChannelId) {
					messages.patchLinkPreviews(event.messageId, event.linkPreviews);
				}
				if (event.dmChannelId && event.dmChannelId === dms.activeDmChannelId) {
					dms.patchDmLinkPreviews(event.messageId, event.linkPreviews);
				}
			},

			/* ─── Mentions ─── */

			onMentionReceived: (event) => {
				channels.handleMentionReceived(event);
			},

			/* ─── Friends ─── */

			onFriendRequestReceived: () => {
				friends.loadFriendRequests();
			},

			onFriendRequestAccepted: () => {
				friends.loadFriends();
				friends.loadFriendRequests();
			},

			onFriendRequestDeclined: () => {
				friends.loadFriendRequests();
			},

			onFriendRequestCancelled: () => {
				friends.loadFriendRequests();
			},

			onFriendRemoved: () => {
				friends.loadFriends();
			},

			/* ─── DMs ─── */

			onReceiveDm: (msg) => {
				dms.handleIncomingDm(msg, ui.showFriendsPanel);
			},

			onDmTyping: (dmChannelId, displayName) => {
				dms.handleDmTyping(dmChannelId, displayName);
			},

			onDmStoppedTyping: (dmChannelId, displayName) => {
				dms.handleDmStoppedTyping(dmChannelId, displayName);
			},

			onDmConversationOpened: () => {
				dms.loadDmConversations();
			},

			onDmMessageDeleted: (event) => {
				dms.handleDmMessageDeleted(event);
			},

			onDmMessageEdited: (event) => {
				dms.handleDmMessageEdited(event);
			},

			onDmReactionUpdated: (update) => {
				dms.handleDmReactionUpdate(update);
			},

			/* ─── Server membership ─── */

			onKickedFromServer: async (event) => {
				try {
					await hub.leaveServer(event.serverId);
				} catch (error) {
					console.error('Failed to leave server after being kicked:', error);
					ui.setTransientError(
						'There was a problem updating your connection after being kicked. Some data may be out of date.'
					);
				}

				servers.servers = servers.servers.filter((s) => s.serverId !== event.serverId);
				if (servers.selectedServerId === event.serverId) {
					servers.selectedServerId = servers.servers[0]?.serverId ?? null;
					channels.channels = [];
					messages.messages = [];
					messages.hasMoreMessages = false;
					servers.members = [];
					if (servers.selectedServerId) {
						channels.loadChannels(servers.selectedServerId);
						servers.loadMembers(servers.selectedServerId);
					}
				}
				ui.setTransientError(`You were kicked from "${event.serverName}".`);
			},

			onBannedFromServer: async (event) => {
				try {
					await hub.leaveServer(event.serverId);
				} catch (error) {
					console.error('Failed to leave server after being banned:', error);
					ui.setTransientError(
						'There was a problem updating your connection after being banned. Some data may be out of date.'
					);
				}

				servers.servers = servers.servers.filter((s) => s.serverId !== event.serverId);
				if (servers.selectedServerId === event.serverId) {
					servers.selectedServerId = servers.servers[0]?.serverId ?? null;
					channels.channels = [];
					messages.messages = [];
					messages.hasMoreMessages = false;
					servers.members = [];
					if (servers.selectedServerId) {
						channels.loadChannels(servers.selectedServerId);
						servers.loadMembers(servers.selectedServerId);
					}
				}
				ui.setTransientError(`You were banned from "${event.serverName}".`);
			},

			onMemberBanned: (event) => {
				servers.handleMemberBanned(event, (userId) => {
					messages.messages = messages.messages.filter((m) => m.authorUserId !== userId);
				});
			},

			onMemberUnbanned: (event) => {
				servers.handleMemberUnbanned(event);
			},

			onMemberJoined: (event) => {
				servers.handleMemberJoined(event);
			},

			onMemberLeft: (event) => {
				servers.handleMemberLeft(event);
			},

			onMemberRoleChanged: (event) => {
				servers.handleMemberRoleChanged(event);
			},

			/* ─── Server settings ─── */

			onServerNameChanged: (event) => {
				servers.handleServerNameChanged(event);
			},

			onServerIconChanged: (event) => {
				servers.handleServerIconChanged(event);
			},

			onServerDeleted: (event) => {
				servers.handleServerDeleted(event);
			},

			onServerDescriptionChanged: (event) => {
				servers.handleServerDescriptionChanged(event);
			},

			/* ─── Channels ─── */

			onChannelNameChanged: (event) => {
				channels.handleChannelNameChanged(event, servers.selectedServerId);
			},

			onChannelDeleted: (event) => {
				channels.handleChannelDeleted(event, servers.selectedServerId);
			},

			onChannelDescriptionChanged: (event) => {
				channels.handleChannelDescriptionChanged(event, servers.selectedServerId);
			},

			onChannelOrderChanged: async (event) => {
				await channels.handleChannelOrderChanged(event, servers.selectedServerId);
				if (event.serverId === servers.selectedServerId) {
					await servers.loadCategories().catch(() => {});
				}
			},

			onChannelOverrideUpdated: async (event) => {
				await channels.handleChannelOverrideUpdated(event, servers.selectedServerId);
			},

			/* ─── Categories ─── */

			onCategoryCreated: (event) => {
				servers.handleCategoryCreated(event);
			},

			onCategoryRenamed: (event) => {
				servers.handleCategoryRenamed(event);
			},

			onCategoryDeleted: (event) => {
				const updatedChannels = servers.handleCategoryDeleted(event, channels.channels);
				if (updatedChannels) {
					channels.channels = updatedChannels;
				}
			},

			onCategoryOrderChanged: (event) => {
				servers.handleCategoryOrderChanged(event);
			},

			/* ─── Custom emojis ─── */

			onCustomEmojiAdded: (event) => {
				servers.handleCustomEmojiAdded(event);
			},

			onCustomEmojiUpdated: (event) => {
				servers.handleCustomEmojiUpdated(event);
			},

			onCustomEmojiDeleted: (event) => {
				servers.handleCustomEmojiDeleted(event);
			},

			/* ─── Presence & status ─── */

			onUserPresenceChanged: (event) => {
				if (event.status === 'offline') {
					ui.userPresence.delete(event.userId);
				} else {
					ui.userPresence.set(event.userId, event.status);
				}
			},

			onUserStatusChanged: (event) => {
				servers.handleUserStatusChanged(event);
			},

			/* ─── Voice ─── */

			onUserJoinedVoice: (event) => {
				voice.handleUserJoinedVoice(event);
			},

			onUserLeftVoice: (event) => {
				voice.handleUserLeftVoice(event);
			},

			onVoiceStateUpdated: (event) => {
				voice.handleVoiceStateUpdated(event);
			},

			onNewProducer: async (event) => {
				await voice.handleNewProducer(event);
			},

			onProducerClosed: (event) => {
				voice.handleProducerClosed(event);
			},

			/* ─── Calls ─── */

			onIncomingCall: (event) => {
				voice.handleIncomingCall(event);
			},

			onCallAccepted: async (event) => {
				await voice.handleCallAccepted(event);
			},

			onCallDeclined: (event) => {
				voice.handleCallDeclined(event);
			},

			onCallEnded: (event) => {
				voice.handleCallEnded(event);
			},

			onCallMissed: (event) => {
				voice.handleCallMissed(event);
			},

			/* ─── Account deletion ─── */

			onAccountDeleted: () => {
				auth.signOut();
			},
		}
	);

	// Post-start setup
	ui.isHubConnected = hub.isConnected;

	if (channels.selectedChannelId) {
		await hub.joinChannel(channels.selectedChannelId);
	}
	if (dms.activeDmChannelId) {
		await hub.joinDmChannel(dms.activeDmChannelId);
	}

	// Check for active call on initial connect.
	await voice.checkActiveCall();
}
