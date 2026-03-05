# Voice Mobile Controls Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make per-user voice volume controls accessible on mobile by adding long-press support and a responsive bottom sheet presentation.

**Architecture:** Create a reusable `longpress` Svelte action for touch/pointer detection. Refactor `VoiceMemberContextMenu` into a responsive `UserActionSheet` that renders as a positioned popup on desktop and a bottom sheet on mobile. Detection uses `pointer: fine` media query.

**Tech Stack:** Svelte 5 (runes), TypeScript, CSS custom properties from `tokens.css`

**Design doc:** `docs/plans/2026-03-04-voice-mobile-controls-design.md`

---

### Task 1: Create the `longpress` Svelte action

**Files:**
- Create: `apps/web/src/lib/utils/long-press.ts`

**Step 1: Create the action file**

This is the first Svelte action in the codebase. It must conform to the Svelte action contract: a function that receives a node and options, returns `{ destroy() }`.

```ts
export function longpress(
	node: HTMLElement,
	options: { onpress: (x: number, y: number) => void; duration?: number }
) {
	const duration = options.duration ?? 500;
	let timer: ReturnType<typeof setTimeout> | null = null;
	let startX = 0;
	let startY = 0;
	let fired = false;

	function handlePointerDown(e: PointerEvent) {
		// Only primary button (left click / touch)
		if (e.button !== 0) return;
		startX = e.clientX;
		startY = e.clientY;
		fired = false;

		timer = setTimeout(() => {
			fired = true;
			options.onpress(e.clientX, e.clientY);
		}, duration);
	}

	function handlePointerMove(e: PointerEvent) {
		if (timer === null) return;
		const dx = e.clientX - startX;
		const dy = e.clientY - startY;
		if (dx * dx + dy * dy > 100) {
			// 10px threshold (squared)
			clearTimeout(timer);
			timer = null;
		}
	}

	function handlePointerUp() {
		if (timer !== null) {
			clearTimeout(timer);
			timer = null;
		}
	}

	function handleClick(e: MouseEvent) {
		if (fired) {
			e.preventDefault();
			e.stopPropagation();
			fired = false;
		}
	}

	node.addEventListener('pointerdown', handlePointerDown);
	node.addEventListener('pointermove', handlePointerMove);
	node.addEventListener('pointerup', handlePointerUp);
	node.addEventListener('pointercancel', handlePointerUp);
	node.addEventListener('click', handleClick, true);

	return {
		destroy() {
			if (timer !== null) clearTimeout(timer);
			node.removeEventListener('pointerdown', handlePointerDown);
			node.removeEventListener('pointermove', handlePointerMove);
			node.removeEventListener('pointerup', handlePointerUp);
			node.removeEventListener('pointercancel', handlePointerUp);
			node.removeEventListener('click', handleClick, true);
		}
	};
}
```

**Step 2: Verify it compiles**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | head -20`

Expected: No errors related to `long-press.ts`

**Step 3: Commit**

```
git add apps/web/src/lib/utils/long-press.ts
git commit -m "feat: add longpress Svelte action for touch support"
```

---

### Task 2: Create `UserActionSheet.svelte` (replaces `VoiceMemberContextMenu`)

**Files:**
- Create: `apps/web/src/lib/components/voice/UserActionSheet.svelte`
- Reference: `apps/web/src/lib/components/voice/VoiceMemberContextMenu.svelte` (for existing logic)
- Reference: `apps/web/src/lib/styles/tokens.css` (for CSS custom properties)

**Step 1: Create the new component**

This component detects pointer type on mount and renders either a positioned popup (desktop) or bottom sheet (mobile). All the menu content (volume slider, reset) is shared between both modes.

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let {
		userId,
		displayName,
		x,
		y,
		onclose
	}: {
		userId: string;
		displayName: string;
		x?: number;
		y?: number;
		onclose: () => void;
	} = $props();

	const app = getAppState();

	const isMobile = !window.matchMedia('(pointer: fine)').matches;

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

	function handleBackdropClick(e: MouseEvent) {
		if (!(e.target as Element).closest('.action-sheet')) {
			onclose();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') onclose();
	}

	// Desktop positioning — clamp to viewport
	const menuWidth = 220;
	const menuHeight = 140;
	const clampedX = $derived(Math.min(x ?? 0, window.innerWidth - menuWidth - 8));
	const clampedY = $derived(Math.min(y ?? 0, window.innerHeight - menuHeight - 8));
</script>
```

