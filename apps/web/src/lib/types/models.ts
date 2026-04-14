/** Message type enum matching C# MessageType. */
export enum MessageType {
	Regular = 0,
	VoiceCallEvent = 1,
	PinNotification = 2
}

/** Report status enum matching C# ReportStatus. */
export enum ReportStatus {
	Open = 0,
	Reviewing = 1,
	Resolved = 2,
	Dismissed = 3
}

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
	roles: MemberRole[];
	sortOrder: number;
	permissions: number;
	isOwner: boolean;
};

/** A role assigned to a member (subset of ServerRole for API responses). */
export type MemberRole = {
	id: string;
	name: string;
	color?: string | null;
	position: number;
	isSystemRole: boolean;
};

/** A custom or system role within a server. */
export type ServerRole = {
	id: string;
	name: string;
	color?: string | null;
	position: number;
	permissions: number;
	isSystemRole: boolean;
	isHoisted: boolean;
	isMentionable: boolean;
	memberCount?: number;
};

/** Per-channel permission override for a role. */
export type ChannelPermissionOverride = {
	channelId: string;
	roleId: string;
	roleName: string;
	allow: number;
	deny: number;
};

/** Granular permission flags (matches the API Permission enum). */
export const Permission = {
	None: 0,
	ViewChannels: 1 << 0,
	ManageChannels: 1 << 1,
	ManageServer: 1 << 2,
	ManageRoles: 1 << 3,
	ManageEmojis: 1 << 4,
	ViewAuditLog: 1 << 5,
	CreateInvites: 1 << 6,
	ManageInvites: 1 << 7,
	KickMembers: 1 << 10,
	BanMembers: 1 << 11,
	SendMessages: 1 << 20,
	EmbedLinks: 1 << 21,
	AttachFiles: 1 << 22,
	AddReactions: 1 << 23,
	MentionEveryone: 1 << 24,
	ManageMessages: 1 << 25,
	PinMessages: 1 << 26,
	Connect: 1 << 30,
	Speak: 2 ** 31,
	MuteMembers: 2 ** 32,
	DeafenMembers: 2 ** 33,
	// Administrator uses 2**40 which exceeds 32-bit bitwise range.
	// We use a float constant and compare via isAdministrator() helper.
	Administrator: 2 ** 40,
} as const;

/** Check whether a permission value includes the Administrator flag (2^40, beyond 32-bit range). */
function isAdministrator(permissions: number): boolean {
	// The API sends permissions as a JSON number. For values ≥ 2^40, bitwise & truncates to 32 bits.
	// Instead, check via float division: if bit 40 is set, floor(p / 2^40) is odd.
	return Math.floor(permissions / Permission.Administrator) % 2 === 1;
}

/** Check if a permission set includes the given flag. */
export function hasPermission(permissions: number, flag: number): boolean {
	// Administrator grants everything
	if (isAdministrator(permissions)) return true;
	if (flag === Permission.Administrator) return false;
	// Flags or masks beyond 2^30 exceed 32-bit signed range; use float arithmetic
	if (flag > (1 << 30) || permissions > (1 << 30)) {
		return Math.floor(permissions / flag) % 2 === 1;
	}
	return (permissions & flag) === flag;
}

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
	uploadedByUserId: string | null;
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
	importedAuthorName?: string | null;
	importedAuthorAvatarUrl?: string | null;
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
	roles: MemberRole[];
	permissions: number;
	displayRole?: MemberRole | null;
	highestPosition: number;
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
		googleSubject?: string | null;
	};
	isNewUser?: boolean;
	needsLinking?: boolean;
	email?: string;
};

/** Response from POST /auth/register, POST /auth/login, and POST /auth/google. */
export type AuthResponse = {
	accessToken: string;
	refreshToken: string;
	isNewUser?: boolean;
	needsLinking?: boolean;
	email?: string;
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
	createdByUserId: string | null;
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
	| 'MemberRolesUpdated'
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

/* ───── Reports ───── */

export enum ReportType {
	User = 0,
	Message = 1,
	Server = 2,
}

/** Discord import job status. */
export type DiscordImportStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Cancelled' | 'RehostingMedia';

/** Discord import job. */
export type DiscordImport = {
	id: string;
	status: DiscordImportStatus;
	importedChannels: number;
	importedMessages: number;
	importedMembers: number;
	startedAt?: string | null;
	completedAt?: string | null;
	lastSyncedAt?: string | null;
	errorMessage?: string | null;
	discordGuildId: string;
	stage?: string | null;
	percentComplete?: number | null;
};

/** Discord user mapping for identity claiming. */
export type DiscordUserMapping = {
	discordUserId: string;
	discordUsername: string;
	discordAvatarUrl?: string | null;
	codecUserId?: string | null;
	claimedAt?: string | null;
};
