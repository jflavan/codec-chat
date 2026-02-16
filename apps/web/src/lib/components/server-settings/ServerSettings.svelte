<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let serverNameEdit = $state('');
	let isEditingServerName = $state(false);
	let channelEditId = $state<string | null>(null);
	let channelEditName = $state('');

	function startEditingServerName() {
		serverNameEdit = app.selectedServerName;
		isEditingServerName = true;
	}

	function cancelEditingServerName() {
		isEditingServerName = false;
		serverNameEdit = '';
	}

	async function saveServerName() {
		if (!serverNameEdit.trim()) return;
		await app.updateServerName(serverNameEdit);
		isEditingServerName = false;
		serverNameEdit = '';
	}

	function startEditingChannel(channelId: string, currentName: string) {
		channelEditId = channelId;
		channelEditName = currentName;
	}

	function cancelEditingChannel() {
		channelEditId = null;
		channelEditName = '';
	}

	async function saveChannelName(channelId: string) {
		if (!channelEditName.trim()) return;
		await app.updateChannelName(channelId, channelEditName);
		channelEditId = null;
		channelEditName = '';
	}
</script>

<div class="server-settings">
	<h1 class="settings-title">Server Settings</h1>

	<!-- Server Overview Section -->
	<section class="settings-section">
		<h2 class="section-title">Server Overview</h2>
		
		<div class="form-group">
			<label for="server-name" class="label">Server Name</label>
			{#if isEditingServerName}
				<div class="inline-edit">
					<input
						id="server-name"
						type="text"
						class="input"
						bind:value={serverNameEdit}
						maxlength="100"
						disabled={app.isUpdatingServerName}
						onkeydown={(e) => {
							if (e.key === 'Enter') saveServerName();
							if (e.key === 'Escape') cancelEditingServerName();
						}}
					/>
					<div class="inline-actions">
						<button
							type="button"
							class="btn-primary"
							disabled={app.isUpdatingServerName || !serverNameEdit.trim()}
							onclick={() => saveServerName()}
						>
							{app.isUpdatingServerName ? 'Saving...' : 'Save'}
						</button>
						<button
							type="button"
							class="btn-secondary"
							disabled={app.isUpdatingServerName}
							onclick={cancelEditingServerName}
						>
							Cancel
						</button>
					</div>
				</div>
			{:else}
				<div class="display-field">
					<span class="field-value">{app.selectedServerName}</span>
					{#if app.canManageChannels}
						<button type="button" class="btn-edit" onclick={startEditingServerName}>
							Edit
						</button>
					{/if}
				</div>
			{/if}
		</div>
	</section>

	<!-- Channel Management Section -->
	<section class="settings-section">
		<h2 class="section-title">Channels</h2>
		<div class="channel-list">
			{#each app.channels as channel (channel.id)}
				<div class="channel-item">
					{#if channelEditId === channel.id}
						<div class="inline-edit">
							<span class="channel-hash">#</span>
							<input
								type="text"
								class="input"
								bind:value={channelEditName}
								maxlength="100"
								disabled={app.isUpdatingChannelName}
								onkeydown={(e) => {
									if (e.key === 'Enter') saveChannelName(channel.id);
									if (e.key === 'Escape') cancelEditingChannel();
								}}
							/>
							<div class="inline-actions">
								<button
									type="button"
									class="btn-primary"
									disabled={app.isUpdatingChannelName || !channelEditName.trim()}
									onclick={() => saveChannelName(channel.id)}
								>
									{app.isUpdatingChannelName ? '…' : 'Save'}
								</button>
								<button
									type="button"
									class="btn-secondary"
									disabled={app.isUpdatingChannelName}
									onclick={cancelEditingChannel}
								>
									Cancel
								</button>
							</div>
						</div>
					{:else}
						<div class="channel-display">
							<span class="channel-hash">#</span>
							<span class="channel-name">{channel.name}</span>
							{#if app.canManageChannels}
								<button
									type="button"
									class="btn-edit"
									onclick={() => startEditingChannel(channel.id, channel.name)}
								>
									Edit
								</button>
							{/if}
						</div>
					{/if}
				</div>
			{/each}
		</div>
	</section>

	<!-- Member Management Section -->
	<section class="settings-section">
		<h2 class="section-title">Members</h2>
		<p class="section-description">
			Server members are managed from the Members sidebar. 
			{#if app.canKickMembers}
				You can kick members by clicking on them in the member list.
			{/if}
		</p>
		<div class="member-count">
			<strong>{app.members.length}</strong> member{app.members.length === 1 ? '' : 's'}
		</div>
	</section>
</div>

<style>
	.server-settings {
		max-width: 600px;
	}

	.settings-title {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 24px;
	}

	.settings-section {
		margin-bottom: 32px;
		padding-bottom: 32px;
		border-bottom: 1px solid var(--border);
	}

	.settings-section:last-child {
		border-bottom: none;
	}

	.section-title {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 16px;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.section-description {
		color: var(--text-muted);
		font-size: 14px;
		margin-bottom: 12px;
		line-height: 1.5;
	}

	.form-group {
		margin-bottom: 16px;
	}

	.label {
		display: block;
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
		margin-bottom: 8px;
	}

	.display-field {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.field-value {
		font-size: 16px;
		color: var(--text-normal);
		flex: 1;
	}

	.input {
		width: 100%;
		padding: 10px 12px;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		transition: border-color 150ms ease;
	}

	.input:focus {
		outline: none;
		border-color: var(--accent);
	}

	.input:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.inline-edit {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.inline-actions {
		display: flex;
		gap: 8px;
	}

	.btn-primary {
		padding: 8px 16px;
		background: var(--accent);
		color: white;
		border: none;
		border-radius: 4px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.btn-primary:hover:not(:disabled) {
		opacity: 0.9;
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		padding: 8px 16px;
		background: var(--bg-secondary);
		color: var(--text-normal);
		border: 1px solid var(--border);
		border-radius: 4px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background-color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		background: var(--bg-message-hover);
	}

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-edit {
		padding: 6px 12px;
		background: none;
		color: var(--accent);
		border: 1px solid var(--accent);
		border-radius: 4px;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.btn-edit:hover {
		background: var(--accent);
		color: white;
	}

	.channel-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.channel-item {
		padding: 8px 0;
	}

	.channel-display {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.channel-hash {
		color: var(--text-muted);
		font-weight: 600;
		flex-shrink: 0;
	}

	.channel-name {
		font-size: 15px;
		color: var(--text-normal);
		flex: 1;
	}

	.member-count {
		padding: 12px 16px;
		background: var(--bg-secondary);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
	}

	@media (max-width: 899px) {
		.inline-actions {
			flex-direction: column;
		}

		.btn-primary,
		.btn-secondary {
			width: 100%;
		}
	}
</style>
