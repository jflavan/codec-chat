<script lang="ts">
	let {
		applicationId = $bindable()
	}: {
		applicationId: string;
	} = $props();

	const inviteUrl = $derived(
		applicationId.trim()
			? `https://discord.com/oauth2/authorize?client_id=${encodeURIComponent(applicationId.trim())}&scope=bot&permissions=66560`
			: ''
	);
</script>

<div class="step">
	<h2>Set up your Discord bot</h2>
	<p class="subtitle">Follow these steps to create a bot that can read your Discord server's content.</p>

	<ol class="steps-list">
		<li>
			Go to the <a href="https://discord.com/developers/applications" target="_blank" rel="noopener">Discord Developer Portal</a> and click <strong>New Application</strong>
		</li>
		<li>
			In your application, go to <strong>Bot</strong> in the left sidebar
		</li>
		<li>
			Scroll down to <strong>Privileged Gateway Intents</strong> and enable:
			<ul>
				<li><strong>Server Members Intent</strong></li>
				<li><strong>Message Content Intent</strong></li>
			</ul>
		</li>
		<li>
			Click <strong>Reset Token</strong> to generate a bot token — copy it for the next step
		</li>
		<li>
			Copy the <strong>Application ID</strong> from the General Information page and paste it below:
		</li>
	</ol>

	<div class="input-group">
		<label class="form-label" for="wizard-app-id">Application ID</label>
		<input
			id="wizard-app-id"
			type="text"
			class="form-input"
			placeholder="e.g. 1234567890123456789"
			bind:value={applicationId}
		/>
	</div>

	{#if inviteUrl}
		<div class="invite-section">
			<p class="invite-label">Click this link to add the bot to your Discord server:</p>
			<a href={inviteUrl} target="_blank" rel="noopener" class="invite-link">
				Add Bot to Discord Server
			</a>
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
		margin: 0 0 16px;
		line-height: 1.4;
	}

	.steps-list {
		margin: 0 0 20px;
		padding-left: 20px;
		display: flex;
		flex-direction: column;
		gap: 10px;
		color: var(--text-normal);
		font-size: 14px;
		line-height: 1.5;
	}

	.steps-list a {
		color: var(--accent);
		text-decoration: none;
	}

	.steps-list a:hover {
		text-decoration: underline;
	}

	.steps-list ul {
		margin: 4px 0 0;
		padding-left: 20px;
	}

	.input-group {
		margin-bottom: 16px;
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

	.invite-section {
		padding: 12px 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
	}

	.invite-label {
		font-size: 13px;
		color: var(--text-muted);
		margin: 0 0 8px;
	}

	.invite-link {
		display: inline-block;
		padding: 8px 16px;
		background: var(--accent);
		color: #fff;
		font-size: 14px;
		font-weight: 600;
		border-radius: 4px;
		text-decoration: none;
	}

	.invite-link:hover {
		opacity: 0.9;
	}
</style>
