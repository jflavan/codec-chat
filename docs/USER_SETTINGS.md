# User Settings Feature Specification

This document describes the **User Settings** feature for Codec — a centralized settings screen that allows users to view and manage their profile, preferences, and account information.

## Overview

The User Settings screen provides a single, dedicated location for users to manage their personal profile and account. It is accessed from the User Panel at the bottom of the channel sidebar and opens as a full-screen modal overlay. The initial release focuses on profile management (nickname, avatar), with the architecture designed to support additional settings categories in future iterations.

## Goals

- Provide a discoverable, centralized location for all user-facing settings
- Support profile customization — nickname and avatar management — from a single screen
- Follow existing UI patterns (modal overlay, CODEC CRT theme) for visual consistency
- Design the settings layout to be extensible for future categories (notifications, privacy, appearance, account)
- Keep the settings screen lightweight — changes are saved individually, not as a batch form submission

## Terminology

| Term | Definition |
|------|-----------|
| **User Settings** | The full-screen modal overlay containing all user-configurable options |
| **Settings Category** | A top-level grouping of related settings (e.g., Profile, Account) |
| **User Panel** | The existing component at the bottom of the channel sidebar showing the current user's avatar, name, and role |
| **Nickname** | A user-chosen display name that overrides the Google-provided display name (see [NICKNAMES.md](NICKNAMES.md)) |
| **Effective Display Name** | The name shown to other users, resolved via the fallback chain: nickname → Google display name |

## User Stories

### Opening User Settings
> As an authenticated user, I want to click a settings icon in the User Panel so that I can access my profile and account settings.

### Viewing My Profile
> As an authenticated user, I want to see my current profile information (avatar, nickname, display name, email) in the settings screen so that I know what other users see.

### Editing My Nickname
> As an authenticated user, I want to set or change my nickname from the settings screen so that I can choose how I appear to other users.

### Managing My Avatar
> As an authenticated user, I want to upload, change, or remove my avatar from the settings screen so that I can manage my profile picture in one place.

### Closing User Settings
> As an authenticated user, I want to close the settings screen and return to my previous view so that I can resume chatting.

## UI Design

### Access Point

The User Settings screen is accessed via a **gear icon (⚙)** button in the User Panel at the bottom of the channel sidebar. The gear icon is positioned to the right of the user's display name and role badge.

- Gear icon: 16px, `currentColor` fill, `--text-muted` default color
- Hover: color transitions to `--accent` (150ms ease)
- Click: opens the User Settings modal overlay

### Settings Modal Overlay

The User Settings screen is rendered as a **full-screen modal overlay** on top of the existing application layout.

- **Backdrop:** semi-transparent dark overlay (`rgba(0, 0, 0, 0.85)`)
- **Layout:** two-column panel centered within the viewport
  - Left column (~200px): category navigation sidebar
  - Right column (flexible, max-width 740px): settings content area
- **Close button:** "✕" icon at top-right of the overlay, or press `Escape`
- **Z-index:** modal layer (50), consistent with existing z-index strategy

### Category Navigation Sidebar

The left column displays a vertical list of settings categories. The initial release includes:

| Category | Icon | Content |
|----------|------|---------|
| **My Profile** | 👤 | Nickname, avatar, display name preview |
| **My Account** | 🔒 | Email, Google account info (read-only) |

Future categories (not in initial scope):
- Appearance (theme, font size)
- Notifications (push, sounds, DM alerts)
- Privacy (who can send friend requests, DM permissions)

**Styling:**
- Background: `--bg-tertiary`
- Category items: `--text-muted` default, `--text-header` when active
- Active indicator: `--bg-message-hover` background with `--accent` left border (3px)
- Hover: `--bg-message-hover` background
- 8px vertical padding per item, 16px horizontal padding

### My Profile Section

The primary settings section for the initial release.

#### Profile Preview Card

At the top of the My Profile section, a preview card shows how the user appears to others:

- Avatar (64px, circular) with hover overlay for upload (reuses existing avatar upload pattern)
- Effective display name (nickname if set, otherwise Google display name)
- Email address in `--text-muted`
- "This is how others see you" helper text in `--text-dim`

#### Nickname Field

