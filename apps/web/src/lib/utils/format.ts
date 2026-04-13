const timeCache = new Map<string, string>();
const timestampCache = new Map<string, string>();
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

/**
 * Format a message timestamp with date context.
 * - Today: "2:30 PM"
 * - Yesterday: "Yesterday at 2:30 PM"
 * - Older: "04/11/2026 2:30 PM"
 */
export function formatMessageTimestamp(value: string): string {
	const now = new Date();
	const today = `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}`;
	const cacheKey = `${today}:${value}`;

	const cached = timestampCache.get(cacheKey);
	if (cached !== undefined) return cached;

	const date = new Date(value);
	if (Number.isNaN(date.getTime())) return '';

	const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

	let result: string;
	if (isSameDay(date, now)) {
		result = time;
	} else if (isYesterday(date, now)) {
		result = `Yesterday at ${time}`;
	} else {
		result = date.toLocaleDateString([], { month: '2-digit', day: '2-digit', year: 'numeric' }) + ' ' + time;
	}

	if (timestampCache.size >= TIME_CACHE_MAX) timestampCache.clear();
	timestampCache.set(cacheKey, result);
	return result;
}

/**
 * Format a date for a separator divider.
 * - Today: "Today"
 * - Yesterday: "Yesterday"
 * - This year: "April 11"
 * - Older: "April 11, 2025"
 */
export function formatDateSeparator(value: string): string {
	const date = new Date(value);
	if (Number.isNaN(date.getTime())) return '';

	const now = new Date();
	if (isSameDay(date, now)) return 'Today';
	if (isYesterday(date, now)) return 'Yesterday';

	const sameYear = date.getFullYear() === now.getFullYear();
	return date.toLocaleDateString([], {
		month: 'long',
		day: 'numeric',
		...(sameYear ? {} : { year: 'numeric' })
	});
}

/** Check whether two ISO date strings fall on different calendar days. */
export function isDifferentDay(a: string, b: string): boolean {
	const da = new Date(a);
	const db = new Date(b);
	return da.getFullYear() !== db.getFullYear() ||
		da.getMonth() !== db.getMonth() ||
		da.getDate() !== db.getDate();
}

function isSameDay(a: Date, b: Date): boolean {
	return a.getFullYear() === b.getFullYear() &&
		a.getMonth() === b.getMonth() &&
		a.getDate() === b.getDate();
}

function isYesterday(date: Date, now: Date): boolean {
	const yesterday = new Date(now);
	yesterday.setDate(yesterday.getDate() - 1);
	return isSameDay(date, yesterday);
}
