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
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&display=swap"
		rel="stylesheet"
	/>
</svelte:head>

<div class="app-shell">
	<!-- Server sidebar (narrow icon rail) -->
	<nav class="server-sidebar" aria-label="Servers">
		<div class="server-list">
			<div class="server-icon home-icon" aria-label="Home">
				<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M2.3 7.7l9-5.4a1.2 1.2 0 0 1 1.4 0l9 5.4a.6.6 0 0 1-.3 1.1H19v8.6a.6.6 0 0 1-.6.6h-3.8v-5a1 1 0 0 0-1-1h-3.2a1 1 0 0 0-1 1v5H5.6a.6.6 0 0 1-.6-.6V8.8H2.6a.6.6 0 0 1-.3-1.1z"/>
				</svg>
			</div>

			<div class="server-separator" role="separator"></div>

			{#if !isSignedIn}
				<p class="muted server-hint">Sign in</p>
			{:else if isLoadingServers}
				<p class="muted server-hint">…</p>
			{:else}
				{#each servers as server}
					<div class="server-pill-wrapper">
						<div class="server-pill" class:active={server.serverId === selectedServerId}></div>
						<button
							class="server-icon"
							class:active={server.serverId === selectedServerId}
							onclick={() => selectServer(server.serverId)}
							aria-label="Server: {server.name}"
							title={server.name}
						>
							{server.name.slice(0, 1).toUpperCase()}
						</button>
					</div>
				{/each}
			{/if}

			{#if isSignedIn}
				{#if showCreateServer}
					<div class="server-create-popover">
						<form class="inline-form" onsubmit={(e) => { e.preventDefault(); createServer(); }}>
							<input
								type="text"
								placeholder="Server name"
								maxlength="100"
								bind:value={newServerName}
								disabled={isCreatingServer}
							/>
							<div class="inline-form-actions">
								<button type="submit" class="btn-primary" disabled={isCreatingServer || !newServerName.trim()}>
									{isCreatingServer ? '…' : 'Create'}
								</button>
								<button type="button" class="btn-secondary" onclick={() => { showCreateServer = false; newServerName = ''; }}>Cancel</button>
							</div>
						</form>
					</div>
				{:else}
					<button
						class="server-icon add-server"
						aria-label="Create a server"
						title="Create a server"
						onclick={() => { showCreateServer = true; }}
					>
						<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
							<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
						</svg>
					</button>
				{/if}
			{/if}

			{#if isSignedIn && discoverServers.some((server) => !server.isMember)}
				<div class="server-separator" role="separator"></div>
				{#if isLoadingDiscover}
					<p class="muted server-hint">…</p>
				{:else}
					{#each discoverServers as server}
						{#if !server.isMember}
							<button
								class="server-icon discover-icon"
								onclick={() => joinServer(server.id)}
								disabled={isJoining}
								aria-label="Join {server.name}"
								title="Join {server.name}"
							>
								{server.name.slice(0, 1).toUpperCase()}
							</button>
						{/if}
					{/each}
				{/if}
			{/if}
		</div>
	</nav>

	<!-- Channel sidebar -->
	<aside class="channel-sidebar" aria-label="Channels">
		<div class="channel-header">
			<h2 class="server-name">
				{#if selectedServerId}
					{servers.find((s) => s.serverId === selectedServerId)?.name ?? 'Server'}
				{:else}
					Codec
				{/if}
			</h2>
		</div>

		<div class="channel-list-scroll">
			<div class="channel-category">
				<span class="category-label">Text Channels</span>
				{#if canManageChannels && selectedServerId}
					{#if showCreateChannel}
						<!-- inline create form replaces button -->
					{:else}
						<button class="category-action" aria-label="Create channel" onclick={() => { showCreateChannel = true; }}>
							<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
								<path d="M8 2a1 1 0 0 1 1 1v4h4a1 1 0 1 1 0 2H9v4a1 1 0 1 1-2 0V9H3a1 1 0 0 1 0-2h4V3a1 1 0 0 1 1-1z"/>
							</svg>
						</button>
					{/if}
				{/if}
			</div>

			<ul class="channel-list" role="list">
				{#if isLoadingChannels}
					<li class="muted channel-item">Loading…</li>
				{:else if channels.length === 0}
					<li class="muted channel-item">No channels yet.</li>
				{:else}
					{#each channels as channel}
						<li>
							<button
								class="channel-item"
								class:active={channel.id === selectedChannelId}
								onclick={() => selectChannel(channel.id)}
							>
								<svg class="channel-hash" width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
									<path d="M5.88 21 7.1 14H3.5l.3-2h3.6l.9-5H4.8l.3-2h3.5L9.8 3h2l-1.2 2h5L16.8 3h2l-1.2 2H21l-.3 2h-3.5l-.9 5h3.5l-.3 2h-3.6L14.7 21h-2l1.2-7h-5L7.7 21h-2zm4.3-9h5l.9-5h-5l-.9 5z"/>
								</svg>
								<span>{channel.name}</span>
							</button>
						</li>
					{/each}
				{/if}
			</ul>

			{#if canManageChannels && selectedServerId && showCreateChannel}
				<form class="inline-form channel-create-form" onsubmit={(e) => { e.preventDefault(); createChannel(); }}>
					<input
						type="text"
						placeholder="new-channel"
						maxlength="100"
						bind:value={newChannelName}
						disabled={isCreatingChannel}
					/>
					<div class="inline-form-actions">
						<button type="submit" class="btn-primary" disabled={isCreatingChannel || !newChannelName.trim()}>
							{isCreatingChannel ? '…' : 'Create'}
						</button>
						<button type="button" class="btn-secondary" onclick={() => { showCreateChannel = false; newChannelName = ''; }}>Cancel</button>
					</div>
				</form>
			{/if}
		</div>

		<!-- User panel at bottom -->
		<div class="user-panel">
			{#if me}
				<div class="user-panel-info">
					{#if me.user.avatarUrl}
						<img class="user-panel-avatar" src={me.user.avatarUrl} alt="Your avatar" />
					{:else}
						<div class="user-panel-avatar placeholder" aria-hidden="true">
							{me.user.displayName.slice(0, 1).toUpperCase()}
						</div>
					{/if}
					<div class="user-panel-names">
						<span class="user-panel-display">{me.user.displayName}</span>
						{#if currentServerRole}
							<span class="user-panel-role">{currentServerRole}</span>
						{/if}
					</div>
				</div>
			{:else}
				<div id="google-button" class="google-button"></div>
				<span class="user-panel-status">{status}</span>
			{/if}
		</div>
	</aside>

	<!-- Main chat area -->
	<main class="chat-main" aria-label="Chat">
		<header class="chat-header">
			<div class="chat-header-left">
				<svg class="channel-hash" width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M5.88 21 7.1 14H3.5l.3-2h3.6l.9-5H4.8l.3-2h3.5L9.8 3h2l-1.2 2h5L16.8 3h2l-1.2 2H21l-.3 2h-3.5l-.9 5h3.5l-.3 2h-3.6L14.7 21h-2l1.2-7h-5L7.7 21h-2zm4.3-9h5l.9-5h-5l-.9 5z"/>
				</svg>
				<h1 class="chat-channel-name">
					{#if selectedChannelId}
						{channels.find((c) => c.id === selectedChannelId)?.name ?? 'channel'}
					{:else}
						Select a channel
					{/if}
				</h1>
			</div>
		</header>

		{#if error}
			<div class="error-banner" role="alert">{error}</div>
		{/if}

		<div class="message-feed">
			{#if isLoadingMessages}
				<p class="muted feed-status">Loading messages…</p>
			{:else if messages.length === 0}
				<p class="muted feed-status">No messages yet. Start the conversation!</p>
			{:else}
				{#each messages as message, i}
					{@const prevMessage = i > 0 ? messages[i - 1] : null}
					{@const isGrouped = prevMessage?.authorUserId === message.authorUserId && prevMessage?.authorName === message.authorName}
					<article class="message" class:grouped={isGrouped}>
						{#if !isGrouped}
							<div class="message-avatar-col">
								<div class="message-avatar" aria-hidden="true">
									{message.authorName.slice(0, 1).toUpperCase()}
								</div>
							</div>
							<div class="message-content">
								<div class="message-header">
									<strong class="message-author">{message.authorName}</strong>
									<time class="message-time">{formatTime(message.createdAt)}</time>
								</div>
								<p class="message-body">{message.body}</p>
							</div>
						{:else}
							<div class="message-avatar-col">
								<time class="message-time-inline">{formatTime(message.createdAt)}</time>
							</div>
							<div class="message-content">
								<p class="message-body">{message.body}</p>
							</div>
						{/if}
					</article>
				{/each}
			{/if}
		</div>

		<form class="composer" onsubmit={(e) => { e.preventDefault(); sendMessage(); }}>
			<input
				class="composer-input"
				type="text"
				placeholder={selectedChannelId ? `Message #${channels.find((c) => c.id === selectedChannelId)?.name ?? 'channel'}` : 'Select a channel…'}
				bind:value={messageBody}
				disabled={!selectedChannelId || isSending}
			/>
			<button
				class="composer-send"
				type="submit"
				disabled={!selectedChannelId || !messageBody.trim() || isSending}
				aria-label="Send message"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
				</svg>
			</button>
		</form>
	</main>

	<!-- Members sidebar -->
	<aside class="members-sidebar" aria-label="Members">
		{#if !isSignedIn}
			<p class="muted sidebar-status">Sign in to see members.</p>
		{:else if !selectedServerId}
			<p class="muted sidebar-status">Select a server.</p>
		{:else if isLoadingMembers}
			<p class="muted sidebar-status">Loading members…</p>
		{:else if members.length === 0}
			<p class="muted sidebar-status">No members yet.</p>
		{:else}
			{@const owners = members.filter((m) => m.role === 'Owner')}
			{@const admins = members.filter((m) => m.role === 'Admin')}
			{@const regulars = members.filter((m) => m.role === 'Member')}

			{#if owners.length > 0}
				<h3 class="member-group-heading">Owner — {owners.length}</h3>
				<ul class="member-list" role="list">
					{#each owners as member}
						<li class="member-item">
							{#if member.avatarUrl}
								<img class="member-avatar-img" src={member.avatarUrl} alt="" />
							{:else}
								<div class="member-avatar-placeholder" aria-hidden="true">
									{member.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<span class="member-name">{member.displayName}</span>
						</li>
					{/each}
				</ul>
			{/if}

			{#if admins.length > 0}
				<h3 class="member-group-heading">Admin — {admins.length}</h3>
				<ul class="member-list" role="list">
					{#each admins as member}
						<li class="member-item">
							{#if member.avatarUrl}
								<img class="member-avatar-img" src={member.avatarUrl} alt="" />
							{:else}
								<div class="member-avatar-placeholder" aria-hidden="true">
									{member.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<span class="member-name">{member.displayName}</span>
						</li>
					{/each}
				</ul>
			{/if}

			{#if regulars.length > 0}
				<h3 class="member-group-heading">Other — {regulars.length}</h3>
				<ul class="member-list" role="list">
					{#each regulars as member}
						<li class="member-item">
							{#if member.avatarUrl}
								<img class="member-avatar-img" src={member.avatarUrl} alt="" />
							{:else}
								<div class="member-avatar-placeholder" aria-hidden="true">
									{member.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<span class="member-name">{member.displayName}</span>
						</li>
					{/each}
				</ul>
			{/if}
		{/if}
	</aside>
</div>

<style>
	/* ===== Design tokens ===== */
	:global(:root) {
		--bg-primary: #313338;
		--bg-secondary: #2b2d31;
		--bg-tertiary: #1e1f22;
		--bg-message-hover: #2e3035;
		--accent: #5865f2;
		--accent-hover: #4752c4;
		--text-normal: #dbdee1;
		--text-muted: #949ba4;
		--text-header: #f2f3f5;
		--danger: #da373c;
		--success: #23a559;
		--border: #3f4147;
	}

	:global(body) {
		margin: 0;
		font-family: 'Space Grotesk', 'Segoe UI', system-ui, sans-serif;
		background: var(--bg-tertiary);
		color: var(--text-normal);
		font-size: 15px;
		line-height: 1.375;
	}

	/* ===== App shell - full viewport grid ===== */
	.app-shell {
		display: grid;
		grid-template-columns: 72px 240px minmax(0, 1fr) 240px;
		height: 100vh;
		overflow: hidden;
	}

	/* ===== Server sidebar (icon rail) ===== */
	.server-sidebar {
		background: var(--bg-tertiary);
		display: flex;
		flex-direction: column;
		align-items: center;
		padding: 12px 0 12px;
		overflow-y: auto;
		scrollbar-width: none;
	}

	.server-sidebar::-webkit-scrollbar {
		display: none;
	}

	.server-list {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		width: 100%;
	}

	.server-pill-wrapper {
		position: relative;
		display: flex;
		align-items: center;
		justify-content: center;
		width: 100%;
	}

	.server-pill {
		position: absolute;
		left: 0;
		width: 4px;
		border-radius: 0 4px 4px 0;
		background: var(--text-header);
		height: 8px;
		opacity: 0;
		transition: height 150ms ease, opacity 150ms ease;
	}

	.server-pill-wrapper:hover .server-pill:not(.active) {
		opacity: 1;
		height: 20px;
	}

	.server-pill.active {
		opacity: 1;
		height: 36px;
	}

	.server-icon {
		width: 48px;
		height: 48px;
		border-radius: 50%;
		border: none;
		background: var(--bg-primary);
		color: var(--text-header);
		font-size: 18px;
		font-weight: 600;
		display: grid;
		place-items: center;
		cursor: pointer;
		transition: border-radius 200ms ease, background-color 200ms ease, color 200ms ease;
		font-family: inherit;
	}

	.server-icon:hover {
		border-radius: 16px;
		background: var(--accent);
		color: #fff;
	}

	.server-icon.active {
		border-radius: 16px;
		background: var(--accent);
		color: #fff;
	}

	.home-icon {
		background: var(--bg-primary);
		color: var(--text-header);
	}

	.home-icon:hover {
		background: var(--accent);
		color: #fff;
	}

	.add-server {
		background: var(--bg-primary);
		color: var(--success);
	}

	.add-server:hover {
		background: var(--success);
		color: #fff;
		border-radius: 16px;
	}

	.discover-icon {
		background: var(--bg-primary);
		color: var(--success);
		border: 2px dashed var(--border);
		width: 44px;
		height: 44px;
	}

	.discover-icon:hover {
		border-color: var(--success);
		background: var(--success);
		color: #fff;
		border-radius: 16px;
	}

	.discover-icon:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.server-separator {
		width: 32px;
		height: 2px;
		background: var(--border);
		border-radius: 1px;
		margin: 4px 0;
	}

	.server-hint {
		font-size: 10px;
		text-align: center;
		margin: 0;
	}

	.server-create-popover {
		position: absolute;
		left: 76px;
		top: 50%;
		transform: translateY(-50%);
		z-index: 40;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		padding: 12px;
		width: 220px;
		box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
	}

	/* ===== Channel sidebar ===== */
	.channel-sidebar {
		background: var(--bg-secondary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.channel-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--bg-tertiary);
		flex-shrink: 0;
	}

	.server-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.channel-list-scroll {
		flex: 1;
		overflow-y: auto;
		padding: 8px 8px 16px;
		scrollbar-width: thin;
		scrollbar-color: var(--bg-tertiary) transparent;
	}

	.channel-category {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 16px 8px 4px;
	}

	.category-label {
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.category-action {
		background: none;
		border: none;
		padding: 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		border-radius: 3px;
		width: 18px;
		height: 18px;
	}

	.category-action:hover {
		color: var(--text-header);
	}

	.channel-list {
		list-style: none;
		padding: 0;
		margin: 2px 0 0;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.channel-item {
		display: flex;
		align-items: center;
		gap: 6px;
		width: 100%;
		padding: 6px 8px;
		border-radius: 4px;
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-size: 15px;
		font-weight: 500;
		cursor: pointer;
		text-align: left;
		font-family: inherit;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.channel-item:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.channel-item.active {
		background: var(--bg-message-hover);
		color: var(--text-header);
		font-weight: 600;
	}

	.channel-hash {
		flex-shrink: 0;
		color: var(--text-muted);
		opacity: 0.7;
	}

	.channel-create-form {
		padding: 8px;
	}

	/* ===== User panel (bottom of channel sidebar) ===== */
	.user-panel {
		flex-shrink: 0;
		padding: 8px;
		background: var(--bg-tertiary);
		border-top: 1px solid var(--border);
		display: flex;
		align-items: center;
		gap: 8px;
		min-height: 52px;
	}

	.user-panel-info {
		display: flex;
		align-items: center;
		gap: 8px;
		overflow: hidden;
	}

	.user-panel-avatar {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.user-panel-avatar.placeholder {
		background: var(--accent);
		color: #fff;
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
	}

	.user-panel-names {
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.user-panel-display {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.user-panel-role {
		font-size: 12px;
		color: var(--text-muted);
	}

	.user-panel-status {
		font-size: 12px;
		color: var(--text-muted);
	}

	/* ===== Main chat area ===== */
	.chat-main {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.chat-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--bg-tertiary);
		flex-shrink: 0;
	}

	.chat-header-left {
		display: flex;
		align-items: center;
		gap: 6px;
	}

	.chat-channel-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.error-banner {
		padding: 8px 16px;
		background: var(--danger);
		color: #fff;
		font-size: 14px;
		flex-shrink: 0;
	}

	/* ===== Message feed ===== */
	.message-feed {
		flex: 1;
		overflow-y: auto;
		padding: 16px 0 8px;
		scrollbar-width: thin;
		scrollbar-color: var(--bg-tertiary) transparent;
	}

	.feed-status {
		padding: 16px;
		text-align: center;
	}

	.message {
		display: grid;
		grid-template-columns: 56px 1fr;
		padding: 2px 16px;
		transition: background-color 150ms ease;
	}

	.message:hover {
		background: var(--bg-message-hover);
	}

	.message:not(.grouped) {
		margin-top: 16px;
	}

	.message.grouped {
		margin-top: 0;
	}

	.message-avatar-col {
		display: flex;
		justify-content: center;
		padding-top: 2px;
	}

	.message-avatar {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: var(--accent);
		color: #fff;
		font-weight: 700;
		font-size: 16px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.message-content {
		min-width: 0;
	}

	.message-header {
		display: flex;
		align-items: baseline;
		gap: 8px;
	}

	.message-author {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.message-time {
		font-size: 12px;
		color: var(--text-muted);
	}

	.message-time-inline {
		font-size: 11px;
		color: transparent;
		text-align: center;
		width: 100%;
		display: block;
	}

	.message:hover .message-time-inline {
		color: var(--text-muted);
	}

	.message-body {
		margin: 2px 0 0;
		color: var(--text-normal);
		line-height: 1.375;
		word-break: break-word;
	}

	/* ===== Composer ===== */
	.composer {
		flex-shrink: 0;
		padding: 0 16px 24px;
		display: flex;
		align-items: center;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-input {
		flex: 1;
		padding: 12px 16px;
		border-radius: 8px 0 0 8px;
		border: none;
		background: var(--bg-tertiary);
		color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		outline: none;
		min-height: 20px;
	}

	.composer-input::placeholder {
		color: var(--text-muted);
	}

	.composer-input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.composer-input:disabled {
		opacity: 0.5;
	}

	.composer-send {
		background: var(--bg-tertiary);
		border: none;
		padding: 12px 12px;
		border-radius: 0 8px 8px 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-send:hover:not(:disabled) {
		color: var(--accent);
		background: var(--bg-tertiary);
	}

	.composer-send:disabled {
		opacity: 0.3;
		cursor: not-allowed;
		background: var(--bg-tertiary);
	}

	/* ===== Members sidebar ===== */
	.members-sidebar {
		background: var(--bg-secondary);
		padding: 16px 8px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--bg-tertiary) transparent;
	}

	.sidebar-status {
		padding: 8px;
		font-size: 13px;
		text-align: center;
	}

	.member-group-heading {
		padding: 16px 8px 4px;
		margin: 0;
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.member-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
	}

	.member-item {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 6px 8px;
		border-radius: 4px;
		cursor: default;
		transition: background-color 150ms ease;
	}

	.member-item:hover {
		background: var(--bg-message-hover);
	}

	.member-avatar-img {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.member-avatar-placeholder {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		background: var(--accent);
		color: #fff;
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.member-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.member-item:hover .member-name {
		color: var(--text-normal);
	}

	/* ===== Shared form styles ===== */
	.inline-form {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.inline-form input {
		padding: 8px 10px;
		border-radius: 4px;
		border: none;
		background: var(--bg-tertiary);
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		outline: none;
	}

	.inline-form input::placeholder {
		color: var(--text-muted);
	}

	.inline-form input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.inline-form-actions {
		display: flex;
		gap: 6px;
	}

	.btn-primary {
		border: none;
		border-radius: 3px;
		padding: 6px 14px;
		background: var(--accent);
		color: #fff;
		font-weight: 600;
		font-size: 13px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		border: none;
		border-radius: 3px;
		padding: 6px 14px;
		background: transparent;
		color: var(--text-muted);
		font-weight: 500;
		font-size: 13px;
		cursor: pointer;
		font-family: inherit;
		transition: color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		color: var(--text-header);
		background: transparent;
	}

	/* ===== Utility ===== */
	.muted {
		color: var(--text-muted);
	}

	/* ===== Accessibility ===== */
	:global(:focus-visible) {
		outline: 2px solid var(--accent);
		outline-offset: 2px;
	}

	@media (prefers-reduced-motion: reduce) {
		:global(*) {
			transition-duration: 0ms !important;
		}
	}

	/* ===== Responsive ===== */
	@media (max-width: 1199px) {
		.app-shell {
			grid-template-columns: 72px 240px minmax(0, 1fr);
		}

		.members-sidebar {
			display: none;
		}
	}

	@media (max-width: 899px) {
		.app-shell {
			grid-template-columns: 1fr;
			grid-template-rows: 1fr;
		}

		.server-sidebar,
		.channel-sidebar,
		.members-sidebar {
			display: none;
		}

		.chat-main {
			height: 100vh;
		}
	}
</style>
