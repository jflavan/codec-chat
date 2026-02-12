# Nicknames Feature Specification

This document describes the **Nicknames** feature for Codec — a profile customization capability that allows users to choose a custom display name (nickname) that is shown across the application in place of their Google-provided display name.

## Overview

Nicknames give users control over how they are identified within Codec. By default, a user's display name is sourced from their Google account on each sign-in. The Nicknames feature allows a user to set an optional nickname that overrides the Google-provided name wherever the user's name is shown — in messages, member lists, friend lists, and the User Panel. Nicknames are managed through the [User Settings](USER_SETTINGS.md) screen.

## Goals

- Allow users to choose how they appear to others without changing their Google account
- Provide a simple, low-friction flow for setting, updating, and removing a nickname
- Maintain a clear fallback chain: nickname → Google display name, ensuring a name is always available
- Propagate the effective display name consistently across all surfaces (messages, member lists, friends, User Panel)
- Denormalize the effective display name into messages at send time (consistent with the existing `AuthorName` pattern)

## Terminology

| Term | Definition |
|------|-----------|
| **Nickname** | A user-chosen display name, stored on the User entity, that takes precedence over the Google-provided display name |
| **Google Display Name** | The `name` claim from the user's Google ID token, updated on each sign-in |
| **Effective Display Name** | The name shown to other users, resolved as: nickname (if set) → Google display name |
| **User Settings** | The modal overlay where users manage their profile, including nickname (see [USER_SETTINGS.md](USER_SETTINGS.md)) |

## User Stories

### Setting a Nickname
> As an authenticated user, I want to set a nickname so that I can choose how my name appears to other users across Codec.

### Updating a Nickname
> As an authenticated user, I want to change my existing nickname so that I can update how I appear to others.

### Removing a Nickname
> As an authenticated user, I want to remove my nickname so that my Google display name is used instead.

### Seeing My Nickname in Use
> As an authenticated user, I want to see my nickname displayed in the User Panel, my messages, member lists, and friend lists so that I know others see my chosen name.

### Seeing Another User's Nickname
> As an authenticated user, I want to see other users' nicknames in messages, member lists, and friend lists so that I can identify them by their chosen names.

## Data Model

### User Entity Changes

A single nullable column is added to the existing `User` table.

| Column | Type | Description |
|--------|------|-------------|
| `Nickname` | string? (max 32) | User-chosen display name. `null` means the Google display name is used. |

### Effective Display Name Resolution

The effective display name is resolved using a simple fallback:

```
Nickname (if not null and not empty)
  └─► Google DisplayName (always present)
```

This resolution is performed:
- **Server-side:** In `UserService` when building user profile responses and when snapshotting `AuthorName` on new messages
- **Client-side:** In `AppState` when displaying the current user's name in the User Panel

### Schema Change

```
┌─────────────────┐
│   User          │
│─────────────────│
│ Id (PK)         │
│ GoogleSubject   │
│ DisplayName     │  ← Google-provided (updated on each sign-in)
│ Nickname        │  ← NEW: user-chosen (nullable, max 32 chars)
│ Email           │
│ AvatarUrl       │
│ CustomAvatarPath│
│ CreatedAt       │
│ UpdatedAt       │
└─────────────────┘
```

### Constraints

