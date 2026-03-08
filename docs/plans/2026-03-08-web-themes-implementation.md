# Web Client Themes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add 4 preset color themes (Phosphor Green, Midnight, Ember, Light) selectable from Settings.

**Architecture:** CSS variable swapping via `data-theme` attribute on `<html>`. Theme stored in localStorage. Inline script in `app.html` prevents flash of wrong theme on load.

**Tech Stack:** SvelteKit, Svelte 5 runes, CSS custom properties, localStorage

**Design doc:** `docs/plans/2026-03-08-web-themes-design.md`

---

### Task 1: Add theme palettes to tokens.css

**Files:**
- Modify: `apps/web/src/lib/styles/tokens.css`

**Step 1: Add Midnight theme block after the `:root` block**

Add at line 26 (after the closing `}`):

```css
/* ===== Midnight – dark navy/slate palette ===== */

[data-theme='midnight'] {
	--bg-primary: #1E2233;
	--bg-secondary: #171B2E;
	--bg-secondary-rgb: 23, 27, 46;
	--bg-tertiary: #111525;
	--bg-message-hover: #252A3E;
	--accent: #5B8DEF;
	--accent-rgb: 91, 141, 239;
	--accent-hover: #7BA6FF;
	--text-normal: #C8CDD8;
	--text-muted: #8B919C;
	--text-header: #E2E6EF;
	--text-dim: #6B7280;
	--warn: #FFB000;
	--danger: #FF4D4D;
	--danger-rgb: 255, 77, 77;
	--success: #00FF66;
	--border: #2A3045;
	--grid: #1A2340;
	--mention-bg: #1A2845;
	--selection-bg: #182A48;
	--input-bg: #141828;
}
```

**Step 2: Add Ember theme block**

```css
/* ===== Ember – warm amber/orange palette ===== */

[data-theme='ember'] {
	--bg-primary: #1F1710;
	--bg-secondary: #18120C;
	--bg-secondary-rgb: 24, 18, 12;
	--bg-tertiary: #110D08;
	--bg-message-hover: #2A1F15;
	--accent: #F0A030;
	--accent-rgb: 240, 160, 48;
	--accent-hover: #FFB84D;
	--text-normal: #D4C4A8;
	--text-muted: #9A8A70;
	--text-header: #EAD8B8;
	--text-dim: #7D6D55;
	--warn: #FFB000;
	--danger: #FF3B3B;
	--danger-rgb: 255, 59, 59;
	--success: #00FF66;
	--border: #3A2E20;
	--grid: #2A2010;
	--mention-bg: #302415;
	--selection-bg: #2E2618;
	--input-bg: #15100A;
}
```

**Step 3: Add Light (Apple-inspired) theme block**

```css
/* ===== Light – Apple-inspired light palette ===== */

[data-theme='light'] {
	--bg-primary: #FFFFFF;
	--bg-secondary: #F5F5F7;
	--bg-secondary-rgb: 245, 245, 247;
	--bg-tertiary: #E8E8ED;
	--bg-message-hover: #F0F0F5;
	--accent: #0055CC;
	--accent-rgb: 0, 85, 204;
	--accent-hover: #0063D1;
	--text-normal: #1D1D1F;
	--text-muted: #6E6E73;
	--text-header: #000000;
	--text-dim: #8E8E93;
	--warn: #A35200;
	--danger: #CC0000;
	--danger-rgb: 204, 0, 0;
	--success: #34C759;
	--border: #D2D2D7;
	--grid: #E8E8ED;
	--mention-bg: #E8F0FE;
	--selection-bg: #DCE8FC;
	--input-bg: #F5F5F7;
}
```

**Step 4: Verify — open the app and manually add `data-theme="midnight"` to `<html>` in devtools**

Confirm all colors change. Repeat for `ember` and `light`.

**Step 5: Commit**

```bash
git add apps/web/src/lib/styles/tokens.css
git commit -m "feat: add midnight, ember, and light theme palettes to tokens.css"
```

---

### Task 2: Create theme utility module

