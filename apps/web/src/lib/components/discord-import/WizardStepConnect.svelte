<script lang="ts">
	let {
		botToken = $bindable(),
		guildId = $bindable(),
		validatedGuild = $bindable()
	}: {
		botToken: string;
		guildId: string;
		validatedGuild: { name: string; icon: string | null; memberCount: number | null } | null;
	} = $props();

	// Auto-enable when both fields are filled
	$effect(() => {
		if (botToken.trim() && guildId.trim()) {
			validatedGuild = { name: '', icon: null, memberCount: null };
		} else {
			validatedGuild = null;
		}
	});
</script>

<div class="step">
	<h2>Connect to Discord</h2>
	<p class="subtitle">Enter your bot token and the ID of the Discord server you want to import.</p>

	<div class="input-group">
		<label class="form-label" for="wizard-bot-token">Bot Token</label>
		<input
			id="wizard-bot-token"
			type="password"
			class="form-input"
			placeholder="Paste the bot token from the previous step"
			bind:value={botToken}
		/>
	</div>

	<div class="input-group">
		<label class="form-label" for="wizard-guild-id">Discord Server ID</label>
		<input
			id="wizard-guild-id"
			type="text"
			class="form-input"
			placeholder="e.g. 503029308789620752"
			bind:value={guildId}
		/>
		<p class="help-text">
			Right-click your server icon in Discord, select "Copy Server ID".
			You may need to enable Developer Mode in Discord settings first.
		</p>
	</div>
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

	.help-text {
		font-size: 12px;
		color: var(--text-muted);
		margin: 6px 0 0;
		line-height: 1.4;
	}
</style>