- Label: "Nickname" with helper text "This is how you'll appear across Codec"
- Text input field with the current nickname value (or empty placeholder)
- Placeholder: "Enter a nickname..."
- Max length: 32 characters, with character counter shown as `{count}/32`
- **Save** button (`--accent`, primary style) to the right of the input, enabled only when the value has changed
- **Reset** link (`--danger` text) below the input to remove the nickname and revert to Google display name
- Input follows existing styling: `--input-bg` background, `--accent` focus glow, 8px border-radius
- See [NICKNAMES.md](NICKNAMES.md) for full nickname feature specification

#### Avatar Management

- Current avatar displayed at 64px (circular) with hover overlay showing "Change" text
- Click to open file picker (reuses existing `POST /me/avatar` upload flow)
- "Remove Avatar" button (`--danger` style) visible only when a custom avatar is set
- Removing reverts to Google profile picture (reuses existing `DELETE /me/avatar` endpoint)
- Accepted formats: JPG, JPEG, PNG, WebP, GIF (consistent with existing validation)
- Max file size: 10 MB (consistent with existing validation)

### My Account Section

A read-only section displaying account information.

- **Email:** displays the user's email address (from Google, not editable)
- **Display Name:** displays the Google-provided display name (not editable — this comes from Google)
- **Account Created:** displays the user's account creation date
- **Sign Out** button (`--danger` style) at the bottom of the section (reuses existing sign-out logic)

### Responsive Behavior

| Breakpoint | Layout |
|-----------|--------|
| ≥ 900px | Two-column: category sidebar (200px) + content area (flexible) |
| < 900px | Single-column: category tabs at the top, full-width content below |

### Accessibility

- Modal traps focus within the overlay when open
- `Escape` key closes the modal
- Category navigation uses `role="tablist"` with `role="tab"` items
- Content panels use `role="tabpanel"` with `aria-labelledby` referencing the active tab
- All form fields have associated `<label>` elements
- Focus is moved to the first interactive element when the modal opens
- Focus is restored to the gear icon when the modal closes

## Component Architecture

The User Settings feature introduces the following new Svelte components, organized under a new `settings/` feature directory:

```
src/lib/components/settings/
├── UserSettingsModal.svelte      # Full-screen modal overlay shell
├── SettingsSidebar.svelte        # Category navigation sidebar
├── ProfileSettings.svelte        # My Profile section (nickname + avatar)
└── AccountSettings.svelte        # My Account section (read-only info + sign out)
```

### Component Responsibilities

| Component | Responsibility |
|-----------|---------------|
| `UserSettingsModal.svelte` | Modal overlay, backdrop, close behavior, layout shell, renders sidebar + active section |
| `SettingsSidebar.svelte` | Category list, active category tracking, keyboard navigation |
| `ProfileSettings.svelte` | Profile preview card, nickname input + save/reset, avatar upload + remove |
| `AccountSettings.svelte` | Read-only account info display, sign-out button |

### State Management

The User Settings modal is controlled via the existing `AppState` class:

- `settingsOpen: boolean` — tracks whether the settings modal is visible (new `$state` field)
- `openSettings()` / `closeSettings()` — methods to toggle the modal
- Nickname and avatar state are managed through the existing `me` profile object in `AppState`
- The gear icon in `UserPanel.svelte` calls `appState.openSettings()`

## Acceptance Criteria

### AC-1: Open User Settings
- [ ] A gear icon (⚙) is visible in the User Panel at the bottom of the channel sidebar
- [ ] Clicking the gear icon opens the User Settings modal overlay
- [ ] The modal has a semi-transparent dark backdrop
- [ ] The modal displays a two-column layout with category sidebar and content area

### AC-2: Close User Settings
- [ ] Clicking the "✕" close button closes the modal
- [ ] Pressing `Escape` closes the modal
- [ ] Clicking the backdrop outside the modal content closes the modal
- [ ] Focus is restored to the gear icon after closing

### AC-3: Category Navigation
- [ ] The sidebar displays "My Profile" and "My Account" categories
- [ ] Clicking a category switches the content panel to that section
- [ ] The active category is visually highlighted with an accent left border
- [ ] "My Profile" is selected by default when the modal opens