**Files:**
- Create: `apps/web/src/lib/utils/theme.ts`

**Step 1: Create the theme utility**

```typescript
export const THEMES = [
	{ id: 'phosphor', name: 'Phosphor Green' },
	{ id: 'midnight', name: 'Midnight' },
	{ id: 'ember', name: 'Ember' },
	{ id: 'light', name: 'Light' }
] as const;

export type ThemeId = (typeof THEMES)[number]['id'];

const STORAGE_KEY = 'codec-theme';

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
	try {
		localStorage.setItem(STORAGE_KEY, id);
	} catch {
		// ignore — storage full or unavailable
	}
}
```

Note: `phosphor` is the default (`:root` styles), so we remove `data-theme` rather than setting it. This means the existing app works unchanged without any attribute.

**Step 2: Commit**

```bash
git add apps/web/src/lib/utils/theme.ts
git commit -m "feat: add theme utility for reading/applying themes from localStorage"
```

---

### Task 3: Add flash-prevention script to app.html

**Files:**
- Modify: `apps/web/src/app.html:10-11`

**Step 1: Add inline script and dynamic theme-color meta tag**

Insert after line 10 (`<meta name="theme-color" content="#050B07" />`), add:

```html
		<script>
			(function () {
				var t = '';
				try { t = localStorage.getItem('codec-theme') || ''; } catch (e) {}
				if (t && t !== 'phosphor') document.documentElement.dataset.theme = t;
			})();
		</script>
```

This runs synchronously before first paint, preventing a flash of the default theme.

**Step 2: Verify — set `localStorage.setItem('codec-theme', 'midnight')` in devtools console, then hard-refresh**

The page should load directly in the Midnight theme with no green flash.

**Step 3: Commit**

```bash
git add apps/web/src/app.html
git commit -m "feat: add inline theme script to prevent flash of wrong theme"
```

---

### Task 4: Add theme state to AppState

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add import**

At the top of the file, after the existing imports (around line 30), add:

```typescript
import { getTheme, applyTheme, type ThemeId } from '$lib/utils/theme.js';
```

**Step 2: Add theme state property**

Find the `settingsCategory` state declaration (line 124):

```typescript
settingsCategory = $state<'profile' | 'account' | 'voice-audio'>('profile');
```

Add directly below it:

```typescript
theme = $state<ThemeId>(getTheme());
```

**Step 3: Update settingsCategory type to include 'appearance'**

Change line 124 from:

```typescript
settingsCategory = $state<'profile' | 'account' | 'voice-audio'>('profile');
```

to:

```typescript
settingsCategory = $state<'profile' | 'account' | 'voice-audio' | 'appearance'>('profile');
```

**Step 4: Add setTheme method**

Find the `closeSettings()` method (around line 337). Add after it:

```typescript
setTheme(id: ThemeId): void {
	this.theme = id;
	applyTheme(id);
}
```

**Step 5: Apply theme on init**

In the `init()` method (around line 354), add `applyTheme(this.theme);` as the first line:

```typescript
init(): void {
	applyTheme(this.theme);
	this._loadUserVolumes();
	this._loadVoicePreferences();
```

This ensures the theme is applied when the app initializes (covers cases where the inline script in `app.html` might not fire, e.g., SPA navigation).

