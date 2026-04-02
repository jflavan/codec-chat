<script lang="ts">
	let {
		columns,
		rows,
		onRowClick
	}: {
		columns: { key: string; label: string }[];
		rows: any[];
		onRowClick?: (row: any) => void;
	} = $props();
</script>

<div class="table-wrapper">
	<table>
		<thead>
			<tr>
				{#each columns as col}
					<th>{col.label}</th>
				{/each}
			</tr>
		</thead>
		<tbody>
			{#each rows as row}
				<tr
					class:clickable={!!onRowClick}
					onclick={() => onRowClick?.(row)}
					onkeydown={(e) => e.key === 'Enter' && onRowClick?.(row)}
					tabindex={onRowClick ? 0 : undefined}
					role={onRowClick ? 'button' : undefined}
				>
					{#each columns as col}
						<td>{row[col.key] ?? ''}</td>
					{/each}
				</tr>
			{:else}
				<tr>
					<td colspan={columns.length} class="empty">No results found.</td>
				</tr>
			{/each}
		</tbody>
	</table>
</div>

<style>
	.table-wrapper {
		overflow-x: auto;
		border: 1px solid var(--border);
		border-radius: var(--radius);
	}
	table {
		width: 100%;
		border-collapse: collapse;
		font-size: 14px;
	}
	thead {
		background: var(--bg-tertiary);
	}
	th {
		padding: 10px 14px;
		text-align: left;
		font-size: 11px;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		color: var(--text-muted);
		white-space: nowrap;
		border-bottom: 1px solid var(--border);
	}
	td {
		padding: 10px 14px;
		color: var(--text-primary);
		border-bottom: 1px solid var(--border);
	}
	tbody tr:last-child td {
		border-bottom: none;
	}
	tbody tr.clickable {
		cursor: pointer;
	}
	tbody tr.clickable:hover td {
		background: var(--bg-tertiary);
	}
	.empty {
		text-align: center;
		color: var(--text-muted);
		padding: 32px;
	}
</style>
