<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';

	const auth = getAuthStore();
	const ui = getUIStore();

	let dialogEl: HTMLDialogElement;
	let titleInputEl = $state<HTMLInputElement>(undefined!);
	let title = $state('');
	let description = $state('');
	let submitting = $state(false);
	let successUrl = $state<string | null>(null);
	let error = $state<string | null>(null);
	let previousFocus: HTMLElement | null = null;

	$effect(() => {
		if (ui.bugReportOpen) {
			previousFocus = document.activeElement as HTMLElement | null;
			title = '';
			description = '';
			successUrl = null;
			error = null;
			dialogEl?.showModal();
			// Focus first input after dialog opens
			requestAnimationFrame(() => titleInputEl?.focus());
		} else {
			dialogEl?.close();
			previousFocus?.focus();
		}
	});

	function close() {
		ui.bugReportOpen = false;
	}

	function handleBackdropClick(e: MouseEvent) {
		if (e.target === dialogEl) close();
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault();
			close();
		}
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (submitting || !title.trim() || !description.trim()) return;

		submitting = true;
		error = null;

		try {
			const result = await auth.submitBugReport(
				title.trim(),
				description.trim(),
				navigator.userAgent,
				window.location.pathname
			);
			successUrl = result.issueUrl;
		} catch {
			error = 'Something went wrong. Please try again.';
		} finally {
			submitting = false;
		}
	}
</script>

<dialog
	bind:this={dialogEl}
	class="bug-report-dialog"
	aria-modal="true"
	aria-labelledby="bug-report-title"
	onclick={handleBackdropClick}
	onkeydown={handleKeydown}
>
	<div class="bug-report-panel" role="document">
		<button class="close-btn" onclick={close} aria-label="Close dialog" title="Close">
			<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/>
			</svg>
		</button>

		<h2 id="bug-report-title">Report a Bug</h2>

		{#if successUrl}
			<div class="success">
				<p>Bug report submitted! Thanks for helping improve Codec.</p>
				<a href={successUrl} target="_blank" rel="noopener noreferrer">View on GitHub</a>
				<button class="dismiss-btn" onclick={close}>Close</button>
			</div>
		{:else}
			<form onsubmit={handleSubmit}>
				<label class="field">
					<span class="label-text">Title</span>
					<input
						type="text"
						bind:value={title}
						bind:this={titleInputEl}
						maxlength={200}
						placeholder="Brief summary of the issue"
						required
						disabled={submitting}
					/>
				</label>

				<label class="field">
					<span class="label-text">Description</span>
					<textarea
						bind:value={description}
						maxlength={5000}
						rows={6}
						placeholder="What happened? What did you expect?"
						required
						disabled={submitting}
					></textarea>
				</label>

				{#if error}
					<p class="error" role="alert" aria-live="assertive">{error}</p>
				{/if}

				<button type="submit" class="submit-btn" disabled={submitting || !title.trim() || !description.trim()}>
					{submitting ? 'Submitting...' : 'Submit Bug Report'}
				</button>
			</form>
		{/if}
	</div>
</dialog>

<style>
	.bug-report-dialog {
		position: fixed;
		inset: 0;
		width: 100vw;
		height: 100vh;
		max-width: 100vw;
		max-height: 100vh;
		margin: 0;
		padding: 0;
		border: none;
		background: transparent;
		z-index: 60;
		display: grid;
		place-items: center;
	}

	.bug-report-dialog::backdrop {
		background: rgba(0, 0, 0, 0.7);
	}

	.bug-report-panel {
		background: var(--bg-secondary);
		border-radius: 8px;
		padding: 24px;
		width: 90vw;
		max-width: 480px;
		position: relative;
	}

	.close-btn {
		position: absolute;
		top: 12px;
		right: 12px;
		background: none;
		border: none;
		padding: 6px;
		border-radius: 50%;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		min-width: 44px;
		min-height: 44px;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.close-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	h2 {
		color: var(--text-header);
		font-size: 1.25rem;
		margin: 0 0 16px;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 4px;
		margin-bottom: 12px;
	}

	.label-text {
		color: var(--text-muted);
		font-size: 0.8rem;
		font-weight: 600;
		text-transform: uppercase;
	}

	input, textarea {
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		padding: 8px 12px;
		font-size: 0.9rem;
		font-family: inherit;
		resize: vertical;
	}

	input:focus, textarea:focus {
		outline: none;
		border-color: var(--accent);
	}

	input:disabled, textarea:disabled {
		opacity: 0.6;
	}

	.error {
		color: var(--danger, #f04747);
		font-size: 0.85rem;
		margin: 0 0 8px;
	}

	.submit-btn {
		width: 100%;
		padding: 8px 16px;
		min-height: 44px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 0.9rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 150ms;
	}

	.submit-btn:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.submit-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.success {
		text-align: center;
	}

	.success p {
		color: var(--text-normal);
		margin: 0 0 12px;
	}

	.success a {
		color: var(--accent);
		text-decoration: underline;
		font-size: 0.9rem;
	}

	.dismiss-btn {
		display: block;
		width: 100%;
		margin-top: 12px;
		padding: 8px 16px;
		min-height: 44px;
		background: transparent;
		border: 1px solid var(--border);
		color: var(--text-muted);
		border-radius: 3px;
		font-size: 0.85rem;
		cursor: pointer;
		transition: border-color 150ms, color 150ms;
	}

	.dismiss-btn:hover {
		border-color: var(--accent);
		color: var(--text-normal);
	}
</style>
