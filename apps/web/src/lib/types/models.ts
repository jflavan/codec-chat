/** Server the current user is a member of. */
export type MemberServer = {
	serverId: string;
	name: string;
	role: string;
};

/** Text channel within a server. */
export type Channel = {
	id: string;
	name: string;
	serverId: string;
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
	createdAt: string;
	editedAt?: string | null;
	channelId: string;
	authorUserId?: string | null;
	authorAvatarUrl?: string | null;
	reactions: Reaction[];
	linkPreviews: LinkPreview[];
	mentions: Mention[];
	replyContext: ReplyContext | null;
};

/** Member of a server. */
export type Member = {
	userId: string;
	displayName: string;
	email?: string | null;
	avatarUrl?: string | null;
	role: string;
	joinedAt: string;
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
	};
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

/** Direct message in a DM conversation. */
export type DirectMessage = {
	id: string;
	dmChannelId: string;
	authorUserId: string;
	authorName: string;
	body: string;
	imageUrl?: string | null;
	createdAt: string;
	editedAt?: string | null;
	authorAvatarUrl?: string | null;
	linkPreviews: LinkPreview[];
	replyContext: ReplyContext | null;
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
