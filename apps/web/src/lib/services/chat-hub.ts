import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import type { Message, Reaction, FriendUser, DirectMessage, DmParticipant, LinkPreview, MessagePinnedEvent, MessageUnpinnedEvent } from '$lib/types/index.js';

export type ReactionUpdate = {
	messageId: string;
	channelId: string;
	reactions: Reaction[];
};

export type DmReactionUpdate = {
	messageId: string;
	dmChannelId: string;
	reactions: Reaction[];
};

export type FriendRequestReceivedEvent = {
	requestId: string;
	requester: FriendUser;
	createdAt: string;
};

export type FriendRequestAcceptedEvent = {
	friendshipId: string;
	user: FriendUser;
	since: string;
};

export type FriendRequestDeclinedEvent = {
	requestId: string;
};

export type FriendRequestCancelledEvent = {
	requestId: string;
};

export type FriendRemovedEvent = {
	friendshipId: string;
	userId: string;
};

export type ReceiveDmEvent = DirectMessage;

export type DmConversationOpenedEvent = {
	dmChannelId: string;
	participant: DmParticipant;
};

export type KickedFromServerEvent = {
	serverId: string;
	serverName: string;
};

export type LinkPreviewsReadyEvent = {
	messageId: string;
	channelId?: string;
	dmChannelId?: string;
	linkPreviews: LinkPreview[];
};

export type CustomEmojiAddedEvent = {
	serverId: string;
	emoji: {
		id: string;
		name: string;
		imageUrl: string;
		contentType: string;
		isAnimated: boolean;
		createdAt: string;
		uploadedByUserId: string;
	};
};

export type CustomEmojiUpdatedEvent = {
	serverId: string;
	emojiId: string;
	name: string;
};

export type CustomEmojiDeletedEvent = {
	serverId: string;
	emojiId: string;
};

export type MentionReceivedEvent = {
	id: string;
	channelId: string;
	serverId: string;
	authorName: string;
	body: string;
};

export type ChannelOverrideUpdatedEvent = {
	channelId: string;
	roleId: string;
	allow: number;
	deny: number;
};

export type ChannelOrderChangedEvent = { serverId: string };
export type CategoryOrderChangedEvent = { serverId: string };
export type CategoryCreatedEvent = { serverId: string; categoryId: string; name: string; position: number };
export type CategoryRenamedEvent = { serverId: string; categoryId: string; name: string };
export type CategoryDeletedEvent = { serverId: string; categoryId: string };
export type ServerDescriptionChangedEvent = { serverId: string; description: string };
export type ChannelDescriptionChangedEvent = { serverId: string; channelId: string; description: string };

export type MemberJoinedEvent = {
	serverId: string;
};

export type MemberLeftEvent = {
	serverId: string;
};

export type MemberRoleChangedEvent = {
	serverId: string;
	userId: string;
	newRole: string;
	permissions?: number;
};

export type BannedFromServerEvent = {
	serverId: string;
	serverName: string;
};

export type MemberBannedEvent = {
	serverId: string;
	userId: string;
	deletedMessageCount: number;
};

export type MemberUnbannedEvent = {
	serverId: string;
	userId: string;
};

export type MessageDeletedEvent = {
	messageId: string;
	channelId: string;
};

export type ChannelPurgedEvent = {
	channelId: string;
};

export type DmMessageDeletedEvent = {
	messageId: string;
	dmChannelId: string;
};

export type MessageEditedEvent = {
	messageId: string;
	channelId: string;
	body: string;
	editedAt: string;
};

export type DmMessageEditedEvent = {
	messageId: string;
	dmChannelId: string;
	body: string;
	editedAt: string;
};

export type ServerNameChangedEvent = {
	serverId: string;
	name: string;
};

export type ServerIconChangedEvent = {
	serverId: string;
	iconUrl: string | null;
};

export type ChannelNameChangedEvent = {
	serverId: string;
	channelId: string;
	name: string;
};

