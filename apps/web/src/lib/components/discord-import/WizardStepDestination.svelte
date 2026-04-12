<script lang="ts">
	import type { MemberServer } from '$lib/types/index.js';

	let {
		mode = $bindable(),
		newServerName = $bindable(),
		selectedServerId = $bindable(),
		ownedServers
	}: {
		mode: 'create' | 'existing';
		newServerName: string;
		selectedServerId: string | null;
		ownedServers: MemberServer[];
	} = $props();
</script>

<div class="step">
	<h2>Where do you want to import?</h2>
	<p class="subtitle">Choose whether to create a new server or import into one you already own.</p>

	<div class="options">
		<label class="option" class:selected={mode === 'create'}>
			<input type="radio" bind:group={mode} value="create" />
			<div class="option-content">
				<strong>Create a new server</strong>
				<span class="option-desc">Start fresh with your Discord content</span>
			</div>
		</label>

		<label class="option" class:selected={mode === 'existing'}>
			<input type="radio" bind:group={mode} value="existing" />
			<div class="option-content">
				<strong>Import into existing server</strong>
				<span class="option-desc">Add Discord content to a server you own</span>
			</div>
		</label>
	</div>

	{#if mode === 'create'}
		<div class="input-group">
			<label class="form-label" for="wizard-server-name">Server Name</label>
			<input
				id="wizard-server-name"
				type="text"
				class="form-input"
				placeholder="My Server"
				maxlength="100"
				bind:value={newServerName}
			/>
		</div>
	{:else}
		<div class="input-group">
			<label class="form-label" for="wizard-server-select">Select Server</label>
			{#if ownedServers.length === 0}
				<p class="no-servers">You don't own any servers yet.</p>
			{:else}
				<select id="wizard-server-select" class="form-input" bind:value={selectedServerId}>
					<option value={null} disabled>Choose a server...</option>
					{#each ownedServers as server (server.serverId)}
						<option value={server.serverId}>{server.name}</option>
					{/each}
				</select>
			{/if}
		</div>
	{/if}
</div>

<style>
	.step h2 {
		margin: 0 0 4px;
		font-size: 20px;
		color: var(--text-header);
	}

	.subtitle {
		color: var(--text-muted);
		font-size: 14px;
		margin: 0 0 20px;
		line-height: 1.4;
	}

	.options {
		display: flex;
		flex-direction: column;
		gap: 8px;
		margin-bottom: 20px;
	}

	.option {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 14px 16px;
		border: 2px solid var(--bg-tertiary);
		border-radius: 8px;
		cursor: pointer;
		transition: border-color 150ms ease, background 150ms ease;
	}

	.option:hover {
		background: var(--bg-message-hover);
	}

	.option.selected {
		border-color: var(--accent);
		background: var(--bg-message-hover);
	}

	.option input[type="radio"] {
		accent-color: var(--accent);
	}

	.option-content {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.option-content strong {
		font-size: 14px;
		color: var(--text-header);
	}

	.option-desc {
		font-size: 13px;
		color: var(--text-muted);
	}

	.input-group {
		margin-top: 4px;
	}

	.form-label {
		display: block;
		font-size: 12px;
		font-weight: 700;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
		margin-bottom: 6px;
	}

	.form-input {
		display: block;
		width: 100%;
		padding: 10px 12px;
		font-size: 14px;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: 1px solid var(--bg-tertiary);
		border-radius: 4px;
		outline: none;
		box-sizing: border-box;
	}

	.form-input:focus {
		border-color: var(--accent);
	}

	select.form-input {
		appearance: auto;
	}

	.no-servers {
		color: var(--text-muted);
		font-size: 14px;
	}
</style>
