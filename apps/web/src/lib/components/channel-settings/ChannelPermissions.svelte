<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getChannelStore } from '$lib/state/channel-store.svelte.js';
	import { Permission, hasPermission, type ChannelPermissionOverride } from '$lib/types/models.js';

	const auth = getAuthStore();
	const servers = getServerStore();
	const channelStore = getChannelStore();

	let { channelId }: { channelId: string } = $props();

	let overrides = $state<ChannelPermissionOverride[]>([]);
	let selectedRoleId = $state<string | null>(null);
	let isLoading = $state(false);
	let isSaving = $state(false);
	let errorMessage = $state<string | null>(null);

	/** Load overrides whenever the channelId changes. */
	$effect(() => {
		if (channelId) {
			loadOverrides();
		}
	});

	async function loadOverrides(): Promise<void> {
		isLoading = true;
		errorMessage = null;
		try {
			overrides = await channelStore.getChannelOverrides(channelId);
		} catch {
			overrides = [];
			errorMessage = 'Failed to load permission overrides. You may not have permission to manage this channel.';
		} finally {
			isLoading = false;
		}
	}

	/** The override for the currently selected role, if any. */
	const selectedOverride = $derived(
		selectedRoleId ? overrides.find((o) => o.roleId === selectedRoleId) ?? null : null
	);

	/** Current allow bitmask for the selected role (0 if none). */
	const currentAllow = $derived(selectedOverride?.allow ?? 0);
	/** Current deny bitmask for the selected role (0 if none). */
	const currentDeny = $derived(selectedOverride?.deny ?? 0);

	/** Check if a single flag is set in a bitmask, handling values beyond 32-bit range. */
	function hasBit(mask: number, flag: number): boolean {
		if (flag > (1 << 30) || mask > (1 << 30)) {
			return Math.floor(mask / flag) % 2 === 1;
		}
		return (mask & flag) === flag;
	}

	/** Set a flag in a bitmask, handling values beyond 32-bit range via float arithmetic. */
	function setBit(mask: number, flag: number): number {
		if (hasBit(mask, flag)) return mask;
		return flag > (1 << 30) || mask > (1 << 30) ? mask + flag : mask | flag;
	}

	/** Clear a flag from a bitmask, handling values beyond 32-bit range via float arithmetic. */
	function clearBit(mask: number, flag: number): number {
		if (!hasBit(mask, flag)) return mask;
		return flag > (1 << 30) || mask > (1 << 30) ? mask - flag : mask & ~flag;
	}

	/** Three-state for a permission: 'allow' | 'neutral' | 'deny'. */
	function getState(flag: number): 'allow' | 'neutral' | 'deny' {
		if (hasBit(currentAllow, flag)) return 'allow';
		if (hasBit(currentDeny, flag)) return 'deny';
		return 'neutral';
	}

	async function setPermissionState(flag: number, targetState: 'allow' | 'neutral' | 'deny'): Promise<void> {
		if (!selectedRoleId || !auth.idToken || isSaving) return;

		// Compute new allow/deny bitmasks
		let newAllow = clearBit(currentAllow, flag);
		let newDeny = clearBit(currentDeny, flag);

		if (targetState === 'allow') {
			newAllow = setBit(newAllow, flag);
		} else if (targetState === 'deny') {
			newDeny = setBit(newDeny, flag);
		}

		isSaving = true;
		try {
			if (newAllow === 0 && newDeny === 0) {
				// No overrides remain — delete the row
				await channelStore.deleteChannelOverride(channelId, selectedRoleId);
				overrides = overrides.filter((o) => o.roleId !== selectedRoleId);
			} else {
				await channelStore.setChannelOverride(channelId, selectedRoleId, newAllow, newDeny);
				const role = servers.serverRoles.find((r) => r.id === selectedRoleId);
				const updated: ChannelPermissionOverride = {
					channelId,
					roleId: selectedRoleId,
					roleName: role?.name ?? '',
					allow: newAllow,
					deny: newDeny
				};
				const exists = overrides.some((o) => o.roleId === selectedRoleId);
				if (exists) {
					overrides = overrides.map((o) => (o.roleId === selectedRoleId ? updated : o));
				} else {
					overrides = [...overrides, updated];
				}
			}
		} catch {
			errorMessage = 'Failed to save permission override. Check that you have the required permissions.';
		} finally {
			isSaving = false;
		}
	}

	/** Permission categories shown in the grid. */
	const permissionCategories = [
		{
			name: 'General',
			permissions: [
				{ flag: Permission.ViewChannels, label: 'View Channels' },
				{ flag: Permission.ManageChannels, label: 'Manage Channels' },
				{ flag: Permission.ManageServer, label: 'Manage Server' },
				{ flag: Permission.ManageRoles, label: 'Manage Roles' },
				{ flag: Permission.ManageEmojis, label: 'Manage Emojis' },
				{ flag: Permission.ViewAuditLog, label: 'View Audit Log' },
				{ flag: Permission.CreateInvites, label: 'Create Invites' },
				{ flag: Permission.ManageInvites, label: 'Manage Invites' },
			]
		},
		{
			name: 'Membership',
			permissions: [
				{ flag: Permission.KickMembers, label: 'Kick Members' },
				{ flag: Permission.BanMembers, label: 'Ban Members' },
			]
		},
		{
			name: 'Messages',
			permissions: [
				{ flag: Permission.SendMessages, label: 'Send Messages' },
				{ flag: Permission.EmbedLinks, label: 'Embed Links' },
				{ flag: Permission.AttachFiles, label: 'Attach Files' },
				{ flag: Permission.AddReactions, label: 'Add Reactions' },
				{ flag: Permission.MentionEveryone, label: 'Mention @everyone' },
				{ flag: Permission.ManageMessages, label: 'Manage Messages' },
				{ flag: Permission.PinMessages, label: 'Pin Messages' },
			]
		},
		{
			name: 'Voice',
			permissions: [
				{ flag: Permission.Connect, label: 'Connect' },
				{ flag: Permission.Speak, label: 'Speak' },
				{ flag: Permission.MuteMembers, label: 'Mute Members' },
				{ flag: Permission.DeafenMembers, label: 'Deafen Members' },
			]
		},
	];
