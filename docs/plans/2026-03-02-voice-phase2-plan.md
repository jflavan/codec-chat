# Voice Phase 2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add per-user volume control (Web Audio GainNode + right-click context menu) and push-to-talk (configurable keybind with Voice Activity / PTT mode toggle in settings) to voice channels.

**Architecture:** Both features are entirely client-side — no backend changes needed. Volume uses Web Audio API `GainNode` per consumer track with localStorage persistence. PTT uses global `keydown`/`keyup` listeners to pause/resume the mediasoup producer, with preferences stored in localStorage. A new "Voice & Audio" settings tab hosts the PTT configuration.

**Tech Stack:** Svelte 5, TypeScript, Web Audio API, mediasoup-client, localStorage

---

### Task 1: Replace HTMLAudioElement with Web Audio GainNode Pipeline

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts` (lines 153-159, 234-238, 1467-1481, 1502-1525, 1826-1829)

**Step 1: Add new private fields for Web Audio pipeline**

In the `/* ───── internals ─────` section (after line 238), replace `remoteAudioElements` and add new fields:

```typescript
// Replace line 238:
// private remoteAudioElements = new Map<string, HTMLAudioElement>();
// With:
private audioContext: AudioContext | null = null;
private audioNodes = new Map<string, { source: MediaStreamAudioSourceNode; gain: GainNode }>();
/** participantId → userId lookup for volume. Built during attach. */
private audioParticipantUserMap = new Map<string, string>();
```

**Step 2: Add public volume state (in the `/* ───── voice ─────` section, after line 159)**

```typescript
/** Per-user volume levels: userId → 0.0–1.0. Loaded from localStorage on init. */
userVolumes = $state<Map<string, number>>(new Map());
```

**Step 3: Rewrite `_attachRemoteAudio` (replace lines 1502-1508)**

The signature changes to accept `userId` so we can look up saved volume:

```typescript
private _attachRemoteAudio(participantId: string, userId: string, track: MediaStreamTrack): void {
	if (!this.audioContext) {
		this.audioContext = new AudioContext();
	}
	const source = this.audioContext.createMediaStreamSource(new MediaStream([track]));
	const gain = this.audioContext.createGain();
	const volume = this.isDeafened ? 0 : (this.userVolumes.get(userId) ?? 1.0);
	gain.gain.value = volume;
	source.connect(gain);
	gain.connect(this.audioContext.destination);
	this.audioNodes.set(participantId, { source, gain });
	this.audioParticipantUserMap.set(participantId, userId);
}
```

**Step 4: Rewrite `_detachRemoteAudio` (replace lines 1510-1517)**

```typescript
private _detachRemoteAudio(participantId: string): void {
	const nodes = this.audioNodes.get(participantId);
	if (nodes) {
		nodes.source.disconnect();
		nodes.gain.disconnect();
		this.audioNodes.delete(participantId);
	}
	this.audioParticipantUserMap.delete(participantId);
}
```

**Step 5: Rewrite `_cleanupRemoteAudio` (replace lines 1519-1525)**

```typescript
private _cleanupRemoteAudio(): void {
	for (const nodes of this.audioNodes.values()) {
		nodes.source.disconnect();
		nodes.gain.disconnect();
	}
	this.audioNodes.clear();
	this.audioParticipantUserMap.clear();
}
```

**Step 6: Update `toggleDeafen` (replace lines 1476-1479)**

Replace the `remoteAudioElements` loop with GainNode manipulation:

```typescript
// Mute/unmute remote audio based on deafened state
for (const [pid, nodes] of this.audioNodes) {
	const userId = this.audioParticipantUserMap.get(pid);
	nodes.gain.gain.value = this.isDeafened ? 0 : (this.userVolumes.get(userId ?? '') ?? 1.0);
}
```

**Step 7: Update `teardownVoiceSync` (after line 1828)**

Add AudioContext cleanup:

```typescript
teardownVoiceSync(): void {
	this.voice.teardownSync();
	this._cleanupRemoteAudio();
	this.audioContext?.close().catch(() => {});
	this.audioContext = null;
}
```

**Step 8: Update all `_attachRemoteAudio` call sites to pass userId**

There are two call sites that pass callbacks:

1. In `joinVoiceChannel()` (line 1376): The callbacks are created before the members list is known. We need to look up the userId from `voiceChannelMembers` at call time:

```typescript
onNewTrack: (pid, track) => {
	const userId = this._findUserIdByParticipant(pid);
	this._attachRemoteAudio(pid, userId, track);
},
```

2. In `onNewProducer` (line 1809): The event already has `userId`:

```typescript
onNewTrack: (pid, track) => this._attachRemoteAudio(pid, event.userId, track),
```

3. Add the lookup helper:

```typescript
private _findUserIdByParticipant(participantId: string): string {
	if (!this.activeVoiceChannelId) return '';
	const members = this.voiceChannelMembers.get(this.activeVoiceChannelId) ?? [];
	return members.find((m) => m.participantId === participantId)?.userId ?? '';
}
```

**Step 9: Verify build**

Run: `cd apps/web && npm run check`

**Step 10: Commit**

```
feat(voice): replace HTMLAudioElement with Web Audio GainNode pipeline
```

---

### Task 2: Add Per-User Volume Persistence and Control Method

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add localStorage load/save helpers**

Add near the other private methods (after `_cleanupRemoteAudio`):

```typescript
private _loadUserVolumes(): void {
	try {
		const raw = localStorage.getItem('codec-user-volumes');
		if (raw) {
			const parsed = JSON.parse(raw) as Record<string, number>;
			this.userVolumes = new Map(Object.entries(parsed));
		}
	} catch {
		// ignore corrupt data
	}
}

