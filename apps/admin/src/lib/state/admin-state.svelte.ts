import { getContext, setContext } from 'svelte';
import type { AdminStats } from '$lib/types/models';

const ADMIN_STATE_KEY = 'admin-state';

export class AdminState {
	stats = $state<AdminStats | null>(null);
	openReportCount = $state(0);
	currentUser = $state<any>(null);

	updateLiveStats(data: { activeUsers: number; activeConnections: number; messagesPerMinute: number; openReports: number }) {
		if (this.stats) {
			this.stats.live.activeConnections = data.activeConnections;
			this.stats.live.messagesPerMinute = data.messagesPerMinute;
		}
		this.openReportCount = data.openReports;
	}
}

export function createAdminState(): AdminState {
	const state = new AdminState();
	setContext(ADMIN_STATE_KEY, state);
	return state;
}

export function getAdminState(): AdminState {
	return getContext<AdminState>(ADMIN_STATE_KEY);
}
