# Message Action Overflow Fix — Design

## Problem

Message action buttons (reply, react, edit, delete) and emoji pickers use `position: absolute` within each `MessageItem`, and the message feed has `overflow-y: auto`. When a message is scrolled near the top of the viewport, the toolbar and pickers are clipped by the scroll container's overflow boundary.

## Solution: Viewport Boundary Flip + Mobile Bottom Sheet

### Flip Logic (All Viewports)

When the message action toolbar becomes visible (mouseenter/focus), check the message's position relative to the viewport using `getBoundingClientRect()`. If there is less than ~50px above the message, add a `flipped` CSS class to the action bar container.

**Flipped positioning:**
- Action toolbar: `top: -14px` becomes `bottom: -14px` (renders below the message)
- Quick emoji picker: `bottom: calc(100% + 4px)` becomes `top: calc(100% + 4px)` (opens downward)
- Full emoji picker: same flip, plus `max-height` constrained to available viewport space below: `max-height: min(420px, calc(100vh - bottomEdge - 16px))`
- Reaction popover: independent flip check on hover — if reaction pill is near viewport top, popover renders below instead of above

**When the check runs:** Once on hover/focus. No continuous repositioning on scroll. Closing and re-opening recalculates.

**CSS approach:** A single `flipped` class on the action bar container controls all child positioning via descendant selectors.

### Mobile Bottom Sheet (max-width: 768px)

On mobile viewports, emoji pickers (both quick and full) render as bottom sheets instead of inline flipped elements.

**Bottom sheet styling:**
- `position: fixed; bottom: 0; left: 0; right: 0; z-index: 100`
- Slide-up animation: `transform: translateY(100%)` to `translateY(0)`, ~200ms ease
- Fixed backdrop: `position: fixed; inset: 0; z-index: 99; background: rgba(0,0,0,0.5)`, tap to dismiss
- `border-radius: 12px 12px 0 0` on top corners
- `max-height: 60vh` with internal scrolling for the full picker
- `padding-bottom: env(safe-area-inset-bottom)` for notched devices
- Quick picker: same frequent emojis + "More" button, horizontally centered, 44x44px touch targets

**Not bottom-sheeted:** Action toolbar and reaction popover stay inline with flip logic (small enough to always fit).

### Edge Cases

- **Scroll during open picker:** Picker stays in current position. Close + reopen recalculates.
- **Extremely short viewport:** `max-height` constraint ensures full picker scrolls internally.
- **Keyboard navigation:** Flip direction does not affect tab order.
- **State management:** `flipped` state is local to each `MessageItem`. No changes to `AppState`.

## Files to Modify

- `apps/web/src/lib/components/chat/MessageItem.svelte` — flip logic + flipped CSS for toolbar, quick picker, full picker
- `apps/web/src/lib/components/chat/EmojiPicker.svelte` — flipped prop for downward positioning, mobile bottom sheet
- `apps/web/src/lib/components/chat/ReactionBar.svelte` — flip logic for reaction popover
