<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	function handleClose(): void {
		app.closeImagePreview();
	}

	function handleBackdropClick(e: MouseEvent): void {
		if (e.target === e.currentTarget) {
			handleClose();
		}
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			e.preventDefault();
			handleClose();
		}
	}
</script>

{#if app.lightboxImageUrl}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="lightbox-backdrop"
		role="dialog"
		aria-label="Image preview"
		aria-modal="true"
		tabindex="-1"
		onclick={handleBackdropClick}
		onkeydown={handleKeydown}
	>
		<div class="lightbox-content">
			<div class="lightbox-toolbar">
				<a
					class="toolbar-btn"
					href={app.lightboxImageUrl}
					target="_blank"
					rel="noopener noreferrer"
					title="Open original"
					aria-label="Open original image in new tab"
				>
					<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M8.636 3.5a.5.5 0 0 0-.5-.5H1.5A1.5 1.5 0 0 0 0 4.5v10A1.5 1.5 0 0 0 1.5 16h10a1.5 1.5 0 0 0 1.5-1.5V7.864a.5.5 0 0 0-1 0V14.5a.5.5 0 0 1-.5.5h-10a.5.5 0 0 1-.5-.5v-10a.5.5 0 0 1 .5-.5h6.636a.5.5 0 0 0 .5-.5z"/>
						<path d="M16 .5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0 0 1h3.793L6.146 9.146a.5.5 0 1 0 .708.708L15 1.707V5.5a.5.5 0 0 0 1 0v-5z"/>
					</svg>
				</a>
				<button
					class="toolbar-btn close-btn"
					onclick={handleClose}
					aria-label="Close image preview"
				>
					<svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
					</svg>
				</button>
			</div>
			<img
				src={app.lightboxImageUrl}
				alt="Full-size preview"
				class="lightbox-image"
			/>
		</div>
	</div>
{/if}

<svelte:window onkeydown={app.lightboxImageUrl ? handleKeydown : undefined} />

<style>
	.lightbox-backdrop {
		position: fixed;
		inset: 0;
		z-index: 9999;
		display: grid;
		place-items: center;
		background: rgba(0, 0, 0, 0.85);
		overflow: hidden;
	}

	.lightbox-content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		max-width: 90vw;
		max-height: 90vh;
		pointer-events: none;
	}

	.lightbox-toolbar {
		display: flex;
		align-items: center;
		gap: 8px;
		align-self: flex-end;
		pointer-events: auto;
	}

	.toolbar-btn {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 44px;
		height: 44px;
		border-radius: 8px;
		border: none;
		background: rgba(255, 255, 255, 0.1);
		color: rgba(255, 255, 255, 0.8);
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
		text-decoration: none;
	}

	.toolbar-btn:hover {
		background: rgba(255, 255, 255, 0.2);
		color: #fff;
	}

	.close-btn {
		width: 44px;
		height: 44px;
	}

	.lightbox-image {
		max-width: 90vw;
		max-height: calc(90vh - 52px);
		object-fit: contain;
		border-radius: 4px;
		box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
		user-select: none;
		pointer-events: auto;
	}
</style>
