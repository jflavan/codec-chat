<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { Permission, hasPermission } from '$lib/types/index.js';

	const app = getAppState();

	let newRoleName = $state('');
	let newRoleColor = $state('#99aab5');
	let isCreating = $state(false);
	let editingRoleId = $state<string | null>(null);
	let editName = $state('');
	let editColor = $state('');
	let deletingRoleId = $state<string | null>(null);

	$effect(() => {
		if (app.selectedServerId) {
			app.loadRoles();
		}
	});

	async function createRole(): Promise<void> {
		if (!newRoleName.trim()) return;
		isCreating = true;
		await app.createRole(newRoleName.trim(), { color: newRoleColor, isHoisted: true });
		newRoleName = '';
		newRoleColor = '#99aab5';
		isCreating = false;
	}

	function startEdit(role: { id: string; name: string; color?: string | null }): void {
		editingRoleId = role.id;
		editName = role.name;
		editColor = role.color ?? '#99aab5';
	}

	async function saveEdit(): Promise<void> {
		if (!editingRoleId || !editName.trim()) return;
		await app.updateRole(editingRoleId, { name: editName.trim(), color: editColor });
		editingRoleId = null;
	}

	async function confirmDelete(roleId: string): Promise<void> {
		if (deletingRoleId !== roleId) {
			deletingRoleId = roleId;
			return;
		}
		await app.deleteRole(roleId);
		deletingRoleId = null;
	}

	/** Permission labels for the UI. */
	const permissionEntries = [
		{ flag: Permission.ManageChannels, label: 'Manage Channels' },
		{ flag: Permission.ManageServer, label: 'Manage Server' },
		{ flag: Permission.ManageRoles, label: 'Manage Roles' },
		{ flag: Permission.ManageEmojis, label: 'Manage Emojis' },
		{ flag: Permission.ViewAuditLog, label: 'View Audit Log' },
		{ flag: Permission.CreateInvites, label: 'Create Invites' },
		{ flag: Permission.ManageInvites, label: 'Manage Invites' },
		{ flag: Permission.KickMembers, label: 'Kick Members' },
		{ flag: Permission.SendMessages, label: 'Send Messages' },
		{ flag: Permission.AttachFiles, label: 'Attach Files' },
		{ flag: Permission.AddReactions, label: 'Add Reactions' },
		{ flag: Permission.ManageMessages, label: 'Manage Messages' },
		{ flag: Permission.PinMessages, label: 'Pin Messages' },
		{ flag: Permission.Connect, label: 'Connect to Voice' },
		{ flag: Permission.Administrator, label: 'Administrator' },
	];
</script>

