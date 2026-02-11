<script lang="ts">
	import { onMount } from 'svelte';
	import { env } from '$env/dynamic/public';

	type Server = { id: string; name: string };
	type Channel = { id: string; name: string; serverId: string };
	type Message = { id: string; authorName: string; body: string; createdAt: string; channelId: string };

	let servers: Server[] = [];
	let channels: Channel[] = [];
	let messages: Message[] = [];

	let selectedServerId: string | null = null;
	let selectedChannelId: string | null = null;
	let messageBody = '';

	let idToken: string | null = null;
	let status = 'Signed out';
	let error: string | null = null;
	let meResponse: string | null = null;

	const apiBaseUrl = env.PUBLIC_API_BASE_URL;

	function setError(message: string) {
		error = message;
		meResponse = null;
	}

	function formatTime(value: string) {
		const date = new Date(value);
		if (Number.isNaN(date.getTime())) {
			return '';
		}
		return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	async function loadServers() {
		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			return;
		}

		const response = await fetch(`${apiBaseUrl}/servers`);
		if (!response.ok) {
			setError(`API error: ${response.status}`);
			return;
		}

		servers = await response.json();
		selectedServerId = servers[0]?.id ?? null;
		if (selectedServerId) {
			await loadChannels(selectedServerId);
		}
	}

	async function loadChannels(serverId: string) {
		const response = await fetch(`${apiBaseUrl}/servers/${serverId}/channels`);
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
	}

	async function loadMessages(channelId: string) {
		const response = await fetch(`${apiBaseUrl}/channels/${channelId}/messages`);
		if (!response.ok) {
			setError(`API error: ${response.status}`);
			return;
		}

		messages = await response.json();
	}

	async function selectServer(serverId: string) {
		selectedServerId = serverId;
		await loadChannels(serverId);
	}

	async function selectChannel(channelId: string) {
		selectedChannelId = channelId;
		await loadMessages(channelId);
	}

	async function callMe() {
		error = null;
		meResponse = null;

		if (!idToken) {
			setError('Sign in with Google first.');
			return;
		}

		if (!apiBaseUrl) {
			setError('Missing PUBLIC_API_BASE_URL.');
			return;
		}

		const response = await fetch(`${apiBaseUrl}/me`, {
			headers: {
				Authorization: `Bearer ${idToken}`
			}
		});

		if (!response.ok) {
			setError(`API error: ${response.status}`);
			return;
		}

		meResponse = JSON.stringify(await response.json(), null, 2);
	}

	async function sendMessage() {
		if (!idToken) {
			setError('Sign in with Google first.');
			return;
		}

		if (!selectedChannelId) {
			setError('Select a channel first.');
			return;
		}

		const body = messageBody.trim();
		if (!body) {
			setError('Message body is required.');
			return;
		}

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
	}

	onMount(() => {
		loadServers();

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
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
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
				{#each servers as server}
					<li class:active={server.id === selectedServerId}>
						<button class="list-button" on:click={() => selectServer(server.id)}>
							{server.name}
						</button>
					</li>
				{/each}
			</ul>
		</aside>

		<aside class="channels">
			<h2>Channels</h2>
			<ul>
				{#each channels as channel}
					<li class:active={channel.id === selectedChannelId}>
						<button class="list-button" on:click={() => selectChannel(channel.id)}>
							#{channel.name}
						</button>
					</li>
				{/each}
			</ul>
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
				<button on:click={callMe}>Call /me</button>
			</div>

			<div class="messages">
				{#if messages.length === 0}
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
					disabled={!selectedChannelId}
				/>
				<button on:click={sendMessage} disabled={!selectedChannelId || !messageBody.trim()}>
					Send
				</button>
			</div>
		</section>

		<aside class="inspector">
			<h2>Auth status</h2>
			{#if error}
				<p class="error">{error}</p>
			{/if}
			{#if meResponse}
				<pre>{meResponse}</pre>
			{:else}
				<p class="muted">Call /me to see the authenticated response.</p>
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

	li.active {
		background: #e0e7ff;
		color: #1e3a8a;
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

	pre {
		background: #0f172a;
		color: #f8fafc;
		padding: 1rem;
		border-radius: 12px;
		overflow: auto;
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
