/** User presence status. */
export type PresenceStatus = 'online' | 'idle' | 'offline';

/** A single user's presence entry. */
export interface PresenceEntry {
	userId: string;
	status: PresenceStatus;
}

/** Server the current user is a member of (or visible to global admins). */
export type MemberServer = {
	serverId: string;
	name: string;
	description?: string;
	iconUrl?: string | null;
	role: string | null;
	sortOrder: number;
};

/** Channel type discriminator. */
export type ChannelType = 'text' | 'voice';

/** Text or voice channel within a server. */
export type Channel = {
	id: string;
	name: string;
	serverId: string;
	type?: ChannelType;
	description?: string;
	categoryId?: string;
	position: number;
};

/** A user currently connected to a voice channel. */
export type VoiceChannelMember = {
	userId: string;
	displayName: string;
	avatarUrl?: string | null;
	isMuted: boolean;
	isDeafened: boolean;
	isVideoEnabled?: boolean;
	isScreenSharing?: boolean;
	participantId: string;
};

/** Aggregated emoji reaction on a message. */
export type Reaction = {
	emoji: string;
	count: number;
	userIds: string[];
};

/** Link preview metadata for a URL in a message. */
export type LinkPreview = {
	url: string;
	title: string | null;
	description: string | null;
	imageUrl: string | null;
	siteName: string | null;
	canonicalUrl: string | null;
};

/** Server custom emoji uploaded by a member. */
export type CustomEmoji = {
	id: string;
	name: string;
	imageUrl: string;
	contentType: string;
	isAnimated: boolean;
	createdAt: string;
	uploadedByUserId: string;
};

/** Resolved user mention in a message. */
export type Mention = {
	userId: string;
	displayName: string;
};

/** Compact preview of the message being replied to. */
export type ReplyContext = {
	messageId: string;
	authorName: string;
	authorAvatarUrl: string | null;
	authorUserId: string | null;
	bodyPreview: string;
	isDeleted: boolean;
};

/** Chat message in a channel. */
export type Message = {
	id: string;
	authorName: string;
	body: string;
	imageUrl?: string | null;
	fileUrl?: string | null;
	fileName?: string | null;
	fileSize?: number | null;
	fileContentType?: string | null;
	createdAt: string;
	editedAt?: string | null;
	channelId: string;
	authorUserId?: string | null;
	authorAvatarUrl?: string | null;
	reactions: Reaction[];
	linkPreviews: LinkPreview[];
	mentions: Mention[];
	replyContext: ReplyContext | null;
	messageType?: number;
};

/** Paginated message response from the API. */
export type PaginatedMessages = {
	hasMore: boolean;
	messages: Message[];
};

/** Member of a server. */
export type Member = {
	userId: string;
	displayName: string;
	email?: string | null;
	avatarUrl?: string | null;
	role: string;
	joinedAt: string;
	statusText?: string | null;
	statusEmoji?: string | null;
};

/** Current user profile returned by GET /me. */
export type UserProfile = {
	user: {
		id: string;
		displayName: string;
		nickname?: string | null;
		effectiveDisplayName: string;
		email?: string;
		avatarUrl?: string;
		isGlobalAdmin?: boolean;
		emailVerified?: boolean;
		statusText?: string | null;
		statusEmoji?: string | null;
	};
	isNewUser?: boolean;
	needsLinking?: boolean;
	email?: string;
};

/** Response from POST /auth/register and POST /auth/login. */
export type AuthResponse = {
	accessToken: string;
	refreshToken: string;
	user: {
		id: string;
		displayName: string;
		nickname?: string | null;
		effectiveDisplayName: string;
		email?: string;
		avatarUrl?: string;
		isGlobalAdmin?: boolean;
		emailVerified?: boolean;
	};
};

/** Response from POST /auth/refresh. */
export type TokenRefreshResponse = {
	accessToken: string;
	refreshToken: string;
};

/** Friendship status enum matching the API. */
export type FriendshipStatus = 'Pending' | 'Accepted' | 'Declined';

/** A user summary as returned by friend-related endpoints. */
export type FriendUser = {
	id: string;
	displayName: string;
	avatarUrl?: string | null;
};

/** Confirmed friend entry returned by GET /friends. */
export type Friend = {
	friendshipId: string;
	user: FriendUser;
	since: string;
};

/** Pending friend request returned by GET /friends/requests. */
export type FriendRequest = {
	id: string;
	requester: FriendUser;
	recipient: FriendUser;
	status: FriendshipStatus;
	createdAt: string;
};

/** User search result returned by GET /users/search. */
export type UserSearchResult = {
	id: string;
	displayName: string;
	effectiveDisplayName?: string;
	email?: string | null;
	avatarUrl?: string | null;
	relationshipStatus: string;
};

/** Participant info in a DM conversation. */
export type DmParticipant = {
	id: string;
	displayName: string;
	avatarUrl?: string | null;
};

