<script lang="ts">
	import type { SearchFilters } from '$lib/types/models.js';
	import { getChannelStore } from '$lib/state/channel-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import { getDmStore } from '$lib/state/dm-store.svelte.js';

	let { isDm = false }: { isDm?: boolean } = $props();

	const channelStore = getChannelStore();
	const msgStore = getMessageStore();
	const dms = getDmStore();

	let scope = $state<'channel' | 'server'>('channel');
	let afterDate = $state('');
	let beforeDate = $state('');
	let hasImage = $state(false);
	let hasLink = $state(false);

	function buildFilters(): SearchFilters {
		const filters: SearchFilters = {};

		// In "channel" scope, include the channelId to restrict search
		if (scope === 'channel') {
			if (isDm) {
				filters.channelId = dms.activeDmChannelId ?? undefined;
			} else {
				filters.channelId = channelStore.selectedChannelId ?? undefined;
			}
		}
		// In "server" scope, omit channelId so it searches all channels

		if (afterDate) filters.after = afterDate;
		if (beforeDate) filters.before = beforeDate;
		if (hasImage) filters.has = 'image';
		else if (hasLink) filters.has = 'link';

		return filters;
	}

	function triggerSearch(): void {
		if (msgStore.searchQuery.trim().length >= 2) {
			msgStore.searchMessages(msgStore.searchQuery, buildFilters());
		}
	}

	function setScope(s: 'channel' | 'server'): void {
		scope = s;
		triggerSearch();
	}

	function toggleHasImage(): void {
		hasImage = !hasImage;
		if (hasImage) hasLink = false;
		triggerSearch();
	}

	function toggleHasLink(): void {
		hasLink = !hasLink;
		if (hasLink) hasImage = false;
		triggerSearch();
	}

	function handleAfterChange(e: Event): void {
		afterDate = (e.target as HTMLInputElement).value;
		triggerSearch();
	}

	function handleBeforeChange(e: Event): void {
		beforeDate = (e.target as HTMLInputElement).value;
		triggerSearch();
	}
</script>

<div class="filter-bar">
	<div class="filter-row">
		<div class="scope-toggle">
			<button
				class="scope-btn"
				class:active={scope === 'channel'}
				onclick={() => setScope('channel')}
				type="button"
			>
				{isDm ? 'This DM' : 'This Channel'}
			</button>
			<button
				class="scope-btn"
				class:active={scope === 'server'}
				onclick={() => setScope('server')}
				type="button"
			>
				{isDm ? 'All DMs' : 'Server'}
			</button>
		</div>

		<div class="has-chips">
			<button
				class="chip"
				class:active={hasImage}
				onclick={toggleHasImage}
				type="button"
			>Image</button>
			<button
				class="chip"
				class:active={hasLink}
				onclick={toggleHasLink}
				type="button"
			>Link</button>
		</div>
	</div>

	<div class="filter-row date-row">
		<label class="date-filter">
			<span class="date-label">After</span>
			<input type="date" class="date-input" value={afterDate} onchange={handleAfterChange} />
		</label>
		<label class="date-filter">
			<span class="date-label">Before</span>
			<input type="date" class="date-input" value={beforeDate} onchange={handleBeforeChange} />
		</label>
	</div>
</div>

<style>
	.filter-bar {
		display: flex;
		flex-direction: column;
		gap: 8px;
		padding: 8px 12px;
		border-bottom: 1px solid var(--border);
	}

	.filter-row {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.scope-toggle {
		display: flex;
		border: 1px solid var(--border);
		border-radius: 4px;
		overflow: hidden;
	}

	.scope-btn {
		background: none;
		border: none;
		padding: 4px 10px;
		font-size: 11px;
		font-family: inherit;
		color: var(--text-muted);
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
		white-space: nowrap;
	}

	.scope-btn:hover {
		background: var(--bg-message-hover);
	}

	.scope-btn.active {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.has-chips {
		display: flex;
		gap: 4px;
		margin-left: auto;
	}

	.chip {
		background: none;
		border: 1px solid var(--border);
		border-radius: 12px;
		padding: 2px 10px;
		font-size: 11px;
		font-family: inherit;
		color: var(--text-muted);
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease, border-color 150ms ease;
		white-space: nowrap;
	}

	.chip:hover {
		border-color: var(--text-muted);
	}

	.chip.active {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.15);
		border-color: var(--accent);
		color: var(--accent);
	}

	.date-row {
		gap: 8px;
	}

	.date-filter {
		display: flex;
		align-items: center;
		gap: 4px;
		flex: 1;
	}

	.date-label {
		font-size: 11px;
		color: var(--text-muted);
		white-space: nowrap;
	}

	.date-input {
		flex: 1;
		min-width: 0;
		padding: 3px 6px;
		border: 1px solid var(--border);
		border-radius: 4px;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 11px;
		font-family: inherit;
	}

	.date-input:focus {
		outline: none;
		border-color: var(--accent);
	}

	/* Webkit date input color scheme override for dark theme */
	.date-input::-webkit-calendar-picker-indicator {
		filter: invert(0.7) sepia(1) saturate(3) hue-rotate(90deg);
	}
</style>
