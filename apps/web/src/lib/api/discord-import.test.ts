import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ApiClient, ApiError } from './client.js';

const BASE_URL = 'http://localhost:5050';
const TOKEN = 'test-token';
const SERVER_ID = 'server-123';

describe('ApiClient — Discord Import methods', () => {
	let client: ApiClient;
	let fetchSpy: ReturnType<typeof vi.fn>;

	beforeEach(() => {
		fetchSpy = vi.fn();
		vi.stubGlobal('fetch', fetchSpy);
		client = new ApiClient(BASE_URL);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	/* ───── startDiscordImport ───── */

	describe('startDiscordImport', () => {
		it('sends POST to the correct URL with correct body', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ id: 'import-abc' })
			});

			const result = await client.startDiscordImport(TOKEN, SERVER_ID, 'bot-token-xyz', 'guild-456');

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import`);
			expect(init.method).toBe('POST');
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBe('application/json');
			expect(JSON.parse(init.body)).toEqual({ botToken: 'bot-token-xyz', discordGuildId: 'guild-456' });
			expect(result).toEqual({ id: 'import-abc' });
		});

		it('URL-encodes the serverId', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ id: 'import-def' })
			});

			await client.startDiscordImport(TOKEN, 'server/with spaces', 'bt', 'gid');

			const [url] = fetchSpy.mock.calls[0];
			expect(url).toContain('server%2Fwith%20spaces');
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'Bad request' })
			});

			await expect(
				client.startDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid')
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError with correct status on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.startDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid').catch((e) => e);
			expect(err).toBeInstanceOf(ApiError);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError with correct status on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Internal error' })
			});

			const err = await client.startDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid').catch((e) => e);
			expect(err).toBeInstanceOf(ApiError);
			expect((err as ApiError).status).toBe(500);
		});
	});

	/* ───── getDiscordImportStatus ───── */

	describe('getDiscordImportStatus', () => {
		const importRecord = {
			id: 'import-abc',
			status: 'InProgress' as const,
			importedChannels: 3,
			importedMessages: 120,
			importedMembers: 10,
			startedAt: '2026-01-01T00:00:00Z',
			completedAt: null,
			errorMessage: null,
			discordGuildId: 'guild-456'
		};

		it('sends GET to the correct URL', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve(importRecord)
			});

			const result = await client.getDiscordImportStatus(TOKEN, SERVER_ID);

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import`);
			expect(init.method).toBeUndefined();
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBeUndefined();
			expect(result).toEqual(importRecord);
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'No import found' })
			});

			await expect(
				client.getDiscordImportStatus(TOKEN, SERVER_ID)
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.getDiscordImportStatus(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve(null)
			});

			const err = await client.getDiscordImportStatus(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(500);
		});
	});

	/* ───── resyncDiscordImport ───── */

	describe('resyncDiscordImport', () => {
		it('sends POST to /resync with correct body', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ id: 'import-resync-1' })
			});

			const result = await client.resyncDiscordImport(TOKEN, SERVER_ID, 'bot-token-xyz', 'guild-456');

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import/resync`);
			expect(init.method).toBe('POST');
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBe('application/json');
			expect(JSON.parse(init.body)).toEqual({ botToken: 'bot-token-xyz', discordGuildId: 'guild-456' });
			expect(result).toEqual({ id: 'import-resync-1' });
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'Bad request' })
			});

			await expect(
				client.resyncDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid')
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.resyncDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid').catch((e) => e);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Server error' })
			});

			const err = await client.resyncDiscordImport(TOKEN, SERVER_ID, 'bt', 'gid').catch((e) => e);
			expect((err as ApiError).status).toBe(500);
		});
	});

	/* ───── cancelDiscordImport ───── */

	describe('cancelDiscordImport', () => {
		it('sends DELETE to the correct URL and returns void', async () => {
			fetchSpy.mockResolvedValueOnce({ ok: true });

			const result = await client.cancelDiscordImport(TOKEN, SERVER_ID);

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import`);
			expect(init.method).toBe('DELETE');
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBeUndefined();
			expect(result).toBeUndefined();
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'No active import' })
			});

			await expect(
				client.cancelDiscordImport(TOKEN, SERVER_ID)
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.cancelDiscordImport(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Server error' })
			});

			const err = await client.cancelDiscordImport(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(500);
		});
	});

	/* ───── getDiscordUserMappings ───── */

	describe('getDiscordUserMappings', () => {
		const mappings = [
			{
				discordUserId: 'disc-user-1',
				discordUsername: 'DiscordUser#1234',
				discordAvatarUrl: 'https://cdn.discordapp.com/avatars/1/avatar.png',
				codecUserId: 'codec-user-1',
				claimedAt: '2026-01-02T00:00:00Z'
			},
			{
				discordUserId: 'disc-user-2',
				discordUsername: 'AnotherUser#5678',
				discordAvatarUrl: null,
				codecUserId: null,
				claimedAt: null
			}
		];

		it('sends GET to /mappings and returns array', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve(mappings)
			});

			const result = await client.getDiscordUserMappings(TOKEN, SERVER_ID);

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import/mappings`);
			expect(init.method).toBeUndefined();
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBeUndefined();
			expect(result).toEqual(mappings);
			expect(result).toHaveLength(2);
		});

		it('returns an empty array when no mappings exist', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve([])
			});

			const result = await client.getDiscordUserMappings(TOKEN, SERVER_ID);
			expect(result).toEqual([]);
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'Bad request' })
			});

			await expect(
				client.getDiscordUserMappings(TOKEN, SERVER_ID)
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.getDiscordUserMappings(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Server error' })
			});

			const err = await client.getDiscordUserMappings(TOKEN, SERVER_ID).catch((e) => e);
			expect((err as ApiError).status).toBe(500);
		});
	});

	/* ───── claimDiscordIdentity ───── */

	describe('claimDiscordIdentity', () => {
		it('sends POST to /claim with correct body', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ claimed: true })
			});

			const result = await client.claimDiscordIdentity(TOKEN, SERVER_ID, 'disc-user-1');

			expect(fetchSpy).toHaveBeenCalledOnce();
			const [url, init] = fetchSpy.mock.calls[0];
			expect(url).toBe(`${BASE_URL}/servers/${SERVER_ID}/discord-import/claim`);
			expect(init.method).toBe('POST');
			expect(init.headers['Authorization']).toBe(`Bearer ${TOKEN}`);
			expect(init.headers['Content-Type']).toBe('application/json');
			expect(JSON.parse(init.body)).toEqual({ discordUserId: 'disc-user-1' });
			expect(result).toEqual({ claimed: true });
		});

		it('returns { claimed: false } when identity already claimed', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ claimed: false })
			});

			const result = await client.claimDiscordIdentity(TOKEN, SERVER_ID, 'disc-user-1');
			expect(result.claimed).toBe(false);
		});

		it('throws ApiError on 400', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'Invalid discordUserId' })
			});

			await expect(
				client.claimDiscordIdentity(TOKEN, SERVER_ID, 'disc-user-bad')
			).rejects.toBeInstanceOf(ApiError);
		});

		it('throws ApiError on 403', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			const err = await client.claimDiscordIdentity(TOKEN, SERVER_ID, 'disc-user-1').catch((e) => e);
			expect((err as ApiError).status).toBe(403);
		});

		it('throws ApiError on 500', async () => {
			fetchSpy.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Server error' })
			});

			const err = await client.claimDiscordIdentity(TOKEN, SERVER_ID, 'disc-user-1').catch((e) => e);
			expect((err as ApiError).status).toBe(500);
		});
	});
});
