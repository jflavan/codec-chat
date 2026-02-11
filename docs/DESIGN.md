# Codec Front-End Design Specification

This document defines the visual design language for Codec, inspired by Discord's proven chat-application patterns.

## Layout Architecture

Codec uses a **three-column layout** as its core structure:

### Left Sidebar – Server List (~72px wide)

- Vertical list of server icons rendered as circular avatars
- Home / logo button at the top
- "Add Server" button at the bottom
- Circular icons (48px) with 3px rounded-rectangle pill indicator for active server
- Hover: slight scale-up + rounded-rectangle shape morph
- Separator line between home icon and server list

### Middle Sidebar – Channels / Members (~240px wide)

- Server name displayed at top (bold, 16px)
- Categorized channel list below
  - Text channels prefixed with `#` icon
  - Collapsible categories (future)
- "Add Channel" button for Owner/Admin roles
- **User panel** pinned to bottom: avatar, display name, role badge, and status controls

### Main Content Area – Flexible Width

- **Top bar**: channel name with `#` prefix, description area
- **Message feed**: scrollable, with author avatars, usernames, timestamps, and message body
- **Message composer**: dark input field pinned to the bottom with rounded corners and a send button

### Right Sidebar – Members / Inspector (~240px wide)

- Online members list grouped by role (Owner, Admin, Member)
- Member cards: avatar, display name, role badge
- User profile card for the current user

## Color Scheme & Theming

### Dark Mode (Default)

| Token                | Value      | Usage                                  |
| -------------------- | ---------- | -------------------------------------- |
| `--bg-primary`       | `#313338`  | Main content background                |
| `--bg-secondary`     | `#2b2d31`  | Channel sidebar background             |
| `--bg-tertiary`      | `#1e1f22`  | Server sidebar & input field bg        |
| `--bg-message-hover` | `#2e3035`  | Message hover state                    |
| `--accent`           | `#5865f2`  | Blurple – primary accent/buttons       |
| `--accent-hover`     | `#4752c4`  | Button hover state                     |
| `--text-normal`      | `#dbdee1`  | Primary readable text                  |
| `--text-muted`       | `#949ba4`  | Secondary/timestamp text               |
| `--text-header`      | `#f2f3f5`  | Headers and channel names              |
| `--danger`           | `#da373c`  | Error states and destructive actions   |
| `--success`          | `#23a559`  | Success states and online indicators   |
| `--border`           | `#3f4147`  | Subtle dividers between sections       |

### Light Mode (Future)

| Token           | Value     |
| --------------- | --------- |
| `--bg-primary`  | `#ffffff` |
| `--bg-secondary`| `#f2f3f5` |
| Maintains same accent and status colors |

## Typography

| Element          | Font Family                                    | Size   | Weight | Color              |
| ---------------- | ---------------------------------------------- | ------ | ------ | ------------------ |
| App title        | `'gg sans', 'Space Grotesk', system-ui`        | 20px   | 700    | `--text-header`    |
| Server name      | same                                           | 16px   | 600    | `--text-header`    |
| Channel name     | same                                           | 16px   | 500    | `--text-normal`    |
| Category header  | same                                           | 12px   | 700    | `--text-muted`     |
| Message author   | same                                           | 16px   | 600    | `--text-header`    |
| Message body     | same                                           | 15px   | 400    | `--text-normal`    |
| Timestamp        | same                                           | 12px   | 400    | `--text-muted`     |
| Muted / meta     | same                                           | 13px   | 400    | `--text-muted`     |

## Key UI Components

### Messages

- **Avatar** on the left (40px, circular)
- **Username** and **timestamp** displayed inline on the first message in a group
- Grouped consecutive messages from the same author collapse the avatar/name (compact spacing)
- Hover reveals subtle background highlight
- Full message width; no chat-bubble wrapping

### Server Icons

- 48px circular by default
- Active server: pill indicator on the left (4px wide, rounded, accent-colored)
- Hover: border-radius morphs from circle toward rounded-rectangle (16px)
- Server initials shown when no avatar is present (colored background)

### Channel List Items

- Prefixed with `#` icon for text channels
- Active channel: lighter background + bold text + white color
- Hover: lighter background
- Muted channels show in `--text-muted`

### Buttons

- **Primary**: `--accent` background, white text, 3px border-radius
- **Secondary**: transparent background, `--text-normal` text
- **Danger**: `--danger` background, white text
- Hover states: slight color shift
- Disabled: reduced opacity (0.5), `not-allowed` cursor

### Input Fields

- Dark background (`--bg-tertiary`)
- Rounded corners (8px)
- Focus state: subtle `--accent` outline/glow
- Placeholder text in `--text-muted`
- Multiline composer with auto-grow (future)

### User Panel (Bottom of Channel Sidebar)

- Fixed to bottom of channel sidebar
- Current user avatar (32px), display name, role
- Subtle top border separator

### Member List

- Grouped by role with section headers (OWNER, ADMIN, MEMBER)
- Member cards: avatar (32px), display name, role badge
- Online indicator dot (future)

## Animations & Micro-interactions

- **Transitions**: 150–200ms ease for background/color changes
- **Server icon hover**: scale(1.1) + border-radius morph
- **Active pill**: height animates from 8px (hover) → 36px (active)
- **Channel hover**: background-color transition
- **Message hover**: background highlight
- **Buttons**: color shift on hover/active

## Responsive Design

| Breakpoint   | Layout                                                              |
| ------------ | ------------------------------------------------------------------- |
| ≥ 1200px     | Full four-column: servers + channels + chat + members               |
| 900–1199px   | Three-column: servers + channels + chat (members hidden/collapsed)  |
| ≤ 899px      | Single-column: slide-out navigation, full-width chat                |

## Accessibility

- **Keyboard navigation**: all interactive elements focusable, logical tab order
- **Focus indicators**: visible focus ring on interactive elements (`--accent` outline)
- **Screen reader**: semantic HTML (`<nav>`, `<main>`, `<aside>`, `<article>`), ARIA labels
- **Color contrast**: text meets WCAG AA minimum (4.5:1 for normal text)
- **Reduced motion**: respects `prefers-reduced-motion` media query

## Visual Hierarchy

- **Z-index layers**: Modals (50) > Dropdowns (40) > Tooltips (30) > Overlays (20) > Content (1)
- **Spacing**: consistent 8px base grid (4, 8, 12, 16, 24, 32px)
- **Depth**: subtle elevation via background color changes (no heavy drop shadows in dark mode)
- **Grouping**: visual separation via background color differences between columns

## Icons

- SVG-based inline icons for `#` (text channel), speaker (voice, future), settings cog (future)
- Consistent 20px size for sidebar icons, 16px for inline icons
- `currentColor` for icon fills so they follow text color

## Implementation Notes

- All colors defined as CSS custom properties on `:root` for easy theming
- Component-scoped styles in Svelte `<style>` blocks
- Font loaded via Google Fonts (`Space Grotesk` as primary, system-ui fallback)
- Semantic HTML throughout for accessibility
- `prefers-color-scheme` media query ready for future light mode toggle