export type ServerDeletedEvent = {
	serverId: string;
};

export type ChannelDeletedEvent = {
	serverId: string;
	channelId: string;
};

export type UserPresenceChangedEvent = {
	userId: string;
	status: 'online' | 'idle' | 'offline';
};

export type UserStatusChangedEvent = {
	userId: string;
	statusText: string | null;
	statusEmoji: string | null;
};

export type UserJoinedVoiceEvent = {
	channelId: string;
	userId: string;
	displayName: string;
	avatarUrl?: string | null;
	participantId: string;
};

export type UserLeftVoiceEvent = {
	channelId: string;
	userId: string;
	participantId: string;
};

export type VoiceStateUpdatedEvent = {
	channelId: string;
	userId: string;
	isMuted: boolean;
	isDeafened: boolean;
};

export type NewProducerEvent = {
	channelId: string;
	userId: string;
	participantId: string;
	producerId: string;
	label?: string;
};

export type ProducerClosedEvent = {
	channelId: string;
	userId: string;
	participantId: string;
	producerId: string;
	label: string;
};

export type IncomingCallEvent = {
	callId: string;
	dmChannelId: string;
	callerUserId: string;
	callerDisplayName: string;
	callerAvatarUrl?: string | null;
};

export type CallAcceptedEvent = {
	callId: string;
	dmChannelId: string;
	roomId: string;
};

export type CallDeclinedEvent = {
	callId: string;
	dmChannelId: string;
};

export type CallEndedEvent = {
	callId: string;
	dmChannelId: string;
	endReason: string;
	durationSeconds?: number | null;
};

export type CallMissedEvent = {
	callId: string;
	dmChannelId: string;
};

export type ImportProgressEvent = {
	stage: string;
	completed: number;
	total: number;
	percentComplete: number;
};

export type ImportCompletedEvent = {
	importedChannels: number;
	importedMessages: number;
	importedMembers: number;
};

export type ImportRehostCompletedEvent = {
	importedChannels: number;
	importedMessages: number;
	importedMembers: number;
};

export type ImportFailedEvent = {
	errorMessage: string;
};

export type ImportMessagesAvailableEvent = {
	channelId: string;
	count: number;
};

