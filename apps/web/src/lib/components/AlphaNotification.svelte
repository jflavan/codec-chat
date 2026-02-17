<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const BUG_REPORT_URL = 'https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml';

	function dismiss(): void {
		app.dismissAlphaNotification();
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			dismiss();
		}
	}
</script>

{#if app.showAlphaNotification}
<div class="overlay" role="dialog" aria-modal="true" aria-label="Alpha notice" onkeydown={handleKeydown}>
	<div class="banner">
		<div class="badge">ALPHA</div>
		<h2>Welcome to Codec Alpha</h2>
		<p>
			You're using an early alpha build. Things may break, features may change, and
			bugs are expected. Your feedback is what makes this app better.
		</p>
		<p>
			Found a bug? Please report it on GitHub using our bug report template:
		</p>
		<a
			class="report-link"
			href={BUG_REPORT_URL}
			target="_blank"
			rel="noopener noreferrer"
		>
			<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
				<path d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z"/>
			</svg>
			Report a Bug on GitHub
		</a>
		<button class="dismiss-btn" onclick={dismiss}>Got it</button>
	</div>
</div>
{/if}

<style>
	.overlay {
		position: fixed;
		inset: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		background: rgba(0, 0, 0, 0.7);
		z-index: 100;
		animation: fade-in 200ms ease;
	}

	.banner {
		background: var(--bg-secondary);
		border: 1px solid var(--accent);
		border-radius: 12px;
		padding: 2rem 2.5rem;
		max-width: 480px;
		width: 90vw;
		text-align: center;
		box-shadow: 0 0 30px rgba(0, 255, 102, 0.15);
		animation: scale-in 200ms ease;
	}

	.badge {
		display: inline-block;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 0.7rem;
		letter-spacing: 0.15em;
		padding: 0.25rem 0.75rem;
		border-radius: 999px;
		margin-bottom: 1rem;
	}

	h2 {
		color: var(--text-header);
		font-size: 1.35rem;
		margin: 0 0 0.75rem;
	}

	p {
		color: var(--text-normal);
		font-size: 0.9rem;
		line-height: 1.5;
		margin: 0 0 0.75rem;
	}

	.report-link {
		display: inline-flex;
		align-items: center;
		gap: 0.5rem;
		color: var(--bg-tertiary);
		background: var(--accent);
		padding: 0.75rem 1.25rem;
		min-height: 44px;
		border-radius: 6px;
		text-decoration: none;
		font-weight: 600;
		font-size: 0.85rem;
		margin: 0.5rem 0 1.25rem;
		transition: background 150ms;
	}

	.report-link:hover {
		background: var(--accent-hover);
	}

	.dismiss-btn {
		display: block;
		width: 100%;
		background: transparent;
		border: 1px solid var(--border);
		color: var(--text-muted);
		padding: 0.75rem 1rem;
		min-height: 44px;
		border-radius: 6px;
		font-size: 0.85rem;
		cursor: pointer;
		transition: border-color 150ms, color 150ms;
	}

	.dismiss-btn:hover {
		border-color: var(--accent);
		color: var(--text-normal);
	}

	@keyframes fade-in {
		from { opacity: 0; }
		to { opacity: 1; }
	}

	@keyframes scale-in {
		from { opacity: 0; transform: scale(0.95); }
		to { opacity: 1; transform: scale(1); }
	}

	@media (prefers-reduced-motion: reduce) {
		.overlay,
		.banner {
			animation: none;
		}
	}
</style>
