<script lang="ts">
	let { channelName }: { channelName: string } = $props();
</script>

<div class="channel-welcome">
	<div class="crt-illustration" aria-hidden="true">
		<svg width="120" height="100" viewBox="0 0 120 100" fill="none" xmlns="http://www.w3.org/2000/svg">
			<!-- CRT monitor body -->
			<rect x="10" y="5" width="100" height="70" rx="6" stroke="var(--accent)" stroke-width="2" />
			<!-- Screen inner -->
			<rect x="18" y="13" width="84" height="54" rx="3" stroke="var(--accent)" stroke-width="1" opacity="0.5" />
			<!-- Screen glare line -->
			<line x1="22" y1="18" x2="40" y2="18" stroke="var(--accent)" stroke-width="1" opacity="0.3" />
			<!-- Stand neck -->
			<rect x="50" y="75" width="20" height="10" fill="var(--accent)" opacity="0.4" />
			<!-- Stand base -->
			<rect x="35" y="85" width="50" height="6" rx="3" stroke="var(--accent)" stroke-width="1.5" />
			<!-- Power LED -->
			<circle cx="60" cy="90" r="2" fill="var(--accent)" class="power-led" />
			<!-- Scan lines -->
			{#each Array(8) as _, i}
				<line x1="20" y1={17 + i * 7} x2="98" y2={17 + i * 7} stroke="var(--accent)" stroke-width="0.5" opacity="0.08" />
			{/each}
			<!-- Cursor on screen -->
			<rect x="24" y="38" width="8" height="12" fill="var(--accent)" opacity="0.6" class="screen-cursor" />
		</svg>
	</div>

	<div class="terminal-text">
		<p class="terminal-line"><span class="prompt">&gt;</span> channel <span class="channel-name">#{channelName}</span> initialized<span class="ellipsis">...</span></p>
		<p class="terminal-line"><span class="prompt">&gt;</span> connection established</p>
		<p class="terminal-line"><span class="prompt">&gt;</span> start transmitting<span class="cursor">_</span></p>
	</div>
</div>

<style>
	.channel-welcome {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		min-height: 420px;
		padding: 32px 16px;
		user-select: none;
	}

	.crt-illustration {
		margin-bottom: 24px;
		filter: drop-shadow(0 0 8px rgba(0, 255, 102, 0.3));
	}

	.power-led {
		animation: led-pulse 2s ease-in-out infinite;
	}

	.screen-cursor {
		animation: cursor-blink 1s step-end infinite;
	}

	.terminal-text {
		font-family: 'Space Grotesk', monospace;
		font-size: 15px;
		line-height: 1.8;
		text-align: left;
	}

	.terminal-line {
		margin: 0;
		color: var(--text-muted);
	}

	.prompt {
		color: var(--accent);
		font-weight: 600;
	}

	.channel-name {
		color: var(--accent);
		font-weight: 600;
	}

	.ellipsis {
		opacity: 0.6;
	}

	.cursor {
		color: var(--accent);
		animation: cursor-blink 1s step-end infinite;
	}

	@keyframes cursor-blink {
		0%, 100% { opacity: 1; }
		50% { opacity: 0; }
	}

	@keyframes led-pulse {
		0%, 100% { opacity: 0.8; }
		50% { opacity: 0.3; }
	}

	@media (prefers-reduced-motion: reduce) {
		.cursor,
		.screen-cursor {
			animation: none;
		}
		.power-led {
			animation: none;
			opacity: 0.6;
		}
	}

	@media (max-width: 768px) {
		.channel-welcome {
			min-height: 360px;
			padding: 24px 16px;
		}
		.terminal-text {
			font-size: 14px;
		}
	}
</style>