private _saveUserVolumes(): void {
	const obj: Record<string, number> = {};
	for (const [k, v] of this.userVolumes) {
		obj[k] = v;
	}
	localStorage.setItem('codec-user-volumes', JSON.stringify(obj));
}
```

**Step 2: Add public `setUserVolume` method**

Add in the voice actions section (after `toggleDeafen`):

```typescript
setUserVolume(userId: string, volume: number): void {
	const clamped = Math.max(0, Math.min(1, volume));
	const updated = new Map(this.userVolumes);
	if (clamped === 1.0) {
		updated.delete(userId); // default = no entry
	} else {
		updated.set(userId, clamped);
	}
	this.userVolumes = updated;
	this._saveUserVolumes();

	// Apply to any active audio node for this user
	if (!this.isDeafened) {
		for (const [pid, uid] of this.audioParticipantUserMap) {
			if (uid === userId) {
				const nodes = this.audioNodes.get(pid);
				if (nodes) nodes.gain.gain.value = clamped;
			}
		}
	}
}

resetUserVolume(userId: string): void {
	this.setUserVolume(userId, 1.0);
}
```

**Step 3: Call `_loadUserVolumes()` during initialization**

In the `init()` method (or wherever initial state is loaded after sign-in), add:

```typescript
this._loadUserVolumes();
```

Find the right place by searching for `_loadAllVoiceStates` calls — add `_loadUserVolumes` nearby, early in init.

**Step 4: Verify build**

Run: `cd apps/web && npm run check`

**Step 5: Commit**

```
feat(voice): add per-user volume persistence and control methods
```

---

### Task 3: Create VoiceMemberContextMenu Component

**Files:**
- Create: `apps/web/src/lib/components/voice/VoiceMemberContextMenu.svelte`

**Step 1: Create the context menu component**

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let {
		userId,
		displayName,
		x,
		y,
		onclose,
	}: {
		userId: string;
		displayName: string;
		x: number;
		y: number;
		onclose: () => void;
	} = $props();

	const app = getAppState();

	let sliderValue = $state(Math.round((app.userVolumes.get(userId) ?? 1.0) * 100));

	function handleVolumeChange(e: Event) {
		const val = parseInt((e.target as HTMLInputElement).value, 10);
		sliderValue = val;
		app.setUserVolume(userId, val / 100);
	}

	function handleReset() {
		sliderValue = 100;
		app.resetUserVolume(userId);
	}

	function handleClickOutside(e: MouseEvent) {
		if (!(e.target as Element).closest('.voice-context-menu')) {
			onclose();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') onclose();
	}

	// Clamp position to viewport
	const menuWidth = 200;
	const menuHeight = 120;
	const clampedX = $derived(Math.min(x, window.innerWidth - menuWidth - 8));
	const clampedY = $derived(Math.min(y, window.innerHeight - menuHeight - 8));
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="voice-context-overlay" onclick={handleClickOutside} onkeydown={handleKeydown}>
	<div
		class="voice-context-menu"
		style="left: {clampedX}px; top: {clampedY}px;"
		role="menu"
	>
		<div class="context-header">{displayName}</div>
		<div class="volume-control">
			<label class="volume-label">
				Volume
				<span class="volume-value">{sliderValue}%</span>
			</label>
			<input
				type="range"
				min="0"
				max="100"
				value={sliderValue}
				oninput={handleVolumeChange}
				class="volume-slider"
			/>
		</div>
		{#if sliderValue !== 100}
			<button class="reset-btn" onclick={handleReset} role="menuitem">
				Reset Volume
			</button>
		{/if}
	</div>
</div>

<style>
	.voice-context-overlay {
		position: fixed;
		inset: 0;
		z-index: 100;
	}

	.voice-context-menu {
		position: fixed;
		width: 200px;
		background: var(--bg-secondary, #2f3136);
		border: 1px solid var(--border);
		border-radius: 6px;
		padding: 8px;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
		z-index: 101;
	}

	.context-header {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-header);
		padding: 4px 4px 8px;
		border-bottom: 1px solid var(--border);
		margin-bottom: 8px;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.volume-control {
		padding: 0 4px;
	}

	.volume-label {
		display: flex;
		justify-content: space-between;
		font-size: 11px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.04em;
		margin-bottom: 6px;
	}

	.volume-value {
		color: var(--text-normal);
	}

	.volume-slider {
		width: 100%;
		height: 4px;
		-webkit-appearance: none;
		appearance: none;
		background: var(--bg-tertiary);
		border-radius: 2px;
		outline: none;
		cursor: pointer;
	}

	.volume-slider::-webkit-slider-thumb {
		-webkit-appearance: none;
		appearance: none;
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent, #5865f2);
		cursor: pointer;
	}

	.volume-slider::-moz-range-thumb {
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--accent, #5865f2);
		cursor: pointer;
		border: none;
	}

	.reset-btn {
		display: block;
		width: 100%;
		padding: 6px 4px;
		margin-top: 8px;
		background: none;
		border: none;
		border-radius: 4px;
		color: var(--text-muted);
		font-size: 13px;
		text-align: left;
		cursor: pointer;
	}

	.reset-btn:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}
</style>
```