export type SignalRCallbacks = {
	onMessage: (msg: Message) => void;
	onUserTyping: (channelId: string, displayName: string) => void;
	onUserStoppedTyping: (channelId: string, displayName: string) => void;
	onReactionUpdated: (update: ReactionUpdate) => void;
	onFriendRequestReceived?: (event: FriendRequestReceivedEvent) => void;
	onFriendRequestAccepted?: (event: FriendRequestAcceptedEvent) => void;
	onFriendRequestDeclined?: (event: FriendRequestDeclinedEvent) => void;
	onFriendRequestCancelled?: (event: FriendRequestCancelledEvent) => void;
	onFriendRemoved?: (event: FriendRemovedEvent) => void;
	onReceiveDm?: (event: ReceiveDmEvent) => void;
	onDmTyping?: (dmChannelId: string, displayName: string) => void;
	onDmStoppedTyping?: (dmChannelId: string, displayName: string) => void;
	onDmConversationOpened?: (event: DmConversationOpenedEvent) => void;
	onKickedFromServer?: (event: KickedFromServerEvent) => void;
	onLinkPreviewsReady?: (event: LinkPreviewsReadyEvent) => void;
	onMentionReceived?: (event: MentionReceivedEvent) => void;
	onMemberJoined?: (event: MemberJoinedEvent) => void;
	onMemberLeft?: (event: MemberLeftEvent) => void;
	onMemberRoleChanged?: (event: MemberRoleChangedEvent) => void;
	onBannedFromServer?: (event: BannedFromServerEvent) => void;
	onMemberBanned?: (event: MemberBannedEvent) => void;
	onMemberUnbanned?: (event: MemberUnbannedEvent) => void;
	onMessageDeleted?: (event: MessageDeletedEvent) => void;
	onDmMessageDeleted?: (event: DmMessageDeletedEvent) => void;
	onMessageEdited?: (event: MessageEditedEvent) => void;
	onDmMessageEdited?: (event: DmMessageEditedEvent) => void;
	onDmReactionUpdated?: (update: DmReactionUpdate) => void;
	onServerNameChanged?: (event: ServerNameChangedEvent) => void;
	onServerIconChanged?: (event: ServerIconChangedEvent) => void;
	onChannelNameChanged?: (event: ChannelNameChangedEvent) => void;
	onServerDeleted?: (event: ServerDeletedEvent) => void;
	onChannelDeleted?: (event: ChannelDeletedEvent) => void;
	onChannelPurged?: (event: ChannelPurgedEvent) => void;
	onUserJoinedVoice?: (event: UserJoinedVoiceEvent) => void;
	onUserLeftVoice?: (event: UserLeftVoiceEvent) => void;
	onVoiceStateUpdated?: (event: VoiceStateUpdatedEvent) => void;
	onNewProducer?: (event: NewProducerEvent) => void;
	onProducerClosed?: (event: ProducerClosedEvent) => void;
	onIncomingCall?: (event: IncomingCallEvent) => void;
	onCallAccepted?: (event: CallAcceptedEvent) => void;
	onCallDeclined?: (event: CallDeclinedEvent) => void;
	onCallEnded?: (event: CallEndedEvent) => void;
	onCallMissed?: (event: CallMissedEvent) => void;
	onCustomEmojiAdded?: (event: CustomEmojiAddedEvent) => void;
	onCustomEmojiUpdated?: (event: CustomEmojiUpdatedEvent) => void;
	onCustomEmojiDeleted?: (event: CustomEmojiDeletedEvent) => void;
	onUserPresenceChanged?: (event: UserPresenceChangedEvent) => void;
	onUserStatusChanged?: (event: UserStatusChangedEvent) => void;
	onChannelOrderChanged?: (event: ChannelOrderChangedEvent) => void;
	onCategoryOrderChanged?: (event: CategoryOrderChangedEvent) => void;
	onCategoryCreated?: (event: CategoryCreatedEvent) => void;
	onCategoryRenamed?: (event: CategoryRenamedEvent) => void;
	onCategoryDeleted?: (event: CategoryDeletedEvent) => void;
	onServerDescriptionChanged?: (event: ServerDescriptionChangedEvent) => void;
	onChannelDescriptionChanged?: (event: ChannelDescriptionChangedEvent) => void;
	onMessagePinned?: (event: MessagePinnedEvent) => void;
	onMessageUnpinned?: (event: MessageUnpinnedEvent) => void;
	onChannelOverrideUpdated?: (event: ChannelOverrideUpdatedEvent) => void;
	onAccountDeleted?: () => void;
	onImportProgress?: (event: ImportProgressEvent) => void;
	onImportCompleted?: (event: ImportCompletedEvent) => void;
	onImportRehostCompleted?: (event: ImportRehostCompletedEvent) => void;
	onImportFailed?: (event: ImportFailedEvent) => void;
	onImportMessagesAvailable?: (event: ImportMessagesAvailableEvent) => void;
	onReconnecting?: () => void;
	onReconnected?: () => void;
	onClose?: (error?: Error) => void;
};

/**
 * Manages the SignalR hub connection lifecycle.
 *
 * This is a plain class (not reactive) so it can be used from any context.
 * The owning component/state layer wires callbacks into reactive state.
 */
export class ChatHubService {
	private connection: HubConnection | null = null;
	private typingTimeout: ReturnType<typeof setTimeout> | null = null;
	private heartbeatInterval: ReturnType<typeof setInterval> | null = null;
	private restartTimer: ReturnType<typeof setTimeout> | null = null;
	private restartAttempt = 0;
	private isUserActive = false;
	private isStopped = false;
	private isRestarting = false;
	private activityHandler = () => { this.isUserActive = true; };
	private savedAccessTokenFactory: (() => string | Promise<string>) | null = null;
	private savedCallbacks: SignalRCallbacks | null = null;
	private visibilityHandler: (() => void) | null = null;

