# Reaction Hover Popover Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace browser-native `title` tooltips on reaction pills with a custom popover showing reactor names, supporting hover on desktop and long-press on mobile.

**Architecture:** All changes in `apps/web/src/lib/components/chat/ReactionBar.svelte`. Add reactive state to track which emoji's popover is open, hover/touch event handlers with delay timers, and a styled absolutely-positioned popover element. Refactor `reactionTitle()` into `getReactorNames()` returning an array for list rendering.

**Tech Stack:** Svelte 5 runes, CSS custom properties from `tokens.css`

---

### Task 1: Refactor reactionTitle() to return structured data

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte:20-38`

**Step 1: Replace `reactionTitle()` with `getReactorNames()`**

Change the helper from returning a formatted string to returning a `{ names: string[], remaining: number }` object. This lets the popover render names as a list instead of a comma-separated title.

Replace lines 20-38 in `ReactionBar.svelte`:

```typescript
function getReactorNames(reaction: Reaction): { names: string[]; remaining: number } {
	if (members.length === 0) {
		return { names: [], remaining: 0 };
	}
	const memberMap = new Map(members.map((m) => [m.userId, m.displayName]));
	const names = reaction.userIds
		.map((id) => memberMap.get(id))
		.filter((name): name is string => name !== undefined);
	const MAX_NAMES = 10;
	if (names.length <= MAX_NAMES) {
		return { names, remaining: 0 };
	}
	return { names: names.slice(0, MAX_NAMES), remaining: names.length - MAX_NAMES };
}
```

**Step 2: Remove the `title` attribute from the button**

In the template, remove `title={reactionTitle(reaction)}` from the `<button>` element.

**Step 3: Verify the app builds**

Run: `cd apps/web && npm run check`
Expected: No type errors

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "refactor: extract getReactorNames from reactionTitle for popover use"
```

---

### Task 2: Add hover state and desktop popover

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte`

**Step 1: Add reactive hover state**

Add these state variables after the `$props()` block (after line 14):

```typescript
let hoveredEmoji: string | null = $state(null);
let hoverTimeout: ReturnType<typeof setTimeout> | null = $state(null);

function showPopover(emoji: string) {
	if (hoverTimeout) clearTimeout(hoverTimeout);
	hoverTimeout = setTimeout(() => {
		hoveredEmoji = emoji;
	}, 250);
}

