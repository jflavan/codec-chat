<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import type { WebhookEventType } from '$lib/types/index.js';

	const servers = getServerStore();

	const allEventTypes: WebhookEventType[] = [
		'MessageCreated',
		'MessageUpdated',
		'MessageDeleted',
		'MemberJoined',
		'MemberLeft',
		'MemberRoleChanged',
		'ChannelCreated',
		'ChannelUpdated',
		'ChannelDeleted'
	];

	let nameInput = $state('');
	let urlInput = $state('');
	let secretInput = $state('');
	let selectedEvents = $state<Set<string>>(new Set());
	let showCreateForm = $state(false);

	$effect(() => {
		if (servers.selectedServerId) {
			servers.loadWebhooks();
		}
	});

	function toggleEvent(eventType: string) {
		const next = new Set(selectedEvents);
		if (next.has(eventType)) {
			next.delete(eventType);
		} else {
			next.add(eventType);
		}
		selectedEvents = next;
	}

	async function handleCreate() {
		if (!nameInput.trim() || !urlInput.trim() || selectedEvents.size === 0) return;
		await servers.createWebhook({
			name: nameInput.trim(),
			url: urlInput.trim(),
			secret: secretInput.trim() || undefined,
			eventTypes: [...selectedEvents]
		});
		nameInput = '';
		urlInput = '';
		secretInput = '';
		selectedEvents = new Set();
		showCreateForm = false;
	}

	async function handleToggleActive(webhookId: string, currentActive: boolean) {
		await servers.updateWebhook(webhookId, { isActive: !currentActive });
	}

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleDateString(undefined, {
			month: 'short',
			day: 'numeric',
			year: 'numeric'
		});
	}

	function formatEventType(type: string): string {
		return type.replace(/([A-Z])/g, ' $1').trim();
	}
</script>

