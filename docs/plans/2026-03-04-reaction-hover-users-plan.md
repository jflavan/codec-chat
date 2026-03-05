# Reaction Hover Users Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Show who reacted with each emoji when hovering a reaction pill on text channel messages.

**Architecture:** Pass the already-loaded server `members` array into `ReactionBar` as a new prop, resolve `userIds` to display names, and show them in the native `title` tooltip. No backend changes needed.

**Tech Stack:** Svelte 5, TypeScript

---

### Task 1: Update ReactionBar to accept members and show names in tooltip

**Files:**
- Modify: `apps/web/src/lib/components/chat/ReactionBar.svelte`

**Step 1: Add members prop and name resolution helper**

Update the `<script>` block in `ReactionBar.svelte` to:

```svelte
<script lang="ts">
	import type { Reaction, Member } from '$lib/types/index.js';

	let {
		reactions,
		currentUserId,
		onToggle,
		members = []
	}: {
		reactions: Reaction[];
		currentUserId: string | null;
		onToggle: (emoji: string) => void;
		members?: Member[];
	} = $props();

	function hasReacted(reaction: Reaction): boolean {
		return currentUserId !== null && reaction.userIds.includes(currentUserId);
	}

	function reactionTitle(reaction: Reaction): string {
		if (members.length === 0) {
			return `${reaction.count} ${reaction.count === 1 ? 'reaction' : 'reactions'}`;
		}
		const memberMap = new Map(members.map((m) => [m.userId, m.displayName]));
		const names = reaction.userIds
			.map((id) => memberMap.get(id))
			.filter((name): name is string => name !== undefined);
		const MAX_NAMES = 10;
		if (names.length === 0) {
			return `${reaction.count} ${reaction.count === 1 ? 'reaction' : 'reactions'}`;
		}
		if (names.length <= MAX_NAMES) {
			return names.join(', ');
		}
		const shown = names.slice(0, MAX_NAMES);
		const remaining = names.length - MAX_NAMES;
		return `${shown.join(', ')}, and ${remaining} more`;
	}
</script>
```

**Step 2: Update the title attribute on the reaction pill**

Replace line 25:
```svelte
			title="{reaction.count} {reaction.count === 1 ? 'reaction' : 'reactions'}"
```
with:
```svelte
			title={reactionTitle(reaction)}
```

**Step 3: Run type check**

Run: `cd apps/web && npm run check`
Expected: PASS (no type errors)

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/chat/ReactionBar.svelte
git commit -m "feat: show reactor names in reaction pill tooltip"
```

---

### Task 2: Pass members from MessageItem to ReactionBar

**Files:**
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte:244-249,301-306`

**Step 1: Add members prop to both ReactionBar usages**

There are two `<ReactionBar>` instances in `MessageItem.svelte` (lines 245-249 and 302-306). Update both to pass `members={app.members}`:

First instance (around line 245):
```svelte
			<ReactionBar
				reactions={message.reactions}
				{currentUserId}
				onToggle={handleToggleReaction}
				members={app.members}
			/>
```

Second instance (around line 302):
```svelte
			<ReactionBar
				reactions={message.reactions}
				{currentUserId}
				onToggle={handleToggleReaction}
				members={app.members}
			/>
```

**Step 2: Run type check**

Run: `cd apps/web && npm run check`
Expected: PASS

**Step 3: Manual smoke test**

Run: `cd apps/web && npm run dev`
- Open a text channel with messages that have reactions
- Hover a reaction pill — tooltip should show user display names
- Verify DMs do not show reaction pills (they don't support reactions)

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageItem.svelte
git commit -m "feat: pass server members to ReactionBar for name resolution"
```
