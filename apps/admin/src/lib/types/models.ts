export interface PaginatedResponse<T> {
	items: T[];
	totalCount: number;
	page: number;
	pageSize: number;
	totalPages: number;
}

export interface AdminStats {
	users: { total: number; new24h: number; new7d: number; new30d: number };
	servers: { total: number; new24h: number; new7d: number; new30d: number };
	messages: { last24h: number; last7d: number; last30d: number };
	openReports: number;
	live: { activeConnections: number; messagesPerMinute: number };
}

export interface AdminUser {
	id: string;
	displayName: string;
	nickname: string | null;
	email: string | null;
	avatarUrl: string | null;
	isGlobalAdmin: boolean;
	isDisabled: boolean;
	createdAt: string;
	hasGoogle: boolean;
	hasGitHub: boolean;
	hasDiscord: boolean;
	hasSaml: boolean;
	hasPassword: boolean;
}

export interface AdminServer {
	id: string;
	name: string;
	iconUrl: string | null;
	description: string | null;
	createdAt: string;
	isQuarantined: boolean;
	memberCount: number;
}

export interface Report {
	id: string;
	reportType: number;
	targetId: string;
	reason: string;
	status: number;
	createdAt: string;
	reporterName: string;
	assignedToUserId: string | null;
	relatedCount: number;
}

export interface AdminAction {
	id: string;
	actionType: string;
	targetType: string;
	targetId: string;
	reason: string | null;
	details: string | null;
	createdAt: string;
	actorName: string;
}

export interface SystemAnnouncement {
	id: string;
	title: string;
	body: string;
	isActive: boolean;
	createdAt: string;
	expiresAt: string | null;
	createdBy: string;
}
