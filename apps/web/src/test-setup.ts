// Polyfill localStorage for jsdom environments where it may not be available
if (typeof globalThis.localStorage === 'undefined' || typeof globalThis.localStorage.clear !== 'function') {
	const store = new Map<string, string>();
	const localStorage = {
		getItem: (key: string) => store.get(key) ?? null,
		setItem: (key: string, value: string) => store.set(key, value),
		removeItem: (key: string) => store.delete(key),
		clear: () => store.clear(),
		get length() { return store.size; },
		key: (index: number) => [...store.keys()][index] ?? null,
	};
	Object.defineProperty(globalThis, 'localStorage', { value: localStorage, writable: true });
}
