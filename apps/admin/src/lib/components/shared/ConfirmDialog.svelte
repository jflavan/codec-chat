<script lang="ts">
	let {
		open,
		title,
		message,
		confirmLabel = 'Confirm',
		destructive = false,
		requireInput,
		onConfirm,
		onCancel
	}: {
		open: boolean;
		title: string;
		message: string;
		confirmLabel?: string;
		destructive?: boolean;
		requireInput?: string;
		onConfirm: () => void;
		onCancel: () => void;
	} = $props();

	let inputValue = $state('');
	const canConfirm = $derived(!requireInput || inputValue === requireInput);

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') onCancel();
	}
</script>

{#if open}
	<div class="overlay" role="dialog" aria-modal="true" tabindex="-1" onkeydown={handleKeydown}>
		<div class="dialog">
			<h3 class="title">{title}</h3>
			<p class="message">{message}</p>
			{#if requireInput}
				<div class="input-group">
					<label for="confirm-input">
						Type <strong>{requireInput}</strong> to confirm:
					</label>
					<input
						id="confirm-input"
						type="text"
						bind:value={inputValue}
						placeholder={requireInput}
						autocomplete="off"
					/>
				</div>
			{/if}
			<div class="actions">
				<button class="btn-cancel" onclick={onCancel}>Cancel</button>
				<button
					class="btn-confirm"
					class:destructive
					disabled={!canConfirm}
					onclick={onConfirm}
				>
					{confirmLabel}
				</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.overlay {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.6);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
	}
	.dialog {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: var(--radius);
		padding: 24px;
		width: 420px;
		max-width: 95vw;
	}
	.title {
		font-size: 16px;
		font-weight: 600;
		margin-bottom: 10px;
	}
	.message {
		font-size: 14px;
		color: var(--text-secondary);
		margin-bottom: 16px;
		line-height: 1.5;
	}
	.input-group {
		margin-bottom: 16px;
	}
	label {
		display: block;
		font-size: 13px;
		color: var(--text-secondary);
		margin-bottom: 6px;
	}
	input {
		width: 100%;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: var(--radius);
		color: var(--text-primary);
		padding: 8px 10px;
		font-size: 14px;
	}
	input:focus {
		outline: none;
		border-color: var(--accent);
	}
	.actions {
		display: flex;
		gap: 10px;
		justify-content: flex-end;
	}
	button {
		padding: 8px 18px;
		border-radius: var(--radius);
		font-size: 14px;
		font-weight: 500;
		border: 1px solid var(--border);
		transition: opacity 0.15s;
	}
	.btn-cancel {
		background: var(--bg-tertiary);
		color: var(--text-secondary);
	}
	.btn-cancel:hover {
		color: var(--text-primary);
	}
	.btn-confirm {
		background: var(--accent);
		border-color: var(--accent);
		color: #fff;
	}
	.btn-confirm.destructive {
		background: var(--danger);
		border-color: var(--danger);
	}
	.btn-confirm:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
	.btn-confirm:not(:disabled):hover {
		opacity: 0.85;
	}
</style>
