# Web Client Themes — Design

## Summary

4 preset color themes selectable from a new "Appearance" category in the Settings modal. Theme preference persisted in localStorage (`codec-theme`). Themes work by swapping CSS custom property values at the document root via `data-theme` attribute — no component changes needed since all components already reference CSS variables.

## Themes

### Phosphor Green (default, `phosphor`)

Current palette — unchanged. CRT hacker aesthetic, hue ~140 (green).

| Token | Value |
|---|---|
| `--bg-primary` | `#0B1A10` |
| `--bg-secondary` | `#07110A` |
| `--bg-secondary-rgb` | `7, 17, 10` |
| `--bg-tertiary` | `#050B07` |
| `--bg-message-hover` | `#102417` |
| `--accent` | `#00FF66` |
| `--accent-rgb` | `0, 255, 102` |
| `--accent-hover` | `#33FFB2` |
| `--text-normal` | `#86FF6B` |
| `--text-muted` | `#3ED44E` |
| `--text-header` | `#B7FF9A` |
| `--text-dim` | `#2D7A3A` |
| `--warn` | `#FFB000` |
| `--danger` | `#FF3B3B` |
| `--danger-rgb` | `255, 59, 59` |
| `--success` | `#00FF66` |
| `--border` | `#1E3A26` |
| `--grid` | `#14301F` |
| `--mention-bg` | `#123A22` |
| `--selection-bg` | `#0F3A22` |
| `--input-bg` | `#06160C` |

### Midnight (`midnight`)

Dark navy/slate palette. Softer on the eyes, hue ~228 (blue).

| Token | Value |
|---|---|
| `--bg-primary` | `#1E2233` |
| `--bg-secondary` | `#171B2E` |
| `--bg-secondary-rgb` | `23, 27, 46` |
| `--bg-tertiary` | `#111525` |
| `--bg-message-hover` | `#252A3E` |
| `--accent` | `#5B8DEF` |
| `--accent-rgb` | `91, 141, 239` |
| `--accent-hover` | `#7BA6FF` |
| `--text-normal` | `#C8CDD8` |
| `--text-muted` | `#8B919C` |
| `--text-header` | `#E2E6EF` |
| `--text-dim` | `#6B7280` |
| `--warn` | `#FFB000` |
| `--danger` | `#FF4D4D` |
| `--danger-rgb` | `255, 77, 77` |
| `--success` | `#00FF66` |
| `--border` | `#2A3045` |
| `--grid` | `#1A2340` |
| `--mention-bg` | `#1A2845` |
| `--selection-bg` | `#182A48` |
| `--input-bg` | `#141828` |

### Ember (`ember`)

Warm dark theme with amber/orange tones, hue ~32 (amber).

| Token | Value |
|---|---|
| `--bg-primary` | `#1F1710` |
| `--bg-secondary` | `#18120C` |
| `--bg-secondary-rgb` | `24, 18, 12` |
| `--bg-tertiary` | `#110D08` |
| `--bg-message-hover` | `#2A1F15` |
| `--accent` | `#F0A030` |
| `--accent-rgb` | `240, 160, 48` |
| `--accent-hover` | `#FFB84D` |
| `--text-normal` | `#D4C4A8` |
| `--text-muted` | `#9A8A70` |
| `--text-header` | `#EAD8B8` |
| `--text-dim` | `#7D6D55` |
| `--warn` | `#FFB000` |
| `--danger` | `#FF3B3B` |
| `--danger-rgb` | `255, 59, 59` |
| `--success` | `#00FF66` |
| `--border` | `#3A2E20` |
| `--grid` | `#2A2010` |
| `--mention-bg` | `#302415` |
| `--selection-bg` | `#2E2618` |
| `--input-bg` | `#15100A` |

### Light — Apple-inspired (`light`)

Light background, dark text. Apple system color family, hue ~240 (cool gray).