/** Last message preview in a DM conversation. */
export type DmLastMessage = {
	authorName: string;
	body: string;
	createdAt: string;
};

/** DM conversation entry returned by GET /dm/channels. */
export type DmConversation = {
	id: string;
	participant: DmParticipant;
	lastMessage: DmLastMessage | null;
	sortDate: string;
};

/** Paginated DM message response from the API. */
export type PaginatedDmMessages = {
	hasMore: boolean;
	messages: DirectMessage[];
};

/** Direct message in a DM conversation. */
export type DirectMessage = {
	id: string;
	dmChannelId: string;
	authorUserId: string;
	authorName: string;
	body: string;
	imageUrl?: string | null;
	fileUrl?: string | null;
	fileName?: string | null;
	fileSize?: number | null;
	fileContentType?: string | null;
	createdAt: string;
	editedAt?: string | null;
	authorAvatarUrl?: string | null;
	linkPreviews: LinkPreview[];
	replyContext: ReplyContext | null;
	reactions: Reaction[];
	messageType?: number; // 0 = regular, 1 = voiceCallEvent
};

/** Active or ringing call returned by GET /voice/active-call. */
export type ActiveCallResponse = {
	id: string;
	dmChannelId: string;
	callerUserId: string;
	recipientUserId: string;
	status: 'ringing' | 'active' | 'ended';
	startedAt: string;
	answeredAt?: string | null;
	otherUserId: string;
	otherDisplayName: string;
	otherAvatarUrl?: string | null;
};

/** Server invite code created by an Owner or Admin. */
export type ServerInvite = {
	id: string;
	serverId: string;
	code: string;
	expiresAt: string | null;
	maxUses: number | null;
	useCount: number;
	createdAt: string;
	createdByUserId: string;
};

/** A message search result, extending Message with context info. */
export type SearchResult = Message & {
	channelName?: string;
	dmChannelId?: string;
};

/** Paginated response from the search endpoint. */
export type PaginatedSearchResults = {
	totalCount: number;
	page: number;
	pageSize: number;
	results: SearchResult[];
};

/** Messages around a target message for jump-to-message. */
export type AroundMessages = {
	hasMoreBefore: boolean;
	hasMoreAfter: boolean;
	messages: Message[];
};

/** Filters for message search queries. */
export type SearchFilters = {
	channelId?: string;
	authorId?: string;
	before?: string;
	after?: string;
	has?: 'image' | 'link';
	page?: number;
	pageSize?: number;
};

/** A named category that groups channels within a server. */
export interface ChannelCategory {
	id: string;
	serverId: string;
	name: string;
	position: number;
}

/** A single entry in a server's audit log. */
export interface AuditLogEntry {
	id: string;
	action: string;
	targetType?: string;
	targetId?: string;
	details?: string;
	createdAt: string;
	actorUserId?: string;
	actorDisplayName: string;
	actorAvatarUrl?: string;
}

/** Paginated audit log response from the API. */
export interface PaginatedAuditLog {
	hasMore: boolean;
	entries: AuditLogEntry[];
}

/** Notification preferences for a server and its channels. */
export interface NotificationPreferences {
	serverMuted: boolean;
	channelOverrides: { channelId: string; isMuted: boolean }[];
}

/** A pinned message with pin metadata and the full message. */
export type PinnedMessage = {
	messageId: string;
	channelId: string;
	pinnedBy: { userId: string; displayName: string };
	pinnedAt: string;
	message: Message;
};

/** SignalR event when a message is pinned. */
export type MessagePinnedEvent = {
	messageId: string;
	channelId: string;
	pinnedBy: { userId: string; displayName: string };
	pinnedAt: string;
};

/** SignalR event when a message is unpinned. */
export type MessageUnpinnedEvent = {
	messageId: string;
	channelId: string;
	unpinnedBy: { userId: string; displayName: string };
};

/** Supported webhook event types. */
export type WebhookEventType =
	| 'MessageCreated'
	| 'MessageUpdated'
	| 'MessageDeleted'
	| 'MemberJoined'
	| 'MemberLeft'
	| 'MemberRoleChanged'
	| 'ChannelCreated'
	| 'ChannelUpdated'
	| 'ChannelDeleted';

/** An outgoing webhook configured for a server. */
export type Webhook = {
	id: string;
	serverId: string;
	name: string;
	url: string;
	eventTypes: string[];
	isActive: boolean;
	createdByUserId: string;
	createdAt: string;
	hasSecret: boolean;
};

/** A single delivery attempt for a webhook. */
export type WebhookDelivery = {
	id: string;
	eventType: string;
	statusCode: number | null;
	errorMessage: string | null;
	success: boolean;
	attempt: number;
	createdAt: string;
};

/** A banned user in a server. */
export type BannedMember = {
	userId: string;
	displayName: string;
	avatarUrl?: string | null;
	reason?: string | null;
	bannedAt: string;
	bannedByUserId: string;
};
