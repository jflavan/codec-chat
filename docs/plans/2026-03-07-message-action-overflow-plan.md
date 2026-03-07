# Message Action Overflow Fix — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix message action buttons and emoji pickers being clipped when messages are near the top of the scroll viewport, by flipping their direction and using mobile bottom sheets.

**Architecture:** Add viewport boundary detection via `getBoundingClientRect()` to flip toolbar/picker direction. A `flipped` CSS class on the action bar switches all child elements from rendering above to below. On mobile (≤768px), emoji pickers render as fixed bottom sheets instead.

**Tech Stack:** Svelte 5, CSS custom properties, no new dependencies

---

### Task 1: Add flip detection to MessageItem toolbar

**Files:**
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte`

**Step 1: Add flip state and detection function**

In the `<script>` block, after the `showFullPicker` declaration (line 62), add:

```ts
let isFlipped = $state(false);
let messageEl: HTMLElement | undefined = $state(undefined);

function checkFlip() {
	if (!messageEl) return;
	const rect = messageEl.getBoundingClientRect();
	isFlipped = rect.top < 50;
}
```

**Step 2: Bind the message element and add mouseenter handler**

Change the `<article>` tag (line 139) from:
```svelte
<article class="message" class:grouped class:mentioned={isMentioned}>
```
to:
```svelte
<article
	bind:this={messageEl}
	class="message"
	class:grouped
	class:mentioned={isMentioned}
	onmouseenter={checkFlip}
>
```

**Step 3: Add flipped class to action bar**

Change line 141 from:
```svelte
<div class="message-actions" class:picker-open={showPicker || showFullPicker}>
```
to:
```svelte
<div class="message-actions" class:picker-open={showPicker || showFullPicker} class:flipped={isFlipped}>
```

**Step 4: Add flipped CSS for the toolbar**

In the `<style>` block, after the `.message-actions` rule (after line 407), add:

```css
.message-actions.flipped {
	top: unset;
	bottom: -14px;
}
```

**Step 5: Add flipped CSS for the quick emoji picker**

After the `.emoji-picker` rule (after line 463), add:

```css
.message-actions.flipped .emoji-picker {
	bottom: unset;
	top: calc(100% + 4px);
}
```

**Step 6: Verify visually**

Run: `cd apps/web && npm run dev`
- Scroll a message to the very top of the chat viewport
- Hover over it — toolbar should appear below the message, not above
- Click the emoji button — quick picker should appear below the toolbar

**Step 7: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageItem.svelte
git commit -m "feat(web): add flip detection for message action toolbar"
```

---

### Task 2: Add flipped prop to EmojiPicker

**Files:**
- Modify: `apps/web/src/lib/components/chat/EmojiPicker.svelte`
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte`

**Step 1: Add flipped prop to EmojiPicker**

In `EmojiPicker.svelte`, update the props (lines 6-16) to add `flipped`:

```ts
let {
	onSelect,
	mode,
	onClose,
	customEmojis = [],
	flipped = false
}: {
	onSelect: (emoji: string) => void;
	mode: 'reaction' | 'insert';
	onClose: () => void;
	customEmojis?: CustomEmoji[];
	flipped?: boolean;
} = $props();
```

**Step 2: Add flipped class to the container**

Change the container div (line 100) from:
```svelte
<div class="emoji-picker-container" role="dialog" aria-label="Emoji picker">
```
to:
```svelte
<div class="emoji-picker-container" class:flipped role="dialog" aria-label="Emoji picker">
```

**Step 3: Add flipped CSS and dynamic max-height**

After the `.emoji-picker-container` rule (after line 168), add:

```css
.emoji-picker-container.flipped {
	bottom: unset;
	top: calc(100% + 4px);
}
```

**Step 4: Pass flipped prop from MessageItem**

In `MessageItem.svelte`, update the EmojiPicker usage (lines 224-229) from:
```svelte
<EmojiPicker
	mode="reaction"
	onSelect={handleToggleReaction}
	onClose={() => { showFullPicker = false; }}
	customEmojis={app.customEmojis}
/>
```
to:
```svelte
<EmojiPicker
	mode="reaction"
	onSelect={handleToggleReaction}
	onClose={() => { showFullPicker = false; }}
	customEmojis={app.customEmojis}
	flipped={isFlipped}
/>
```

**Step 5: Constrain max-height when flipped**

In `EmojiPicker.svelte`, add a reactive style calculation. After the `searchInput` state declaration (line 20), add:

```ts
let containerEl = $state<HTMLDivElement>();

const dynamicMaxHeight = $derived.by(() => {
	if (!flipped || !containerEl) return '420px';
	const rect = containerEl.getBoundingClientRect();
	const available = window.innerHeight - rect.top - 16;
	return `${Math.min(420, Math.max(200, available))}px`;
});
```

Update the container div to bind the element and apply the style:
```svelte
<div
	bind:this={containerEl}
	class="emoji-picker-container"
	class:flipped
	style:max-height={flipped ? dynamicMaxHeight : undefined}
	role="dialog"
	aria-label="Emoji picker"
