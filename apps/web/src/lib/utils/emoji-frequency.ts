const STORAGE_KEY = 'codec_frequent_emojis';
const DEFAULT_EMOJIS = ['👍', '❤️', '😂', '🎉', '🔥', '👀', '🚀', '💯'];

type EmojiFrequency = { emoji: string; count: number };

function load(): EmojiFrequency[] {
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (!raw) return [];
		return JSON.parse(raw) as EmojiFrequency[];
	} catch {
		return [];
	}
}

function save(data: EmojiFrequency[]): void {
	localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
}

export function recordEmojiUse(emoji: string): void {
	const data = load();
	const existing = data.find((e) => e.emoji === emoji);
	if (existing) {
		existing.count++;
	} else {
		data.push({ emoji, count: 1 });
	}
	data.sort((a, b) => b.count - a.count);
	save(data);
}

export function getFrequentEmojis(limit = 8): string[] {
	const data = load();
	if (data.length >= limit) {
		return data.slice(0, limit).map((e) => e.emoji);
	}
	// Pad with defaults not already in the list
	const result = data.map((e) => e.emoji);
	for (const d of DEFAULT_EMOJIS) {
		if (result.length >= limit) break;
		if (!result.includes(d)) result.push(d);
	}
	return result;
}