**Step 6: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat: add theme state and setTheme method to AppState"
```

---

### Task 5: Add Appearance category to Settings sidebar

**Files:**
- Modify: `apps/web/src/lib/components/settings/SettingsSidebar.svelte:6-10`

**Step 1: Add appearance category**

Find the `categories` array (line 6-10):

```typescript
const categories = [
	{ id: 'profile' as const, label: 'My Profile', icon: '👤' },
	{ id: 'account' as const, label: 'My Account', icon: '🔒' },
	{ id: 'voice-audio' as const, label: 'Voice & Audio', icon: '🎙️' }
];
```

Add the Appearance entry:

```typescript
const categories = [
	{ id: 'profile' as const, label: 'My Profile', icon: '👤' },
	{ id: 'account' as const, label: 'My Account', icon: '🔒' },
	{ id: 'voice-audio' as const, label: 'Voice & Audio', icon: '🎙️' },
	{ id: 'appearance' as const, label: 'Appearance', icon: '🎨' }
];
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/components/settings/SettingsSidebar.svelte
git commit -m "feat: add appearance category to settings sidebar"
```

---

### Task 6: Create AppearanceSettings component

**Files:**
- Create: `apps/web/src/lib/components/settings/AppearanceSettings.svelte`

**Step 1: Create the component**

Look at existing settings components (e.g., `VoiceAudioSettings.svelte`, `ProfileSettings.svelte`) for the styling pattern — they use the class `settings-content` wrapper and heading styles. Match that pattern.

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { THEMES, type ThemeId } from '$lib/utils/theme.js';

	const app = getAppState();

	const themeColors: Record<ThemeId, { bg: string; sidebar: string; accent: string; text: string }> = {
		phosphor: { bg: '#0B1A10', sidebar: '#07110A', accent: '#00FF66', text: '#86FF6B' },
		midnight: { bg: '#1E2233', sidebar: '#171B2E', accent: '#5B8DEF', text: '#C8CDD8' },
		ember: { bg: '#1F1710', sidebar: '#18120C', accent: '#F0A030', text: '#D4C4A8' },
		light: { bg: '#FFFFFF', sidebar: '#F5F5F7', accent: '#0055CC', text: '#1D1D1F' }
	};
</script>

<div class="settings-content">
	<h2>Appearance</h2>
	<p class="section-description">Choose a theme for Codec.</p>

	<div class="theme-grid">
		{#each THEMES as theme}
			{@const colors = themeColors[theme.id]}
			{@const isActive = app.theme === theme.id}
			<button
				class="theme-card"
				class:active={isActive}
				onclick={() => app.setTheme(theme.id)}
				style="--card-border: {isActive ? colors.accent : 'var(--border)'}"
			>
				<div class="theme-preview">
					<div class="preview-sidebar" style="background: {colors.sidebar}"></div>
					<div class="preview-main" style="background: {colors.bg}">
						<div class="preview-text" style="background: {colors.text}"></div>
						<div class="preview-text short" style="background: {colors.text}"></div>
						<div class="preview-accent" style="background: {colors.accent}"></div>
					</div>
				</div>
				<span class="theme-name">{theme.name}</span>
				{#if isActive}
					<span class="theme-check">✓</span>
				{/if}
			</button>
		{/each}
	</div>
</div>

<style>
	.settings-content {
		padding: 1rem 1.5rem;
	}

	h2 {
		color: var(--text-header);
		font-size: 1.1rem;
		font-weight: 600;
		margin: 0 0 0.25rem;
	}

	.section-description {
		color: var(--text-muted);
		font-size: 0.85rem;
		margin: 0 0 1.25rem;
	}

	.theme-grid {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: 0.75rem;
	}

	.theme-card {
		position: relative;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem;
		background: var(--bg-secondary);
		border: 2px solid var(--card-border);
		border-radius: 8px;
		cursor: pointer;
		transition: border-color 0.15s;
	}

	.theme-card:hover {
		border-color: var(--text-muted);
	}

	.theme-card.active:hover {
		border-color: var(--card-border);
	}

	.theme-preview {
		display: flex;
		width: 100%;
		height: 60px;
		border-radius: 4px;
		overflow: hidden;
		border: 1px solid var(--border);
	}

	.preview-sidebar {
		width: 25%;
	}

	.preview-main {
		flex: 1;
		display: flex;
		flex-direction: column;
		justify-content: center;
		gap: 4px;
		padding: 8px;
	}

	.preview-text {
		height: 4px;
		border-radius: 2px;
		width: 70%;
		opacity: 0.7;
	}

	.preview-text.short {
		width: 45%;
	}

	.preview-accent {
		height: 4px;
		border-radius: 2px;
		width: 30%;
	}

	.theme-name {
		color: var(--text-normal);
		font-size: 0.8rem;
		font-weight: 500;
	}

	.theme-check {
		position: absolute;
		top: 6px;
		right: 8px;
		color: var(--accent);
		font-size: 0.85rem;
		font-weight: 700;
	}

	@media (max-width: 480px) {
		.theme-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/components/settings/AppearanceSettings.svelte
git commit -m "feat: add AppearanceSettings component with theme picker grid"
```