	constructor(private readonly hubUrl: string) {}

	get isConnected(): boolean {
		return this.connection?.state === HubConnectionState.Connected;
	}

	async start(accessTokenFactory: () => string | Promise<string>, callbacks: SignalRCallbacks): Promise<void> {
		this.savedAccessTokenFactory = accessTokenFactory;
		this.savedCallbacks = callbacks;
		this.isStopped = false;
		this.restartAttempt = 0;
		await this.buildAndStart(accessTokenFactory, callbacks);
		this.listenForVisibility();
	}

	private async buildAndStart(accessTokenFactory: () => string | Promise<string>, callbacks: SignalRCallbacks): Promise<void> {
		const connection = new HubConnectionBuilder()
			.withUrl(this.hubUrl, { accessTokenFactory })
			.withAutomaticReconnect([0, 1000, 2000, 5000, 10_000, 30_000, 60_000, 60_000, 60_000, 60_000])
			.configureLogging(LogLevel.Warning)
			.build();

		// Match the server's extended ClientTimeoutInterval (90 s) so the client
		// doesn't declare the server dead before the server considers the client gone.
		connection.serverTimeoutInMilliseconds = 120_000;
		connection.keepAliveIntervalInMilliseconds = 30_000;

		connection.on('ReceiveMessage', callbacks.onMessage);
		connection.on('UserTyping', callbacks.onUserTyping);
		connection.on('UserStoppedTyping', callbacks.onUserStoppedTyping);
		connection.on('ReactionUpdated', callbacks.onReactionUpdated);

		if (callbacks.onFriendRequestReceived) {
			connection.on('FriendRequestReceived', callbacks.onFriendRequestReceived);
		}
		if (callbacks.onFriendRequestAccepted) {
			connection.on('FriendRequestAccepted', callbacks.onFriendRequestAccepted);
		}
		if (callbacks.onFriendRequestDeclined) {
			connection.on('FriendRequestDeclined', callbacks.onFriendRequestDeclined);
		}
		if (callbacks.onFriendRequestCancelled) {
			connection.on('FriendRequestCancelled', callbacks.onFriendRequestCancelled);
		}
		if (callbacks.onFriendRemoved) {
			connection.on('FriendRemoved', callbacks.onFriendRemoved);
		}
		if (callbacks.onReceiveDm) {
			connection.on('ReceiveDm', callbacks.onReceiveDm);
		}
		if (callbacks.onDmTyping) {
			connection.on('DmTyping', callbacks.onDmTyping);
		}
		if (callbacks.onDmStoppedTyping) {
			connection.on('DmStoppedTyping', callbacks.onDmStoppedTyping);
		}
		if (callbacks.onDmConversationOpened) {
			connection.on('DmConversationOpened', callbacks.onDmConversationOpened);
		}
		if (callbacks.onKickedFromServer) {
			connection.on('KickedFromServer', callbacks.onKickedFromServer);
		}
		if (callbacks.onLinkPreviewsReady) {
			connection.on('LinkPreviewsReady', callbacks.onLinkPreviewsReady);
		}
		if (callbacks.onMentionReceived) {
			connection.on('MentionReceived', callbacks.onMentionReceived);
		}
		if (callbacks.onMemberJoined) {
			connection.on('MemberJoined', callbacks.onMemberJoined);
		}
		if (callbacks.onMemberLeft) {
			connection.on('MemberLeft', callbacks.onMemberLeft);
		}
		if (callbacks.onMemberRoleChanged) {
			connection.on('MemberRoleChanged', callbacks.onMemberRoleChanged);
		}
		if (callbacks.onBannedFromServer) {
			connection.on('BannedFromServer', callbacks.onBannedFromServer);
		}
		if (callbacks.onMemberBanned) {
			connection.on('MemberBanned', callbacks.onMemberBanned);
		}
		if (callbacks.onMemberUnbanned) {
			connection.on('MemberUnbanned', callbacks.onMemberUnbanned);
		}
		if (callbacks.onMessageDeleted) {
			connection.on('MessageDeleted', callbacks.onMessageDeleted);
		}
		if (callbacks.onDmMessageDeleted) {
			connection.on('DmMessageDeleted', callbacks.onDmMessageDeleted);
		}
		if (callbacks.onMessageEdited) {
			connection.on('MessageEdited', callbacks.onMessageEdited);
		}
		if (callbacks.onDmMessageEdited) {
			connection.on('DmMessageEdited', callbacks.onDmMessageEdited);
		}
		if (callbacks.onDmReactionUpdated) {
			connection.on('DmReactionUpdated', callbacks.onDmReactionUpdated);
		}
		if (callbacks.onServerNameChanged) {
			connection.on('ServerNameChanged', callbacks.onServerNameChanged);
		}
		if (callbacks.onServerIconChanged) {
			connection.on('ServerIconChanged', callbacks.onServerIconChanged);
		}
		if (callbacks.onChannelNameChanged) {
			connection.on('ChannelNameChanged', callbacks.onChannelNameChanged);
		}
		if (callbacks.onServerDeleted) {
			connection.on('ServerDeleted', callbacks.onServerDeleted);
		}
		if (callbacks.onChannelDeleted) {
			connection.on('ChannelDeleted', callbacks.onChannelDeleted);
		}
		if (callbacks.onChannelPurged) {
			connection.on('ChannelPurged', callbacks.onChannelPurged);
		}
		if (callbacks.onUserJoinedVoice) {
			connection.on('UserJoinedVoice', callbacks.onUserJoinedVoice);
		}
		if (callbacks.onUserLeftVoice) {
			connection.on('UserLeftVoice', callbacks.onUserLeftVoice);
		}
		if (callbacks.onVoiceStateUpdated) {
			connection.on('VoiceStateUpdated', callbacks.onVoiceStateUpdated);
		}
		if (callbacks.onNewProducer) {
			connection.on('NewProducer', callbacks.onNewProducer);
		}
		if (callbacks.onProducerClosed) {
			connection.on('ProducerClosed', callbacks.onProducerClosed);
		}
		if (callbacks.onIncomingCall) {
			connection.on('IncomingCall', callbacks.onIncomingCall);
		}
		if (callbacks.onCallAccepted) {
			connection.on('CallAccepted', callbacks.onCallAccepted);
		}
		if (callbacks.onCallDeclined) {
			connection.on('CallDeclined', callbacks.onCallDeclined);
		}
		if (callbacks.onCallEnded) {
			connection.on('CallEnded', callbacks.onCallEnded);
		}
		if (callbacks.onCallMissed) {
			connection.on('CallMissed', callbacks.onCallMissed);
		}
		if (callbacks.onCustomEmojiAdded) {
			connection.on('CustomEmojiAdded', callbacks.onCustomEmojiAdded);
		}
		if (callbacks.onCustomEmojiUpdated) {
			connection.on('CustomEmojiUpdated', callbacks.onCustomEmojiUpdated);
		}
		if (callbacks.onCustomEmojiDeleted) {
			connection.on('CustomEmojiDeleted', callbacks.onCustomEmojiDeleted);
		}
		if (callbacks.onUserPresenceChanged) {
			connection.on('UserPresenceChanged', callbacks.onUserPresenceChanged);
		}
		if (callbacks.onUserStatusChanged) {
			connection.on('UserStatusChanged', callbacks.onUserStatusChanged);
		}
		if (callbacks.onChannelOrderChanged) {
			connection.on('ChannelOrderChanged', callbacks.onChannelOrderChanged);
		}
		if (callbacks.onCategoryOrderChanged) {
			connection.on('CategoryOrderChanged', callbacks.onCategoryOrderChanged);
		}
		if (callbacks.onCategoryCreated) {
			connection.on('CategoryCreated', callbacks.onCategoryCreated);
		}
		if (callbacks.onCategoryRenamed) {
			connection.on('CategoryRenamed', callbacks.onCategoryRenamed);
		}
		if (callbacks.onCategoryDeleted) {
			connection.on('CategoryDeleted', callbacks.onCategoryDeleted);
		}
		if (callbacks.onServerDescriptionChanged) {
			connection.on('ServerDescriptionChanged', callbacks.onServerDescriptionChanged);
		}
		if (callbacks.onChannelDescriptionChanged) {
			connection.on('ChannelDescriptionChanged', callbacks.onChannelDescriptionChanged);
		}

		if (callbacks.onMessagePinned) {
			connection.on('MessagePinned', callbacks.onMessagePinned);
		}
		if (callbacks.onMessageUnpinned) {
			connection.on('MessageUnpinned', callbacks.onMessageUnpinned);
		}
		if (callbacks.onChannelOverrideUpdated) {
			connection.on('ChannelOverrideUpdated', callbacks.onChannelOverrideUpdated);
		}
		if (callbacks.onAccountDeleted) {
			connection.on('AccountDeleted', callbacks.onAccountDeleted);
		}
		if (callbacks.onImportProgress) {
			connection.on('ImportProgress', callbacks.onImportProgress);
		}
		if (callbacks.onImportCompleted) {
			connection.on('ImportCompleted', callbacks.onImportCompleted);
		}
		if (callbacks.onImportRehostCompleted) {
			connection.on('ImportRehostCompleted', callbacks.onImportRehostCompleted);
		}
		if (callbacks.onImportFailed) {
			connection.on('ImportFailed', callbacks.onImportFailed);
		}
		if (callbacks.onImportMessagesAvailable) {
			connection.on('ImportMessagesAvailable', callbacks.onImportMessagesAvailable);
		}

		if (callbacks.onReconnecting) {
			connection.onreconnecting(() => {
				this.stopHeartbeat();
				callbacks.onReconnecting!();
			});
		} else {
			connection.onreconnecting(() => {
				this.stopHeartbeat();
			});
		}
		if (callbacks.onReconnected) {
			connection.onreconnected(() => {
				this.startHeartbeat();
				callbacks.onReconnected!();
			});
		} else {
			connection.onreconnected(() => {
				this.startHeartbeat();
			});
		}
		connection.onclose((error) => {
			this.stopHeartbeat();
			callbacks.onClose?.(error);
			// All automatic reconnect attempts exhausted — schedule a fresh restart
			// instead of forcing a page reload.  Skip if scheduleRestart already
			// initiated this stop to avoid re-entrant scheduling.
			if (!this.isStopped && !this.isRestarting) this.scheduleRestart();
		});

		try {
			await connection.start();
			this.connection = connection;
			this.restartAttempt = 0;
			this.startHeartbeat();
		} catch {
			// SignalR unavailable — schedule a restart so we keep trying in the
			// background rather than permanently giving up.
			if (!this.isStopped) this.scheduleRestart();
		}
	}

