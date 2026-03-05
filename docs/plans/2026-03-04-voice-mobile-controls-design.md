# Voice Mobile Controls Design

## Problem

Per-user voice volume controls are bound to right-click (`oncontextmenu`), which is unavailable on mobile browsers. This is a mobile-first app, so touch users have no way to adjust other users' volume.

## Solution

Unified `UserActionSheet` component that supports both right-click (desktop) and long-press (mobile), with responsive presentation: positioned popup on desktop, bottom sheet on mobile.

## Interaction: Long-Press + Right-Click

Add a `longpress` Svelte action (`lib/utils/long-press.ts`) that:

- Listens for `pointerdown` → starts a 500ms timer
- On timer fire → triggers callback with pointer coordinates, prevents the next `click`
- Cancels on `pointerup`, `pointercancel`, or `pointermove` beyond 10px threshold
- Existing `oncontextmenu` handler remains — both gestures open the same menu

Signature:
```ts
export function longpress(node: HTMLElement, options: { onpress: (x: number, y: number) => void; duration?: number })
```

## Responsive Presentation

Rename `VoiceMemberContextMenu.svelte` → `UserActionSheet.svelte`.

**Detection:** `window.matchMedia('(pointer: fine)')` checked once on open.

**Desktop (pointer: fine):** Positioned popup at cursor coordinates, viewport-clamped, 220px wide.

**Mobile (pointer: coarse / none):** Bottom sheet — full-width, slides up from bottom (~200ms ease-out), rounded top corners, semi-transparent backdrop. Closes on backdrop tap or Escape.

Props:
```ts
type Props = {
    userId: string;
    displayName: string;
    x?: number;  // only used in desktop mode
    y?: number;
    onclose: () => void;
};
```

## Menu Content

Structure for future extensibility:

```
┌─────────────────────┐
│ Display Name        │  ← header
├─────────────────────┤
│ Volume    75%       │  ← volume section
│ ═══════●════════    │
│ Reset Volume        │
├─────────────────────┤
│ (future actions)    │  ← action items (empty for now)
└─────────────────────┘
```

Mobile adjustments: slider thumb 24px (up from 14px), buttons min-height 44px, 12px padding (up from 8px). Applied via `.action-sheet--mobile` / `.action-sheet--desktop` CSS class.

## ChannelSidebar Integration

- Swap import to `UserActionSheet`
- Add `use:longpress` to each `.voice-member` `<li>`
- Keep existing `oncontextmenu` handler — both set the same `contextMenu` state
- Make `x`/`y` optional in context menu state type