</script>

<div class="channel-permissions">
	<div class="permissions-layout">
		<!-- Role list -->
		<aside class="role-sidebar">
			<h4 class="sidebar-title">Roles</h4>
			{#if servers.isLoadingRoles}
				<p class="loading-text">Loading roles…</p>
			{:else}
				<ul class="role-list" role="list">
					{#each servers.serverRoles as role (role.id)}
						{@const hasOverride = overrides.some((o) => o.roleId === role.id)}
						<li>
							<button
								class="role-item {selectedRoleId === role.id ? 'role-item--selected' : ''}"
								onclick={() => (selectedRoleId = role.id)}
							>
								<span class="role-dot" style:background={role.color ?? 'var(--text-muted)'}></span>
								<span class="role-item-name">{role.name}</span>
								{#if hasOverride}
									<span class="override-badge" title="Has custom overrides">●</span>
								{/if}
							</button>
						</li>
					{/each}
				</ul>
			{/if}
		</aside>

		<!-- Permission grid -->
		<div class="permission-editor">
			{#if errorMessage}
				<p class="error-text">{errorMessage}</p>
			{/if}
			{#if !selectedRoleId}
				<p class="select-prompt">Select a role to edit its channel permissions.</p>
			{:else if isLoading}
				<p class="loading-text">Loading overrides…</p>
			{:else}
				{@const role = servers.serverRoles.find((r) => r.id === selectedRoleId)}
				<h4 class="editor-title">
					{role?.name ?? 'Role'} — Channel Overrides
				</h4>
				<p class="editor-desc">
					Overrides take priority over role permissions for this channel.
					Use <strong>Allow</strong> to grant a permission regardless of role settings,
					<strong>Deny</strong> to block it, or leave <strong>Neutral</strong> to inherit from the role.
				</p>

				{#each permissionCategories as category (category.name)}
					<div class="perm-category">
						<h5 class="perm-category-name">{category.name}</h5>
						<div class="perm-rows">
							{#each category.permissions as perm (perm.flag)}
								{@const state = getState(perm.flag)}
								<div class="perm-row">
									<span class="perm-label">{perm.label}</span>
									<div class="perm-toggles" role="group" aria-label="{perm.label} override">
										<button
											class="perm-toggle perm-toggle--allow {state === 'allow' ? 'perm-toggle--active' : ''}"
											onclick={() => {
												if (state !== 'allow') setPermissionState(perm.flag, 'allow');
											}}
											disabled={isSaving}
											title="Allow"
											aria-pressed={state === 'allow'}
										>✓</button>
										<button
											class="perm-toggle perm-toggle--neutral {state === 'neutral' ? 'perm-toggle--active' : ''}"
											onclick={() => {
												if (state !== 'neutral') setPermissionState(perm.flag, 'neutral');
											}}
											disabled={isSaving}
											title="Neutral (inherit)"
											aria-pressed={state === 'neutral'}
										>–</button>
										<button
											class="perm-toggle perm-toggle--deny {state === 'deny' ? 'perm-toggle--active' : ''}"
											onclick={() => {
												if (state !== 'deny') setPermissionState(perm.flag, 'deny');
											}}
											disabled={isSaving}
											title="Deny"
											aria-pressed={state === 'deny'}
										>✕</button>
									</div>
								</div>
							{/each}
						</div>
					</div>
				{/each}
			{/if}
		</div>
	</div>
</div>

<style>
	.channel-permissions {
		width: 100%;
	}

	.permissions-layout {
		display: flex;
		gap: 24px;
		align-items: flex-start;
	}

	/* Role sidebar */
	.role-sidebar {
		flex-shrink: 0;
		width: 180px;
	}

	.sidebar-title {
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
		margin: 0 0 8px;
	}

	.role-list {
		list-style: none;
		margin: 0;
		padding: 0;
	}

	.role-item {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
		padding: 7px 8px;
		border-radius: 4px;
		border: none;
		background: transparent;
		color: var(--text-normal);
		font-size: 13px;
		cursor: pointer;
		text-align: left;
		transition: background-color 100ms ease;
	}

	.role-item:hover {
		background: var(--bg-message-hover);
	}

	.role-item--selected {
		background: var(--bg-tertiary);
		color: var(--text-header);
		font-weight: 600;
	}

	.role-dot {
		width: 10px;
		height: 10px;
		border-radius: 50%;
		flex-shrink: 0;
	}

	.role-item-name {
		flex: 1;
		min-width: 0;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.override-badge {
		font-size: 8px;
		color: var(--accent);
		flex-shrink: 0;
	}

	/* Permission editor */
	.permission-editor {
		flex: 1;
		min-width: 0;
	}

	.error-text {
		font-size: 13px;
		color: var(--danger);
		margin: 0 0 12px;
	}

	.select-prompt,
	.loading-text {
		font-size: 13px;
		color: var(--text-muted);
		margin: 0;
	}

	.editor-title {
		font-size: 15px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 6px;
	}

	.editor-desc {
		font-size: 12px;
		color: var(--text-muted);
		margin: 0 0 16px;
		line-height: 1.5;
	}

	.perm-category {
		margin-bottom: 20px;
	}

	.perm-category-name {
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
		margin: 0 0 8px;
	}

	.perm-rows {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.perm-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 6px 8px;
		border-radius: 3px;
		transition: background-color 100ms ease;
	}

	.perm-row:hover {
		background: var(--bg-message-hover);
	}

	.perm-label {
		font-size: 13px;
		color: var(--text-normal);
		flex: 1;
	}

	.perm-toggles {
		display: flex;
		gap: 2px;
		flex-shrink: 0;
	}

	.perm-toggle {
		width: 28px;
		height: 28px;
		border-radius: 3px;
		border: 1px solid var(--border);
		background: transparent;
		color: var(--text-muted);
		font-size: 13px;
		font-weight: 700;
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: background-color 100ms ease, color 100ms ease, border-color 100ms ease;
		padding: 0;
	}

	.perm-toggle:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.perm-toggle:hover:not(:disabled) {
		border-color: var(--text-normal);
		color: var(--text-normal);
	}

	/* Allow: green when active */
	.perm-toggle--allow.perm-toggle--active {
		background: #3ba55d;
		border-color: #3ba55d;
		color: #fff;
	}

	/* Neutral: subtle when active */
	.perm-toggle--neutral.perm-toggle--active {
		background: var(--bg-tertiary);
		border-color: var(--text-muted);
		color: var(--text-normal);
	}

	/* Deny: red when active */
	.perm-toggle--deny.perm-toggle--active {
		background: var(--danger);
		border-color: var(--danger);
		color: #fff;
	}
</style>