	/** Permanently tear down the connection and stop all reconnection attempts. */
	async stop(): Promise<void> {
		this.isStopped = true;
		this.stopHeartbeat();
		this.stopVisibilityListener();
		if (this.restartTimer) {
			clearTimeout(this.restartTimer);
			this.restartTimer = null;
		}
		if (this.typingTimeout) clearTimeout(this.typingTimeout);
		if (this.connection) {
			try {
				await this.connection.stop();
			} catch {
				// ignore errors during disconnect
			}
			this.connection = null;
		}
	}

	/** Schedule a full connection rebuild with exponential backoff. */
	private scheduleRestart(): void {
		if (this.isStopped || !this.savedAccessTokenFactory || !this.savedCallbacks) return;
		if (this.restartTimer) clearTimeout(this.restartTimer);

		// Exponential backoff: 2s, 4s, 8s, 16s, 30s (capped)
		const delay = Math.min(2000 * Math.pow(2, this.restartAttempt), 30_000);
		this.restartAttempt++;

		this.restartTimer = setTimeout(async () => {
			if (this.isStopped) return;
			// Tear down the old connection before building a new one.
			// Set isRestarting so the onclose handler doesn't re-enter scheduleRestart.
			if (this.connection) {
				this.isRestarting = true;
				try { await this.connection.stop(); } catch { /* ignore */ }
				this.connection = null;
				this.isRestarting = false;
			}
			await this.buildAndStart(this.savedAccessTokenFactory!, this.savedCallbacks!);
		}, delay);
	}

