// apps/web/src/lib/utils/reactions.ts
//
// Shared reaction-snapshot helpers used by both MessageStore and DmStore
// to deduplicate optimistic reaction updates against SignalR echoes.

import type { UIStore } from '$lib/state/ui-store.svelte.js';

interface ReactionLike {
	emoji: string;
	count: number;
	userIds: string[];
}

/** Deterministic JSON representation of a reaction array for comparison. */
export function serializeReactionSnapshot(reactions: ReadonlyArray<ReactionLike>): string {
	return JSON.stringify(
		reactions
			.map((reaction) => ({
				emoji: reaction.emoji,
				count: reaction.count,
				userIds: [...reaction.userIds].sort()
			}))
			.sort((a, b) => a.emoji.localeCompare(b.emoji))
	);
}

/** Record an optimistic reaction update so the SignalR echo can be ignored. */
export function rememberReactionUpdate(
	ui: UIStore,
	messageId: string,
	reactions: ReadonlyArray<ReactionLike>
): void {
	const serialized = serializeReactionSnapshot(reactions);
	const next = new Map(ui.ignoredReactionUpdates);
	next.set(messageId, [...(next.get(messageId) ?? []), serialized]);
	ui.ignoredReactionUpdates = next;
}

/** Check if a SignalR reaction update matches an optimistic update and remove it. Returns true if matched. */
export function matchAndRemoveReactionSnapshot(
	ui: UIStore,
	messageId: string,
	reactions: ReadonlyArray<ReactionLike>
): boolean {
	const queue = ui.ignoredReactionUpdates.get(messageId);
	if (!queue?.length) return false;

	const serialized = serializeReactionSnapshot(reactions);
	const matchedIndex = queue.indexOf(serialized);
	if (matchedIndex === -1) return false;

	const next = new Map(ui.ignoredReactionUpdates);
	const remaining = queue.filter((_, index) => index !== matchedIndex);
	if (remaining.length > 0) {
		next.set(messageId, remaining);
	} else {
		next.delete(messageId);
	}
	ui.ignoredReactionUpdates = next;
	return true;
}