function hidePopover() {
	if (hoverTimeout) clearTimeout(hoverTimeout);
	hoverTimeout = null;
	hoveredEmoji = null;
}
```

**Step 2: Update the template with hover handlers and popover**

Replace the `{#each}` block with:

```svelte
{#each reactions as reaction}
	<div
		class="reaction-pill-wrapper"
		onmouseenter={() => showPopover(reaction.emoji)}
		onmouseleave={hidePopover}
	>
		<button
			class="reaction-pill"
			class:reacted={hasReacted(reaction)}
			onclick={() => { onToggle(reaction.emoji); hidePopover(); }}
		>
			<span class="reaction-emoji">{reaction.emoji}</span>
			<span class="reaction-count">{reaction.count}</span>
		</button>

		{#if hoveredEmoji === reaction.emoji}
			{@const info = getReactorNames(reaction)}
			<div class="reaction-popover" role="tooltip">
				<div class="popover-emoji">{reaction.emoji}</div>
				{#if info.names.length > 0}
					<ul class="popover-names">
						{#each info.names as name}
							<li>{name}</li>
						{/each}
						{#if info.remaining > 0}
							<li class="popover-more">and {info.remaining} more</li>
						{/if}
					</ul>
				{:else}
					<div class="popover-fallback">
						{reaction.count} {reaction.count === 1 ? 'reaction' : 'reactions'}
					</div>
				{/if}
			</div>
		{/if}
	</div>
{/each}
```

**Step 3: Add styles for the wrapper and popover**

Add these CSS rules inside the `<style>` block:

```css
.reaction-pill-wrapper {
	position: relative;
}

.reaction-popover {
	position: absolute;
	bottom: calc(100% + 8px);
	left: 50%;
	transform: translateX(-50%);
	background: var(--bg-tertiary);
	border: 1px solid var(--border);
	border-radius: 8px;
	padding: 8px 12px;
	min-width: 120px;
	max-width: 200px;
	box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
	z-index: 100;
	pointer-events: auto;
	animation: popover-fade-in 150ms ease;
}

@keyframes popover-fade-in {
	from {
		opacity: 0;
		transform: translateX(-50%) translateY(4px);
	}
	to {
		opacity: 1;
		transform: translateX(-50%) translateY(0);
	}
}

.popover-emoji {
	font-size: 20px;
	text-align: center;
	margin-bottom: 4px;
}

.popover-names {
	list-style: none;
	margin: 0;
	padding: 0;
	font-size: 12px;
	color: var(--text-normal);
	line-height: 1.6;
}

.popover-more {
	color: var(--text-muted);
	font-style: italic;
}

.popover-fallback {
	font-size: 12px;
	color: var(--text-muted);
	text-align: center;
}
```

**Step 4: Verify the app builds and hover works**

Run: `cd apps/web && npm run check`
Expected: No type errors

**Step 5: Commit**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "feat: add desktop hover popover for reaction pills"
```

---

### Task 3: Add mobile long-press support

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte`

**Step 1: Add touch state and handlers**

Add these variables and functions alongside the hover state (after the `hidePopover` function):

```typescript
let touchTimeout: ReturnType<typeof setTimeout> | null = $state(null);
let touchTriggered = $state(false);

function handleTouchStart(emoji: string) {
	touchTriggered = false;
	touchTimeout = setTimeout(() => {
		touchTriggered = true;
		hoveredEmoji = emoji;
	}, 500);
}

function handleTouchEnd(e: TouchEvent, emoji: string) {
	if (touchTimeout) {
		clearTimeout(touchTimeout);
		touchTimeout = null;
	}
	if (touchTriggered) {
		e.preventDefault();
		touchTriggered = false;
	}
}

function handleTouchMove() {
	if (touchTimeout) {
		clearTimeout(touchTimeout);
		touchTimeout = null;
	}
}

function handleBackdropTap() {
	hoveredEmoji = null;
}
```

**Step 2: Add touch handlers to the pill wrapper**

Update the `<div class="reaction-pill-wrapper">` element to include touch handlers:

```svelte
<div
	class="reaction-pill-wrapper"
	onmouseenter={() => showPopover(reaction.emoji)}
	onmouseleave={hidePopover}
	ontouchstart={() => handleTouchStart(reaction.emoji)}
	ontouchend={(e) => handleTouchEnd(e, reaction.emoji)}
	ontouchmove={handleTouchMove}
	oncontextmenu={(e) => { if (touchTriggered) e.preventDefault(); }}
>
```

**Step 3: Add mobile-safe CSS to the pill**

Add to the `.reaction-pill` CSS rule:

```css
-webkit-touch-callout: none;
-webkit-user-select: none;
user-select: none;
touch-action: manipulation;
```

**Step 4: Add backdrop for mobile dismissal**

Add this just before the closing `</div>` of `reaction-bar`, outside the `{#each}`:

```svelte
{#if hoveredEmoji !== null}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="popover-backdrop"
		onclick={handleBackdropTap}
		ontouchstart={handleBackdropTap}
	></div>
{/if}
```

Add the backdrop CSS:

```css
.popover-backdrop {
	position: fixed;
	inset: 0;
	z-index: 99;
}
```

Also add `z-index: 100;` to `.reaction-pill-wrapper` when popover is shown — update the `.reaction-popover` to ensure it's above the backdrop (z-index 100 is already set).

**Step 5: Verify the app builds**

Run: `cd apps/web && npm run check`
Expected: No type errors

**Step 6: Commit**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "feat: add mobile long-press support for reaction popover"
```

---

### Task 4: Final check and cleanup

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte` (if needed)

**Step 1: Run full type check**

Run: `cd apps/web && npm run check`
Expected: All checks pass

**Step 2: Manual verification checklist**

Confirm in the browser:
- Desktop: hover a reaction pill → popover appears above after ~250ms with emoji header and name list
- Desktop: move mouse away → popover disappears
- Desktop: click pill → reaction toggles, popover dismissed
- Mobile (dev tools device mode): long-press → popover appears, no text selection
- Mobile: short tap → reaction toggles (no popover)
- Mobile: tap outside popover → popover dismissed

**Step 3: Commit if any cleanup was needed**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "fix: cleanup reaction popover edge cases"
```
