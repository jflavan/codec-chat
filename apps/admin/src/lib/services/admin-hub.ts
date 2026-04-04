import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { env } from '$env/dynamic/public';
import { getToken } from '$lib/auth/auth';

export class AdminHubService {
	private connection: HubConnection | null = null;

	async start(onStatsUpdated: (data: any) => void) {
		if (!getToken()) return;

		const baseUrl = env.PUBLIC_API_BASE_URL || 'http://localhost:5050';
		this.connection = new HubConnectionBuilder()
			.withUrl(`${baseUrl}/hubs/admin`, { accessTokenFactory: () => getToken() ?? '' })
			.withAutomaticReconnect()
			.build();

		this.connection.on('StatsUpdated', onStatsUpdated);

		await this.connection.start();
	}

	async stop() {
		if (this.connection?.state === HubConnectionState.Connected) {
			await this.connection.stop();
		}
		this.connection = null;
	}
}
