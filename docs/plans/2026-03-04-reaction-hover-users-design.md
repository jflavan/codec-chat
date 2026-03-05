# Reaction Hover Users Design

## Goal

Show who reacted with each emoji when hovering a reaction pill on text channel messages. DMs are unaffected (they don't support reactions).

## Current State

- Backend already returns `userIds: string[]` per aggregated reaction
- `ReactionBar.svelte` displays reaction pills with `title="{count} reactions"`
- `AppState.members` contains loaded server members with `userId` and `displayName`

## Approach

Resolve `userIds` to display names using the already-loaded `members` array and show them in the native `title` attribute tooltip.

- Pass `members` into `ReactionBar` as a new prop
- Build a helper that maps `userIds` → display names, capping at ~10 names with "and N others"
- Replace the generic count title with the resolved name list

## Files Changed

1. `apps/web/src/lib/components/chat/ReactionBar.svelte` — add `members` prop, name resolution helper, updated `title`
2. `apps/web/src/lib/components/chat/MessageItem.svelte` — pass `members` from AppState to `ReactionBar`

## No Backend Changes

The API already sends `userIds` per reaction. No new endpoints or model changes needed.
