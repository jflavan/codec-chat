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
