## Metal Gear Solid “CODEC”–inspired palette (phosphor-green CRT)

This scheme aims for: near-black green “glass”, deep olive panels, bright phosphor text, subtle grid/border lines, and a small amount of amber for “warning/attention” beats.

### Core tokens (recommended)
| Token | Use | Hex |
|---|---|---|
| `bg-0` | App background / behind panels | `#050B07` |
| `bg-1` | Main chat background | `#07110A` |
| `surface-1` | Message list surface / cards | `#0B1A10` |
| `surface-2` | Elevated popouts / modals | `#102417` |
| `border` | Dividers / outlines | `#1E3A26` |
| `grid` | Subtle UI lines | `#14301F` |
| `text-strong` | Primary text | `#B7FF9A` |
| `text` | Normal text | `#86FF6B` |
| `text-muted` | Timestamps / secondary labels | `#3ED44E` |
| `text-dim` | Placeholders / disabled | `#2D7A3A` |
| `accent` | Primary accent (links, buttons) | `#00FF66` |
| `accent-2` | Secondary accent (info) | `#33FFB2` |
| `warn` | Warning/attention (amber) | `#FFB000` |
| `danger` | Destructive actions / errors | `#FF3B3B` |
| `mention-bg` | Mention highlight background | `#123A22` |
| `selection-bg` | Text selection background | `#0F3A22` |
| `input-bg` | Composer/input background | `#06160C` |

If you want it *more* “monochrome CRT”, drop `warn`/`danger` saturation (or tint them green) and use them sparingly.

---

## How to apply it to a chat UI (general mapping)

Most chat apps boil down to the same surfaces:

- **Window background:** `bg-1`
- **Sidebar / channel list:** `bg-0` or `surface-1`
- **Chat surface:** `surface-1`
- **Popouts/modals/tooltips:** `surface-2`
- **Dividers/borders:** `border` / `grid`
- **Primary text:** `text-strong` (headers), `text` (body)
- **Secondary text:** `text-muted` (timestamps, metadata), `text-dim` (placeholder/disabled)
- **Interactive:** `accent` (links, primary buttons, toggles)
- **Mention & selection:** `mention-bg`, `selection-bg`
- **Input:** `input-bg` with `border`

**Accessibility note:** neon greens can bloom on dark backgrounds. If legibility suffers, use `text-strong` for body text and reserve `text` for secondary content.

---

## Discord (practical application)

### Option A: Use a client theme (e.g., BetterDiscord / Vencord)
Discord exposes many CSS variables you can override. Map your tokens to Discord’s common variables:

```css
:root {
  --background-primary: #0B1A10;   /* surface-1 */
  --background-secondary: #07110A; /* bg-1 */
  --background-tertiary: #050B07;  /* bg-0 */
  --background-floating: #102417;  /* surface-2 */

  --text-normal: #86FF6B;          /* text */
  --text-muted: #3ED44E;           /* text-muted */
  --header-primary: #B7FF9A;       /* text-strong */
  --header-secondary: #86FF6B;

  --interactive-normal: #3ED44E;
  --interactive-hover: #86FF6B;
  --interactive-active: #B7FF9A;

  --channels-default: #3ED44E;
  --channeltextarea-background: #06160C; /* input-bg */

  --brand-experiment: #00FF66;     /* accent */
  --brand-experiment-560: #33FFB2; /* accent-2 */

  --background-mentioned: #123A22; /* mention-bg */
  --background-mentioned-hover: #163F27;
  --background-message-hover: #0F3A22; /* selection-ish */
  --border-subtle: #1E3A26;        /* border */
}
```

**Optional “CRT scanline” vibe (subtle):**
- Add a faint repeating linear gradient overlay on the chat container only. Keep opacity very low to avoid readability issues.

### Option B: No custom CSS
Discord’s stock theming is limited; your best approximation is:
- Use **Dark** theme
- Choose an **Accent Color** close to `#00FF66` (where available)
- Use a matching wallpaper/background only if your client supports it

---

## Slack (works well: built-in “Custom theme”)

Slack allows a custom theme with a fixed set of UI colors (mainly sidebar). In Slack, go to **Preferences → Themes → Custom** and paste an 8-color theme.

A good CODEC-like Slack theme (8 slots) is:

1. **Column BG:** `#050B07`  
2. **Menu BG Hover:** `#07110A`  
3. **Active Item:** `#123A22`  
4. **Active Item Text:** `#B7FF9A`  
5. **Hover Item:** `#0B1A10`  
6. **Text Color:** `#86FF6B`  
7. **Active Presence:** `#00FF66`  
8. **Mention Badge:** `#FFB000`  *(or `#00FF66` if you want pure monochrome)*

Slack won’t fully recolor the message pane to phosphor green, but this gets the “terminal sidebar + neon accents” feel immediately.

---

## Microsoft Teams (more constrained)

Teams (the standard desktop/web client) generally does **not** allow a full custom color scheme like Slack. Practical options:

1. **Use Teams Dark theme + OS accent color**
   - Set Windows/macOS accent color near `#00FF66` and use **Dark** in Teams.
   - You’ll get green accents in some places, but not full recoloring.

2. **If you’re building a Teams app (custom UI)**
   - You *can* apply this scheme inside your app (tabs) using Fluent UI theming.
   - Map tokens like:
     - Backgrounds: `bg-1/surface-1`
     - Primary text: `text-strong`
     - Brand/accent: `accent`
     - Borders: `border`

If you tell me whether you mean “Teams client theme” or “a Teams app you control,” I can give the exact Fluent UI token mapping.

---

## Quick “best fit” recommendation
- **Slack:** easiest to implement and looks closest with built-in custom themes.
- **Discord:** best if you can use CSS-based theming (BetterDiscord/Vencord).
- **Teams:** closest match only inside custom apps; otherwise limited.

If you say which platform you’re targeting first (Discord/Slack/Teams) and whether you can use custom CSS/plugins, I’ll tailor the exact mappings and provide a ready-to-paste theme file/snippet for that platform.