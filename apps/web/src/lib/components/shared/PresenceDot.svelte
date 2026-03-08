<script lang="ts">
	import type { PresenceStatus } from '$lib/types/models.js';

	let { status = 'offline' }: { status?: PresenceStatus } = $props();
</script>

<span class="presence-dot presence-{status}" aria-label="{status}" role="img"></span>

<style>
	.presence-dot {
		display: block;
		width: 10px;
		height: 10px;
		border-radius: 50%;
		border: 2px solid var(--bg-secondary);
		flex-shrink: 0;
	}

	.presence-online {
		background: #23a55a;
	}

	.presence-idle {
		background: #f0b232;
	}

	.presence-offline {
		background: #80848e;
	}
</style>