	private listenForVisibility(): void {
		if (typeof document === 'undefined') return;
		this.visibilityHandler = () => {
			if (document.visibilityState !== 'visible') return;
			if (this.isStopped) return;
			// Tab became visible — if the connection is dead, restart immediately
			if (!this.isConnected && this.connection?.state !== HubConnectionState.Reconnecting) {
				this.restartAttempt = 0;
				this.scheduleRestart();
			}
		};
		document.addEventListener('visibilitychange', this.visibilityHandler);
	}

	private stopVisibilityListener(): void {
		if (this.visibilityHandler && typeof document !== 'undefined') {
			document.removeEventListener('visibilitychange', this.visibilityHandler);
			this.visibilityHandler = null;
		}
	}

	startHeartbeat(): void {
		this.stopHeartbeat();
		if (typeof document === 'undefined') return;
		// Listen for user activity
		document.addEventListener('mousemove', this.activityHandler);
		document.addEventListener('keydown', this.activityHandler);
		document.addEventListener('pointerdown', this.activityHandler);

		// Send heartbeat every 30s
		this.heartbeatInterval = setInterval(async () => {
			if (this.connection?.state === HubConnectionState.Connected) {
				try {
					await this.connection.invoke('Heartbeat', this.isUserActive);
				} catch (e) {
					console.warn('Heartbeat failed:', e);
				}
			}
			this.isUserActive = false;
		}, 30_000);
	}

