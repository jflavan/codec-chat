# Voice Phase 2 Design: Per-User Volume & Push-to-Talk

## Summary

Phase 2 adds two features to voice channels:

1. **Per-user volume control** — Web Audio API GainNode per consumer with right-click context menu
2. **Push-to-talk** — Configurable keybind with Voice Activity / PTT mode toggle in settings

Deafen was already fully implemented in Phase 1 (frontend state, UI, SignalR broadcast, backend persistence).

## Feature 1: Per-User Volume Control

### Audio Pipeline

Each consumed remote audio track is routed through a Web Audio `GainNode`:

```
Consumer Track → MediaStream → AudioContext.createMediaStreamSource()
  → GainNode (0.0–1.0) → AudioContext.destination (speakers)
```

This replaces the current `HTMLAudioElement` approach in `remoteAudioElements`.

### State & Storage

| Location | Key/Field | Type | Description |
|----------|-----------|------|-------------|
| AppState (runtime) | `userVolumes` | `Map<string, number>` | userId → volume (0.0–1.0, default 1.0) |
| localStorage | `codec-user-volumes` | `{ [userId]: number }` | Persisted volume preferences |
| AppState (private) | `audioNodes` | `Map<string, { source, gain }>` | participantId → Web Audio nodes |
| AppState (private) | `audioContext` | `AudioContext \| null` | Shared audio context |

### AudioContext Lifecycle

- Created lazily on first `joinVoiceChannel()` call (browsers require user gesture before AudioContext creation)
- Single shared instance in AppState
- Closed on page unload via `teardownSync()`

### UI: Right-Click Context Menu

- Right-click a voice member in the channel sidebar → context menu opens
- Menu contents: user display name, volume slider (0–100%), "Reset Volume" option
- Slider updates `GainNode.gain.value` in real-time (AudioParam changes are instant, no debounce needed)
- On change: updates `userVolumes` map + writes to localStorage

### Integration Changes

| Method | Current | New |
|--------|---------|-----|
| `_attachRemoteAudio(participantId, track)` | Creates `<audio>` element, sets `srcObject`, calls `.play()` | Creates `AudioContext` source → `GainNode` → destination; applies saved volume for user |
| `_detachRemoteAudio(participantId)` | Pauses element, nulls srcObject | Disconnects audio nodes, removes from map |
| `toggleDeafen()` | Sets `.muted` on all `<audio>` elements | Sets all GainNode gains to 0.0; restores saved volumes on undeafen |

### No Server Changes

Volume control is entirely client-side. No new API endpoints, SignalR events, or database changes.

---

## Feature 2: Push-to-Talk

### Input Modes

| Mode | Behavior |
|------|----------|
| Voice Activity (default) | Mic always active when in voice channel (current behavior) |
| Push to Talk | Mic only active while holding configured key |

### State & Storage

| Location | Key/Field | Type | Description |
|----------|-----------|------|-------------|
| localStorage | `codec-voice-preferences` | `{ inputMode, pttKey }` | Persisted preferences |
| AppState (runtime) | `voiceInputMode` | `'voice-activity' \| 'push-to-talk'` | Current input mode |
| AppState (runtime) | `pttKey` | `string` | KeyboardEvent.code (default: `'KeyV'`) |
| AppState (runtime) | `isPttActive` | `boolean` | True while PTT key is held |

### Keyboard Handling

- Global `keydown`/`keyup` listeners on `window`, registered when joining voice in PTT mode
- Listeners removed when leaving voice or switching to Voice Activity mode
- **keydown**: If `event.code === pttKey` and `!event.repeat` → `producer.resume()`, `isPttActive = true`
- **keyup**: If `event.code === pttKey` → `producer.pause()`, `isPttActive = false`
- **Text input guard**: Ignore events when `document.activeElement` is `<input>`, `<textarea>`, or `[contenteditable]`

### Join Behavior

- PTT mode: producer starts **paused** after creation (mic off until key held)
- Voice Activity mode: producer starts unpaused (current behavior)
- Switching modes while in voice: immediately pauses/resumes producer

### No Server Changes

PTT is entirely client-side:
- No new SignalR events (no visual muted indicator for PTT — audio just pauses silently)
- No VoiceState changes on the backend
- Producer pause/resume is a mediasoup-client operation

### Settings UI: "Voice & Audio" Tab

New settings category added to the existing settings modal (`UserSettingsModal.svelte`):
- **Input Mode**: Radio buttons — "Voice Activity" / "Push to Talk"
- **PTT Key** (visible only when PTT selected): Shows current key name + "Record New Key" button
  - Recording mode: next keypress captures `event.code`, displays friendly name, saves to localStorage
- Changes save to localStorage immediately (no save button)

---

## Decisions Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Volume implementation | Web Audio GainNode | Better audio quality than HTMLAudioElement.volume |
| Volume range | 0–100% (no amplification) | Avoids clipping/distortion; can add later |
| Volume UI | Right-click context menu | Lightweight, doesn't clutter sidebar |
| Volume persistence | localStorage per userId | Device-specific, no server overhead |
| PTT storage | localStorage | Keybinds are device-specific; cross-device sync unnecessary |
| PTT visual indicator | None (silent pause) | User preference; keeps member list clean |
| Default PTT key | KeyV | Common convention, not conflicting with chat |
| Text input guard | Skip PTT events in inputs | Prevents accidental activation while typing |
