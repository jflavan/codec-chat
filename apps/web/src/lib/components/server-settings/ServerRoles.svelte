<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { Permission, hasPermission } from '$lib/types/index.js';

	const servers = getServerStore();

	let newRoleName = $state('');
	let newRoleColor = $state('#99aab5');
	let isCreating = $state(false);
	let editingRoleId = $state<string | null>(null);
	let editName = $state('');
	let editColor = $state('');
	let editPermissions = $state(0);
	let deletingRoleId = $state<string | null>(null);

	$effect(() => {
		if (servers.selectedServerId) {
			servers.loadRoles();
		}
	});

	async function createRole(): Promise<void> {
		if (!newRoleName.trim()) return;
		isCreating = true;
		await servers.createRole(newRoleName.trim(), { color: newRoleColor, isHoisted: true });
		newRoleName = '';
		newRoleColor = '#99aab5';
		isCreating = false;
	}

	function startEdit(role: { id: string; name: string; color?: string | null; permissions: number }): void {
		editingRoleId = role.id;
		editName = role.name;
		editColor = role.color ?? '#99aab5';
		editPermissions = role.permissions;
	}

	async function saveEdit(): Promise<void> {
		if (!editingRoleId || !editName.trim()) return;
		await servers.updateRole(editingRoleId, { name: editName.trim(), color: editColor, permissions: editPermissions });
		editingRoleId = null;
	}

	async function confirmDelete(roleId: string): Promise<void> {
		if (deletingRoleId !== roleId) {
			deletingRoleId = roleId;
			return;
		}
		await servers.deleteRole(roleId);
		deletingRoleId = null;
	}

	/** Check if a single bit is actually set in the bitmask (ignores Administrator grant-all). */
	function hasBit(mask: number, flag: number): boolean {
		if (flag > (1 << 30) || mask > (1 << 30)) {
			return Math.floor(mask / flag) % 2 === 1;
		}
		return (mask & flag) === flag;
	}

	/** Toggle a permission bit, handling values beyond 32-bit range via float arithmetic. */
	function togglePermission(current: number, flag: number): number {
		if (hasBit(current, flag)) {
			return flag > (1 << 30) || current > (1 << 30) ? current - flag : current & ~flag;
		} else {
			return flag > (1 << 30) || current > (1 << 30) ? current + flag : current | flag;
		}
	}

	/** Permission categories for the edit UI. */
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
		{
			name: 'Dangerous',
			permissions: [
				{ flag: Permission.Administrator, label: 'Administrator' },
			]
		}
	];
</script>

<section class="server-roles-settings">
	<h2 class="section-title">Roles</h2>
	<p class="section-desc">Manage roles and permissions for this server.</p>

	{#if servers.isLoadingRoles}
		<p class="loading">Loading roles…</p>
	{:else}
		<ul class="role-list" role="list">
			{#each servers.serverRoles as role (role.id)}
				<li class="role-row {editingRoleId === role.id ? 'role-row--editing' : ''}">
					{#if editingRoleId === role.id}
						<div class="role-edit-panel">
							<div class="role-edit-header">
								<label class="visually-hidden" for="edit-role-name-{role.id}">Role name</label>
								<input
									id="edit-role-name-{role.id}"
									type="text"
									class="role-name-input"
									bind:value={editName}
									disabled={role.isSystemRole}
									placeholder="Role name"
								/>
								<label class="visually-hidden" for="edit-role-color-{role.id}">Role color</label>
								<input id="edit-role-color-{role.id}" type="color" class="role-color-input" bind:value={editColor} />
								<button class="role-btn role-btn-save" onclick={saveEdit}>Save</button>
								<button class="role-btn role-btn-cancel" onclick={() => (editingRoleId = null)}>Cancel</button>
							</div>
							<div class="permissions-section">
								<h4 class="permissions-title">Permissions</h4>
								{#if role.isSystemRole}
									<p class="permissions-readonly-note">This is a system role. All permissions are granted and cannot be changed.</p>
								{/if}
								{#each permissionCategories as category (category.name)}
									<div class="permission-category">
										<h5 class="permission-category-name">{category.name}</h5>
										<div class="permission-grid">
											{#each category.permissions as perm (perm.flag)}
												<label class="permission-toggle {role.isSystemRole ? 'permission-toggle--readonly' : ''}">
													<input
														type="checkbox"
														class="permission-checkbox"
														checked={hasPermission(editPermissions, perm.flag)}
														disabled={role.isSystemRole}
														onchange={() => {
															if (!role.isSystemRole) {
																editPermissions = togglePermission(editPermissions, perm.flag);
															}
														}}
													/>
													<span class="permission-label">{perm.label}</span>
												</label>
											{/each}
										</div>
									</div>
								{/each}
							</div>
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
									<span class="visually-hidden" role="alert">Confirm deletion of role: {role.name}</span>
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
				<label class="visually-hidden" for="new-role-name">Role name</label>
				<input
					id="new-role-name"
					type="text"
					class="role-name-input"
					placeholder="Role name"
					bind:value={newRoleName}
					maxlength={100}
				/>
				<label class="visually-hidden" for="new-role-color">Role color</label>
				<input id="new-role-color" type="color" class="role-color-input" bind:value={newRoleColor} />
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

	.role-row--editing {
		display: block;
		background: var(--bg-message-hover);
		padding: 12px;
		margin-bottom: 4px;
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

	/* Edit panel */
	.role-edit-panel {
		width: 100%;
	}

	.role-edit-header {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
		margin-bottom: 16px;
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

	/* Permissions section */
	.permissions-section {
		border-top: 1px solid var(--border);
		padding-top: 12px;
	}

	.permissions-title {
		font-size: 13px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
		margin: 0 0 12px;
	}

	.permissions-readonly-note {
		font-size: 12px;
		color: var(--text-muted);
		margin: 0 0 12px;
		font-style: italic;
	}

	.permission-category {
		margin-bottom: 16px;
	}

	.permission-category-name {
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
		margin: 0 0 8px;
	}

	.permission-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
		gap: 6px;
	}

	.permission-toggle {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 6px 8px;
		border-radius: 3px;
		cursor: pointer;
		transition: background-color 100ms ease;
		user-select: none;
	}

	.permission-toggle:hover {
		background: var(--bg-tertiary);
	}

	.permission-toggle--readonly {
		cursor: default;
		opacity: 0.7;
	}

	.permission-toggle--readonly:hover {
		background: transparent;
	}

	.permission-checkbox {
		width: 16px;
		height: 16px;
		flex-shrink: 0;
		accent-color: var(--accent);
		cursor: pointer;
	}

	.permission-toggle--readonly .permission-checkbox {
		cursor: default;
	}

	.permission-label {
		font-size: 13px;
		color: var(--text-normal);
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
