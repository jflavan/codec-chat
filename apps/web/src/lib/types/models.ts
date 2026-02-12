/** Server the current user is a member of. */
export type MemberServer = {
	serverId: string;
	name: string;
	role: string;
};

/** Server returned from the discover endpoint. */
export type DiscoverServer = {
	id: string;
	name: string;
	isMember: boolean;
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

/** Chat message in a channel. */
export type Message = {
	id: string;
	authorName: string;
	body: string;
	createdAt: string;
	channelId: string;
	authorUserId?: string | null;
	authorAvatarUrl?: string | null;
	reactions: Reaction[];
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
	email?: string | null;
	avatarUrl?: string | null;
	relationshipStatus: string;
};