### AC-4: Profile Preview
- [ ] The My Profile section shows a preview card with the user's avatar, effective display name, and email
- [ ] The preview updates in real time when the nickname is changed
- [ ] The preview updates when the avatar is changed

### AC-5: Nickname Editing
- [ ] A text input allows the user to set or edit their nickname
- [ ] A character counter shows the current length against the 32-character maximum
- [ ] The Save button is enabled only when the input value differs from the current nickname
- [ ] Saving a nickname updates the profile preview and the User Panel display name
- [ ] A Reset link removes the nickname and reverts to the Google display name
- [ ] See [NICKNAMES.md](NICKNAMES.md) for complete nickname acceptance criteria

### AC-6: Avatar Management
- [ ] Clicking the avatar in the profile preview opens a file picker for upload
- [ ] A "Remove Avatar" button is visible only when a custom avatar is set
- [ ] Uploading a new avatar updates the profile preview immediately
- [ ] Removing the avatar reverts to the Google profile picture

### AC-7: Account Information
- [ ] The My Account section displays the user's email, Google display name, and account creation date
- [ ] All fields in the My Account section are read-only
- [ ] A "Sign Out" button is present and triggers the existing sign-out flow

### AC-8: Responsive Layout
- [ ] On screens ≥ 900px, the modal shows a two-column layout (sidebar + content)
- [ ] On screens < 900px, categories render as horizontal tabs above the content
- [ ] The modal content is scrollable if it exceeds viewport height

### AC-9: Accessibility
- [ ] Focus is trapped within the modal while open
- [ ] `Escape` key closes the modal
- [ ] Category navigation is keyboard-accessible (arrow keys + Enter)
- [ ] All form inputs have associated labels
- [ ] The modal uses appropriate ARIA roles (`dialog`, `tablist`, `tab`, `tabpanel`)

## Dependencies

- **Prerequisite:** None — the User Settings screen builds on the existing User Panel and profile endpoints (`GET /me`, `POST /me/avatar`, `DELETE /me/avatar`)
- **Enables:** [Nicknames](NICKNAMES.md) — the User Settings screen provides the primary UI for setting and managing nicknames
- **Related:** Future settings categories (notifications, privacy, appearance) will be added as tabs in this same modal

## Task Breakdown

### Web
- [x] Add `settingsOpen` state and `openSettings()` / `closeSettings()` methods to `AppState`
- [x] Create `UserSettingsModal.svelte` with modal overlay, backdrop, close behavior, and two-column layout
- [x] Create `SettingsSidebar.svelte` with category navigation and active state
- [x] Create `ProfileSettings.svelte` with profile preview, nickname input, and avatar management
- [x] Create `AccountSettings.svelte` with read-only account info and sign-out button
- [x] Add gear icon button to `UserPanel.svelte` that opens the settings modal
- [x] Wire nickname editing to the API endpoints defined in [NICKNAMES.md](NICKNAMES.md)
- [x] Wire avatar upload/remove to existing `POST /me/avatar` and `DELETE /me/avatar` endpoints
- [x] Implement responsive layout (two-column ≥ 900px, tabbed < 900px)
- [x] Implement focus trapping and keyboard navigation for accessibility

### Documentation
- [x] Update `ARCHITECTURE.md` with the settings component tree
- [x] Update `DESIGN.md` with User Settings modal UI specification
- [x] Update `FEATURES.md` to track User Settings feature progress

## Open Questions

1. **Settings persistence:** Should user preferences (future: theme, notification settings) be stored server-side or client-side (`localStorage`)? (Recommendation: server-side for cross-device consistency, but defer until the first non-profile setting is added.)
2. **Unsaved changes warning:** Should the modal warn users if they navigate away from a section with unsaved changes? (Recommendation: not for MVP — the initial settings are saved individually, not as a form.)
3. **Avatar crop/resize:** Should the settings screen include an avatar crop tool? (Recommendation: defer — the existing avatar upload flow accepts any supported image and serves it as-is.)
4. **Keyboard shortcut:** Should there be a keyboard shortcut to open settings (e.g., `Ctrl+,`)? (Recommendation: nice-to-have, defer to a future iteration.)