| Token | Value |
|---|---|
| `--bg-primary` | `#FFFFFF` |
| `--bg-secondary` | `#F5F5F7` |
| `--bg-secondary-rgb` | `245, 245, 247` |
| `--bg-tertiary` | `#E8E8ED` |
| `--bg-message-hover` | `#F0F0F5` |
| `--accent` | `#0055CC` |
| `--accent-rgb` | `0, 85, 204` |
| `--accent-hover` | `#0063D1` |
| `--text-normal` | `#1D1D1F` |
| `--text-muted` | `#6E6E73` |
| `--text-header` | `#000000` |
| `--text-dim` | `#8E8E93` |
| `--warn` | `#A35200` |
| `--danger` | `#CC0000` |
| `--danger-rgb` | `204, 0, 0` |
| `--success` | `#34C759` |
| `--border` | `#D2D2D7` |
| `--grid` | `#E8E8ED` |
| `--mention-bg` | `#E8F0FE` |
| `--selection-bg` | `#DCE8FC` |
| `--input-bg` | `#F5F5F7` |

## Contrast Audit

All themes verified against WCAG AA:
- **Body text** (textNormal on all backgrounds): 4.5:1+ across all themes
- **Secondary text** (textMuted on all backgrounds): 4.5:1+ (Light theme textMuted corrected from `#86868B` to `#6E6E73`)
- **Accent on backgrounds**: AA or better (Light accent corrected from `#007AFF` to `#0055CC` for 6.62:1)
- **Danger/warn**: AA on respective backgrounds (Light warn corrected from `#FF9500` to `#A35200`, danger from `#FF3B30` to `#CC0000`)
- **textDim**: 3:1+ (AA-large), acceptable for placeholder/disabled use
- **Borders**: Decorative only, low contrast by design

## Color Consistency

Each theme maintains a single hue family across all tokens:
- **Phosphor Green**: H~140 (green)
- **Midnight**: H~228 (blue-navy)
- **Ember**: H~32 (amber-brown)
- **Light**: H~240 (cool gray) with accent-hued mention/selection highlights

## Architecture

### CSS — `tokens.css`

`:root` keeps Phosphor Green values (default). Three `[data-theme="..."]` selector blocks override all variables. Single file, no dynamic imports.

### Flash prevention — `app.html`

Inline `<script>` before first paint reads `codec-theme` from localStorage and sets `data-theme` on `<html>`. ~4 lines, no imports.

### Theme store — new `lib/utils/theme.ts`

- `THEMES`: array of `{ id, name }` for the picker
- `getTheme()`: reads localStorage, defaults to `'phosphor'`
- `setTheme(id)`: writes localStorage, sets `document.documentElement.dataset.theme`

### App state — `app-state.svelte.ts`

- New `theme` state field initialized from `getTheme()`
- `setTheme(id)` method calls theme store and updates reactive state

### Settings UI

- New `'appearance'` category in Settings modal
- New `AppearanceSettings.svelte` component
- Grid of 4 theme cards with color preview swatches (bg-tertiary, bg-primary, accent, text-normal bars)
- Active theme gets accent-colored border/checkmark
- Clicking applies instantly

## Files Changed

**Modified:**
1. `apps/web/src/lib/styles/tokens.css` — 3 `[data-theme]` blocks
2. `apps/web/src/app.html` — inline flash-prevention script
3. `apps/web/src/lib/state/app-state.svelte.ts` — theme state + setter
4. `apps/web/src/lib/components/settings/SettingsModal.svelte` — Appearance category
5. Settings category type — add `'appearance'`

**Created:**
1. `apps/web/src/lib/utils/theme.ts` — theme constants and read/write
2. `apps/web/src/lib/components/settings/AppearanceSettings.svelte` — theme picker

**No changes to:**
- Any existing component (all already use CSS variables)
- Backend / API
- Any styles besides `tokens.css`

## Storage

- Key: `codec-theme`
- Values: `'phosphor'` | `'midnight'` | `'ember'` | `'light'`
- Default: `'phosphor'` (current look, no migration needed)
- localStorage only — no backend sync
