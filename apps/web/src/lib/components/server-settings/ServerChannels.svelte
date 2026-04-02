<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getChannelStore } from '$lib/state/channel-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import { dndzone } from 'svelte-dnd-action';
	import type { Channel } from '$lib/types/models.js';

	const auth = getAuthStore();
	const servers = getServerStore();
	const channelStore = getChannelStore();
	const msgStore = getMessageStore();

	/* ─── Category creation ─── */
	let newCategoryName = $state('');
	let isAddingCategory = $state(false);

	/* ─── Channel editing ─── */
	let channelEditId = $state<string | null>(null);
	let channelEditName = $state('');
	let channelEditDesc = $state('');
	let confirmDeleteChannelId = $state<string | null>(null);
	let confirmPurgeChannelId = $state<string | null>(null);

	/* ─── Category editing ─── */
	let categoryEditId = $state<string | null>(null);
	let categoryEditName = $state('');
	let confirmDeleteCategoryId = $state<string | null>(null);

	// Derive the ordered list of items for drag-and-drop per group
	type DndItem = Channel & { id: string };

	const uncategorizedItems = $derived(
		channelStore.channels
			.filter((c) => !c.categoryId)
			.sort((a, b) => a.position - b.position)
			.map((c) => ({ ...c, id: c.id })) as DndItem[]
	);

	type CategoryItems = { categoryId: string; name: string; items: DndItem[] };
	const categoryGroups = $derived(
		servers.categories
			.slice()
			.sort((a, b) => a.position - b.position)
			.map((cat): CategoryItems => ({
				categoryId: cat.id,
				name: cat.name,
				items: channelStore.channels
					.filter((c) => c.categoryId === cat.id)
					.sort((a, b) => a.position - b.position)
					.map((c) => ({ ...c, id: c.id })) as DndItem[]
			}))
	);

	// Local mutable copies for drag state
	let localUncategorized = $state<DndItem[]>([]);
	let localCategoryGroups = $state<CategoryItems[]>([]);

	$effect(() => {
		localUncategorized = [...uncategorizedItems];
	});
	$effect(() => {
		localCategoryGroups = categoryGroups.map((g) => ({ ...g, items: [...g.items] }));
	});

	/* ─── Drag-and-drop handlers for uncategorized ─── */
	function handleUncatConsider(e: CustomEvent) {
		localUncategorized = e.detail.items;
	}
	async function handleUncatFinalize(e: CustomEvent) {
		localUncategorized = e.detail.items;
		await saveChannelOrder();
	}

	/* ─── Drag-and-drop handlers for a category ─── */
	function handleCatConsider(catIdx: number, e: CustomEvent) {
		localCategoryGroups = localCategoryGroups.map((g, i) =>
			i === catIdx ? { ...g, items: e.detail.items } : g
		);
	}
	async function handleCatFinalize(catIdx: number, e: CustomEvent) {
		localCategoryGroups = localCategoryGroups.map((g, i) =>
			i === catIdx ? { ...g, items: e.detail.items } : g
		);
		await saveChannelOrder();
	}

	async function saveChannelOrder() {
		const updates: { channelId: string; categoryId?: string; position: number }[] = [];
		localUncategorized.forEach((ch, idx) => {
			updates.push({ channelId: ch.id, position: idx });
		});
		localCategoryGroups.forEach((group) => {
			group.items.forEach((ch, idx) => {
				updates.push({ channelId: ch.id, categoryId: group.categoryId, position: idx });
			});
		});
		await channelStore.saveChannelOrder(updates);
	}

	/* ─── Channel edit helpers ─── */
	function startEditChannel(channelId: string, name: string, desc: string | undefined) {
		channelEditId = channelId;
		channelEditName = name;
		channelEditDesc = desc ?? '';
	}

	function cancelEditChannel() {
		channelEditId = null;
		channelEditName = '';
		channelEditDesc = '';
	}

	async function saveChannelEdits(channelId: string) {
		if (!channelEditName.trim()) return;
		await channelStore.updateChannelName(channelId, channelEditName);
		if (channelEditDesc !== undefined) {
			await channelStore.updateChannelDescription(channelId, channelEditDesc);
		}
		cancelEditChannel();
	}

	async function handleDeleteChannel(channelId: string) {
		await channelStore.deleteChannel(channelId);
		confirmDeleteChannelId = null;
	}

	async function handlePurgeChannel(channelId: string) {
		await msgStore.purgeChannel(channelId);
		confirmPurgeChannelId = null;
	}

	/* ─── Category helpers ─── */
	async function addCategory() {
		const name = newCategoryName.trim();
		if (!name) return;
		isAddingCategory = true;
		await servers.createCategory(name);
		newCategoryName = '';
		isAddingCategory = false;
	}

	function startEditCategory(catId: string, name: string) {
		categoryEditId = catId;
		categoryEditName = name;
	}

	async function saveCategoryName(catId: string) {
		if (!categoryEditName.trim()) return;
		await servers.renameCategory(catId, categoryEditName);
		categoryEditId = null;
		categoryEditName = '';
	}

	async function handleDeleteCategory(catId: string) {
		await servers.deleteCategory(catId);
		confirmDeleteCategoryId = null;
	}
