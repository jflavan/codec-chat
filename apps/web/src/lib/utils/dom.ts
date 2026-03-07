/** Find the nearest ancestor with overflow-y: auto or scroll (the clipping boundary). */
export function findScrollParent(el: HTMLElement): HTMLElement | null {
	let parent = el.parentElement;
	while (parent) {
		const { overflowY } = getComputedStyle(parent);
		if (overflowY === 'auto' || overflowY === 'scroll') return parent;
		parent = parent.parentElement;
	}
	return null;
}

/**
 * Check whether an element is too close to the top of its scroll container
 * for content to render above it without being clipped.
 */
export function isNearScrollTop(el: HTMLElement, threshold: number, cachedParent?: HTMLElement | null): { flipped: boolean; scrollParent: HTMLElement | null } {
	const rect = el.getBoundingClientRect();
	const scrollParent = cachedParent?.isConnected ? cachedParent : findScrollParent(el);
	if (scrollParent) {
		const containerRect = scrollParent.getBoundingClientRect();
		return { flipped: rect.top - containerRect.top < threshold, scrollParent };
	}
	return { flipped: rect.top < threshold, scrollParent: null };
}
