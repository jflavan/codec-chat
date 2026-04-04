<script lang="ts">
	let { label, value, color = 'var(--text-primary)' }: { label: string; value: string | number; color?: string } = $props();
</script>

<div class="stat-card">
	<div class="label">{label}</div>
	<div class="value" style:color>{value}</div>
</div>

<style>
	.stat-card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; }
	.label { font-size: 11px; text-transform: uppercase; color: var(--text-muted); letter-spacing: 0.5px; margin-bottom: 8px; }
	.value { font-size: 28px; font-weight: 700; }
</style>