<section class="server-roles-settings">
	<h2 class="section-title">Roles</h2>
	<p class="section-desc">Manage roles and permissions for this server.</p>

	{#if app.isLoadingRoles}
		<p class="loading">Loading roles…</p>
	{:else}
		<ul class="role-list" role="list">
			{#each app.serverRoles as role (role.id)}
				<li class="role-row">
					{#if editingRoleId === role.id}
						<div class="role-edit">
							<input
								type="text"
								class="role-name-input"
								bind:value={editName}
								disabled={role.isSystemRole}
								placeholder="Role name"
							/>
							<input type="color" class="role-color-input" bind:value={editColor} />
							<button class="role-btn role-btn-save" onclick={saveEdit}>Save</button>
							<button class="role-btn role-btn-cancel" onclick={() => (editingRoleId = null)}>Cancel</button>
						</div>
					{:else}
						<div class="role-info">
							<span class="role-dot" style:background={role.color ?? 'var(--text-muted)'}></span>
							<span class="role-name">{role.name}</span>
							{#if role.isSystemRole}
								<span class="system-badge">System</span>
							{/if}
							<span class="role-member-count">{role.memberCount ?? 0} members</span>
						</div>
						<div class="role-actions">
							<button class="role-btn role-btn-edit" onclick={() => startEdit(role)}>Edit</button>
							{#if !role.isSystemRole}
								{#if deletingRoleId === role.id}
									<button class="role-btn role-btn-danger" onclick={() => confirmDelete(role.id)}>Confirm</button>
									<button class="role-btn role-btn-cancel" onclick={() => (deletingRoleId = null)}>Cancel</button>
								{:else}
									<button class="role-btn role-btn-delete" onclick={() => confirmDelete(role.id)}>Delete</button>
								{/if}
							{/if}
						</div>
					{/if}
				</li>
			{/each}
		</ul>

		<div class="create-role">
			<h3 class="subsection-title">Create Role</h3>
			<div class="create-role-form">
				<input
					type="text"
					class="role-name-input"
					placeholder="Role name"
					bind:value={newRoleName}
					maxlength={100}
				/>
				<input type="color" class="role-color-input" bind:value={newRoleColor} />
				<button class="role-btn role-btn-create" onclick={createRole} disabled={isCreating || !newRoleName.trim()}>
					{isCreating ? 'Creating…' : 'Create'}
				</button>
			</div>
		</div>
	{/if}
</section>

<style>
	.server-roles-settings {
		max-width: 660px;
	}

	.section-title {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 4px;
	}

	.section-desc {
		font-size: 13px;
		color: var(--text-muted);
		margin: 0 0 20px;
	}

	.subsection-title {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		margin: 24px 0 8px;
	}

	.loading {
		color: var(--text-muted);
		font-size: 13px;
	}

	.role-list {
		list-style: none;
		margin: 0;
		padding: 0;
	}

	.role-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 8px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.role-row:hover {
		background: var(--bg-message-hover);
	}

	.role-info {
		display: flex;
		align-items: center;
		gap: 8px;
		flex: 1;
		min-width: 0;
	}

	.role-dot {
		width: 12px;
		height: 12px;
		border-radius: 50%;
		flex-shrink: 0;
	}

	.role-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
	}

	.system-badge {
		font-size: 10px;
		font-weight: 700;
		text-transform: uppercase;
		color: var(--text-muted);
		background: var(--bg-tertiary);
		padding: 1px 5px;
		border-radius: 3px;
	}

	.role-member-count {
		font-size: 12px;
		color: var(--text-muted);
		margin-left: auto;
	}

	.role-actions {
		display: flex;
		gap: 6px;
		flex-shrink: 0;
		margin-left: 12px;
	}

	.role-edit {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
	}

	.role-name-input {
		padding: 6px 10px;
		font-size: 13px;
		border-radius: 3px;
		border: 1px solid var(--border);
		background: var(--bg-tertiary);
		color: var(--text-normal);
		flex: 1;
		min-width: 0;
	}

	.role-name-input:disabled {
		opacity: 0.5;
	}

	.role-color-input {
		width: 32px;
		height: 32px;
		border: none;
		border-radius: 4px;
		cursor: pointer;
		background: transparent;
		padding: 0;
	}

	.create-role-form {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.role-btn {
		padding: 6px 12px;
		min-height: 32px;
		font-size: 12px;
		font-weight: 600;
		border-radius: 3px;
		cursor: pointer;
		border: 1px solid transparent;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.role-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.role-btn-create,
	.role-btn-save {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.role-btn-create:hover:not(:disabled),
	.role-btn-save:hover {
		background: var(--accent-hover);
	}

	.role-btn-edit {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-edit:hover {
		color: var(--text-normal);
		border-color: var(--text-normal);
	}

	.role-btn-delete {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-delete:hover {
		background: var(--danger);
		color: #fff;
		border-color: var(--danger);
	}

	.role-btn-danger {
		background: var(--danger);
		color: #fff;
		border-color: var(--danger);
	}

	.role-btn-cancel {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-cancel:hover {
		color: var(--text-normal);
		border-color: var(--text-normal);
	}
</style>
