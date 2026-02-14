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
- Current user avatar (32px), **effective display name** (nickname if set, otherwise Google display name), role
- Subtle top border separator
- **Gear icon (⚙)**: 16px, `--text-muted` default, transitions to `--accent` on hover (150ms). Opens the User Settings modal.
- **Sign-out icon (⏻)**: 16px, `--text-muted` default, transitions to `--text-header` on hover (150ms). Signs the user out.
- Both icons are positioned to the right of the user info, displayed side by side
- **Avatar upload**: clicking the avatar opens a file picker; hover shows a semi-transparent overlay with a "+" icon

### Avatar Display

- **Messages**: 40px circular avatar to the left of the author name; falls back to a colored initial placeholder when no image is available
- **Member list**: 32px circular avatar; same fallback behavior
- **User panel**: 32px circular avatar with click-to-upload overlay
- **Fallback chain**: server avatar → custom global avatar → Google profile picture → initial letter on accent-colored circle

### Member List

- Grouped by role with section headers (OWNER, ADMIN, MEMBER)
- Member cards: avatar (32px), **effective display name** (nickname if set), role badge
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

## User Settings Modal

The User Settings screen is a full-screen modal overlay, accessed via the gear icon (⚙) in the User Panel. It uses the HTML `<dialog>` element for native focus trapping.

### Modal Overlay

- **Backdrop:** semi-transparent dark overlay (`rgba(0, 0, 0, 0.85)`)
- **Layout:** two-column panel centered within the viewport
  - Left column (~200px): category navigation sidebar (`--bg-tertiary`)
  - Right column (flexible, max-width 740px): settings content area (`--bg-primary`)
- **Close button:** "✕" icon at top-right of the content area, or press `Escape`, or click backdrop
- **Z-index:** modal layer (50), consistent with existing z-index strategy

### Category Navigation Sidebar

| Category | Icon | Content |
|----------|------|---------|
| **My Profile** | 👤 | Nickname editing, avatar management, profile preview |
| **My Account** | 🔒 | Read-only email/Google display name, sign-out |

- Background: `--bg-tertiary`
- Category items: `--text-muted` default, `--text-header` when active
- Active indicator: `--bg-message-hover` background with `--accent` left border (3px)
- Hover: `--bg-message-hover` background
- ARIA: `role="tablist"` with `role="tab"` items

### My Profile Section

- **Profile preview card**: 64px circular avatar with "Change" hover overlay, effective display name (updates live), email in `--text-muted`
- **Nickname input**: `--input-bg` background, `--accent` focus glow, 8px border-radius, character counter (`{count}/32`)
- **Save button**: `--accent` primary style, enabled only when value differs from current nickname
- **Reset link**: `--danger` text, removes nickname and reverts to Google display name
- **Avatar upload**: click avatar to open file picker; "Remove Avatar" button (`--danger`) shown when custom avatar is set

### My Account Section

- Read-only display of email and Google display name
- **Sign Out** button: `--danger` background, calls `closeSettings()` then `signOut()`

### Responsive Behavior

| Breakpoint | Layout |
|-----------|--------|
| ≥ 900px | Two-column: category sidebar (200px) + content area (flexible) |
| < 900px | Single-column: horizontal category tabs at top, full-width content below |

## Kick Error Banner (Overlay)

When a user is kicked from a server, a transient error banner appears as an overlay on top of the chat content. It does not affect the layout or push content down.

- **Positioning:** `position: absolute`, pinned to top of the chat body area (`top: 0; left: 0; right: 0`)
- **Z-index:** 10 (above chat content, below modals)
- **Styling:** `--danger` background, `--bg-tertiary` text, 14px font, 500 weight, centered text
- **Behavior:** `pointer-events: none` — does not block interaction with underlying chat content
- **Animation:** `banner-lifecycle` keyframe (5s total)
  - 0–75% (0–3.75s): fully visible (`opacity: 1`)
  - 75–100% (3.75–5s): fades out (`opacity: 0`) with slight upward slide (`translateY(-8px)`)
- **State management:** `AppState.setTransientError()` sets the error and schedules auto-clear after 5 seconds via `setTimeout`
- **Scope:** Applied in both `ChatArea.svelte` and `DmChatArea.svelte`

## Link Preview Card

Link preview cards render below the message body when a message contains URLs. Metadata (title, description, image, site name) is fetched asynchronously and delivered via SignalR.

### Layout

