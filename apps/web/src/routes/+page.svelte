<script lang="ts">
	import { onMount } from 'svelte';
	import { env } from '$env/dynamic/public';

	type MemberServer = { serverId: string; name: string; role: string };
	type DiscoverServer = { id: string; name: string; isMember: boolean };
	type Channel = { id: string; name: string; serverId: string };
	type Message = {
		id: string;
		authorName: string;
		body: string;
		createdAt: string;
		channelId: string;
		authorUserId?: string | null;
	};
	type Member = {
		userId: string;
		displayName: string;
		email?: string | null;
		avatarUrl?: string | null;
		role: string;
		joinedAt: string;
	};

	let servers = $state<MemberServer[]>([]);
	let discoverServers = $state<DiscoverServer[]>([]);
	let channels = $state<Channel[]>([]);
	let messages = $state<Message[]>([]);
	let members = $state<Member[]>([]);

	let selectedServerId = $state<string | null>(null);
	let selectedChannelId = $state<string | null>(null);
	let messageBody = $state('');

	let idToken = $state<string | null>(null);
	let status = $state('Signed out');
	let error = $state<string | null>(null);
	let me =
		$state<{ user: { id: string; displayName: string; email?: string; avatarUrl?: string } } | null>(
			null
		);

	let isLoadingServers = $state(false);
	let isLoadingDiscover = $state(false);
	let isLoadingChannels = $state(false);
	let isLoadingMessages = $state(false);
	let isSending = $state(false);
	let isJoining = $state(false);
	let isLoadingMe = $state(false);
	let isLoadingMembers = $state(false);
	let isCreatingServer = $state(false);
	let showCreateServer = $state(false);
	let newServerName = $state('');
	let isCreatingChannel = $state(false);
	let showCreateChannel = $state(false);
	let newChannelName = $state('');

	const isSignedIn = $derived.by(() => Boolean(idToken));

	// Derives the current user's role in the selected server.
	const currentServerRole = $derived(
		servers.find((s) => s.serverId === selectedServerId)?.role ?? null
	);
	const canManageChannels = $derived(
		currentServerRole === 'Owner' || currentServerRole === 'Admin'
	);

	const apiBaseUrl = env.PUBLIC_API_BASE_URL;

	function setError(message: string) {
		error = message;
		me = null;
	}

	function formatTime(value: string) {
		const date = new Date(value);
		if (Number.isNaN(date.getTime())) {
			return '';
		}
		return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	async function loadServers() {
		isLoadingServers = true;
		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			isLoadingServers = false;
			return;
		}

		if (!idToken) {
			isLoadingServers = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/servers`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});
			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			servers = await response.json();
			selectedServerId = servers[0]?.serverId ?? null;
			if (selectedServerId) {
				await loadChannels(selectedServerId);
				await loadMembers(selectedServerId);
			}
		} finally {
			isLoadingServers = false;
		}
	}

	async function loadDiscoverServers() {
		isLoadingDiscover = true;
		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			isLoadingDiscover = false;
			return;
		}

		if (!idToken) {
			isLoadingDiscover = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/servers/discover`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});

			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			discoverServers = await response.json();
		} finally {
			isLoadingDiscover = false;
		}
	}

	async function loadChannels(serverId: string) {
		isLoadingChannels = true;
		if (!idToken) {
			isLoadingChannels = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/servers/${serverId}/channels`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});
			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			channels = await response.json();
			selectedChannelId = channels[0]?.id ?? null;
			if (selectedChannelId) {
				await loadMessages(selectedChannelId);
			} else {
				messages = [];
			}
		} finally {
			isLoadingChannels = false;
		}
	}

	async function loadMessages(channelId: string) {
		isLoadingMessages = true;
		if (!idToken) {
			isLoadingMessages = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/channels/${channelId}/messages`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});
			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			messages = await response.json();
		} finally {
			isLoadingMessages = false;
		}
	}

	async function selectServer(serverId: string) {
		selectedServerId = serverId;
		await loadChannels(serverId);
		await loadMembers(serverId);
	}

	async function selectChannel(channelId: string) {
		selectedChannelId = channelId;
		await loadMessages(channelId);
	}

	async function callMe() {
		error = null;
		me = null;

		if (!idToken) {
			setError('Sign in with Google first.');
			return;
		}

		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			return;
		}

		isLoadingMe = true;
		try {
			const response = await fetch(`${apiBaseUrl}/me`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});

			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			me = await response.json();
		} finally {
			isLoadingMe = false;
		}
	}

	async function sendMessage() {
		isSending = true;
		if (!idToken) {
			setError('Sign in with Google first.');
			isSending = false;
			return;
		}

		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			isSending = false;
			return;
		}

		if (!selectedChannelId) {
			setError('Select a channel first.');
			isSending = false;
			return;
		}

		const body = messageBody.trim();
		if (!body) {
			setError('Message body is required.');
			isSending = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/channels/${selectedChannelId}/messages`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					Authorization: `Bearer ${idToken}`
				},
				body: JSON.stringify({ body })
			});

			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			messageBody = '';
			await loadMessages(selectedChannelId);
		} finally {
			isSending = false;
		}
	}

	async function joinServer(serverId: string) {
		if (!idToken) {
			setError('Sign in with Google first.');
			return;
		}

		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			return;
		}

		isJoining = true;
		try {
			const response = await fetch(`${apiBaseUrl}/servers/${serverId}/join`, {
				method: 'POST',
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});

			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			await loadServers();
			await loadDiscoverServers();
		} finally {
			isJoining = false;
		}
	}

	async function createServer() {
		const name = newServerName.trim();
		if (!name) {
			setError('Server name is required.');
			return;
		}

		if (!idToken || !apiBaseUrl) {
			setError('Sign in first.');
			return;
		}

		isCreatingServer = true;
		try {
			const response = await fetch(`${apiBaseUrl}/servers`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					Authorization: `Bearer ${idToken}`
				},
				body: JSON.stringify({ name })
			});

			if (!response.ok) {
				const data = await response.json().catch(() => null);
				setError(data?.error ?? `API error: ${response.status}`);
				return;
			}

			const created = await response.json();
			newServerName = '';
			showCreateServer = false;
			await loadServers();
			await loadDiscoverServers();
			await selectServer(created.id);
		} finally {
			isCreatingServer = false;
		}
	}

	async function createChannel() {
		const name = newChannelName.trim();
		if (!name) {
			setError('Channel name is required.');
			return;
		}

		if (!idToken || !apiBaseUrl || !selectedServerId) {
			setError('Select a server first.');
			return;
		}

		isCreatingChannel = true;
		try {
			const response = await fetch(`${apiBaseUrl}/servers/${selectedServerId}/channels`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					Authorization: `Bearer ${idToken}`
				},
				body: JSON.stringify({ name })
			});

			if (!response.ok) {
				const data = await response.json().catch(() => null);
				setError(data?.error ?? `API error: ${response.status}`);
				return;
			}

			const created = await response.json();
			newChannelName = '';
			showCreateChannel = false;
			await loadChannels(selectedServerId);
			await selectChannel(created.id);
		} finally {
			isCreatingChannel = false;
		}
	}

	async function loadMembers(serverId: string) {
		isLoadingMembers = true;
		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			isLoadingMembers = false;
			return;
		}

		if (!idToken) {
			isLoadingMembers = false;
			return;
		}

		try {
			const response = await fetch(`${apiBaseUrl}/servers/${serverId}/members`, {
				headers: {
					Authorization: `Bearer ${idToken}`
				}
			});

			if (!response.ok) {
				setError(`API error: ${response.status}`);
				return;
			}

			members = await response.json();
		} finally {
			isLoadingMembers = false;
		}
	}

	onMount(() => {
		if (!env.PUBLIC_GOOGLE_CLIENT_ID) {
			setError('Missing PUBLIC_GOOGLE_CLIENT_ID.');
			return;
		}

		const script = document.createElement('script');
		script.src = 'https://accounts.google.com/gsi/client';
		script.async = true;
		script.defer = true;
		script.onload = () => {
			const google = (window as unknown as { google?: any }).google;
			if (!google?.accounts?.id) {
				setError('Google Identity Services failed to load.');
				return;
			}

			google.accounts.id.initialize({
				client_id: env.PUBLIC_GOOGLE_CLIENT_ID,
				callback: (response: { credential: string }) => {
					idToken = response.credential;
					status = 'Signed in';
					callMe();
					loadServers();
					loadDiscoverServers();
				}
			});

			google.accounts.id.renderButton(document.getElementById('google-button'), {
				theme: 'outline',
				size: 'large'
			});
		};

		document.head.appendChild(script);
	});
</script>

<svelte:head>
	<title>Codec</title>
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin="" />
	<link
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;600;700&display=swap"
		rel="stylesheet"
	/>
</svelte:head>

<main>
	<header class="topbar">
		<div>
			<h1>Codec</h1>
			<p>Discord-like chat, starting with Google Sign-In.</p>
		</div>
		<div class="auth">
			<div id="google-button" class="google-button"></div>
			<p class="status">{status}</p>
		</div>
	</header>

	<section class="layout">
		<aside class="servers">
			<h2>Servers</h2>
			<ul>
				{#if !isSignedIn}
					<li class="muted">Sign in to load your servers.</li>
				{:else if isLoadingServers}
					<li class="muted">Loading servers...</li>
				{:else if servers.length === 0}
					<li class="muted">No servers yet.</li>
				{:else}
					{#each servers as server}
						<li class:active={server.serverId === selectedServerId}>
							<button class="list-button" onclick={() => selectServer(server.serverId)}>
								{server.name}
							</button>
						</li>
					{/each}
				{/if}
			</ul>
			{#if isSignedIn}
				<div class="create-form-toggle">
					{#if showCreateServer}
						<form class="inline-form" onsubmit={(e) => { e.preventDefault(); createServer(); }}>
							<input
								type="text"
								placeholder="Server name"
								maxlength="100"
								bind:value={newServerName}
								disabled={isCreatingServer}
							/>
							<div class="inline-form-actions">
								<button type="submit" disabled={isCreatingServer || !newServerName.trim()}>
									{isCreatingServer ? 'Creating...' : 'Create'}
								</button>
								<button type="button" class="cancel-button" onclick={() => { showCreateServer = false; newServerName = ''; }}>Cancel</button>
							</div>
						</form>
					{:else}
						<button class="list-button secondary" onclick={() => { showCreateServer = true; }}>+ Create Server</button>
					{/if}
				</div>
			{/if}
			{#if isSignedIn && discoverServers.some((server) => !server.isMember)}
				<div class="discover">
					<p>Join a server</p>
					<ul>
						{#if isLoadingDiscover}
							<li class="muted">Loading servers...</li>
						{:else}
							{#each discoverServers as server}
								{#if !server.isMember}
									<li>
										<button
											class="list-button secondary"
											onclick={() => joinServer(server.id)}
											disabled={isJoining}
										>
											Join {server.name}
										</button>
									</li>
								{/if}
							{/each}
						{/if}
					</ul>
				</div>
			{/if}
		</aside>

		<aside class="channels">
			<h2>Channels</h2>
			<ul>
				{#if isLoadingChannels}
					<li class="muted">Loading channels...</li>
				{:else if channels.length === 0}
					<li class="muted">No channels yet.</li>
				{:else}
					{#each channels as channel}
						<li class:active={channel.id === selectedChannelId}>
							<button class="list-button" onclick={() => selectChannel(channel.id)}>
								#{channel.name}
							</button>
						</li>
					{/each}
				{/if}
			</ul>
			{#if canManageChannels && selectedServerId}
				<div class="create-form-toggle">
					{#if showCreateChannel}
						<form class="inline-form" onsubmit={(e) => { e.preventDefault(); createChannel(); }}>
							<input
								type="text"
								placeholder="Channel name"
								maxlength="100"
								bind:value={newChannelName}
								disabled={isCreatingChannel}
							/>
							<div class="inline-form-actions">
								<button type="submit" disabled={isCreatingChannel || !newChannelName.trim()}>
									{isCreatingChannel ? 'Creating...' : 'Create'}
								</button>
								<button type="button" class="cancel-button" onclick={() => { showCreateChannel = false; newChannelName = ''; }}>Cancel</button>
							</div>
						</form>
					{:else}
						<button class="list-button secondary" onclick={() => { showCreateChannel = true; }}>+ Add Channel</button>
					{/if}
				</div>
			{/if}
		</aside>

		<section class="chat">
			<div class="chat-header">
				<div>
					<h2>
						{#if selectedChannelId}
							#{channels.find((channel) => channel.id === selectedChannelId)?.name ?? 'channel'}
						{:else}
							Select a channel
						{/if}
					</h2>
					<p>Ship logs and quick updates.</p>
				</div>
				<button onclick={callMe}>Call /me</button>
			</div>

			<div class="messages">
				{#if isLoadingMessages}
					<p class="muted">Loading messages...</p>
				{:else if messages.length === 0}
					<p class="muted">No messages yet.</p>
				{:else}
					{#each messages as message}
						<article>
							<div>
								<strong>{message.authorName}</strong>
								<span>{formatTime(message.createdAt)}</span>
							</div>
							<p>{message.body}</p>
						</article>
					{/each}
				{/if}
			</div>

			<div class="composer">
				<input
					type="text"
					placeholder="Message #build-log"
					bind:value={messageBody}
					disabled={!selectedChannelId || isSending}
				/>
				<button
					onclick={sendMessage}
					disabled={!selectedChannelId || !messageBody.trim() || isSending}
				>
					Send
				</button>
			</div>
		</section>

		<aside class="inspector">
			<h2>Auth status</h2>
			{#if error}
				<p class="error">{error}</p>
			{/if}
			{#if isLoadingMe}
				<p class="muted">Loading profile...</p>
			{:else if me}
				<div class="user-card">
					{#if me.user.avatarUrl}
						<img src={me.user.avatarUrl} alt="Avatar" />
					{/if}
					<div>
						<strong>{me.user.displayName}</strong>
						{#if me.user.email}
							<p class="muted">{me.user.email}</p>
						{/if}
					</div>
				</div>
			{:else}
				<p class="muted">Call /me to see the authenticated response.</p>
			{/if}

			<h2>Members</h2>
			{#if !isSignedIn}
				<p class="muted">Sign in to see server members.</p>
			{:else if !selectedServerId}
				<p class="muted">Select a server to view members.</p>
			{:else if isLoadingMembers}
				<p class="muted">Loading members...</p>
			{:else if members.length === 0}
				<p class="muted">No members yet.</p>
			{:else}
				<ul class="member-list">
					{#each members as member}
						<li>
							<div class="member-card">
								{#if member.avatarUrl}
									<img src={member.avatarUrl} alt="Avatar" />
								{:else}
									<div class="member-avatar" aria-hidden="true">
										{member.displayName.slice(0, 1).toUpperCase()}
									</div>
								{/if}
								<div>
									<strong>{member.displayName}</strong>
									<span class="role-badge">{member.role}</span>
									{#if member.email}
										<p class="muted">{member.email}</p>
									{/if}
								</div>
							</div>
						</li>
					{/each}
				</ul>
			{/if}
		</aside>
	</section>
</main>

<style>
	:global(body) {
		margin: 0;
		font-family: 'Space Grotesk', 'Segoe UI', sans-serif;
		background: radial-gradient(circle at top, #f8f0e8 0%, #f2f2f2 45%, #ffffff 100%);
		color: #161616;
	}

	main {
		max-width: 1200px;
		margin: 0 auto;
		padding: 3rem 1.5rem 4rem;
		display: flex;
		flex-direction: column;
		gap: 2.5rem;
	}

	.topbar {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 2rem;
	}

	.topbar h1 {
		font-size: clamp(2.5rem, 4vw, 3.5rem);
		margin: 0 0 0.5rem;
	}

	.topbar p {
		margin: 0;
		color: #4a4a4a;
	}

	.auth {
		text-align: right;
	}

	.layout {
		display: grid;
		grid-template-columns: 150px 200px minmax(0, 1fr) 260px;
		gap: 1.5rem;
	}

	.status {
		margin: 0.5rem 0 0;
		color: #2a2a2a;
		font-weight: 600;
	}

	.servers,
	.channels,
	.inspector {
		padding: 1.25rem;
		border-radius: 18px;
		background: #ffffff;
		box-shadow: 0 18px 45px rgba(20, 20, 20, 0.08);
	}

	.chat {
		padding: 1.25rem;
		border-radius: 18px;
		background: #ffffff;
		box-shadow: 0 18px 45px rgba(20, 20, 20, 0.08);
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.servers h2,
	.channels h2,
	.inspector h2,
	.chat-header h2 {
		margin: 0 0 0.75rem;
		font-size: 1rem;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #6b6b6b;
	}

	ul {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 0.75rem;
	}

	.servers li,
	.channels li {
		border-radius: 12px;
		background: #f4f4f4;
		font-weight: 600;
	}

	.list-button {
		width: 100%;
		text-align: left;
		border: none;
		background: transparent;
		font-weight: 600;
		padding: 0.5rem 0.75rem;
		cursor: pointer;
	}

	.list-button.secondary {
		background: #eef2ff;
		color: #1e3a8a;
		border-radius: 10px;
	}

	li.active {
		background: #e0e7ff;
		color: #1e3a8a;
	}

	.discover {
		margin-top: 1rem;
		display: grid;
		gap: 0.75rem;
	}

	.discover p {
		margin: 0;
		font-size: 0.85rem;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #6b6b6b;
	}

	.chat-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
	}

	.chat-header p {
		margin: 0;
		color: #6b6b6b;
	}

	.messages {
		display: grid;
		gap: 1rem;
	}

	article {
		padding: 0.75rem 1rem;
		border-radius: 14px;
		background: #f8f8f8;
	}

	article div {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	article span {
		color: #8a8a8a;
		font-size: 0.85rem;
	}

	article p {
		margin: 0.5rem 0 0;
	}

	.composer {
		display: grid;
		grid-template-columns: 1fr auto;
		gap: 0.75rem;
	}

	input {
		padding: 0.75rem 1rem;
		border-radius: 12px;
		border: 1px solid #e5e5e5;
		background: #fafafa;
	}

	button {
		border: none;
		border-radius: 12px;
		padding: 0.75rem 1.5rem;
		background: #1d4ed8;
		color: #ffffff;
		font-weight: 600;
		cursor: pointer;
	}

	button:disabled {
		background: #cbd5f5;
		cursor: not-allowed;
	}

	button:hover:not(:disabled) {
		background: #1e40af;
	}

	.error {
		color: #b91c1c;
		margin-top: 0.75rem;
	}

	.muted {
		color: #6b6b6b;
	}

	.user-card {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem;
		border-radius: 12px;
		background: #f4f4f4;
	}

	.user-card img {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		object-fit: cover;
	}

	.member-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 0.75rem;
	}

	.member-card {
		display: flex;
		gap: 0.75rem;
		align-items: center;
		padding: 0.75rem;
		border-radius: 12px;
		background: #f8f8f8;
	}

	.member-card img {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		object-fit: cover;
	}

	.member-avatar {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		display: grid;
		place-items: center;
		background: #dbeafe;
		color: #1e40af;
		font-weight: 700;
	}

	.role-badge {
		display: inline-block;
		margin-left: 0.5rem;
		padding: 0.2rem 0.5rem;
		border-radius: 999px;
		background: #e0e7ff;
		color: #1e3a8a;
		font-size: 0.7rem;
		text-transform: uppercase;
		letter-spacing: 0.08em;
	}

	.create-form-toggle {
		margin-top: 0.75rem;
	}

	.inline-form {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.inline-form input {
		padding: 0.5rem 0.75rem;
		border-radius: 10px;
		border: 1px solid #e5e5e5;
		background: #fafafa;
		font-size: 0.85rem;
	}

	.inline-form-actions {
		display: flex;
		gap: 0.5rem;
	}

	.inline-form-actions button {
		padding: 0.4rem 0.75rem;
		font-size: 0.8rem;
		border-radius: 10px;
	}

	.cancel-button {
		background: #e5e5e5;
		color: #333;
	}

	.cancel-button:hover:not(:disabled) {
		background: #d1d1d1;
	}

	@media (max-width: 1100px) {
		.layout {
			grid-template-columns: 140px 190px minmax(0, 1fr);
		}

		.inspector {
			grid-column: 1 / -1;
		}
	}

	@media (max-width: 840px) {
		.layout {
			grid-template-columns: 1fr;
		}

		.topbar {
			flex-direction: column;
			align-items: flex-start;
		}
	}
</style>
