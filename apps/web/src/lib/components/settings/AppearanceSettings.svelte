<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { THEMES, type ThemeId } from '$lib/utils/theme.js';

	const app = getAppState();

	const themeColors: Record<ThemeId, { bg: string; sidebar: string; accent: string; text: string }> = {
		phosphor: { bg: '#0B1A10', sidebar: '#07110A', accent: '#00FF66', text: '#86FF6B' },
		midnight: { bg: '#1E2233', sidebar: '#171B2E', accent: '#5B8DEF', text: '#C8CDD8' },
		ember: { bg: '#1F1710', sidebar: '#18120C', accent: '#F0A030', text: '#D4C4A8' },
		light: { bg: '#FFFFFF', sidebar: '#F5F5F7', accent: '#0055CC', text: '#1D1D1F' }
	};
</script>

<section class="settings-section">
	<h2 class="settings-heading">Appearance</h2>
	<p class="section-description">Choose a theme for Codec.</p>

	<div class="theme-grid">
		{#each THEMES as theme}
			{@const colors = themeColors[theme.id]}
			{@const isActive = app.theme === theme.id}
			<button
				class="theme-card"
				class:active={isActive}
				onclick={() => app.setTheme(theme.id)}
				style="--card-border: {isActive ? colors.accent : 'var(--border)'}"
			>
				<div class="theme-preview">
					<div class="preview-sidebar" style="background: {colors.sidebar}"></div>
					<div class="preview-main" style="background: {colors.bg}">
						<div class="preview-text" style="background: {colors.text}"></div>
						<div class="preview-text short" style="background: {colors.text}"></div>
						<div class="preview-accent" style="background: {colors.accent}"></div>
					</div>
				</div>
				<span class="theme-name">{theme.name}</span>
				{#if isActive}
					<span class="theme-check">&#10003;</span>
				{/if}
			</button>
		{/each}
	</div>
</section>

<style>
	.settings-section {
		max-width: 600px;
	}

	.settings-heading {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 4px;
	}

	.section-description {
		color: var(--text-muted);
		font-size: 13px;
		margin: 0 0 20px;
	}

	.theme-grid {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: 12px;
	}

	.theme-card {
		position: relative;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		padding: 12px;
		background: var(--bg-secondary);
		border: 2px solid var(--card-border);
		border-radius: 8px;
		cursor: pointer;
		transition: border-color 150ms ease;
	}

	.theme-card:hover {
		border-color: var(--text-muted);
	}

	.theme-card.active:hover {
		border-color: var(--card-border);
	}

	.theme-preview {
		display: flex;
		width: 100%;
		height: 60px;
		border-radius: 4px;
		overflow: hidden;
		border: 1px solid var(--border);
	}

	.preview-sidebar {
		width: 25%;
	}

	.preview-main {
		flex: 1;
		display: flex;
		flex-direction: column;
		justify-content: center;
		gap: 4px;
		padding: 8px;
	}

	.preview-text {
		height: 4px;
		border-radius: 2px;
		width: 70%;
		opacity: 0.7;
	}

	.preview-text.short {
		width: 45%;
	}

	.preview-accent {
		height: 4px;
		border-radius: 2px;
		width: 30%;
	}

	.theme-name {
		color: var(--text-normal);
		font-size: 13px;
		font-weight: 500;
	}

	.theme-check {
		position: absolute;
		top: 6px;
		right: 8px;
		color: var(--accent);
		font-size: 14px;
		font-weight: 700;
	}

	@media (max-width: 480px) {
		.theme-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