```
┌──────────────────────────────────────────────────────────────┐
│ ┌─accent-border (3px, --accent)                              │
│ │                                                            │
│ │  EXAMPLE.COM                          ┌──────────────┐     │
│ │  **Example Domain**                   │              │     │
│ │  This domain is for use in            │  thumbnail   │     │
│ │  illustrative examples in             │   (80×80)    │     │
│ │  documents.                           │              │     │
│ │                                       └──────────────┘     │
│ └                                                            │
└──────────────────────────────────────────────────────────────┘
```

### Styling

| Element | Style |
|---------|-------|
| Card container | `--bg-secondary` background, `--border` top/right/bottom border, `--accent` left border (3px), 8px border-radius, 12px padding, max-width 520px |
| Site name | 12px, `--text-muted`, uppercase, letter-spacing 0.02em |
| Title | 15px, 600 weight, `--accent` color, hover underline, clickable link (`target="_blank"`, `rel="noopener noreferrer"`) |
| Description | 13px, `--text-normal`, max 3 lines with `-webkit-line-clamp`, truncated at 300 characters |
| Thumbnail | 80×80px, 4px border-radius, `object-fit: cover`, right-aligned, wrapped in clickable link to same URL as title |
| Card spacing | 8px margin-top below message body, 4px gap between multiple preview cards |

### Rendering Rules

- If `title` is null/empty, the card is not rendered (graceful no-op)
- Title links to `canonicalUrl ?? url`
- Thumbnail image links to the same URL as the title
- Only `https://` image URLs from `og:image` are displayed
- Only previews with `Status = Success` are shown

### Responsive Behavior

| Breakpoint | Behavior |
|-----------|----------|
| ≥ 600px | Side-by-side layout: text left, thumbnail right |
| < 600px | Stacked layout: thumbnail above text (full width, max-height 160px) |

### Component Integration

- Server messages: `LinkPreviewCard` rendered inside `MessageItem.svelte`, below image attachments, above reactions
- DM messages: `LinkPreviewCard` rendered inline in `DmChatArea.svelte`, same positioning

## Message Replies ([detailed spec](REPLIES.md))

Inline replies allow users to reference a previous message. The reply system is available in both server channels and DMs.

### Reply Button

- Added to the floating action bar on message hover (positioned before the emoji react button)
- Icon: ↩ reply arrow (16×16 SVG), same styling as existing action buttons
- Clicking sets the reply target in `AppState` and focuses the composer

### Reply Reference (above message body)

Compact clickable bar displayed above the message body when `message.replyContext` is non-null.

```
┌─────────────────────────────────────────────────┐
│  ↩  [avatar 16px]  AuthorName  Body preview...  │
└─────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Container | Flex row, 4px 8px padding, 4px border-radius, semi-transparent `--bg-secondary`, cursor pointer |
| Reply icon (↩) | 14px, `--text-muted`, 0.6 opacity |
| Avatar | 16px circular, same fallback as message avatars |
| Author name | 13px, 600 weight, `--accent` color |
| Body preview | 13px, `--text-muted`, truncated with ellipsis, max-width 400px |
| Deleted state | Italic "Original message was deleted" in `--text-muted`, no avatar, no click action |
| Hover | Background shifts to darker `--bg-secondary` (0.8 opacity) |
| Focus | `--accent` outline, 1px offset |
| Click action | Scrolls the parent message into view with highlight animation |

### Reply Composer Bar

"Replying to {authorName}" banner shown above the composer input when a reply is active.

```
┌──────────────────────────────────────────────────────────────┐
│  Replying to **AuthorName**                                ✕ │
│  Body preview text...                                        │
└──────────────────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Container | `--bg-secondary` background, `--border` bottom border, 8px 12px padding, border-radius 8px 8px 0 0 |
| "Replying to" label | 13px, `--text-muted` |
| Author name | 13px, 600 weight, `--accent` color |
| Body preview | 13px, `--text-muted`, truncated with ellipsis, max-width 500px |
| Cancel button (✕) | 28×28px, transparent background, `--text-muted` color, hover: `--text-normal` + `--bg-message-hover` |
| Escape key | Cancels the reply (when mention picker is not open) |

### Scroll-to-Original Highlight

When clicking a `ReplyReference`, the original message scrolls into view and highlights briefly.

| Property | Value |
|----------|-------|
| Scroll behavior | `smooth`, `block: center` |
| Animation | `reply-highlight-fade` keyframe, 1.5s ease-out |
| Highlight color | `color-mix(in srgb, var(--accent) 15%, transparent)` → transparent |
| Reduced motion | No animation; static `color-mix(in srgb, var(--accent) 10%, transparent)` background |
| Element targeting | `data-message-id` attribute on each message wrapper |

