import { describe, it, expect, vi, beforeEach } from 'vitest';

// ---------------------------------------------------------------------------
// Hoisted mocks — vi.mock factories are hoisted to the top of the file by
// Vitest's transformer, so any variables they reference must be declared with
// vi.hoisted() to be available before the factory runs.
// ---------------------------------------------------------------------------
const { mockGetToken, MockHubConnectionBuilder, mockWithUrl, mockWithAutomaticReconnect, mockBuild } =
	vi.hoisted(() => {
		const mockGetToken = vi.fn<() => string | null>(() => null);

		const mockBuild = vi.fn();
		const mockWithAutomaticReconnect = vi.fn();
		const mockWithUrl = vi.fn();

		const mockBuilder = {
			withUrl: mockWithUrl,
			withAutomaticReconnect: mockWithAutomaticReconnect,
			build: mockBuild
		};

		// Chain returns the same builder object
		mockWithUrl.mockReturnValue(mockBuilder);
		mockWithAutomaticReconnect.mockReturnValue(mockBuilder);

		// Must be a regular function (not arrow) so `new HubConnectionBuilder()` works
		const MockHubConnectionBuilder = vi.fn(function () {
			return mockBuilder;
		});

		return { mockGetToken, MockHubConnectionBuilder, mockWithUrl, mockWithAutomaticReconnect, mockBuild };
	});

vi.mock('$lib/auth/auth', () => ({
	getToken: () => mockGetToken()
}));

vi.mock('@microsoft/signalr', () => ({
	HubConnectionBuilder: MockHubConnectionBuilder,
	HubConnectionState: {
		Connected: 'Connected',
		Disconnected: 'Disconnected'
	}
}));

// ---------------------------------------------------------------------------
// Import after mocks are set up
// ---------------------------------------------------------------------------
import { AdminHubService } from './admin-hub';

// ---------------------------------------------------------------------------
// Helper to create a fresh mock connection
// ---------------------------------------------------------------------------
function makeMockConnection(state = 'Connected') {
	return {
		on: vi.fn(),
		start: vi.fn().mockResolvedValue(undefined),
		stop: vi.fn().mockResolvedValue(undefined),
		state
	};
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe('AdminHubService', () => {
	let hub: AdminHubService;
	let mockConnection: ReturnType<typeof makeMockConnection>;

	beforeEach(() => {
		hub = new AdminHubService();
		mockGetToken.mockReset();

		// Refresh the mock connection for each test
		mockConnection = makeMockConnection();
		mockBuild.mockReturnValue(mockConnection);

		MockHubConnectionBuilder.mockClear();
		mockWithUrl.mockClear();
		mockWithAutomaticReconnect.mockClear();
	});

	// -----------------------------------------------------------------------
	// start()
	// -----------------------------------------------------------------------
	describe('start()', () => {
		it('does not connect when getToken returns null', async () => {
			mockGetToken.mockReturnValue(null);
			const cb = vi.fn();

			await hub.start(cb);

			expect(MockHubConnectionBuilder).not.toHaveBeenCalled();
			expect(mockConnection.start).not.toHaveBeenCalled();
		});

		it('does not connect when getToken returns empty string', async () => {
			mockGetToken.mockReturnValue('');
			const cb = vi.fn();

			await hub.start(cb);

			// empty string is falsy — treated same as null
			expect(MockHubConnectionBuilder).not.toHaveBeenCalled();
		});

		it('builds a connection when token is present', async () => {
			mockGetToken.mockReturnValue('valid-token');
			const cb = vi.fn();

			await hub.start(cb);

			expect(MockHubConnectionBuilder).toHaveBeenCalledOnce();
		});

		it('connects to the correct hub URL', async () => {
			mockGetToken.mockReturnValue('valid-token');
			const cb = vi.fn();

			await hub.start(cb);

			expect(mockWithUrl).toHaveBeenCalledOnce();
			const [url] = mockWithUrl.mock.calls[0];
			expect(url).toBe('http://test-api/hubs/admin');
		});

		it('passes an accessTokenFactory that returns the current token', async () => {
			mockGetToken.mockReturnValue('my-jwt');
			const cb = vi.fn();

			await hub.start(cb);

			const [, options] = mockWithUrl.mock.calls[0];
			const factory = options.accessTokenFactory;
			expect(typeof factory).toBe('function');
			expect(factory()).toBe('my-jwt');
		});

		it('calls withAutomaticReconnect()', async () => {
			mockGetToken.mockReturnValue('tok');
			await hub.start(vi.fn());

			expect(mockWithAutomaticReconnect).toHaveBeenCalledOnce();
		});

		it('subscribes to the StatsUpdated event', async () => {
			mockGetToken.mockReturnValue('tok');
			const cb = vi.fn();

			await hub.start(cb);

			expect(mockConnection.on).toHaveBeenCalledWith('StatsUpdated', cb);
		});

		it('calls connection.start()', async () => {
			mockGetToken.mockReturnValue('tok');
			await hub.start(vi.fn());

			expect(mockConnection.start).toHaveBeenCalledOnce();
		});

		it('passes the callback to StatsUpdated handler and invokes it with payload', async () => {
			mockGetToken.mockReturnValue('tok');
			const cb = vi.fn();

			await hub.start(cb);

			// Simulate the SignalR server sending a StatsUpdated event
			const call = mockConnection.on.mock.calls.find(
				(args: unknown[]) => args[0] === 'StatsUpdated'
			);
			expect(call).toBeDefined();
			const handler = call![1] as (payload: unknown) => void;

			const payload = {
				activeUsers: 5,
				activeConnections: 10,
				messagesPerMinute: 3,
				openReports: 1
			};
			handler(payload);

			expect(cb).toHaveBeenCalledWith(payload);
		});
	});

	// -----------------------------------------------------------------------
	// stop()
	// -----------------------------------------------------------------------
	describe('stop()', () => {
		it('does nothing when never started', async () => {
			await expect(hub.stop()).resolves.toBeUndefined();
		});

		it('calls connection.stop() when connection is Connected', async () => {
			mockGetToken.mockReturnValue('tok');
			mockConnection = makeMockConnection('Connected');
			mockBuild.mockReturnValue(mockConnection);

			await hub.start(vi.fn());
			await hub.stop();

			expect(mockConnection.stop).toHaveBeenCalledOnce();
		});

		it('does not call connection.stop() when state is Disconnected', async () => {
			mockGetToken.mockReturnValue('tok');
			mockConnection = makeMockConnection('Disconnected');
			mockBuild.mockReturnValue(mockConnection);

			await hub.start(vi.fn());
			await hub.stop();

			expect(mockConnection.stop).not.toHaveBeenCalled();
		});

		it('sets connection to null after stopping so a second stop is a no-op', async () => {
			mockGetToken.mockReturnValue('tok');
			mockConnection = makeMockConnection('Connected');
			mockBuild.mockReturnValue(mockConnection);

			await hub.start(vi.fn());
			await hub.stop();

			// Second stop should not throw and stop should only have been called once
			await expect(hub.stop()).resolves.toBeUndefined();
			expect(mockConnection.stop).toHaveBeenCalledTimes(1);
		});
	});
});