The template uses a single overlay with conditional CSS class:

```svelte
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
	class="action-sheet-overlay"
	class:action-sheet-overlay--mobile={isMobile}
	onclick={handleBackdropClick}
	onkeydown={handleKeydown}
>
	<div
		class="action-sheet"
		class:action-sheet--mobile={isMobile}
		class:action-sheet--desktop={!isMobile}
		style={!isMobile ? `left: ${clampedX}px; top: ${clampedY}px;` : undefined}
		role="menu"
	>
		<div class="action-sheet__header">{displayName}</div>
		<div class="action-sheet__section">
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
			<button class="action-sheet__item" onclick={handleReset} role="menuitem">
				Reset Volume
			</button>
		{/if}
	</div>
</div>
```

**Styles** — see full CSS below. Key differences between modes:

- `.action-sheet--desktop`: `position: fixed` at cursor coords, `width: 220px`, small slider thumb (14px)
- `.action-sheet--mobile`: `position: fixed; bottom: 0; left: 0; right: 0;`, rounded top corners, larger thumb (24px), 12px padding, slide-up animation, `padding-bottom: env(safe-area-inset-bottom)` for notched phones

```css
.action-sheet-overlay {
	position: fixed;
	inset: 0;
	z-index: 100;
}

.action-sheet-overlay--mobile {
	background: rgba(0, 0, 0, 0.5);
}

/* ── Shared ── */
.action-sheet {
	background: var(--bg-secondary);
	border: 1px solid var(--border);
	box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
	z-index: 101;
}

.action-sheet__header {
	font-size: 12px;
	font-weight: 600;
	color: var(--text-header);
	border-bottom: 1px solid var(--border);
	white-space: nowrap;
	overflow: hidden;
	text-overflow: ellipsis;
}

.action-sheet__section {
	/* volume control wrapper */
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
	-webkit-appearance: none;
	appearance: none;
	background: var(--bg-tertiary);
	border-radius: 2px;
	outline: none;
	cursor: pointer;
}

.action-sheet__item {
	display: block;
	width: 100%;
	background: none;
	border: none;
	border-radius: 4px;
	color: var(--text-muted);
	font-size: 13px;
	text-align: left;
	cursor: pointer;
	font-family: inherit;
}

.action-sheet__item:hover {
	background: var(--bg-message-hover);
	color: var(--text-normal);
}

/* ── Desktop ── */
.action-sheet--desktop {
	position: fixed;
	width: 220px;
	border-radius: 6px;
	padding: 8px;
}

.action-sheet--desktop .action-sheet__header {
	padding: 4px 4px 8px;
	margin-bottom: 8px;
}

.action-sheet--desktop .action-sheet__section {
	padding: 0 4px;
}

.action-sheet--desktop .volume-slider {
	height: 4px;
}

.action-sheet--desktop .volume-slider::-webkit-slider-thumb {
	-webkit-appearance: none;
	appearance: none;
	width: 14px;
	height: 14px;
	border-radius: 50%;
	background: var(--accent);
	cursor: pointer;
}

.action-sheet--desktop .volume-slider::-moz-range-thumb {
	width: 14px;
	height: 14px;
	border-radius: 50%;
	background: var(--accent);
	cursor: pointer;
	border: none;
}

.action-sheet--desktop .action-sheet__item {
	padding: 6px 4px;
	margin-top: 8px;
}

/* ── Mobile ── */
.action-sheet--mobile {
	position: fixed;
	bottom: 0;
	left: 0;
	right: 0;
	border-radius: 12px 12px 0 0;
	border-bottom: none;
	padding: 12px;
	padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px));
	animation: slide-up 200ms ease-out;
}

.action-sheet--mobile .action-sheet__header {
	padding: 4px 4px 12px;
	margin-bottom: 12px;
	font-size: 14px;
}

.action-sheet--mobile .action-sheet__section {
	padding: 0 4px;
}

.action-sheet--mobile .volume-label {
	font-size: 12px;
	margin-bottom: 10px;
}

.action-sheet--mobile .volume-slider {
	height: 6px;
}

.action-sheet--mobile .volume-slider::-webkit-slider-thumb {
	-webkit-appearance: none;
	appearance: none;
	width: 24px;
	height: 24px;
	border-radius: 50%;
	background: var(--accent);
	cursor: pointer;
}

.action-sheet--mobile .volume-slider::-moz-range-thumb {
	width: 24px;
	height: 24px;
	border-radius: 50%;
	background: var(--accent);
	cursor: pointer;
	border: none;
}

.action-sheet--mobile .action-sheet__item {
	padding: 12px 4px;
	margin-top: 8px;
	min-height: 44px;
	font-size: 15px;
}

@keyframes slide-up {
	from {
		transform: translateY(100%);
	}
	to {
		transform: translateY(0);
	}
}
```