>
```

**Step 6: Verify visually**

- Scroll a message to the top of the viewport
- Hover and open the full emoji picker — should render below the toolbar
- Picker should be constrained to available space below

**Step 7: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageItem.svelte apps/web/src/lib/components/chat/EmojiPicker.svelte
git commit -m "feat(web): flip full emoji picker when message is near top"
```

---

### Task 3: Add flip logic to ReactionBar popover

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte`

**Step 1: Add flip detection to the popover**

In the `<script>` block, after the `touchTriggered` declaration (line 48), add:

```ts
let popoverFlipped = $state(false);
```

Update the `showPopover` function (lines 34-39) to check position:

```ts
function showPopover(emoji: string, el: HTMLElement) {
	if (hoverTimeout) clearTimeout(hoverTimeout);
	hoverTimeout = setTimeout(() => {
		const rect = el.getBoundingClientRect();
		popoverFlipped = rect.top < 80;
		hoveredEmoji = emoji;
	}, 250);
}
```

Update `handleTouchStart` (lines 50-55) similarly:

```ts
function handleTouchStart(emoji: string, el: HTMLElement) {
	touchTriggered = false;
	touchTimeout = setTimeout(() => {
		touchTriggered = true;
		const rect = el.getBoundingClientRect();
		popoverFlipped = rect.top < 80;
		hoveredEmoji = emoji;
	}, 500);
}
```

**Step 2: Update event handlers in template to pass element**

Change the wrapper div event handlers (lines 112-117) from:
```svelte
onmouseenter={() => showPopover(reaction.emoji)}
onmouseleave={hidePopover}
ontouchstart={() => handleTouchStart(reaction.emoji)}
```
to:
```svelte
onmouseenter={(e) => showPopover(reaction.emoji, e.currentTarget)}
onmouseleave={hidePopover}
ontouchstart={(e) => handleTouchStart(reaction.emoji, e.currentTarget)}
```

**Step 3: Add flipped class to popover**

Change the popover div (line 139) from:
```svelte
<div class="reaction-popover" role="tooltip" id="popover-{reactions.indexOf(reaction)}">
```
to:
```svelte
<div class="reaction-popover" class:flipped={popoverFlipped} role="tooltip" id="popover-{reactions.indexOf(reaction)}">
```

**Step 4: Add flipped CSS**

After the `.reaction-popover` rule (after line 267), add:

```css
.reaction-popover.flipped {
	bottom: unset;
	top: calc(100% + 8px);
	animation: popover-fade-in-flipped 150ms ease;
}

@keyframes popover-fade-in-flipped {
	from {
		opacity: 0;
		transform: translateX(-50%) translateY(-4px);
	}
	to {
		opacity: 1;
		transform: translateX(-50%) translateY(0);
	}
}
```

**Step 5: Verify visually**

- Scroll so a message with reactions is near the top
- Hover over a reaction pill — popover should appear below instead of above

**Step 6: Commit**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "feat(web): flip reaction popover when near top of viewport"
```

---

### Task 4: Mobile bottom sheet for emoji pickers

**Files:**
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte`
- Modify: `apps/web/src/lib/components/chat/EmojiPicker.svelte`

**Step 1: Add mobile bottom sheet CSS for the quick emoji picker in MessageItem**

In the `@media (max-width: 768px)` block in `MessageItem.svelte` (line 656), add:

```css
.emoji-picker {
	position: fixed;
	bottom: 0;
	left: 0;
	right: 0;
	top: unset;
	border-radius: 12px 12px 0 0;
	justify-content: center;
	padding: 12px;
	padding-bottom: calc(12px + env(safe-area-inset-bottom));
	z-index: 100;
	animation: slide-up 200ms ease;
}

.message-actions.flipped .emoji-picker {
	top: unset;
	bottom: 0;
}

.picker-backdrop {
	background: rgba(0, 0, 0, 0.5);
	z-index: 99;
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

**Step 2: Add mobile bottom sheet CSS for the full EmojiPicker**

In the `@media (max-width: 768px)` block in `EmojiPicker.svelte` (line 270), replace the existing rule with:

```css
.emoji-picker-container {
	position: fixed;
	bottom: 0;
	left: 0;
	right: 0;
	top: unset;
	width: 100%;
	max-height: 60vh;
	border-radius: 12px 12px 0 0;
	padding-bottom: env(safe-area-inset-bottom);
	animation: slide-up 200ms ease;
	z-index: 100;
}

.emoji-picker-container.flipped {
	top: unset;
	bottom: 0;
}

.picker-backdrop {
	background: rgba(0, 0, 0, 0.5);
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

**Step 3: Verify on mobile viewport**

- Use browser devtools to set viewport to 375px width
- Open quick emoji picker — should slide up from bottom
- Open full emoji picker — should slide up from bottom with 60vh max height
- Tap backdrop to dismiss

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageItem.svelte apps/web/src/lib/components/chat/EmojiPicker.svelte
git commit -m "feat(web): mobile bottom sheet for emoji pickers"
```

---

### Task 5: Run type checks and verify build

**Step 1: Run svelte-check**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 2: Run build**

Run: `cd apps/web && npm run build`
Expected: Build succeeds

**Step 3: Fix any errors found**

If there are type errors or build failures, fix them in the relevant files.

**Step 4: Commit any fixes**

```bash
git add -u
git commit -m "fix(web): resolve type/build errors from overflow fix"
```
