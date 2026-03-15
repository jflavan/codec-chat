import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ApiClient, ApiError } from './client';

const mockFetch = vi.fn();
vi.stubGlobal('fetch', mockFetch);

function jsonResponse(data: unknown, status = 200) {
	return {
		ok: status >= 200 && status < 300,
		status,
		json: () => Promise.resolve(data)
	};
}

function voidResponse(status = 204) {
	return {
		ok: status >= 200 && status < 300,
		status,
		json: () => Promise.resolve(null)
	};
}

describe('ApiError', () => {
	it('has status and message', () => {
		const err = new ApiError(404, 'Not found');
		expect(err.status).toBe(404);
		expect(err.message).toBe('Not found');
		expect(err.name).toBe('ApiError');
	});

	it('has default message', () => {
		const err = new ApiError(500);
		expect(err.message).toContain('500');
	});
});

describe('ApiClient', () => {
	let client: ApiClient;
	const baseUrl = 'http://localhost:5050';
	const token = 'test-token';

	beforeEach(() => {
		mockFetch.mockReset();
		client = new ApiClient(baseUrl);
	});

	// --- User ---

	describe('getMe', () => {
		it('sends GET with auth header', async () => {
			const userData = { user: { id: '1', displayName: 'Test' } };
			mockFetch.mockResolvedValueOnce(jsonResponse(userData));

			const result = await client.getMe(token);
			expect(result).toEqual(userData);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/me`,
				expect.objectContaining({
					headers: expect.objectContaining({ Authorization: `Bearer ${token}` })
				})
			);
		});
	});

	describe('setNickname', () => {
		it('sends PUT with JSON body', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ nickname: 'Nicky' }));

			await client.setNickname(token, 'Nicky');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/me/nickname`,
				expect.objectContaining({
					method: 'PUT',
					body: JSON.stringify({ nickname: 'Nicky' })
				})
			);
		});
	});

	// --- Servers ---

	describe('getServers', () => {
		it('sends GET to /servers', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([{ id: '1', name: 'Test' }]));
			const result = await client.getServers(token);
			expect(result).toHaveLength(1);
		});
	});

	describe('createServer', () => {
		it('sends POST with name', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: '1', name: 'New', role: 'Owner' }));
			const result = await client.createServer(token, 'New');
			expect(result.name).toBe('New');
		});
	});

	describe('deleteServer', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteServer(token, 'server-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/server-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Channels ---

	describe('getChannels', () => {
		it('sends GET with serverId', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([{ id: 'c1', name: 'general' }]));
			await client.getChannels(token, 'server-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/server-1/channels`,
				expect.anything()
			);
		});
	});

	describe('createChannel', () => {
		it('sends POST with type', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'c1', name: 'voice', type: 'voice' }));
			await client.createChannel(token, 'server-1', 'voice', 'voice');
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ name: 'voice', type: 'voice' });
		});
	});

	// --- Messages ---

	describe('getMessages', () => {
		it('sends GET with query params', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ messages: [], hasMore: false }));
			await client.getMessages(token, 'ch-1', { before: '2024-01-01', limit: 25 });
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('before=2024-01-01'),
				expect.anything()
			);
		});

		it('sends GET without params when none provided', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ messages: [], hasMore: false }));
			await client.getMessages(token, 'ch-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/channels/ch-1/messages`,
				expect.anything()
			);
		});
	});

	describe('sendMessage', () => {
		it('sends POST with body and optional fields', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'm1', body: 'hello' }));
			await client.sendMessage(token, 'ch-1', 'hello', 'img.png', 'reply-1');
			const call = mockFetch.mock.calls[0];
			const body = JSON.parse(call[1].body);
			expect(body.body).toBe('hello');
			expect(body.imageUrl).toBe('img.png');
			expect(body.replyToMessageId).toBe('reply-1');
		});
	});

	describe('deleteMessage', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteMessage(token, 'ch-1', 'msg-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/channels/ch-1/messages/msg-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Reactions ---

	describe('toggleReaction', () => {
		it('sends POST with emoji', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ action: 'added', reactions: [] }));
			await client.toggleReaction(token, 'ch-1', 'msg-1', '👍');
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ emoji: '👍' });
		});
	});

	// --- Friends ---

	describe('getFriends', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getFriends(token);
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/friends`, expect.anything());
		});
	});

	describe('sendFriendRequest', () => {
		it('sends POST with recipientUserId', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'fr-1' }));
			await client.sendFriendRequest(token, 'user-2');
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ recipientUserId: 'user-2' });
		});
	});

	// --- DMs ---

	describe('createOrResumeDm', () => {
		it('sends POST', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'dm-1' }));
			await client.createOrResumeDm(token, 'user-2');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/dm/channels`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('sendDm', () => {
		it('sends POST with body', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'dm-msg-1', body: 'hi' }));
			await client.sendDm(token, 'dm-1', 'hi');
			const call = mockFetch.mock.calls[0];
			const body = JSON.parse(call[1].body);
			expect(body.body).toBe('hi');
		});
	});

	// --- Search ---

	describe('searchUsers', () => {
		it('encodes query param', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.searchUsers(token, 'test user');
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('q=test%20user'),
				expect.anything()
			);
		});
	});

	describe('searchServerMessages', () => {
		it('sends GET with filters', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ results: [], totalCount: 0 }));
			await client.searchServerMessages(token, 's-1', 'hello', { channelId: 'ch-1' });
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('channelId=ch-1'),
				expect.anything()
			);
		});
	});

	// --- Voice ---

	describe('getVoiceStates', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getVoiceStates(token, 'ch-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/channels/ch-1/voice-states`,
				expect.anything()
			);
		});
	});

	describe('updateVoiceState', () => {
		it('sends PATCH', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.updateVoiceState(token, true, false);
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ isMuted: true, isDeafened: false });
		});
	});

	describe('getActiveCall', () => {
		it('returns null on 204', async () => {
			mockFetch.mockResolvedValueOnce({ ok: true, status: 204 });
			const result = await client.getActiveCall(token);
			expect(result).toBeNull();
		});

		it('returns call data on 200', async () => {
			const callData = { id: 'call-1', status: 'ringing' };
			mockFetch.mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(callData)
			});
			const result = await client.getActiveCall(token);
			expect(result).toEqual(callData);
		});
	});

	describe('getTurnCredentials', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ urls: ['turn:localhost'], username: 'u', credential: 'c' }));
			const result = await client.getTurnCredentials(token);
			expect(result.urls).toEqual(['turn:localhost']);
		});
	});

	// --- Error handling ---

	describe('error handling', () => {
		it('throws ApiError on non-2xx', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 403,
				json: () => Promise.resolve({ error: 'Forbidden' })
			});

			await expect(client.getMe(token)).rejects.toThrow(ApiError);
		});

		it('includes error message from response', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ detail: 'Bad request detail' })
			});

			try {
				await client.getMe(token);
			} catch (e) {
				expect(e).toBeInstanceOf(ApiError);
				expect((e as ApiError).message).toBe('Bad request detail');
			}
		});
	});

	// --- 401 retry ---

	describe('401 retry', () => {
		it('retries with fresh token on 401', async () => {
			const onUnauthorized = vi.fn().mockResolvedValue('fresh-token');
			client = new ApiClient(baseUrl, onUnauthorized);

			mockFetch
				.mockResolvedValueOnce({ ok: false, status: 401, json: () => Promise.resolve({}) })
				.mockResolvedValueOnce(jsonResponse({ user: { id: '1' } }));

			const result = await client.getMe(token);
			expect(onUnauthorized).toHaveBeenCalled();
			expect(mockFetch).toHaveBeenCalledTimes(2);
		});

		it('does not retry without onUnauthorized callback', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 401,
				json: () => Promise.resolve({ error: 'Unauthorized' })
			});

			await expect(client.getMe(token)).rejects.toThrow(ApiError);
			expect(mockFetch).toHaveBeenCalledTimes(1);
		});
	});

	// --- Presence ---

	describe('getServerPresence', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getServerPresence(token, 's-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s-1/presence`,
				expect.anything()
			);
		});
	});

	// --- Bug Reports ---

	describe('submitBugReport', () => {
		it('sends POST with all fields', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ issueUrl: 'https://github.com/issues/1' }));
			await client.submitBugReport(token, 'Bug', 'Desc', 'Mozilla/5.0', '/page');
			const call = mockFetch.mock.calls[0];
			const body = JSON.parse(call[1].body);
			expect(body.title).toBe('Bug');
			expect(body.description).toBe('Desc');
		});
	});

	// --- Additional User methods ---

	describe('uploadAvatar', () => {
		it('sends POST with FormData', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ avatarUrl: 'https://example.com/avatar.png' }));
			const file = new File(['data'], 'avatar.png', { type: 'image/png' });
			const result = await client.uploadAvatar(token, file);
			expect(result.avatarUrl).toBe('https://example.com/avatar.png');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/me/avatar`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('deleteAvatar', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ avatarUrl: 'https://google.com/pic.jpg' }));
			await client.deleteAvatar(token);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/me/avatar`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('removeNickname', () => {
		it('sends DELETE to me/nickname', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ nickname: null, effectiveDisplayName: 'Test' }));
			await client.removeNickname(token);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/me/nickname`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('uploadServerAvatar', () => {
		it('sends POST with FormData to server path', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ avatarUrl: 'https://example.com/server-avatar.png' }));
			const file = new File(['data'], 'avatar.png', { type: 'image/png' });
			await client.uploadServerAvatar(token, 'server-1', file);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/server-1/avatar`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('deleteServerAvatar', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ avatarUrl: 'fallback.png' }));
			await client.deleteServerAvatar(token, 'server-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/server-1/avatar`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Additional Server methods ---

	describe('reorderServers', () => {
		it('sends PUT with serverIds', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.reorderServers(token, ['s1', 's2', 's3']);
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ serverIds: ['s1', 's2', 's3'] });
		});
	});

	describe('updateServer', () => {
		it('sends PATCH with name', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 's1', name: 'Renamed' }));
			await client.updateServer(token, 's1', 'Renamed');
			const call = mockFetch.mock.calls[0];
			expect(call[1].method).toBe('PATCH');
		});
	});

	describe('uploadServerIcon', () => {
		it('sends POST with FormData', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ iconUrl: 'https://example.com/icon.png' }));
			const file = new File(['data'], 'icon.png', { type: 'image/png' });
			await client.uploadServerIcon(token, 's1', file);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/icon`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('deleteServerIcon', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteServerIcon(token, 's1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/icon`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('deleteChannel', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteChannel(token, 's1', 'ch-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/channels/ch-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('getMembers', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getMembers(token, 's1');
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/servers/s1/members`, expect.anything());
		});
	});

	describe('updateChannel', () => {
		it('sends PATCH', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'ch1', name: 'renamed' }));
			await client.updateChannel(token, 's1', 'ch1', 'renamed');
			expect(mockFetch.mock.calls[0][1].method).toBe('PATCH');
		});
	});

	// --- Additional Messages methods ---

	describe('uploadImage', () => {
		it('sends POST with FormData', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ imageUrl: 'https://example.com/img.png' }));
			const file = new File(['data'], 'img.png', { type: 'image/png' });
			await client.uploadImage(token, file);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/uploads/images`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('purgeChannel', () => {
		it('sends DELETE to messages endpoint', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.purgeChannel(token, 'ch-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/channels/ch-1/messages`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('editMessage', () => {
		it('sends PUT with body', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'm1', body: 'edited', editedAt: '2024-01-01' }));
			await client.editMessage(token, 'ch-1', 'm1', 'edited');
			const call = mockFetch.mock.calls[0];
			expect(call[1].method).toBe('PUT');
			expect(JSON.parse(call[1].body)).toEqual({ body: 'edited' });
		});
	});

	// --- Additional DM Reactions ---

	describe('toggleDmReaction', () => {
		it('sends POST', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ action: 'added', reactions: [] }));
			await client.toggleDmReaction(token, 'dm-1', 'msg-1', '❤️');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/dm/channels/dm-1/messages/msg-1/reactions`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	// --- Additional Friend methods ---

	describe('removeFriend', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.removeFriend(token, 'fr-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/friends/fr-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('getFriendRequests', () => {
		it('sends GET with direction', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getFriendRequests(token, 'sent');
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('direction=sent'),
				expect.anything()
			);
		});
	});

	describe('respondToFriendRequest', () => {
		it('sends PUT with action', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'fr-1', status: 'Accepted' }));
			await client.respondToFriendRequest(token, 'fr-1', 'accept');
			const call = mockFetch.mock.calls[0];
			expect(call[1].method).toBe('PUT');
			expect(JSON.parse(call[1].body)).toEqual({ action: 'accept' });
		});
	});

	describe('cancelFriendRequest', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.cancelFriendRequest(token, 'fr-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/friends/requests/fr-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Additional DM methods ---

	describe('getDmConversations', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getDmConversations(token);
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/dm/channels`, expect.anything());
		});
	});

	describe('getDmMessages', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ messages: [], hasMore: false }));
			await client.getDmMessages(token, 'dm-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/dm/channels/dm-1/messages`,
				expect.anything()
			);
		});
	});

	describe('deleteDmMessage', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteDmMessage(token, 'dm-1', 'msg-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/dm/channels/dm-1/messages/msg-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('editDmMessage', () => {
		it('sends PUT', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'msg-1', body: 'edited', editedAt: '2024-01-01' }));
			await client.editDmMessage(token, 'dm-1', 'msg-1', 'edited');
			expect(mockFetch.mock.calls[0][1].method).toBe('PUT');
		});
	});

	describe('closeDmConversation', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.closeDmConversation(token, 'dm-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/dm/channels/dm-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Server Moderation ---

	describe('kickMember', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.kickMember(token, 's1', 'user-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/members/user-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('updateMemberRole', () => {
		it('sends PATCH', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.updateMemberRole(token, 's1', 'user-1', 'Admin');
			expect(mockFetch.mock.calls[0][1].method).toBe('PATCH');
		});
	});

	// --- Server Invites ---

	describe('createInvite', () => {
		it('sends POST', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ code: 'abc123' }));
			await client.createInvite(token, 's1', { maxUses: 10 });
			const call = mockFetch.mock.calls[0];
			expect(JSON.parse(call[1].body)).toEqual({ maxUses: 10, expiresInHours: null });
		});
	});

	describe('getInvites', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getInvites(token, 's1');
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/servers/s1/invites`, expect.anything());
		});
	});

	describe('revokeInvite', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.revokeInvite(token, 's1', 'inv-1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/invites/inv-1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('joinViaInvite', () => {
		it('sends POST', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ serverId: 's1', userId: 'u1', role: 'Member' }));
			await client.joinViaInvite(token, 'abc123');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/invites/abc123`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	// --- Custom Emojis ---

	describe('getCustomEmojis', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getCustomEmojis(token, 's1');
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/servers/s1/emojis`, expect.anything());
		});
	});

	describe('uploadCustomEmoji', () => {
		it('sends POST with FormData', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'e1', name: 'pepe' }));
			const file = new File(['data'], 'pepe.png', { type: 'image/png' });
			await client.uploadCustomEmoji(token, 's1', 'pepe', file);
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/emojis`,
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('renameCustomEmoji', () => {
		it('sends PATCH', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.renameCustomEmoji(token, 's1', 'e1', 'newname');
			expect(mockFetch.mock.calls[0][1].method).toBe('PATCH');
		});
	});

	describe('deleteCustomEmoji', () => {
		it('sends DELETE', async () => {
			mockFetch.mockResolvedValueOnce(voidResponse());
			await client.deleteCustomEmoji(token, 's1', 'e1');
			expect(mockFetch).toHaveBeenCalledWith(
				`${baseUrl}/servers/s1/emojis/e1`,
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	// --- Messages Around ---

	describe('getMessagesAround', () => {
		it('sends GET with around param', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ messages: [], targetIndex: 0 }));
			await client.getMessagesAround(token, 'ch-1', 'msg-1', 50);
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('around=msg-1'),
				expect.anything()
			);
		});
	});

	// --- Search DM ---

	describe('searchDmMessages', () => {
		it('sends GET with query', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ results: [], totalCount: 0 }));
			await client.searchDmMessages(token, 'hello');
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('dm/search'),
				expect.anything()
			);
		});
	});

	// --- DM Presence ---

	describe('getDmPresence', () => {
		it('sends GET', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse([]));
			await client.getDmPresence(token);
			expect(mockFetch).toHaveBeenCalledWith(`${baseUrl}/dm/presence`, expect.anything());
		});
	});

	// --- DM Messages Around ---

	describe('getDmMessagesAround', () => {
		it('sends GET with around param', async () => {
			mockFetch.mockResolvedValueOnce(jsonResponse({ messages: [], targetIndex: 0 }));
			await client.getDmMessagesAround(token, 'dm-1', 'msg-1');
			expect(mockFetch).toHaveBeenCalledWith(
				expect.stringContaining('around=msg-1'),
				expect.anything()
			);
		});
	});

	// --- Error extraction from errors field ---

	describe('error handling - errors field', () => {
		it('extracts validation errors from errors object', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ errors: { Name: ['Name is required', 'Name too long'] } })
			});
			try {
				await client.getMe(token);
			} catch (e) {
				expect((e as ApiError).message).toBe('Name is required; Name too long');
			}
		});

		it('handles json parse failure gracefully', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.reject(new Error('not json'))
			});
			await expect(client.getMe(token)).rejects.toThrow(ApiError);
		});
	});

	// --- getActiveCall error ---

	describe('getActiveCall error handling', () => {
		it('throws ApiError on non-ok non-204', async () => {
			mockFetch.mockResolvedValueOnce({
				ok: false,
				status: 500,
				json: () => Promise.resolve({ error: 'Server error' })
			});
			await expect(client.getActiveCall(token)).rejects.toThrow(ApiError);
		});
	});

	// --- requestVoid 401 retry ---

	describe('void request 401 retry', () => {
		it('retries void request on 401', async () => {
			const onUnauthorized = vi.fn().mockResolvedValue('fresh-token');
			client = new ApiClient(baseUrl, onUnauthorized);

			mockFetch
				.mockResolvedValueOnce({ ok: false, status: 401, json: () => Promise.resolve({}) })
				.mockResolvedValueOnce(voidResponse());

			await client.deleteServer(token, 's1');
			expect(onUnauthorized).toHaveBeenCalled();
			expect(mockFetch).toHaveBeenCalledTimes(2);
		});
	});
});
