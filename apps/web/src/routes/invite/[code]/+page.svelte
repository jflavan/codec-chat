<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { env } from '$env/dynamic/public';
	import { ApiClient } from '$lib/api/client.js';
	import { loadStoredToken, isTokenExpired } from '$lib/auth/session.js';
	import '$lib/styles/global.css';

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';
	const code = page.params.code ?? '';

	let serverName = $state('');
	let serverIcon = $state<string | null>(null);
	let memberCount = $state(0);
	let error = $state('');
	let isLoading = $state(true);
	let isJoining = $state(false);
	let hasAuth = $state(false);

	onMount(async () => {
		if (!apiBaseUrl) {
			error = 'Application not configured.';
			isLoading = false;
			return;
		}

		const api = new ApiClient(apiBaseUrl, async () => null);

		// Check if user has a valid session
		const token = loadStoredToken();
		hasAuth = !!token && !isTokenExpired(token);

		try {
			const preview = await api.getInvitePreview(code);
			serverName = preview.serverName;
			serverIcon = preview.serverIcon;
			memberCount = preview.memberCount;
		} catch (e: unknown) {
			if (e instanceof Error) {
				error = e.message;
			} else {
				error = 'This invite link is invalid or has expired.';
			}
		} finally {
			isLoading = false;
		}
	});

	async function handleJoin() {
		if (hasAuth) {
			isJoining = true;
			const api = new ApiClient(apiBaseUrl, async () => null);
			const token = loadStoredToken()!;
			try {
				await api.joinViaInvite(token, code);
				window.location.href = '/';
			} catch (e: unknown) {
				error = e instanceof Error ? e.message : 'Failed to join server.';
				isJoining = false;
			}
		} else {
			// Store the invite code and redirect to main app for sign-in
			localStorage.setItem('pendingInviteCode', code);
			window.location.href = '/';
		}
	}
</script>

<svelte:head>
	<title>{serverName ? `Join ${serverName} — Codec` : 'Invite — Codec'}</title>
</svelte:head>

<div class="invite-page">
	<div class="invite-card">
		<div class="logo">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>

		{#if isLoading}
			<p class="status-text">Loading invite...</p>
		{:else if error}
			<div class="error-section">
				<p class="error-text">{error}</p>
				<a href="/" class="home-link">Go to Codec</a>
			</div>
		{:else}
			<p class="invite-label">You've been invited to join</p>

			<div class="server-preview">
				{#if serverIcon}
					<img class="server-icon" src={serverIcon} alt={serverName} />
				{:else}
					<div class="server-icon placeholder">{serverName.charAt(0).toUpperCase()}</div>
				{/if}
				<h1 class="server-name">{serverName}</h1>
				<p class="member-count">{memberCount} {memberCount === 1 ? 'member' : 'members'}</p>
			</div>

			<button class="join-btn" onclick={handleJoin} disabled={isJoining}>
				{#if isJoining}
					Joining...
				{:else if hasAuth}
					Accept Invite
				{:else}
					Sign in to Join
				{/if}
			</button>

			{#if !hasAuth}
				<p class="hint-text">You'll be asked to sign in or create an account first.</p>
			{/if}
		{/if}
	</div>
	<div class="scanlines"></div>
</div>

<style>
	.invite-page {
		position: fixed;
		inset: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-tertiary);
		overflow: auto;
	}

	.scanlines {
		position: fixed;
		inset: 0;
		pointer-events: none;
		background: repeating-linear-gradient(
			to bottom,
			transparent,
			transparent 2px,
			rgba(0, 0, 0, 0.15) 2px,
			rgba(0, 0, 0, 0.15) 4px
		);
	}

	.invite-card {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 20px;
		z-index: 1;
		padding: 40px 32px;
		width: 100%;
		max-width: 420px;
	}

	.logo {
		font-family: 'Space Grotesk', monospace;
		font-size: 40px;
		font-weight: 700;
		letter-spacing: 6px;
		color: var(--accent);
		text-shadow: 0 0 20px rgba(0, 255, 102, 0.5), 0 0 40px rgba(0, 255, 102, 0.2);
	}

	.bracket {
		color: var(--text-muted);
	}

	.name {
		color: var(--accent);
	}

	.invite-label {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 2px;
	}

	.server-preview {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		padding: 24px;
		width: 100%;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
	}

	.server-icon {
		width: 64px;
		height: 64px;
		border-radius: 16px;
		object-fit: cover;
	}

	.server-icon.placeholder {
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-primary);
		border: 1px solid var(--border);
		color: var(--accent);
		font-family: 'Space Grotesk', monospace;
		font-size: 28px;
		font-weight: 700;
	}

	.server-name {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 24px;
		font-weight: 700;
		color: var(--text-header);
		text-align: center;
		word-break: break-word;
	}

	.member-count {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
		color: var(--text-dim);
	}

	.join-btn {
		width: 100%;
		padding: 14px;
		border: none;
		border-radius: 4px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-family: 'Space Grotesk', monospace;
		font-size: 15px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 2px;
		cursor: pointer;
		transition: opacity 0.15s;
	}

	.join-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.join-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.hint-text {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 12px;
		color: var(--text-dim);
		text-align: center;
	}

	.status-text {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		color: var(--text-muted);
	}

	.error-section {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 16px;
	}

	.error-text {
		margin: 0;
		padding: 10px 16px;
		border-radius: 4px;
		background: rgba(255, 59, 59, 0.12);
		border: 1px solid rgba(255, 59, 59, 0.3);
		color: #ff6b6b;
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		text-align: center;
	}

	.home-link {
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
		color: var(--accent);
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.home-link:hover {
		color: var(--accent-hover);
	}
</style>
