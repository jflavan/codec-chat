/** Format an ISO date string as a short time (e.g. "2:30 PM"). */
export function formatTime(value: string): string {
	const date = new Date(value);
	if (Number.isNaN(date.getTime())) return '';
	return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}
