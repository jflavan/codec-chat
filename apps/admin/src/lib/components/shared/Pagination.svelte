<script lang="ts">
	let {
		page,
		totalPages,
		onPageChange
	}: {
		page: number;
		totalPages: number;
		onPageChange: (page: number) => void;
	} = $props();
</script>

<div class="pagination">
	<button disabled={page <= 1} onclick={() => onPageChange(page - 1)}>Previous</button>
	<span class="indicator">Page {page} of {totalPages}</span>
	<button disabled={page >= totalPages} onclick={() => onPageChange(page + 1)}>Next</button>
</div>

<style>
	.pagination {
		display: flex;
		align-items: center;
		gap: 12px;
		margin-top: 16px;
		justify-content: center;
	}
	button {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: var(--radius);
		color: var(--text-primary);
		padding: 6px 14px;
		font-size: 13px;
		transition: border-color 0.15s;
	}
	button:hover:not(:disabled) {
		border-color: var(--accent);
		color: var(--accent);
	}
	button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
	.indicator {
		font-size: 13px;
		color: var(--text-secondary);
		min-width: 110px;
		text-align: center;
	}
</style>