---

### Task 7: Wire AppearanceSettings into the Settings modal

**Files:**
- Modify: `apps/web/src/lib/components/settings/UserSettingsModal.svelte`

**Step 1: Add import**

At the top of the `<script>` block, alongside the existing settings component imports, add:

```typescript
import AppearanceSettings from './AppearanceSettings.svelte';
```

**Step 2: Add rendering branch**

Find the if-else chain that renders settings content based on `app.settingsCategory` (around lines 59-65). It currently looks like:

```svelte
{#if app.settingsCategory === 'voice-audio'}
	<VoiceAudioSettings />
{:else if app.settingsCategory === 'account'}
	<AccountSettings />
{:else}
	<ProfileSettings />
{/if}
```

Change to:

```svelte
{#if app.settingsCategory === 'voice-audio'}
	<VoiceAudioSettings />
{:else if app.settingsCategory === 'account'}
	<AccountSettings />
{:else if app.settingsCategory === 'appearance'}
	<AppearanceSettings />
{:else}
	<ProfileSettings />
{/if}
```

**Step 3: Verify — open Settings, click Appearance, select each theme**

- Theme changes instantly on click
- Preview swatches display correctly
- Active theme shows checkmark and accent border
- Refreshing the page preserves the selected theme
- No flash of default theme on refresh

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/settings/UserSettingsModal.svelte
git commit -m "feat: wire AppearanceSettings into user settings modal"
```

---

### Task 8: Update theme-color meta tag for Light theme

**Files:**
- Modify: `apps/web/src/app.html:10`

**Step 1: Update the inline script**

The existing `<meta name="theme-color" content="#050B07" />` is hardcoded to the Phosphor Green tertiary color. Update the inline theme script (added in Task 3) to also set the theme-color meta tag:

Replace the script from Task 3 with:

```html
		<script>
			(function () {
				var t = '';
				try { t = localStorage.getItem('codec-theme') || ''; } catch (e) {}
				if (t && t !== 'phosphor') document.documentElement.dataset.theme = t;
				var colors = { phosphor: '#050B07', midnight: '#111525', ember: '#110D08', light: '#E8E8ED' };
				var meta = document.querySelector('meta[name="theme-color"]');
				if (meta && colors[t]) meta.setAttribute('content', colors[t]);
			})();
		</script>
```

Also update `applyTheme` in `apps/web/src/lib/utils/theme.ts` to set the meta tag on runtime theme switches:

```typescript
const THEME_COLORS: Record<ThemeId, string> = {
	phosphor: '#050B07',
	midnight: '#111525',
	ember: '#110D08',
	light: '#E8E8ED'
};

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
```

**Step 2: Verify — switch to Light theme, check the browser's title bar / mobile status bar color matches**

**Step 3: Commit**

```bash
git add apps/web/src/app.html apps/web/src/lib/utils/theme.ts
git commit -m "feat: update theme-color meta tag when switching themes"
```

---

### Task 9: Final smoke test and cleanup

**Step 1: Run type checks**

```bash
cd apps/web && npm run check
```

Expected: no errors.

**Step 2: Manual testing checklist**

- [ ] Default (no localStorage) loads Phosphor Green
- [ ] Each theme applies instantly from Settings > Appearance
- [ ] Hard refresh preserves selected theme with no flash
- [ ] All 4 themes: text is readable, borders are visible, accent buttons stand out
- [ ] Light theme: scrollbars, inputs, and modals look correct on light backgrounds
- [ ] Mobile: theme-color meta tag updates for each theme
- [ ] Settings modal itself looks correct in each theme

**Step 3: Fix any issues found**

**Step 4: Final commit if any fixes were needed**

```bash
git add -A
git commit -m "fix: address theme smoke test issues"
```
