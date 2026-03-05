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