**Step 2: Verify build**

Run: `cd apps/web && npm run check`

**Step 3: Commit**

```
feat(voice): add VoiceMemberContextMenu component with volume slider
```

---

### Task 4: Wire Context Menu into Channel Sidebar

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

**Step 1: Import the context menu and add state**

Add to the `<script>` section:

```typescript
import VoiceMemberContextMenu from '$lib/components/voice/VoiceMemberContextMenu.svelte';

let contextMenu = $state<{ userId: string; displayName: string; x: number; y: number } | null>(null);
```

**Step 2: Add right-click handler to voice member list items**

In the voice member `<li>` element (around line 130), add a `contextmenu` handler. Don't add it to the user's own entry (no self-volume control needed):

```svelte
<li
	class="voice-member"
	oncontextmenu={(e) => {
		if (member.userId !== app.me?.user.id) {
			e.preventDefault();
			contextMenu = { userId: member.userId, displayName: member.displayName, x: e.clientX, y: e.clientY };
		}
	}}
>
```

**Step 3: Render the context menu at the bottom of the component**

```svelte
{#if contextMenu}
	<VoiceMemberContextMenu
		userId={contextMenu.userId}
		displayName={contextMenu.displayName}
		x={contextMenu.x}
		y={contextMenu.y}
		onclose={() => { contextMenu = null; }}
	/>
{/if}
```

**Step 4: Verify build**

Run: `cd apps/web && npm run check`

**Step 5: Commit**

```
feat(voice): wire volume context menu into channel sidebar
```

---

### Task 5: Add Voice Preferences State and localStorage Persistence

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add voice preference state fields (in the `/* ───── voice ─────` section, after the `userVolumes` line)**

```typescript
/** Input mode: 'voice-activity' (always-on mic) or 'push-to-talk'. */
voiceInputMode = $state<'voice-activity' | 'push-to-talk'>('voice-activity');
/** KeyboardEvent.code for push-to-talk key. */
pttKey = $state('KeyV');
/** True while the PTT key is held down. */
isPttActive = $state(false);
```

**Step 2: Add load/save helpers for voice preferences**

```typescript
private _loadVoicePreferences(): void {
	try {
		const raw = localStorage.getItem('codec-voice-preferences');
		if (raw) {
			const parsed = JSON.parse(raw) as { inputMode?: string; pttKey?: string };
			if (parsed.inputMode === 'voice-activity' || parsed.inputMode === 'push-to-talk') {
				this.voiceInputMode = parsed.inputMode;
			}
			if (parsed.pttKey) {
				this.pttKey = parsed.pttKey;
			}
		}
	} catch {
		// ignore corrupt data
	}
}

private _saveVoicePreferences(): void {
	localStorage.setItem('codec-voice-preferences', JSON.stringify({
		inputMode: this.voiceInputMode,
		pttKey: this.pttKey,
	}));
}
```