**Step 2: Verify it compiles**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | head -30`

Expected: No errors related to `UserActionSheet.svelte`

**Step 3: Commit**

```
git add apps/web/src/lib/components/voice/UserActionSheet.svelte
git commit -m "feat: add UserActionSheet with responsive desktop/mobile presentation"
```

---

### Task 3: Integrate into ChannelSidebar

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte`

**Step 1: Update imports and context menu state**

In the `<script>` block:

1. Replace import of `VoiceMemberContextMenu` with `UserActionSheet`
2. Add import of `longpress` action
3. Keep the `contextMenu` state as-is (x/y will always be provided by both triggers)

Change:
```ts
import VoiceMemberContextMenu from '$lib/components/voice/VoiceMemberContextMenu.svelte';
```
To:
```ts
import UserActionSheet from '$lib/components/voice/UserActionSheet.svelte';
import { longpress } from '$lib/utils/long-press.js';
```

**Step 2: Add `use:longpress` to voice member list items**

On each `.voice-member` `<li>` (around line 125-132), add the longpress action alongside the existing `oncontextmenu`:

Change:
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
To:
```svelte
<li
	class="voice-member"
	use:longpress={{
		onpress: (x, y) => {
			if (member.userId !== app.me?.user.id) {
				contextMenu = { userId: member.userId, displayName: member.displayName, x, y };
			}
		}
	}}
	oncontextmenu={(e) => {
		if (member.userId !== app.me?.user.id) {
			e.preventDefault();
			contextMenu = { userId: member.userId, displayName: member.displayName, x: e.clientX, y: e.clientY };
		}
	}}
>
```

**Step 3: Swap the component tag**

Near the end of the template (around line 194-202), change:

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
To:
```svelte
{#if contextMenu}
	<UserActionSheet
		userId={contextMenu.userId}
		displayName={contextMenu.displayName}
		x={contextMenu.x}
		y={contextMenu.y}
		onclose={() => { contextMenu = null; }}
	/>
{/if}
```

**Step 4: Verify it compiles**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | head -30`

Expected: No errors

**Step 5: Commit**

```
git add apps/web/src/lib/components/channel-sidebar/ChannelSidebar.svelte
git commit -m "feat: integrate UserActionSheet with longpress in ChannelSidebar"
```

---

### Task 4: Remove old `VoiceMemberContextMenu.svelte`

**Files:**
- Delete: `apps/web/src/lib/components/voice/VoiceMemberContextMenu.svelte`

**Step 1: Verify no other files import it**

Run: `grep -r "VoiceMemberContextMenu" apps/web/src/`

Expected: No results (ChannelSidebar was the only consumer and was updated in Task 3)

**Step 2: Delete the file**

```bash
rm apps/web/src/lib/components/voice/VoiceMemberContextMenu.svelte
```

**Step 3: Verify the build still compiles**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | head -30`

Expected: No errors

**Step 4: Commit**

```
git add -u apps/web/src/lib/components/voice/VoiceMemberContextMenu.svelte
git commit -m "refactor: remove old VoiceMemberContextMenu (replaced by UserActionSheet)"
```

---

### Task 5: Manual verification

**Step 1: Start the dev server**

Run: `cd apps/web && npm run dev`

**Step 2: Test desktop behavior**

1. Open app in desktop browser
2. Join a voice channel with another user present
3. Right-click a voice member → positioned popup should appear with volume slider
4. Long-press (~0.5s) a voice member → same popup should appear
5. Adjust volume slider, verify it works
6. Click "Reset Volume", verify it resets to 100%
7. Click outside / press Escape → menu closes

**Step 3: Test mobile behavior**

1. Open browser DevTools → toggle device toolbar (responsive mode)
2. Set to a mobile device (e.g. iPhone 14)
3. Long-press a voice member → bottom sheet should slide up from bottom
4. Verify larger slider thumb (24px), larger padding
5. Verify backdrop is semi-transparent
6. Tap backdrop → sheet closes
7. Verify safe-area padding at bottom on notched device simulation

**Step 4: Commit any fixes if needed, then final commit**

```
git commit -m "feat: voice mobile controls complete"
```
