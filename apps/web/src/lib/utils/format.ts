const timeCache = new Map<string, string>();
const TIME_CACHE_MAX = 500;

/** Format an ISO date string as a short time (e.g. "2:30 PM"). */
export function formatTime(value: string): string {
	const cached = timeCache.get(value);
	if (cached !== undefined) return cached;

	const date = new Date(value);
	if (Number.isNaN(date.getTime())) return '';
	const result = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

	if (timeCache.size >= TIME_CACHE_MAX) timeCache.clear();
	timeCache.set(value, result);
	return result;
}