- **Max length:** 32 characters (enforced at API and client level)
- **Min length:** 1 character (empty strings are treated as `null` / no nickname)
- **Allowed characters:** Any Unicode characters (including emoji), trimmed of leading/trailing whitespace
- **Uniqueness:** Nicknames are **not** unique — multiple users may have the same nickname (consistent with Discord's behavior)

## API Endpoints

All endpoints require authentication (`Authorization: Bearer <token>`).

### Set or Update Nickname

```
PUT /me/nickname
```

Sets the current user's nickname. If the user already has a nickname, it is replaced.

**Request Body:**
```json
{
  "nickname": "CoolUser42"
}
```

**Success Response:** `200 OK`
```json
{
  "nickname": "CoolUser42",
  "effectiveDisplayName": "CoolUser42"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Nickname is empty, whitespace-only, or exceeds 32 characters |
| `422 Unprocessable Entity` | Nickname contains disallowed content (reserved for future profanity filtering) |

**Side Effects:**
- Updates the `Nickname` column on the User record
- Updates the `UpdatedAt` timestamp
- Future messages will use the new nickname as `AuthorName`
- Existing messages retain their original `AuthorName` snapshot (no retroactive update)

### Remove Nickname

```
DELETE /me/nickname
```

Removes the current user's nickname, reverting to the Google-provided display name.

**Success Response:** `200 OK`
```json
{
  "nickname": null,
  "effectiveDisplayName": "John Doe"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `404 Not Found` | User does not have a nickname set |

**Side Effects:**
- Sets the `Nickname` column to `null`
- Updates the `UpdatedAt` timestamp
- Future messages will use the Google display name as `AuthorName`

### Get Current User Profile (Existing — Enhanced)

```
GET /me
```

The existing `/me` endpoint is enhanced to include the `nickname` and `effectiveDisplayName` fields in the response.

**Enhanced Response:** `200 OK`
```json
{
  "user": {
    "id": "guid",
    "displayName": "John Doe",
    "nickname": "CoolUser42",
    "effectiveDisplayName": "CoolUser42",
    "email": "john@example.com",
    "avatarUrl": "https://..."
  }
}
```

When no nickname is set:
```json
{
  "user": {
    "id": "guid",
    "displayName": "John Doe",
    "nickname": null,
    "effectiveDisplayName": "John Doe",
    "email": "john@example.com",
    "avatarUrl": "https://..."
  }
}
```

## Display Name Propagation

The nickname affects how the user's name appears across all surfaces:

| Surface | Current Behavior | With Nicknames |
|---------|-----------------|----------------|
| **User Panel** (channel sidebar bottom) | Shows `DisplayName` | Shows `effectiveDisplayName` (nickname if set) |
| **Message author name** | `AuthorName` snapshot at send time from `DisplayName` | `AuthorName` snapshot at send time from `effectiveDisplayName` |
| **Member list** | Shows `DisplayName` | Shows `effectiveDisplayName` |
| **Friends list** | Shows `DisplayName` | Shows `effectiveDisplayName` |
| **Friend requests** | Shows `DisplayName` | Shows `effectiveDisplayName` |
| **User search results** | Shows `DisplayName` | Shows `effectiveDisplayName` (original `DisplayName` visible as secondary text) |
| **Typing indicator** | Shows `DisplayName` | Shows `effectiveDisplayName` |

### Important: Message AuthorName Snapshotting

Consistent with the existing architecture, `AuthorName` on messages is a **denormalized snapshot** captured at send time. Changing a nickname does **not** retroactively update the author name on previously sent messages. This is by design — it matches the existing behavior where Google display name changes also do not update past messages.

## UI Integration

The nickname is managed through the [User Settings](USER_SETTINGS.md) screen's **My Profile** section. See that document for complete UI specifications.

### Key UI Elements

- **Nickname input field** in User Settings → My Profile section
- **Character counter** showing `{count}/32`
- **Save button** (enabled only when value has changed)
- **Reset link** to remove nickname and revert to Google display name
- **Profile preview card** showing the effective display name in real time as the user types

### User Panel Update

The User Panel at the bottom of the channel sidebar currently displays `DisplayName`. With the Nicknames feature, it displays the `effectiveDisplayName` instead. No other visual changes to the User Panel are needed (the gear icon for settings access is specified in [USER_SETTINGS.md](USER_SETTINGS.md)).

## Acceptance Criteria

### AC-1: Set a Nickname
- [ ] An authenticated user can set a nickname via `PUT /me/nickname`
- [ ] The API returns `200 OK` with the new nickname and effective display name
- [ ] The nickname is persisted on the User record
- [ ] The `UpdatedAt` timestamp is updated
- [ ] The API returns `400 Bad Request` for empty, whitespace-only, or over-length nicknames

### AC-2: Update a Nickname
- [ ] An authenticated user can update their existing nickname via `PUT /me/nickname`
- [ ] The new nickname replaces the previous one
- [ ] The response reflects the updated nickname and effective display name

### AC-3: Remove a Nickname
- [ ] An authenticated user can remove their nickname via `DELETE /me/nickname`
- [ ] The API returns `200 OK` with `nickname: null` and the Google display name as `effectiveDisplayName`
- [ ] The `Nickname` column is set to `null`
- [ ] The API returns `404 Not Found` if no nickname was set

### AC-4: Profile Endpoint Enhancement
- [ ] `GET /me` returns `nickname` (string or null) and `effectiveDisplayName` fields
- [ ] `effectiveDisplayName` resolves to nickname when set, Google display name otherwise

### AC-5: Display Name Propagation — User Panel
- [ ] The User Panel shows the effective display name (nickname if set, otherwise Google display name)
- [ ] Changing the nickname in User Settings immediately updates the User Panel

### AC-6: Display Name Propagation — Messages
- [ ] New messages use the effective display name as `AuthorName`
- [ ] Existing messages retain their original `AuthorName` (no retroactive update)

### AC-7: Display Name Propagation — Member List
- [ ] The member list shows each user's effective display name
- [ ] The member list updates when a user's nickname changes (on next data fetch)

### AC-8: Display Name Propagation — Friends
- [ ] The friends list shows each friend's effective display name
- [ ] Friend request cards show the effective display name

### AC-9: Display Name Propagation — User Search
- [ ] User search results show the effective display name as the primary name
- [ ] The Google display name is shown as secondary text when a nickname is set, to aid identification

### AC-10: Display Name Propagation — Typing Indicator
- [ ] Typing indicators show the effective display name ("{nickname} is typing…")

### AC-11: Nickname Validation
- [ ] Nicknames must be between 1 and 32 characters (after trimming whitespace)
- [ ] Leading and trailing whitespace is trimmed before saving
- [ ] Empty strings and whitespace-only strings are rejected with `400 Bad Request`
- [ ] Unicode characters (including emoji) are allowed
- [ ] The client-side character counter reflects the 32-character limit

### AC-12: Database Integrity
- [ ] The `Nickname` column is nullable on the User table
- [ ] The `Nickname` column has a max length of 32
- [ ] Existing User records have `Nickname = null` after migration
- [ ] The migration is reversible (down removes the column)

## Dependencies

- **Prerequisite:** None — the Nickname feature adds a column to the existing User table
- **UI Dependency:** [User Settings](USER_SETTINGS.md) — the primary interface for managing nicknames
- **Impacts:** All surfaces that display user names (messages, member list, friends, typing indicators, User Panel, user search)
- **Related:** Per-server nicknames (future) — server-specific name overrides via the `ServerMember` table

## Migration Plan

A single EF Core migration (`AddUserNickname`) will:
1. Add a nullable `Nickname` column (max length 32) to the `Users` table
2. All existing users will have `Nickname = null` (no data migration needed)

```sql
-- Up
ALTER TABLE Users ADD COLUMN Nickname TEXT NULL;

-- Down
-- SQLite does not support DROP COLUMN directly; EF Core handles table rebuild
```

## Task Breakdown

### API
- [ ] Add `Nickname` property (nullable string, max 32) to the `User` entity in `Models/User.cs`
- [ ] Create and apply EF Core migration (`AddUserNickname`)
- [ ] Add `GetEffectiveDisplayName()` helper to `UserService` (returns `Nickname ?? DisplayName`)
- [ ] Create `PUT /me/nickname` endpoint in `UsersController` with validation
- [ ] Create `DELETE /me/nickname` endpoint in `UsersController`
- [ ] Enhance `GET /me` response to include `nickname` and `effectiveDisplayName` fields
- [ ] Update message creation logic to use `effectiveDisplayName` for `AuthorName` snapshot
- [ ] Update user search response to include `effectiveDisplayName`
- [ ] Update friend-related responses to use `effectiveDisplayName`
- [ ] Update member list responses to use `effectiveDisplayName`

### Web
- [ ] Add `nickname` and `effectiveDisplayName` fields to the `UserProfile` type in `models.ts`
- [ ] Add `setNickname(nickname: string)` and `removeNickname()` methods to `ApiClient`
- [ ] Update `AppState` to expose `effectiveDisplayName` derived from the `me` profile
- [ ] Update `UserPanel.svelte` to display `effectiveDisplayName`
- [ ] Wire nickname input in `ProfileSettings.svelte` to the nickname API endpoints (see [USER_SETTINGS.md](USER_SETTINGS.md))
- [ ] Update typing indicator to use `effectiveDisplayName` when broadcasting
- [ ] Update any components that display the current user's name to use `effectiveDisplayName`

### Documentation
- [ ] Update `ARCHITECTURE.md` with new nickname endpoints
- [ ] Update `DATA.md` with the `Nickname` column on the User entity
- [ ] Update `FEATURES.md` to track Nicknames feature progress
- [ ] Update `DESIGN.md` with nickname display rules

## Open Questions

1. **Per-server nicknames:** Should users be able to set different nicknames per server (like Discord)? (Recommendation: defer to a future iteration. The current design is global-only. Per-server nicknames would add a `Nickname` column to `ServerMember` and extend the fallback chain to: server nickname → global nickname → Google display name.)
2. **Nickname history:** Should the system track previous nicknames for moderation purposes? (Recommendation: defer — not needed for MVP. Can be added via an audit log feature later.)
3. **Profanity filtering:** Should nicknames be filtered for inappropriate content? (Recommendation: defer — the `422 Unprocessable Entity` response is reserved for this. Implement when a moderation system is built.)
4. **Nickname visibility:** Should users be able to see another user's Google display name alongside their nickname (e.g., in a profile popover)? (Recommendation: yes for user search results in the initial release; defer profile popovers to a future feature.)
5. **Real-time nickname updates:** Should other users see nickname changes in real time (e.g., member list updates immediately)? (Recommendation: defer real-time propagation — the member list and friends list will reflect nickname changes on the next data fetch. Real-time propagation can be added with a `UserProfileUpdated` SignalR event in a future iteration.)
