<script lang="ts">
	let {
		announcement,
		onDismiss
	}: {
		announcement: { id: string; title: string; body: string };
		onDismiss: (id: string) => void;
	} = $props();
</script>

<div class="announcement-banner" role="status">
	<div class="banner-accent-edge"></div>
	<div class="banner-content">
		<svg class="info-icon" width="18" height="18" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
			<path d="M8 1a7 7 0 1 1 0 14A7 7 0 0 1 8 1Zm0 1a6 6 0 1 0 0 12A6 6 0 0 0 8 2Zm-.5 3h1v1h-1V5Zm0 2h1v5h-1V7Z"/>
		</svg>
		<span class="banner-title">{announcement.title}</span>
		<span class="banner-body">{announcement.body}</span>
	</div>
	<button
		class="dismiss-btn"
		onclick={() => onDismiss(announcement.id)}
		title="Dismiss announcement"
		aria-label="Dismiss announcement"
	>
		<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
			<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708Z"/>
		</svg>
	</button>
</div>

<style>
	.announcement-banner {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 10px 16px;
		background: var(--banner-bg);
		font-size: 14px;
		min-height: 40px;
		flex-shrink: 0;
		border-bottom: 1px solid var(--banner-border);
		position: relative;
	}

	.banner-accent-edge {
		position: absolute;
		left: 0;
		top: 0;
		bottom: 0;
		width: 3px;
		background: var(--banner-icon);
	}

	.banner-content {
		display: flex;
		align-items: center;
		gap: 8px;
		flex: 1;
		min-width: 0;
	}

	.info-icon {
		flex-shrink: 0;
		color: var(--banner-icon);
	}

	.banner-title {
		font-weight: 600;
		flex-shrink: 0;
		color: var(--banner-title);
	}

	.banner-body {
		color: var(--banner-body);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.dismiss-btn {
		flex-shrink: 0;
		background: none;
		border: none;
		color: var(--banner-dismiss);
		cursor: pointer;
		padding: 4px;
		border-radius: 4px;
		display: grid;
		place-items: center;
	}

	.dismiss-btn:hover {
		color: var(--banner-dismiss-hover);
		background: var(--banner-dismiss-hover-bg);
	}
</style>
