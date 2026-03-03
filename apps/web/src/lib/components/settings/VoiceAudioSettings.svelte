<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let isRecordingKey = $state(false);
	let recorderEl: HTMLDivElement;

	$effect(() => {
		if (isRecordingKey && recorderEl) {
			recorderEl.focus();
		}
	});

	function handleModeChange(mode: 'voice-activity' | 'push-to-talk') {
		app.setVoiceInputMode(mode);
	}

	function startKeyRecording() {
		isRecordingKey = true;
	}

	function handleKeyCapture(e: KeyboardEvent) {
		e.preventDefault();
		e.stopPropagation();
		app.setPttKey(e.code);
		isRecordingKey = false;
	}

	/** Convert KeyboardEvent.code to a human-readable key name. */
	function formatKeyCode(code: string): string {
		if (code.startsWith('Key')) return code.slice(3);
		if (code.startsWith('Digit')) return code.slice(5);
		const map: Record<string, string> = {
			Space: 'Space',
			ShiftLeft: 'Left Shift',
			ShiftRight: 'Right Shift',
			ControlLeft: 'Left Ctrl',
			ControlRight: 'Right Ctrl',
			AltLeft: 'Left Alt',
			AltRight: 'Right Alt',
			MetaLeft: 'Left Meta',
			MetaRight: 'Right Meta',
			Backquote: '`',
			Minus: '-',
			Equal: '=',
			BracketLeft: '[',
			BracketRight: ']',
			Backslash: '\\',
			Semicolon: ';',
			Quote: "'",
			Comma: ',',
			Period: '.',
			Slash: '/',
			CapsLock: 'Caps Lock',
			Tab: 'Tab',
		};
		return map[code] ?? code;
	}
</script>

<section class="settings-section">
	<h2 class="settings-heading">Voice & Audio</h2>

	<div class="setting-group">
		<h3 class="setting-label">Input Mode</h3>
		<div class="radio-group">
			<label class="radio-option">
				<input
					type="radio"
					name="inputMode"
					value="voice-activity"
					checked={app.voiceInputMode === 'voice-activity'}
					onchange={() => handleModeChange('voice-activity')}
				/>
				<div class="radio-content">
					<span class="radio-title">Voice Activity</span>
					<span class="radio-desc">Microphone is always active when in a voice channel</span>
				</div>
			</label>
			<label class="radio-option">
				<input
					type="radio"
					name="inputMode"
					value="push-to-talk"
					checked={app.voiceInputMode === 'push-to-talk'}
					onchange={() => handleModeChange('push-to-talk')}
				/>
				<div class="radio-content">
					<span class="radio-title">Push to Talk</span>
					<span class="radio-desc">Microphone is only active while holding a key</span>
				</div>
			</label>
		</div>
	</div>

	{#if app.voiceInputMode === 'push-to-talk'}
		<div class="setting-group">
			<h3 class="setting-label">Push to Talk Key</h3>
			{#if isRecordingKey}
				<!-- svelte-ignore a11y_no_static_element_interactions -->
				<div class="key-recorder" bind:this={recorderEl} onkeydown={handleKeyCapture} tabindex="-1">
					<span class="recording-text">Press any key...</span>
				</div>
			{:else}
				<div class="key-display">
					<span class="current-key">{formatKeyCode(app.pttKey)}</span>
					<button class="record-btn" onclick={startKeyRecording}>
						Change Key
					</button>
				</div>
			{/if}
		</div>
	{/if}
</section>

<style>
	.settings-section {
		max-width: 600px;
	}

	.settings-heading {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 20px;
	}

	.setting-group {
		margin-bottom: 24px;
	}

	.setting-label {
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
		margin: 0 0 8px;
	}

	.radio-group {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.radio-option {
		display: flex;
		align-items: flex-start;
		gap: 10px;
		padding: 10px 12px;
		background: var(--bg-secondary, #2f3136);
		border-radius: 6px;
		cursor: pointer;
		transition: background-color 150ms ease;
	}

	.radio-option:hover {
		background: var(--bg-message-hover);
	}

	.radio-option input[type='radio'] {
		margin-top: 3px;
		accent-color: var(--accent, #5865f2);
	}

	.radio-content {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.radio-title {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
	}

	.radio-desc {
		font-size: 12px;
		color: var(--text-muted);
	}

	.key-display {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.current-key {
		display: inline-flex;
		align-items: center;
		padding: 6px 14px;
		background: var(--bg-secondary, #2f3136);
		border: 1px solid var(--border);
		border-radius: 4px;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		min-width: 40px;
		justify-content: center;
	}

	.record-btn {
		padding: 6px 14px;
		background: var(--accent, #5865f2);
		color: #fff;
		border: none;
		border-radius: 4px;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: background-color 150ms ease;
	}

	.record-btn:hover {
		background: var(--accent-hover, #4752c4);
	}

	.key-recorder {
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 12px;
		background: var(--bg-secondary, #2f3136);
		border: 2px solid var(--accent, #5865f2);
		border-radius: 6px;
		outline: none;
	}

	.recording-text {
		font-size: 14px;
		font-weight: 500;
		color: var(--accent, #5865f2);
		animation: pulse 1.5s ease-in-out infinite;
	}

	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50% { opacity: 0.5; }
	}
</style>
