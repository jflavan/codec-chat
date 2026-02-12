# Codec Front-End Design Specification

This document defines the visual design language for Codec, using a **Metal Gear Solid "CODEC"–inspired phosphor-green CRT palette** (see [THEME.md](THEME.md)) applied to Discord's proven chat-application layout patterns.

## Layout Architecture

Codec uses a **three-column layout** as its core structure:

### Left Sidebar – Server List (~72px wide)

- Vertical list of server icons rendered as circular avatars
- Home / logo button at the top with notification badge for pending friend requests
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

The palette is drawn from the **CODEC phosphor-green CRT** theme defined in [THEME.md](THEME.md). Every surface uses a near-black green "glass", with bright phosphor text and a small amount of amber/red for warning and danger states.

### CODEC CRT Mode (Default)

| Token                | Value      | Usage                                  |
| -------------------- | ---------- | -------------------------------------- |
| `--bg-primary`       | `#0B1A10`  | Main chat surface (surface-1)          |
| `--bg-secondary`     | `#07110A`  | Channel sidebar background (bg-1)      |
| `--bg-tertiary`      | `#050B07`  | Server sidebar & app background (bg-0) |
| `--bg-message-hover` | `#102417`  | Message hover / elevated popouts (surface-2) |
| `--accent`           | `#00FF66`  | Primary accent – links, buttons, toggles |
| `--accent-hover`     | `#33FFB2`  | Secondary accent / button hover state  |
| `--text-normal`      | `#86FF6B`  | Primary readable text (body)           |
| `--text-muted`       | `#3ED44E`  | Timestamps, secondary labels           |
| `--text-header`      | `#B7FF9A`  | Headers, channel names (text-strong)   |
| `--text-dim`         | `#2D7A3A`  | Placeholders, disabled text            |
| `--warn`             | `#FFB000`  | Warning / attention (amber)            |
| `--danger`           | `#FF3B3B`  | Error states and destructive actions   |
| `--success`          | `#00FF66`  | Success states and online indicators   |
| `--border`           | `#1E3A26`  | Dividers / outlines between sections   |
| `--grid`             | `#14301F`  | Subtle UI grid lines                   |
| `--mention-bg`       | `#123A22`  | Mention highlight background           |
| `--selection-bg`     | `#0F3A22`  | Text selection background              |
| `--input-bg`         | `#06160C`  | Composer / input field background      |

## Typography

| Element          | Font Family                                    | Size   | Weight | Color              |
| ---------------- | ---------------------------------------------- | ------ | ------ | ------------------ |
| App title        | `'Space Grotesk', system-ui, sans-serif`        | 20px   | 700    | `--text-header`    |
| Server name      | same                                           | 16px   | 600    | `--text-header`    |
| Channel name     | same                                           | 16px   | 500    | `--text-normal`    |
| Category header  | same                                           | 12px   | 700    | `--text-muted`     |
| Message author   | same                                           | 16px   | 600    | `--text-header`    |
| Message body     | same                                           | 15px   | 400    | `--text-normal`    |
| Timestamp        | same                                           | 12px   | 400    | `--text-muted`     |
| Muted / meta     | same                                           | 13px   | 400    | `--text-muted`     |
| Placeholder      | same                                           | 15px   | 400    | `--text-dim`       |

## Key UI Components

### Messages

- **Avatar** on the left (40px, circular)
- **Username** and **timestamp** displayed inline on the first message in a group
- Grouped consecutive messages from the same author collapse the avatar/name (compact spacing)
- Hover reveals subtle background highlight
- Full message width; no chat-bubble wrapping

### Home Icon Notification Badge

- Red badge (`--danger` background, white text) at bottom-right of the Home icon
- Displays the count of pending incoming friend requests
- Only visible when count > 0
- 18px height, min-width 18px, 9px border-radius (pill shape)
- 11px bold font, centered text
- 3px solid border matching `--bg-tertiary` for visual separation from the icon
- Reactively updates when friend requests are received/accepted/declined via SignalR

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

- Dark background (`--input-bg`)
- Rounded corners (8px)
- Focus state: subtle `--accent` outline/glow
- Placeholder text in `--text-dim`
- Multiline composer with auto-grow (future)

### User Panel (Bottom of Channel Sidebar)

- Fixed to bottom of channel sidebar
- Current user avatar (32px), display name, role
- Subtle top border separator
- **Avatar upload**: clicking the avatar opens a file picker; hover shows a semi-transparent overlay with a "+" icon

### Avatar Display

- **Messages**: 40px circular avatar to the left of the author name; falls back to a colored initial placeholder when no image is available
- **Member list**: 32px circular avatar; same fallback behavior
- **User panel**: 32px circular avatar with click-to-upload overlay
- **Fallback chain**: server avatar → custom global avatar → Google profile picture → initial letter on accent-colored circle

### Member List

- Grouped by role with section headers (OWNER, ADMIN, MEMBER)
- Member cards: avatar (32px), display name, role badge
- Online indicator dot (future)

## Emoji Reactions

### Floating Action Bar

- Appears on message hover, positioned at top-right of the message (`top: -14px; right: 32px`)
- Contains a smiley-face react button (34×32px)
- Styled with `--bg-secondary` background, `--border` border, 6px border-radius
- Hover: background shifts to `--bg-message-hover`, icon to `--accent`

### Emoji Picker Popover

