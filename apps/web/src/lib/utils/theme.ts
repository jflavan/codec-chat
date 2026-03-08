export const THEMES = [
	{ id: 'phosphor', name: 'Phosphor Green' },
	{ id: 'midnight', name: 'Midnight' },
	{ id: 'ember', name: 'Ember' },
	{ id: 'light', name: 'Light' }
] as const;

export type ThemeId = (typeof THEMES)[number]['id'];

const STORAGE_KEY = 'codec-theme';

const THEME_COLORS: Record<ThemeId, string> = {
	phosphor: '#050B07',
	midnight: '#111525',
	ember: '#110D08',
	light: '#E8E8ED'
};

export function getTheme(): ThemeId {
	try {
		const stored = localStorage.getItem(STORAGE_KEY);
		if (stored && THEMES.some((t) => t.id === stored)) {
			return stored as ThemeId;
		}
	} catch {
		// ignore — SSR or storage unavailable
	}
	return 'phosphor';
}

export function applyTheme(id: ThemeId): void {
	if (id === 'phosphor') {
		delete document.documentElement.dataset.theme;
	} else {
		document.documentElement.dataset.theme = id;
	}
	const meta = document.querySelector('meta[name="theme-color"]');
	if (meta) meta.setAttribute('content', THEME_COLORS[id]);
	try {
		localStorage.setItem(STORAGE_KEY, id);
	} catch {
		// ignore — storage full or unavailable
	}
}