	stopHeartbeat(): void {
		if (this.heartbeatInterval) {
			clearInterval(this.heartbeatInterval);
			this.heartbeatInterval = null;
		}
		if (typeof document !== 'undefined') {
			document.removeEventListener('mousemove', this.activityHandler);
			document.removeEventListener('keydown', this.activityHandler);
			document.removeEventListener('pointerdown', this.activityHandler);
		}
		this.isUserActive = false;
	}

	async joinChannel(channelId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('JoinChannel', channelId).catch(() => {});
		}
	}

	async leaveChannel(channelId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('LeaveChannel', channelId).catch(() => {});
		}
	}

	async joinServer(serverId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('JoinServer', serverId).catch(() => {});
		}
	}

	async leaveServer(serverId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('LeaveServer', serverId).catch(() => {});
		}
	}

	/** Emit a typing indicator, auto-clearing after 2 s of inactivity. */
	emitTyping(channelId: string, displayName: string): void {
		if (!this.isConnected) return;

		this.connection!.invoke('StartTyping', channelId, displayName).catch(() => {});

		if (this.typingTimeout) clearTimeout(this.typingTimeout);
		this.typingTimeout = setTimeout(() => {
			this.clearTyping(channelId, displayName);
		}, 2000);
	}

	clearTyping(channelId: string, displayName: string): void {
		if (this.typingTimeout) clearTimeout(this.typingTimeout);
		if (this.isConnected) {
			this.connection!.invoke('StopTyping', channelId, displayName).catch(() => {});
		}
	}

	/* ───── DM channel groups ───── */

	async joinDmChannel(dmChannelId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('JoinDmChannel', dmChannelId).catch(() => {});
		}
	}

	async leaveDmChannel(dmChannelId: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('LeaveDmChannel', dmChannelId).catch(() => {});
		}
	}

	private dmTypingTimeout: ReturnType<typeof setTimeout> | null = null;

	/** Emit a DM typing indicator, auto-clearing after 2 s of inactivity. */
	emitDmTyping(dmChannelId: string, displayName: string): void {
		if (!this.isConnected) return;

		this.connection!.invoke('StartDmTyping', dmChannelId, displayName).catch(() => {});

		if (this.dmTypingTimeout) clearTimeout(this.dmTypingTimeout);
		this.dmTypingTimeout = setTimeout(() => {
			this.clearDmTyping(dmChannelId, displayName);
		}, 2000);
	}

	clearDmTyping(dmChannelId: string, displayName: string): void {
		if (this.dmTypingTimeout) clearTimeout(this.dmTypingTimeout);
		if (this.isConnected) {
			this.connection!.invoke('StopDmTyping', dmChannelId, displayName).catch(() => {});
		}
	}

	/* ───── Voice ───── */

	async joinVoiceChannel(channelId: string): Promise<{
		routerRtpCapabilities: object;
		sendTransportOptions: object;
		recvTransportOptions: object;
		members: object[];
	}> {
		if (!this.isConnected) throw new Error('Hub not connected');
		return this.connection!.invoke('JoinVoiceChannel', channelId);
	}

	async connectTransport(transportId: string, dtlsParameters: object): Promise<void> {
		if (!this.isConnected) throw new Error('Hub not connected');
		await this.connection!.invoke('ConnectTransport', transportId, dtlsParameters);
	}

	async produce(sendTransportId: string, rtpParameters: object, label?: string): Promise<string> {
		if (!this.isConnected) throw new Error('Hub not connected');
		const result = await this.connection!.invoke('Produce', sendTransportId, rtpParameters, label ?? null);
		// The hub returns { producerId: "..." }; extract the string for the caller.
		return typeof result === 'string' ? result : result.producerId;
	}

	async stopProducing(label: string): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('StopProducing', label).catch(() => {});
		}
	}

	async consume(producerId: string, recvTransportId: string, rtpCapabilities: object): Promise<{
		id: string;
		producerId: string;
		kind: string;
		rtpParameters: object;
	}> {
		if (!this.isConnected) throw new Error('Hub not connected');
		return this.connection!.invoke('Consume', producerId, recvTransportId, rtpCapabilities);
	}

	async updateVoiceState(isMuted: boolean, isDeafened: boolean): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('UpdateVoiceState', isMuted, isDeafened).catch(() => {});
		}
	}

	async leaveVoiceChannel(): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('LeaveVoiceChannel').catch(() => {});
		}
	}

	/* ───── DM Voice Calls ───── */

	async startCall(dmChannelId: string): Promise<{
		callId: string;
		recipientUserId: string;
		recipientDisplayName: string;
		recipientAvatarUrl?: string | null;
	}> {
		if (!this.isConnected) throw new Error('Hub not connected');
		return this.connection!.invoke('StartCall', dmChannelId);
	}

	async acceptCall(callId: string): Promise<{
		callId: string;
		roomId: string;
		routerRtpCapabilities: object;
		sendTransportOptions: object;
		recvTransportOptions: object;
		iceServers?: object[];
		alreadyHandled?: boolean;
	}> {
		if (!this.isConnected) throw new Error('Hub not connected');
		return this.connection!.invoke('AcceptCall', callId);
	}

	async setupCallTransports(callId: string): Promise<{
		routerRtpCapabilities: object;
		sendTransportOptions: object;
		recvTransportOptions: object;
		members: object[];
		iceServers?: object[];
	}> {
		if (!this.isConnected) throw new Error('Hub not connected');
		return this.connection!.invoke('SetupCallTransports', callId);
	}

	async declineCall(callId: string): Promise<void> {
		if (!this.isConnected) throw new Error('Hub not connected');
		await this.connection!.invoke('DeclineCall', callId);
	}

	async endCall(): Promise<void> {
		if (this.isConnected) {
			await this.connection!.invoke('EndCall').catch(() => {});
		}
	}
}