- Opens below the react button (`top: calc(100% + 4px); right: 0`)
- Displays 8 quick-select emojis: 👍 ❤️ 😂 🎉 🔥 👀 🚀 💯
- `--bg-secondary` background with `--border` border, 8px border-radius, subtle box-shadow
- Each emoji is a 36×36px button with hover background highlight
- Clicking an emoji toggles the reaction and closes the picker

### Reaction Pills

- Rendered below the message body via `ReactionBar.svelte`
- Each pill: emoji + count, inline-flex, 6px padding, 12px border-radius
- Default: `--bg-tertiary` background, `--border` border, `--text-muted` count text
- Active (current user has reacted): `--accent` border, `--accent` count text
- Clickable to toggle the reaction on/off
- Hover: background shifts to `--bg-message-hover`

## Friends Panel

The Friends panel replaces the channel sidebar + chat area + members sidebar when the Home icon is active. It uses a simplified two-column layout: server icon rail (72px) + Friends panel (flexible width).

### Tab Bar

Three tabs at the top of the Friends panel:

| Tab | Content |
|-----|---------|
| **All Friends** | Confirmed friends list |
| **Pending** | Incoming and outgoing friend requests (badge shows count) |
| **Add Friend** | User search and send request |

- Tab bar uses `--bg-secondary` background with `--accent` underline on the active tab
- Tabs styled as buttons with `--text-muted` default, `--accent` when active
- Pending tab shows a badge count (total incoming + outgoing) styled with `--accent` background

### All Friends Tab

- List of confirmed friends, each showing: avatar (32px circular), display name, and "Friends since" date
- Remove Friend button with `--danger` color, visible on each row
- Empty state message when no friends exist
- Follows the same card pattern as `MemberItem` in `MembersSidebar`

### Pending Tab

- **Incoming Requests** section header, followed by request cards
  - Each card: avatar (32px), display name, request date
  - Accept button (checkmark, `--accent` / primary style) and Decline button (✕, `--danger`)
- **Outgoing Requests** section header, followed by request cards
  - Each card: avatar (32px), display name, request date
  - Cancel button (✕, `--danger`)
- Empty state messages for each section when no requests exist

### Add Friend Tab

- Text input field with placeholder "Search by name or email..."
- Uses `--input-bg` background, `--accent` focus glow (same as Composer)
- Search results appear below after 300ms debounce
- Each result shows: avatar (32px), display name, email, and a contextual action button
  - "Send Request" button (`--accent` primary style) if no existing relationship
  - "Pending" label (disabled, `--text-muted`) if a request already exists
  - "Friends" label (disabled, `--text-muted`) if already friends
- Loading state shown during search with `--text-muted` text

### Friends Mode Responsive Behavior

| Breakpoint | Layout |
|-----------|--------|
| ≥ 1200px | Two-column: server rail (72px) + Friends panel (flexible) |
| 900–1199px | Two-column: server rail (72px) + Friends panel (flexible) |
| ≤ 899px | Single-column: slide-out navigation, full-width Friends panel |

## Direct Messages

### Home Screen Layout

When the Home icon is selected, the layout changes to a three-column design:

| Column | Width | Content |
|--------|-------|---------|
| Server icon rail | 72px | Same as server mode |
| Home sidebar | 240px | Friends navigation button + DM conversation list |
| Main area | Flexible | FriendsPanel (no DM selected) or DmChatArea (DM selected) |

### Home Sidebar

- **Friends navigation button** at the top: icon + "Friends" label, pending badge count
  - Active state: `--bg-message-hover` background, `--text-header` color
  - Clicking deselects any active DM and shows the FriendsPanel
- **DM Conversations list** below: scrollable list of active conversations
  - Section header: "DIRECT MESSAGES" (11px, uppercase, `--text-muted`)

### DM Conversation List Entries

- Each entry shows: participant avatar (32px circular), display name, last message preview (truncated, `--text-muted`)
- Active (selected) conversation: `--bg-message-hover` background, `--text-header` name
- Hover: `--bg-message-hover` background, close button visible
- Close button (✕): appears on hover at right side, `--danger` on hover

### DM Chat Area

The DM chat area reuses existing chat patterns with DM-specific adaptations:

- **Header**: Participant avatar (24px) + display name (no `#` prefix, no server context)
- **Message Feed**: Same grouping, avatars, timestamps, and hover states as server messages
- **Composer**: Placeholder text "Message @{displayName}", same styling as channel composer
- **Typing Indicator**: Shows "{displayName} is typing…" with animated dots
- **Jump to bottom**: Same floating pill button with unread badge

### Starting a DM from Friends List

- Clicking a friend in the **All Friends** tab opens (or creates) a DM conversation
- The friend entry is styled as a button with pointer cursor
- The DM conversation is immediately selected in the sidebar and the chat area updates

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

- All colors defined as CSS custom properties on `:root` in `$lib/styles/tokens.css` following the CODEC CRT palette in [THEME.md](THEME.md)
- Global base styles (resets, font imports, selection, focus-visible) in `$lib/styles/global.css`, imported once in `+layout.svelte`
- Component-scoped styles in Svelte `<style>` blocks within each component
- Feature-grouped component directories: `server-sidebar/`, `channel-sidebar/`, `chat/`, `members/`, `friends/`, `dm/`
- Font loaded via Google Fonts (`Space Grotesk` as primary, system-ui fallback) with preconnect links in `+layout.svelte`
- Semantic HTML throughout for accessibility
- `prefers-color-scheme` media query ready for future light mode toggle
- `prefers-reduced-motion` media query disables transitions when user prefers reduced motion
