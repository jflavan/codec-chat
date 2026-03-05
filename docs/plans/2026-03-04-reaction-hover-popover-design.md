# Reaction Hover Popover Design

## Problem

Reaction pills currently use browser-native `title` attribute tooltips to show reactor names. These are unstyled, inconsistent across browsers, and don't work on mobile (no hover). We want a custom popover that shows who reacted with each emoji, working on both desktop and mobile.

## Approach

All changes in `ReactionBar.svelte` — no new files or dependencies. The existing `reactionTitle()` data flow (members prop, userIds → displayName resolution) is reused but rendered as a styled popover instead of a `title` attribute.

## Desktop Behavior

- Hovering a reaction pill for ~250ms shows a popover above the pill
- Moving the mouse off the pill and popover dismisses it
- Clicking the pill toggles the reaction and dismisses the popover

## Mobile Behavior

- Long press (~500ms) on a reaction pill shows the popover
- `user-select: none` and `-webkit-touch-callout: none` on pills prevent text selection
- `touch-action: manipulation` prevents double-tap zoom
- Short tap (< 500ms) toggles the reaction as before
- Tapping outside the popover dismisses it (transparent backdrop overlay)
- `ontouchstart` starts a timer; `ontouchend`/`ontouchmove` clears it
- If the timer fires, show popover and call `preventDefault()` to suppress context menu

## Popover Content

- Header: the emoji character
- Body: vertical list of display names (one per line)
- If >10 reactors: first 10 names + "and N more"
- Fallback (no members data): "N reactions"

## Positioning

- Absolutely positioned above the pill, horizontally centered
- Each pill wrapped in `position: relative` container for anchoring
- CSS-only (no Floating UI) — sufficient since pills are inside a scrollable message area with space above

## Styling

- Dark background (`--bg-primary`), light text (`--text-normal`)
- Small font (~12px), rounded corners, subtle box shadow
- High `z-index` to float above messages
- Smooth fade-in transition
