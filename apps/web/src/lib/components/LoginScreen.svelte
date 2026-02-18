<script lang="ts">
	import { fade } from 'svelte/transition';
</script>

<div class="login-screen" transition:fade={{ duration: 300 }}>
	<div class="login-content">
		<div class="logo">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>
		<p class="tagline">Real-time chat, open source.</p>
		<div class="sign-in-section">
			<p class="sign-in-label">Sign in to start chatting</p>
			<div id="login-google-button"></div>
		</div>
	</div>
	<div class="scanlines"></div>
</div>

<style>
	.login-screen {
		position: fixed;
		inset: 0;
		z-index: 100;
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

	.login-content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 24px;
		z-index: 1;
		padding: 32px;
	}

	.logo {
		font-family: 'Space Grotesk', monospace;
		font-size: 56px;
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

	.tagline {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 16px;
		color: var(--text-muted);
		letter-spacing: 2px;
	}

	.sign-in-section {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 16px;
		margin-top: 16px;
	}

	.sign-in-label {
		margin: 0;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		letter-spacing: 1px;
		text-transform: uppercase;
	}

	@keyframes glow {
		from {
			text-shadow: 0 0 20px rgba(0, 255, 102, 0.4), 0 0 40px rgba(0, 255, 102, 0.15);
		}
		to {
			text-shadow: 0 0 30px rgba(0, 255, 102, 0.6), 0 0 60px rgba(0, 255, 102, 0.3);
		}
	}

	@media (max-width: 768px) {
		.logo {
			font-size: 36px;
			letter-spacing: 4px;
		}

		.tagline {
			font-size: 14px;
		}
	}

	@media (prefers-reduced-motion: reduce) {
		.logo {
			animation: none;
		}
	}
</style>