### Image Preview Lightbox

Full-screen overlay for viewing images at full resolution. Triggered by clicking any inline image attachment in server channels or DMs.

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                    [↗]  [✕]         │
│                                                                      │
│                        ┌───────────────────┐                         │
│                        │                   │                         │
│                        │   Full-size image  │                         │
│                        │                   │                         │
│                        └───────────────────┘                         │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Overlay | `<div role="dialog">` at z-index 9999, rgba(0,0,0,0.85) backdrop background |
| Image | max 90vw × 90vh, `object-fit: contain`, 4px border-radius, drop shadow |
| Toolbar | flex row, aligned top-right, 8px gap |
| Open-original button (↗) | 36×36px, rgba(255,255,255,0.1) bg, 8px radius, white icon at 80% opacity, hover: 20% bg + full white |
| Close button (✕) | 40×40px, same style as open-original |
| Close triggers | Click backdrop, Escape key, close button |
| Thumbnail hover | `opacity: 0.85` transition (150ms ease) on inline images |

**Component:** `ImagePreview.svelte` — mounted once in `+page.svelte`, reads `lightboxImageUrl` from `AppState`.

## Animations & Micro-interactions

- **Transitions**: 150–200ms ease for background/color changes
- **Server icon hover**: scale(1.1) + border-radius morph
- **Active pill**: height animates from 8px (hover) → 36px (active)
- **Channel hover**: background-color transition
- **Message hover**: background highlight
- **Buttons**: color shift on hover/active
- **Kick error banner**: overlay banner with `banner-lifecycle` keyframe animation — fully visible for 75% of duration (3.75s), then fades out over 1.25s with slight upward slide (`translateY(-8px)`)
- **Reply highlight**: `reply-highlight-fade` keyframe — accent at 15% opacity fading to transparent over 1.5s; respects `prefers-reduced-motion` (no animation, static tinted background instead)

## Responsive Design

| Breakpoint   | Layout                                                              |
| ------------ | ------------------------------------------------------------------- |
| ≥ 1200px     | Full four-column: servers + channels + chat + members               |
| 900–1199px   | Three-column: servers + channels + chat (members hidden/collapsed)  |
| ≤ 899px      | Single-column: slide-out navigation, full-width chat                |

### Mobile Layout (≤ 899px)

On mobile viewports, the app uses a single-column layout with slide-out drawers for navigation and members, inspired by Discord's mobile UX patterns.

#### Navigation Drawer (Left)

- **Trigger**: Hamburger menu button (☰) in the chat/friends/DM header, visible only on mobile
- **Content**: Server icon rail (72px) + Channel sidebar or Home sidebar (flexible width)
- **Width**: 312px (capped at 85vw)
- **Behavior**: slides in from the left with a semi-transparent backdrop overlay (z-index 60–61)
- **Dismiss**: tap backdrop, or auto-closes on channel/server/DM selection and Home navigation
- **Animation**: `slide-in-left` (200ms ease), respects `prefers-reduced-motion`
- Provides full access to all servers, channels, DM conversations, friends navigation, user panel (sign-in/out, settings, avatar upload)

#### Members Drawer (Right)

- **Trigger**: Members icon button in the chat header (server mode only), visible only on mobile
- **Content**: `MembersSidebar` with role-grouped member list
- **Width**: 260px (capped at 80vw)
- **Behavior**: slides in from the right with backdrop overlay
- **Dismiss**: tap backdrop
- **Animation**: `slide-in-right` (200ms ease), respects `prefers-reduced-motion`

#### Mobile Header Buttons

| Button | Location | Action |
|--------|----------|--------|
| Hamburger (☰) | Left of header | Opens navigation drawer |
| Members (👥) | Right of header (server chat only) | Opens members drawer |

All existing features remain accessible on mobile: channel/server navigation, sign in/out, DMs, friends, user settings, profile customization, chat (including replies, reactions, image upload, mentions).

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
- Feature-grouped component directories: `server-sidebar/`, `channel-sidebar/`, `chat/`, `members/`, `friends/`, `dm/`, `settings/`
- Font loaded via Google Fonts (`Space Grotesk` as primary, system-ui fallback) with preconnect links in `+layout.svelte`
- Semantic HTML throughout for accessibility
- `prefers-color-scheme` media query ready for future light mode toggle
- `prefers-reduced-motion` media query disables transitions when user prefers reduced motion