<div class="server-webhooks">
	<h2 class="settings-title">Webhooks</h2>

	<section class="settings-section">
		<div class="section-header">
			<h3 class="section-title">Outgoing Webhooks</h3>
			{#if !showCreateForm}
				<button type="button" class="btn-primary" onclick={() => (showCreateForm = true)}>
					Create Webhook
				</button>
			{/if}
		</div>

		{#if showCreateForm}
			<div class="create-form">
				<div class="form-group">
					<label for="webhook-name" class="label">Name</label>
					<input
						id="webhook-name"
						type="text"
						class="input"
						placeholder="e.g. Slack notifications"
						maxlength="100"
						bind:value={nameInput}
					/>
				</div>
				<div class="form-group">
					<label for="webhook-url" class="label">Payload URL</label>
					<input
						id="webhook-url"
						type="url"
						class="input"
						placeholder="https://example.com/webhook"
						bind:value={urlInput}
					/>
				</div>
				<div class="form-group">
					<label for="webhook-secret" class="label">Secret (optional)</label>
					<input
						id="webhook-secret"
						type="password"
						class="input"
						placeholder="Used to sign payloads (HMAC-SHA256)"
						bind:value={secretInput}
					/>
				</div>
				<fieldset class="form-group events-fieldset">
					<legend class="label">Events</legend>
					<div class="event-grid">
						{#each allEventTypes as eventType}
							<label class="event-checkbox">
								<input
									type="checkbox"
									checked={selectedEvents.has(eventType)}
									onchange={() => toggleEvent(eventType)}
								/>
								<span class="event-label">{formatEventType(eventType)}</span>
							</label>
						{/each}
					</div>
				</fieldset>
				<div class="form-actions">
					<button
						type="button"
						class="btn-primary"
						disabled={servers.isCreatingWebhook ||
							!nameInput.trim() ||
							!urlInput.trim() ||
							selectedEvents.size === 0}
						onclick={handleCreate}
					>
						{servers.isCreatingWebhook ? 'Creating…' : 'Create'}
					</button>
					<button type="button" class="btn-cancel" onclick={() => (showCreateForm = false)}>
						Cancel
					</button>
				</div>
			</div>
		{/if}
	</section>

	<section class="settings-section">
		{#if servers.isLoadingWebhooks}
			<p class="muted">Loading webhooks…</p>
		{:else if servers.webhooks.length === 0}
			<p class="muted">No webhooks configured. Create one to receive event notifications.</p>
		{:else}
			<div class="webhook-list">
				{#each servers.webhooks as webhook (webhook.id)}
					<div class="webhook-card" class:inactive={!webhook.isActive}>
						<div class="webhook-header">
							<div class="webhook-info">
								<span class="webhook-name">{webhook.name}</span>
								<span class="webhook-url muted">{webhook.url}</span>
							</div>
							<div class="webhook-actions">
								<button
									type="button"
									class="btn-toggle"
									class:active={webhook.isActive}
									aria-label={webhook.isActive ? 'Disable webhook' : 'Enable webhook'}
									onclick={() => handleToggleActive(webhook.id, webhook.isActive)}
								>
									{webhook.isActive ? 'Active' : 'Inactive'}
								</button>
								<button
									type="button"
									class="btn-deliveries"
									aria-label="View deliveries for {webhook.name}"
									onclick={() => servers.loadWebhookDeliveries(webhook.id)}
								>
									Deliveries
								</button>
								<button
									type="button"
									class="btn-delete"
									aria-label="Delete webhook {webhook.name}"
									onclick={() => servers.deleteWebhook(webhook.id)}
								>
									Delete
								</button>
							</div>
						</div>
						<div class="webhook-meta">
							<span class="event-tags">
								{#each webhook.eventTypes as et}
									<span class="event-tag">{formatEventType(et)}</span>
								{/each}
							</span>
							<span class="muted">Created {formatDate(webhook.createdAt)}</span>
							{#if webhook.hasSecret}
								<span class="secret-badge">Signed</span>
							{/if}
						</div>

						{#if servers.selectedWebhookId === webhook.id}
							<div class="deliveries-panel">
								<h3 class="deliveries-title">Recent Deliveries</h3>
								{#if servers.isLoadingDeliveries}
									<p class="muted">Loading…</p>
								{:else if servers.webhookDeliveries.length === 0}
									<p class="muted">No deliveries yet.</p>
								{:else}
									<div class="delivery-table" role="table" aria-label="Recent webhook deliveries">
										<div class="delivery-header" role="row">
											<span class="col-event" role="columnheader">Event</span>
											<span class="col-status" role="columnheader">Status</span>
											<span class="col-attempt" role="columnheader">Attempt</span>
											<span class="col-time" role="columnheader">Time</span>
										</div>
										{#each servers.webhookDeliveries as delivery (delivery.id)}
											<div class="delivery-row" role="row" class:success={delivery.success} class:failure={!delivery.success}>
												<span class="col-event" role="cell">{formatEventType(delivery.eventType)}</span>
												<span class="col-status" role="cell">
													{#if delivery.success}
														<span class="status-badge success">{delivery.statusCode}</span>
													{:else if delivery.statusCode}
														<span class="status-badge failure">{delivery.statusCode}</span>
													{:else}
														<span class="status-badge failure" title={delivery.errorMessage ?? ''}>Error</span>
													{/if}
												</span>
												<span class="col-attempt" role="cell">{delivery.attempt}</span>
												<span class="col-time muted" role="cell">{formatDate(delivery.createdAt)}</span>
											</div>
										{/each}
									</div>
								{/if}
								<button
									type="button"
									class="btn-close-deliveries"
									aria-label="Close deliveries for {webhook.name}"
									onclick={() => {
										servers.selectedWebhookId = null;
										servers.webhookDeliveries = [];
									}}
								>
									Close
								</button>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		{/if}
	</section>
</div>

<style>
	.server-webhooks {
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

	.section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 16px;
	}

	.section-title {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.create-form {
		display: flex;
		flex-direction: column;
		gap: 16px;
		padding: 16px;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 4px;
	}

	.form-group {
		display: flex;
		flex-direction: column;
		gap: 6px;
	}

	.label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.input {
		padding: 8px 10px;
		background: var(--bg-primary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		transition: border-color 150ms ease;
		width: 100%;
		box-sizing: border-box;
	}

	.input:focus {
		outline: none;
		border-color: var(--accent);
	}

	.event-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
		gap: 8px;
	}

	.event-checkbox {
		display: flex;
		align-items: center;
		gap: 8px;
		cursor: pointer;
		font-size: 13px;
		color: var(--text-normal);
	}

	.event-label {
		white-space: nowrap;
	}

	.form-actions {
		display: flex;
		gap: 8px;
	}

	.btn-primary {
		padding: 8px 16px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
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

	.btn-cancel {
		padding: 8px 16px;
		background: transparent;
		color: var(--text-muted);
		border: 1px solid var(--border);
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		font-family: inherit;
	}

	.btn-cancel:hover {
		color: var(--text-normal);
		border-color: var(--text-muted);
	}

	.webhook-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.webhook-card {
		border: 1px solid var(--border);
		border-radius: 4px;
		overflow: hidden;
	}

	.webhook-card.inactive {
		opacity: 0.6;
	}

	.webhook-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 12px;
		background: var(--bg-secondary);
		gap: 12px;
	}

	.webhook-info {
		display: flex;
		flex-direction: column;
		gap: 2px;
		min-width: 0;
	}

	.webhook-name {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
	}

	.webhook-url {
		font-size: 12px;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.webhook-actions {
		display: flex;
		gap: 6px;
		flex-shrink: 0;
	}

	.btn-toggle {
		padding: 4px 10px;
		border: 1px solid var(--border);
		border-radius: 3px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
		background: transparent;
		color: var(--text-muted);
	}

	.btn-toggle.active {
		background: var(--accent);
		color: var(--bg-tertiary);
		border-color: var(--accent);
	}

	.btn-deliveries {
		padding: 4px 10px;
		background: transparent;
		color: var(--text-muted);
		border: 1px solid var(--border);
		border-radius: 3px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
	}

	.btn-deliveries:hover {
		color: var(--text-normal);
		border-color: var(--text-muted);
	}

	.btn-delete {
		padding: 4px 10px;
		background: transparent;
		color: var(--danger);
		border: 1px solid var(--danger);
		border-radius: 3px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
	}

	.btn-delete:hover {
		background: var(--danger);
		color: #fff;
	}

	.webhook-meta {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 8px 12px;
		border-top: 1px solid var(--border);
		flex-wrap: wrap;
	}

	.event-tags {
		display: flex;
		gap: 4px;
		flex-wrap: wrap;
	}

	.event-tag {
		padding: 2px 6px;
		background: var(--bg-tertiary);
		border-radius: 3px;
		font-size: 11px;
		color: var(--text-muted);
		white-space: nowrap;
	}

	.secret-badge {
		padding: 2px 6px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border-radius: 3px;
		font-size: 11px;
		font-weight: 600;
	}

	.deliveries-panel {
		padding: 12px;
		border-top: 1px solid var(--border);
		background: var(--bg-primary);
	}

	.deliveries-title {
		font-size: 13px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 8px;
	}

	.delivery-table {
		display: flex;
		flex-direction: column;
		border: 1px solid var(--border);
		border-radius: 4px;
		overflow: hidden;
		margin-bottom: 8px;
	}

	.delivery-header,
	.delivery-row {
		display: grid;
		grid-template-columns: 1fr auto auto 1fr;
		gap: 8px;
		padding: 6px 10px;
		align-items: center;
	}

	.delivery-header {
		background: var(--bg-tertiary);
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.delivery-row {
		background: var(--bg-secondary);
		border-top: 1px solid var(--border);
		font-size: 12px;
	}

	.status-badge {
		padding: 1px 6px;
		border-radius: 3px;
		font-size: 11px;
		font-weight: 600;
	}

	.status-badge.success {
		background: #2d7d46;
		color: #fff;
	}

	.status-badge.failure {
		background: var(--danger);
		color: #fff;
	}

	.btn-close-deliveries {
		padding: 4px 10px;
		background: transparent;
		color: var(--text-muted);
		border: 1px solid var(--border);
		border-radius: 3px;
		font-size: 12px;
		cursor: pointer;
		font-family: inherit;
	}

	.muted {
		color: var(--text-muted);
	}

	.events-fieldset {
		border: none;
		padding: 0;
		margin: 0;
	}

	.events-fieldset legend {
		float: left;
		width: 100%;
		margin-bottom: 6px;
	}

	@media (max-width: 600px) {
		.webhook-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.webhook-actions {
			width: 100%;
		}

		.event-grid {
			grid-template-columns: 1fr;
		}

		.delivery-header,
		.delivery-row {
			grid-template-columns: 1fr auto auto;
		}

		.col-time {
			display: none;
		}
	}
</style>
