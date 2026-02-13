<script lang="ts">
	import { fade } from 'svelte/transition';
</script>

<div class="loading-screen" transition:fade={{ duration: 300 }}>
	<div class="content">
		<div class="logo">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>
		<div class="loader">
			<div class="loader-bar"></div>
		</div>
		<p class="status">Initializing<span class="dots"></span></p>
	</div>
	<div class="scanlines"></div>
</div>

<style>
	.loading-screen {
		position: fixed;
		inset: 0;
		z-index: 9999;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-tertiary);
		overflow: hidden;
	}

	.scanlines {
		position: absolute;
		inset: 0;
		pointer-events: none;
		background: repeating-linear-gradient(
			to bottom,
			transparent,
			transparent 2px,
			rgba(0, 0, 0, 0.15) 2px,
			rgba(0, 0, 0, 0.15) 4px
		);
	}

	.content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 24px;
		z-index: 1;
	}

	.logo {
		font-family: 'Space Grotesk', monospace;
		font-size: 48px;
		font-weight: 700;
		letter-spacing: 8px;
		color: var(--accent);
		text-shadow: 0 0 20px rgba(0, 255, 102, 0.5), 0 0 40px rgba(0, 255, 102, 0.2);
		animation: glow 2s ease-in-out infinite alternate;
	}

	.bracket {
		color: var(--text-muted);
	}

	.name {
		color: var(--accent);
	}

	.loader {
		width: 200px;
		height: 3px;
		background: var(--border);
		border-radius: 2px;
		overflow: hidden;
	}

	.loader-bar {
		width: 40%;
		height: 100%;
		background: var(--accent);
		border-radius: 2px;
		box-shadow: 0 0 8px rgba(0, 255, 102, 0.6);
		animation: slide 1.2s ease-in-out infinite;
	}

	.status {
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
		color: var(--text-muted);
		letter-spacing: 2px;
		text-transform: uppercase;
		margin: 0;
	}

	.dots::after {
		content: '';
		animation: dots 1.5s steps(4, end) infinite;
	}

	@keyframes slide {
		0% {
			transform: translateX(-100%);
		}
		50% {
			transform: translateX(250%);
		}
		100% {
			transform: translateX(-100%);
		}
	}

	@keyframes glow {
		from {
			text-shadow: 0 0 20px rgba(0, 255, 102, 0.4), 0 0 40px rgba(0, 255, 102, 0.15);
		}
		to {
			text-shadow: 0 0 30px rgba(0, 255, 102, 0.6), 0 0 60px rgba(0, 255, 102, 0.3);
		}
	}

	@keyframes dots {
		0% { content: ''; }
		25% { content: '.'; }
		50% { content: '..'; }
		75% { content: '...'; }
	}

	@media (prefers-reduced-motion: reduce) {
		.loader-bar {
			animation: none;
			width: 100%;
		}
		.logo {
			animation: none;
		}
		.dots::after {
			animation: none;
			content: '...';
		}
	}
</style>