**Step 3: Add public setters for settings UI**

```typescript
setVoiceInputMode(mode: 'voice-activity' | 'push-to-talk'): void {
	this.voiceInputMode = mode;
	this._saveVoicePreferences();

	if (this.activeVoiceChannelId) {
		if (mode === 'push-to-talk') {
			// Switch to PTT: pause producer, register listeners
			this.voice.setMuted(true);
			this.isPttActive = false;
			this._registerPttListeners();
		} else {
			// Switch to VA: resume producer (if not manually muted), remove listeners
			this._removePttListeners();
			this.isPttActive = false;
			if (!this.isMuted) {
				this.voice.setMuted(false);
			}
		}
	}
}

setPttKey(code: string): void {
	this.pttKey = code;
	this._saveVoicePreferences();
}
```

**Step 4: Call `_loadVoicePreferences()` during initialization**

Add alongside `_loadUserVolumes()` in the init path.

**Step 5: Verify build**

Run: `cd apps/web && npm run check`

**Step 6: Commit**

```
feat(voice): add voice preferences state with localStorage persistence
```

---

### Task 6: Implement PTT Keyboard Handling

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add PTT listener registration/removal methods**

```typescript
private pttKeydownHandler: ((e: KeyboardEvent) => void) | null = null;
private pttKeyupHandler: ((e: KeyboardEvent) => void) | null = null;

private _registerPttListeners(): void {
	this._removePttListeners();

	this.pttKeydownHandler = (e: KeyboardEvent) => {
		if (e.code !== this.pttKey || e.repeat) return;
		// Don't activate PTT while typing in text fields
		const tag = (document.activeElement as HTMLElement)?.tagName;
		if (tag === 'INPUT' || tag === 'TEXTAREA' || (document.activeElement as HTMLElement)?.isContentEditable) return;

		this.isPttActive = true;
		this.voice.setMuted(false);
	};

	this.pttKeyupHandler = (e: KeyboardEvent) => {
		if (e.code !== this.pttKey) return;

		this.isPttActive = false;
		this.voice.setMuted(true);
	};

	window.addEventListener('keydown', this.pttKeydownHandler);
	window.addEventListener('keyup', this.pttKeyupHandler);
}

private _removePttListeners(): void {
	if (this.pttKeydownHandler) {
		window.removeEventListener('keydown', this.pttKeydownHandler);
		this.pttKeydownHandler = null;
	}
	if (this.pttKeyupHandler) {
		window.removeEventListener('keyup', this.pttKeyupHandler);
		this.pttKeyupHandler = null;
	}
}
```

**Step 2: Update `joinVoiceChannel` — start paused in PTT mode**

After the producer is created (after `this.isMuted = false;` line in `joinVoiceChannel`), add PTT initialization:

```typescript
// After: this.isMuted = false; this.isDeafened = false;
// Add:
if (this.voiceInputMode === 'push-to-talk') {
	this.voice.setMuted(true);
	this.isPttActive = false;
	this._registerPttListeners();
}
```

**Step 3: Update `leaveVoiceChannel` — remove PTT listeners**

After `this.isDeafened = false;` in `leaveVoiceChannel`, add:

```typescript
this.isPttActive = false;
this._removePttListeners();
```

**Step 4: Update `teardownVoiceSync` to remove PTT listeners**

```typescript
teardownVoiceSync(): void {
	this.voice.teardownSync();
	this._cleanupRemoteAudio();
	this._removePttListeners();
	this.audioContext?.close().catch(() => {});
	this.audioContext = null;
}
```

**Step 5: Update voice disconnect handler to remove PTT listeners**

Find the `onReconnecting` handler that tears down voice. Add `this._removePttListeners()` there.

**Step 6: Verify build**

Run: `cd apps/web && npm run check`

**Step 7: Commit**

```
feat(voice): implement push-to-talk keyboard handling
```

---

### Task 7: Create Voice & Audio Settings Component

**Files:**
- Create: `apps/web/src/lib/components/settings/VoiceAudioSettings.svelte`

**Step 1: Create the component**

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let isRecordingKey = $state(false);

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
				<div class="key-recorder" onkeydown={handleKeyCapture} tabindex="-1">
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
```

**Step 2: Auto-focus the key recorder when it appears**

Add an `$effect` in the script:

```typescript
let recorderEl: HTMLDivElement;

