import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import type { Message, Reaction, FriendUser } from '$lib/types/index.js';

export type ReactionUpdate = {
	messageId: string;
	channelId: string;
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

	constructor(private readonly hubUrl: string) {}

	get isConnected(): boolean {
		return this.connection?.state === HubConnectionState.Connected;
	}

	async start(token: string, callbacks: SignalRCallbacks): Promise<void> {
		const connection = new HubConnectionBuilder()
			.withUrl(this.hubUrl, { accessTokenFactory: () => token })
			.withAutomaticReconnect()
			.build();

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

		try {
			await connection.start();
			this.connection = connection;
		} catch {
			// SignalR unavailable; real-time features will be disabled.
		}
	}

	async stop(): Promise<void> {
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
}
