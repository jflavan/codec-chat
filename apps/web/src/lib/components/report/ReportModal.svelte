<script lang="ts">
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import type { ApiClient } from '$lib/api/client.js';
	import { ReportType } from '$lib/types/index.js';

	let { api }: { api: ApiClient } = $props();

	const ui = getUIStore();
	const auth = getAuthStore();

	const modal = $derived(ui.reportModal);
	const typeLabel = $derived(
		modal?.reportType === ReportType.User ? 'User' :
		modal?.reportType === ReportType.Message ? 'Message' : 'Server'
	);

	let reason = $state('');
	let submitting = $state(false);
	let error = $state<string | null>(null);
	let success = $state(false);

	const MAX_REASON = 2000;

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (!modal || !auth.idToken || !reason.trim()) return;
		submitting = true;
		error = null;
		try {
			await api.submitReport(auth.idToken, {
				reportType: modal.reportType,
				targetId: modal.targetId,
				reason: reason.trim()
			});
			success = true;
			setTimeout(() => ui.closeReportModal(), 1500);
		} catch (e: any) {
			if (e?.status === 429) {
				error = 'You can only submit 5 reports per hour.';
			} else if (e?.status === 404) {
				error = 'The item you are trying to report no longer exists.';
			} else {
				error = e?.message ?? 'Failed to submit report.';
			}
		} finally {
			submitting = false;
		}
	}

	function handleClose() {
		reason = '';
		error = null;
		success = false;
		ui.closeReportModal();
	}

	function handleBackdrop(e: MouseEvent) {
		if (e.target === e.currentTarget) handleClose();
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') handleClose();
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if modal}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="modal-backdrop" onclick={handleBackdrop}>
		<div class="modal" role="dialog" aria-labelledby="report-title" aria-modal="true">
			<h2 id="report-title">Report {typeLabel}</h2>
			<p class="context">{modal.targetName}</p>

			{#if success}
				<p class="success-msg">Report submitted. Thank you.</p>
			{:else}
				<form onsubmit={handleSubmit}>
					<label for="report-reason">Reason</label>
					<textarea
						id="report-reason"
						bind:value={reason}
						maxlength={MAX_REASON}
						rows="4"
						placeholder="Describe the issue..."
						disabled={submitting}
					></textarea>
					<span class="char-count">{reason.length}/{MAX_REASON}</span>

					{#if error}
						<p class="error-msg">{error}</p>
					{/if}

					<div class="actions">
						<button type="button" class="btn-cancel" onclick={handleClose} disabled={submitting}>
							Cancel
						</button>
						<button type="submit" class="btn-submit" disabled={submitting || !reason.trim()}>
							{submitting ? 'Submitting...' : 'Submit Report'}
						</button>
					</div>
				</form>
			{/if}
		</div>
	</div>
{/if}

<style>
	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.6);
		display: grid;
		place-items: center;
		z-index: 200;
	}

	.modal {
		background: var(--bg-primary);
		border: 1px solid var(--border);
		border-radius: 12px;
		padding: 24px;
		width: 90%;
		max-width: 440px;
	}

	h2 {
		margin: 0 0 4px;
		font-size: 18px;
	}

	.context {
		color: var(--text-muted);
		font-size: 13px;
		margin: 0 0 16px;
	}

	label {
		display: block;
		font-size: 13px;
		font-weight: 600;
		margin-bottom: 6px;
		color: var(--text-dim);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	textarea {
		width: 100%;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 6px;
		padding: 10px;
		color: var(--text-normal);
		font-family: inherit;
		font-size: 14px;
		resize: vertical;
		box-sizing: border-box;
	}

	textarea:focus {
		outline: none;
		border-color: var(--accent);
	}

	.char-count {
		display: block;
		text-align: right;
		font-size: 11px;
		color: var(--text-muted);
		margin-top: 4px;
	}

	.error-msg {
		color: var(--danger);
		font-size: 13px;
		margin: 8px 0 0;
	}

	.success-msg {
		color: var(--success, #43b581);
		font-size: 14px;
		padding: 16px 0;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 8px;
		margin-top: 16px;
	}

	.btn-cancel, .btn-submit {
		padding: 8px 16px;
		border-radius: 6px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		border: none;
	}

	.btn-cancel {
		background: var(--bg-secondary);
		color: var(--text-normal);
	}

	.btn-cancel:hover {
		background: var(--bg-tertiary);
	}

	.btn-submit {
		background: var(--danger);
		color: white;
	}

	.btn-submit:hover:not(:disabled) {
		filter: brightness(1.1);
	}

	.btn-submit:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
</style>