</script>

<div class="server-channels">
	<h1 class="settings-title">Channels</h1>

	<!-- Category creation -->
	<section class="settings-section">
		<h2 class="section-title">Add Category</h2>
		<div class="add-category-row">
			<input
				type="text"
				class="input"
				placeholder="Category name"
				maxlength="50"
				bind:value={newCategoryName}
				onkeydown={(e) => { if (e.key === 'Enter') addCategory(); }}
			/>
			<button
				type="button"
				class="btn-primary"
				disabled={isAddingCategory || !newCategoryName.trim()}
				onclick={addCategory}
			>
				{isAddingCategory ? 'Adding…' : 'Add Category'}
			</button>
		</div>
	</section>

	<!-- Uncategorized channels -->
	<section class="settings-section">
		<h2 class="section-title">Uncategorized</h2>
		<div
			use:dndzone={{ items: localUncategorized, flipDurationMs: 150 }}
			onconsider={handleUncatConsider}
			onfinalize={handleUncatFinalize}
			class="channel-list dnd-zone"
		>
			{#each localUncategorized as channel (channel.id)}
				{@render channelRow(channel)}
			{/each}
		</div>
	</section>

	<!-- Category sections -->
	{#each localCategoryGroups as group, catIdx (group.categoryId)}
		<section class="settings-section">
			<div class="category-header">
				{#if categoryEditId === group.categoryId}
					<div class="category-edit-row">
						<input
							type="text"
							class="input input-sm"
							bind:value={categoryEditName}
							maxlength="50"
							onkeydown={(e) => {
								if (e.key === 'Enter') saveCategoryName(group.categoryId);
								if (e.key === 'Escape') { categoryEditId = null; categoryEditName = ''; }
							}}
						/>
						<button type="button" class="btn-primary btn-sm" onclick={() => saveCategoryName(group.categoryId)}>Save</button>
						<button type="button" class="btn-secondary btn-sm" onclick={() => { categoryEditId = null; categoryEditName = ''; }}>Cancel</button>
					</div>
				{:else}
					<h2 class="section-title category-name">{group.name}</h2>
					<div class="category-actions">
						<button type="button" class="btn-edit" onclick={() => startEditCategory(group.categoryId, group.name)}>Rename</button>
						{#if confirmDeleteCategoryId === group.categoryId}
							<span class="danger-warning-inline">Delete category?</span>
							<button type="button" class="btn-danger-sm" onclick={() => handleDeleteCategory(group.categoryId)}>Confirm</button>
							<button type="button" class="btn-secondary-sm" onclick={() => (confirmDeleteCategoryId = null)}>Cancel</button>
						{:else}
							<button type="button" class="btn-danger-sm" onclick={() => (confirmDeleteCategoryId = group.categoryId)}>Delete</button>
						{/if}
					</div>
				{/if}
			</div>
			<div
				use:dndzone={{ items: group.items, flipDurationMs: 150 }}
				onconsider={(e) => handleCatConsider(catIdx, e)}
				onfinalize={(e) => handleCatFinalize(catIdx, e)}
				class="channel-list dnd-zone"
			>
				{#each group.items as channel (channel.id)}
					{@render channelRow(channel)}
				{/each}
			</div>
		</section>
	{/each}
</div>

{#snippet channelRow(channel: DndItem)}
	<div class="channel-item">
		{#if channelEditId === channel.id}
			<div class="channel-edit">
				<div class="channel-edit-name-row">
					{@render channelTypeIcon(channel.type)}
					<input
						type="text"
						class="input"
						bind:value={channelEditName}
						maxlength="100"
						disabled={channelStore.isUpdatingChannelName}
						onkeydown={(e) => {
							if (e.key === 'Enter') saveChannelEdits(channel.id);
							if (e.key === 'Escape') cancelEditChannel();
						}}
					/>
				</div>
				{#if channel.type !== 'voice'}
					<textarea
						class="input textarea"
						maxlength="256"
						rows="2"
						placeholder="Channel description (optional)"
						bind:value={channelEditDesc}
					></textarea>
					<div class="char-meta">
						<span class="char-counter" class:warn={channelEditDesc.length >= 256}>{channelEditDesc.length}/256</span>
					</div>
				{/if}
				<div class="inline-actions">
					<button
						type="button"
						class="btn-primary"
						disabled={channelStore.isUpdatingChannelName || !channelEditName.trim()}
						onclick={() => saveChannelEdits(channel.id)}
					>
						{channelStore.isUpdatingChannelName ? '…' : 'Save'}
					</button>
					<button type="button" class="btn-secondary" onclick={cancelEditChannel}>Cancel</button>
				</div>
			</div>
		{:else}
			<div class="channel-display">
				<span class="drag-handle" aria-hidden="true">⠿</span>
				{@render channelTypeIcon(channel.type)}
				<span class="channel-name">{channel.name}</span>
				{#if servers.canManageChannels}
					<button type="button" class="btn-edit" onclick={() => startEditChannel(channel.id, channel.name, channel.description)}>
						Edit
					</button>
				{/if}
				{#if auth.isGlobalAdmin && channel.type !== 'voice'}
					{#if confirmPurgeChannelId === channel.id}
						<span class="danger-warning-inline">Delete all messages?</span>
						<button type="button" class="btn-danger-sm" disabled={msgStore.isPurgingChannel} onclick={() => handlePurgeChannel(channel.id)}>
							{msgStore.isPurgingChannel ? '…' : 'Confirm'}
						</button>
						<button type="button" class="btn-secondary-sm" disabled={msgStore.isPurgingChannel} onclick={() => (confirmPurgeChannelId = null)}>
							Cancel
						</button>
					{:else}
						<button type="button" class="btn-danger-sm" onclick={() => { confirmPurgeChannelId = channel.id; confirmDeleteChannelId = null; }}>
							Purge
						</button>
					{/if}
				{/if}
				{#if servers.canDeleteChannel}
					{#if confirmDeleteChannelId === channel.id}
						<button type="button" class="btn-danger-sm" onclick={() => handleDeleteChannel(channel.id)}>Confirm</button>
						<button type="button" class="btn-secondary-sm" onclick={() => (confirmDeleteChannelId = null)}>Cancel</button>
					{:else}
						<button type="button" class="btn-danger-sm" onclick={() => { confirmDeleteChannelId = channel.id; confirmPurgeChannelId = null; }}>
							Delete
						</button>
					{/if}
				{/if}
			</div>
		{/if}
	</div>
{/snippet}

{#snippet channelTypeIcon(type: string | undefined)}
	{#if type === 'voice'}
		<svg class="channel-icon" width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
			<path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/>
		</svg>
	{:else}
		<span class="channel-hash" aria-hidden="true">#</span>
	{/if}
{/snippet}

<style>
	.server-channels {
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
		margin: 0 0 12px;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.add-category-row {
		display: flex;
		gap: 8px;
		align-items: center;
	}

	.add-category-row .input {
		flex: 1;
	}

	.input {
		width: 100%;
		padding: 8px 10px;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		transition: border-color 150ms ease;
		box-sizing: border-box;
	}

	.input:focus {
		outline: none;
		border-color: var(--accent);
	}

	.input:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.input-sm {
		padding: 6px 8px;
		font-size: 13px;
		width: auto;
		flex: 1;
	}

	.textarea {
		resize: vertical;
		min-height: 56px;
		line-height: 1.5;
	}

	.dnd-zone {
		min-height: 4px;
		outline: none;
	}

	.channel-list {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.channel-item {
		background: var(--bg-secondary);
		border-radius: 4px;
		padding: 6px 8px;
	}

	.channel-display {
		display: flex;
		align-items: center;
		gap: 8px;
		flex-wrap: wrap;
	}

	.channel-edit {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.channel-edit-name-row {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.channel-edit-name-row .input {
		flex: 1;
	}

	.drag-handle {
		color: var(--text-muted);
		opacity: 0.5;
		cursor: grab;
		font-size: 16px;
		flex-shrink: 0;
		user-select: none;
	}

	.channel-hash {
		color: var(--text-muted);
		font-weight: 700;
		font-size: 16px;
		flex-shrink: 0;
	}

	.channel-icon {
		color: var(--text-muted);
		flex-shrink: 0;
		opacity: 0.7;
	}

	.channel-name {
		font-size: 14px;
		color: var(--text-normal);
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.category-header {
		display: flex;
		align-items: center;
		gap: 8px;
		margin-bottom: 8px;
	}

	.category-name {
		flex: 1;
		margin-bottom: 0;
	}

	.category-actions {
		display: flex;
		align-items: center;
		gap: 6px;
		flex-shrink: 0;
	}

	.category-edit-row {
		display: flex;
		align-items: center;
		gap: 6px;
		flex: 1;
	}

	.inline-actions {
		display: flex;
		gap: 8px;
	}

	.char-meta {
		display: flex;
		justify-content: flex-end;
	}

	.char-counter {
		font-size: 11px;
		color: var(--text-muted);
	}

	.char-counter.warn {
		color: var(--danger);
	}

	.btn-primary {
		padding: 8px 14px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
		white-space: nowrap;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-sm {
		padding: 5px 10px;
		font-size: 13px;
	}

	.btn-secondary {
		padding: 8px 14px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		font-family: inherit;
		transition: color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		color: var(--text-header);
	}

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-edit {
		padding: 4px 10px;
		background: none;
		color: var(--accent);
		border: 1px solid var(--accent);
		border-radius: 3px;
		font-size: 12px;
		font-weight: 500;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
		white-space: nowrap;
		flex-shrink: 0;
	}

	.btn-edit:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.btn-danger-sm {
		padding: 4px 10px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 12px;
		font-weight: 500;
		cursor: pointer;
		transition: opacity 150ms ease;
		white-space: nowrap;
		flex-shrink: 0;
	}

	.btn-danger-sm:hover:not(:disabled) {
		opacity: 0.9;
	}

	.btn-danger-sm:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary-sm {
		padding: 4px 10px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 12px;
		font-weight: 500;
		cursor: pointer;
		transition: color 150ms ease;
		white-space: nowrap;
		flex-shrink: 0;
	}

	.btn-secondary-sm:hover:not(:disabled) {
		color: var(--text-header);
	}

	.danger-warning-inline {
		color: var(--danger);
		font-size: 12px;
		white-space: nowrap;
	}
</style>