$effect(() => {
	if (isRecordingKey && recorderEl) {
		recorderEl.focus();
	}
});
```

And bind it: `<div class="key-recorder" bind:this={recorderEl} ...>`

**Step 3: Verify build**

Run: `cd apps/web && npm run check`

**Step 4: Commit**

```
feat(voice): add Voice & Audio settings component
```

---

### Task 8: Wire Voice & Audio Settings into Settings Modal

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts` (line 122)
- Modify: `apps/web/src/lib/components/settings/UserSettingsModal.svelte`
- Modify: `apps/web/src/lib/components/settings/SettingsSidebar.svelte`

**Step 1: Extend `settingsCategory` type**

In `app-state.svelte.ts` line 122, change:

```typescript
settingsCategory = $state<'profile' | 'account'>('profile');
```
to:
```typescript
settingsCategory = $state<'profile' | 'account' | 'voice-audio'>('profile');
```

**Step 2: Add the Voice & Audio tab to SettingsSidebar**

In `SettingsSidebar.svelte`, add a new entry to the `categories` array:

```typescript
const categories = [
	{ id: 'profile' as const, label: 'My Profile', icon: '👤' },
	{ id: 'account' as const, label: 'My Account', icon: '🔒' },
	{ id: 'voice-audio' as const, label: 'Voice & Audio', icon: '🎙️' }
];
```

**Step 3: Add the VoiceAudioSettings component to UserSettingsModal**

Import and render:

```svelte
<script lang="ts">
	// Add import:
	import VoiceAudioSettings from './VoiceAudioSettings.svelte';
</script>

<!-- In the content section, add an else-if: -->
{#if app.settingsCategory === 'profile'}
	<ProfileSettings />
{:else if app.settingsCategory === 'voice-audio'}
	<VoiceAudioSettings />
{:else}
	<AccountSettings />
{/if}
```

**Step 4: Verify build**

Run: `cd apps/web && npm run check`

**Step 5: Commit**

```
feat(voice): wire Voice & Audio settings tab into settings modal
```

---

### Task 9: Add PTT Active Indicator to VoiceConnectedBar

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/VoiceConnectedBar.svelte`

**Step 1: Add a visual indicator when PTT is active**

Update the voice label to show PTT status:

```svelte
<span class="voice-label">
	{#if app.voiceInputMode === 'push-to-talk'}
		{app.isPttActive ? 'Transmitting' : 'Push to Talk'}
	{:else}
		Voice Connected
	{/if}
</span>
```

**Step 2: Add a CSS class for the transmitting state**

```svelte
<div class="voice-bar" class:transmitting={app.isPttActive} role="status" ...>
```

```css
.voice-bar.transmitting .voice-label {
	color: var(--accent, #5865f2);
}
```

**Step 3: Verify build**

Run: `cd apps/web && npm run check`

**Step 4: Commit**

```
feat(voice): add PTT active indicator to voice connected bar
```

---

### Task 10: Update VOICE.md and FEATURES.md Documentation

**Files:**
- Modify: `VOICE.md` (update Phase 2 status and known limitations)
- Modify: `docs/FEATURES.md` (if it exists, add Phase 2 features)

**Step 1: Update Phase 2 status in VOICE.md**

Change Phase 2 status from `📋 Planned` to `✅ Complete`.

Update Known Limitations section to remove Phase 2 items:
- Remove "No deafen/undeafen (planned Phase 2)" (was already done in Phase 1)
- Remove "No per-user volume control (planned Phase 2)"
- Remove "No push-to-talk (planned Phase 2)"

**Step 2: Add Phase 2 features section to VOICE.md**

Document the new SignalR events, state fields, and settings.

**Step 3: Verify build**

Run: `cd apps/web && npm run check`

**Step 4: Commit**

```
docs: update voice docs for Phase 2 completion
```

---

### Task 11: Final Integration Test

**Step 1: Run full type check**

```bash
cd apps/web && npm run check
```

**Step 2: Run build**

```bash
cd apps/web && npm run build
```

**Step 3: Manual verification checklist**

- [ ] Join a voice channel → AudioContext is created, audio plays through GainNode
- [ ] Right-click a voice member → context menu appears with volume slider
- [ ] Adjust slider → volume changes in real-time
- [ ] Leave and rejoin → saved volume is restored from localStorage
- [ ] Open Settings → "Voice & Audio" tab appears
- [ ] Switch to Push to Talk → producer is paused
- [ ] Hold configured key → audio transmits (producer resumed)
- [ ] Release key → audio stops (producer paused)
- [ ] Change PTT key → new key works immediately
- [ ] Switch back to Voice Activity → mic resumes normally
- [ ] Deafen → all GainNodes go to 0
- [ ] Undeafen → GainNodes restore to saved volumes

**Step 4: Commit any fixes**
