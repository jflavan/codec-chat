# Message Replies Feature Specification

This document describes the **Message Replies** feature for Codec — an inline reply system that allows users to reply to a specific previous message, visually linking the reply to the original and enabling conversational context in busy channels and DM conversations.

## Overview

Message Replies let a user compose a new message that explicitly references an earlier message in the same channel or DM conversation. The reply is displayed as a normal message with a compact preview of the original message rendered above it (author name, avatar, and truncated body). Clicking the preview scrolls the message feed to the original message and briefly highlights it. This mirrors the inline reply pattern used by Discord, Telegram, and similar chat applications — it is intentionally _not_ a threaded conversation model (like Slack threads).

## Goals

- Allow users to reply to any message in a server channel or DM conversation, providing conversational context in busy feeds
- Display a compact, clickable reference to the original message above the reply body
- Enable quick navigation to the original message by clicking the reply reference
- Support replies across both server channel messages and direct messages using consistent UX
- Keep the data model simple — a single nullable foreign key on each message entity, no separate threading table

## Non-Goals (Explicitly Deferred)

- **Threaded conversations / thread panels:** Replies are inline only; there is no side panel or nested thread view
- **Reply chains / tree navigation:** The UI shows only the immediate parent, not a chain of replies
- **Cross-channel or cross-DM replies:** A reply must reference a message in the same channel or DM conversation
- **Forwarding messages:** Sharing a message from one channel/DM to another is a separate feature

## Terminology

| Term | Definition |
|------|-----------|
| **Reply** | A new message that references a specific earlier message in the same channel or DM conversation |
| **Original Message** | The message being replied to (also called the "parent message") |
| **Reply Reference** | The compact preview bar rendered above the reply's body showing the original message's author, avatar, and truncated text |
| **Reply Context** | The lightweight DTO included in API and SignalR payloads that describes the original message (id, author, body preview) |

## Prior Art & Research

The design is informed by how established chat applications handle replies:

### Discord
- **Trigger:** Right-click a message → "Reply", or click the reply icon in the floating action bar on message hover
- **Composer state:** A "Replying to @Author" bar appears above the message input with a cancel (✕) button; optionally toggles whether to @-mention the original author (default: on)
- **Display:** A thin connecting line extends from the reply reference bar down to the message body; the reference shows the original author's avatar (16px), display name, and a single-line truncated preview of the original body
- **Click behavior:** Clicking the reply reference scrolls the feed to the original message and briefly highlights it with a background flash
- **Deleted originals:** If the original message was deleted, the reference reads _"Original message was deleted"_ in muted text and is not clickable
- **Scope:** Replies are scoped to the same channel; cross-channel replies are not supported

### Telegram
- Similar inline reply model; shows author name and truncated text in a card above the reply
- Clicking the card scrolls to the original message
- No threading

### Slack (contrast)
- Uses a dedicated "Threads" model: replies open a side panel and create a persistent thread
- More complex data model and UX; not the target pattern for Codec

### Design Decision
Codec follows Discord's inline reply approach: simple, lightweight, and familiar to the target audience. No threading, no side panels, no nested views.

## User Stories

### Replying to a Message
> As a user in a channel or DM, I want to reply to a specific message so that my response is visually linked to the message I'm responding to.

### Viewing a Reply
> As a user reading a channel or DM, I want to see a preview of the original message above a reply so that I understand what is being responded to without scrolling.

### Navigating to the Original Message
> As a user reading a reply, I want to click the reply reference to scroll to and highlight the original message so that I can read it in full context.

### Cancelling a Reply
> As a user composing a reply, I want to cancel the reply and return to composing a normal message so that I am not locked into replying.

### Seeing a Reply to a Deleted Message
> As a user reading a reply whose original message was deleted, I want to see a clear indication that the original is no longer available so that I am not confused by a broken reference.

### Replying with a Mention
> As a user replying to someone's message, I want the option to @-mention the original author so that they receive a notification about my reply.

## Data Model

### Message Entity Changes

A single nullable self-referencing foreign key is added to the existing `Message` table.

| Column | Type | Description |
|--------|------|-------------|
| `ReplyToMessageId` | Guid? (FK → Message) | Reference to the original message being replied to. `null` for non-reply messages. |

### DirectMessage Entity Changes

A single nullable self-referencing foreign key is added to the existing `DirectMessage` table.

| Column | Type | Description |
|--------|------|-------------|
| `ReplyToDirectMessageId` | Guid? (FK → DirectMessage) | Reference to the original DM being replied to. `null` for non-reply messages. |

### Schema Changes

```
┌─────────────────────┐
│   Message           │
│─────────────────────│
│ Id (PK)             │◄──┐
│ ChannelId (FK)      │   │
│ AuthorUserId (FK)   │   │
│ AuthorName          │   │
│ Body                │   │
│ ImageUrl            │   │
│ ReplyToMessageId(FK)│───┘  ← NEW: nullable self-reference
│ CreatedAt           │
│ Reactions[]         │
│ LinkPreviews[]      │
└─────────────────────┘

┌───────────────────────────┐
│   DirectMessage           │
│───────────────────────────│
│ Id (PK)                   │◄──┐
│ DmChannelId (FK)          │   │
│ AuthorUserId (FK)         │   │
│ AuthorName                │   │
│ Body                      │   │
│ ImageUrl                  │   │
│ ReplyToDirectMessageId(FK)│───┘  ← NEW: nullable self-reference
│ CreatedAt                 │
│ LinkPreviews[]            │
└───────────────────────────┘
```

### Constraints

- **Same-channel scope:** `ReplyToMessageId` must reference a `Message` in the same `ChannelId`. Enforced at the API level (not a database constraint, which would require a check constraint with a subquery).
- **Same-DM scope:** `ReplyToDirectMessageId` must reference a `DirectMessage` in the same `DmChannelId`. Enforced at the API level.
- **No cascading delete of replies:** If the original message is deleted (future feature), replies remain in the feed with a "deleted original" indicator — `ReplyToMessageId` is set to `null` via `OnDelete(SetNull)`.
- **No recursive validation:** A reply may itself be replied to (creating a chain), but the API and UI only display the immediate parent.

### Indexes

```
IX_Message_ReplyToMessageId               — fast lookup of replies to a given message
IX_DirectMessage_ReplyToDirectMessageId   — fast lookup of replies to a given DM
```

### Relationships

```csharp
// In CodecDbContext.OnModelCreating

// Message self-reference for replies
modelBuilder.Entity<Message>()
    .HasOne<Message>()
    .WithMany()
    .HasForeignKey(m => m.ReplyToMessageId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<Message>()
    .HasIndex(m => m.ReplyToMessageId);

// DirectMessage self-reference for replies
modelBuilder.Entity<DirectMessage>()
    .HasOne<DirectMessage>()
    .WithMany()
    .HasForeignKey(dm => dm.ReplyToDirectMessageId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<DirectMessage>()
    .HasIndex(dm => dm.ReplyToDirectMessageId);
```

## Reply Context DTO

When returning messages via the API or SignalR, a reply includes a lightweight `replyContext` object describing the original message. This avoids the client needing a separate round-trip to fetch the original.

```csharp
public sealed record ReplyContextDto(
    Guid MessageId,
    string AuthorName,
    string? AuthorAvatarUrl,
    Guid? AuthorUserId,
    string BodyPreview,       // Truncated to 100 characters
    bool IsDeleted            // True if the original message no longer exists
);
```

**Resolution rules:**
- If `ReplyToMessageId` is non-null and the referenced message **exists**, populate all fields from the referenced message and set `IsDeleted = false`
- If `ReplyToMessageId` is non-null but the referenced message **does not exist** (deleted or orphaned `SetNull`), return `IsDeleted = true` with empty/placeholder fields
- If `ReplyToMessageId` is null, `replyContext` is `null` in the payload

## API Changes

No new endpoints are required. The existing message posting endpoints are extended with an optional `replyToMessageId` field, and message retrieval endpoints include reply context in the response.

### Updated: Post a Channel Message

```
POST /channels/{channelId}/messages
```

**Extended Request Body:**
```json
{
  "body": "I agree with this!",
  "imageUrl": null,
  "replyToMessageId": "guid-of-original-message"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `body` | Conditional | Message text (required if no `imageUrl`) |
| `imageUrl` | No | Uploaded image URL |
| `replyToMessageId` | No | ID of the message being replied to (`null` for non-reply) |

**Additional Validation:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | `replyToMessageId` is provided but the referenced message does not exist |
| `400 Bad Request` | `replyToMessageId` references a message in a different channel |

**Extended Response:**
```json
{
  "id": "guid",
  "authorName": "Alice",
  "authorUserId": "guid",
  "body": "I agree with this!",
  "imageUrl": null,
  "createdAt": "2026-02-12T10:05:00Z",
  "channelId": "guid",
  "authorAvatarUrl": "...",
  "reactions": [],
  "linkPreviews": [],
  "mentions": [],
  "replyContext": {
    "messageId": "guid-of-original-message",
    "authorName": "Bob",
    "authorAvatarUrl": "...",
    "authorUserId": "guid",
    "bodyPreview": "Has anyone tried the new feature? It looks really...",
    "isDeleted": false
  }
}
```

### Updated: Post a Direct Message

```
POST /dm/channels/{channelId}/messages
```

**Extended Request Body:**
```json
{
  "body": "Totally!",
  "replyToDirectMessageId": "guid-of-original-dm"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `body` | Conditional | Message text (required if no `imageUrl`) |
| `imageUrl` | No | Uploaded image URL |
| `replyToDirectMessageId` | No | ID of the DM being replied to (`null` for non-reply) |

**Additional Validation:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | `replyToDirectMessageId` is provided but the referenced message does not exist |
| `400 Bad Request` | `replyToDirectMessageId` references a DM in a different DM channel |

### Updated: Get Channel Messages

```
GET /channels/{channelId}/messages
```

Each message in the response now includes a `replyContext` field (null for non-replies).

### Updated: Get DM Messages

```
GET /dm/channels/{channelId}/messages
```

Each DM in the response now includes a `replyContext` field (null for non-replies).

## Real-time Events (SignalR)

No new SignalR events are introduced. The existing `ReceiveMessage` and `ReceiveDm` payloads are extended with the `replyContext` field.

### Updated: `ReceiveMessage` Payload

```json
{
  "id": "guid",
  "authorName": "Alice",
  "authorUserId": "guid",
  "body": "I agree with this!",
  "imageUrl": null,
  "createdAt": "2026-02-12T10:05:00Z",
  "channelId": "guid",
  "authorAvatarUrl": "...",
  "reactions": [],
  "linkPreviews": [],
  "mentions": [],
  "replyContext": {
    "messageId": "guid-of-original-message",
    "authorName": "Bob",
    "authorAvatarUrl": "...",
    "authorUserId": "guid",
    "bodyPreview": "Has anyone tried the new feature?...",
    "isDeleted": false
  }
}
```

### Updated: `ReceiveDm` Payload

```json
{
  "id": "guid",
  "dmChannelId": "guid",
  "authorUserId": "guid",
  "authorName": "Alice",
  "body": "Totally!",
  "imageUrl": null,
  "createdAt": "2026-02-12T10:05:00Z",
  "authorAvatarUrl": "...",
  "linkPreviews": [],
  "replyContext": {
    "messageId": "guid-of-original-dm",
    "authorName": "Bob",
    "authorAvatarUrl": "...",
    "authorUserId": "guid",
    "bodyPreview": "Want to grab coffee later?",
    "isDeleted": false
  }
}
```

## Frontend Changes

### Updated Types

```typescript
/** Compact preview of the message being replied to. */
export type ReplyContext = {
    messageId: string;
    authorName: string;
    authorAvatarUrl: string | null;
    authorUserId: string | null;
    bodyPreview: string;
    isDeleted: boolean;
};

/** Chat message in a channel (updated). */
export type Message = {
    id: string;
    authorName: string;
    body: string;
    imageUrl?: string | null;
    createdAt: string;
    channelId: string;
    authorUserId?: string | null;
    authorAvatarUrl?: string | null;
    reactions: Reaction[];
    linkPreviews: LinkPreview[];
    mentions: Mention[];
    replyContext: ReplyContext | null;       // ← NEW
};

/** Direct message in a DM conversation (updated). */
export type DirectMessage = {
    id: string;
    dmChannelId: string;
    authorUserId: string;
    authorName: string;
    body: string;
    imageUrl?: string | null;
    createdAt: string;
    authorAvatarUrl?: string | null;
    linkPreviews: LinkPreview[];
    replyContext: ReplyContext | null;       // ← NEW
};
```

### State Management

In `AppState`:

```typescript
// New reactive state for the reply-in-progress
replyingTo: { messageId: string; authorName: string; bodyPreview: string } | null = $state(null);

// Methods
startReply(messageId: string, authorName: string, bodyPreview: string): void;
cancelReply(): void;
```

- `startReply()` sets `replyingTo` with the target message info; the composer reads this to show the "Replying to" bar and include the ID in the POST request
- `cancelReply()` clears `replyingTo` back to `null`
- After sending a message with a reply, `replyingTo` is automatically cleared
- `replyingTo` is scoped per context (channel or DM) — switching channels or DMs clears the reply-in-progress

### API Client Changes

```typescript
// Updated sendMessage signature
sendMessage(channelId: string, body: string, imageUrl?: string | null, replyToMessageId?: string | null): Promise<Message>;

// Updated sendDm signature
sendDm(dmChannelId: string, body: string, imageUrl?: string | null, replyToDirectMessageId?: string | null): Promise<DirectMessage>;
```

### New Component: `ReplyReference.svelte`

A compact bar rendered above the message body inside `MessageItem.svelte` for messages that have a `replyContext`.

**Props:**
```typescript
type ReplyReferenceProps = {
    replyContext: ReplyContext;
    onClickGoToOriginal?: (messageId: string) => void;
};
```

**Rendering:**
- **Normal reply:** Thin vertical accent line (`--accent`, 2px) on the left, original author's avatar (16px circle), author name in `--accent` color, body preview truncated to one line with ellipsis
- **Deleted original:** Muted italic text: _"Original message was deleted"_ — not clickable
- **Entire bar is clickable** (when not deleted), triggering `onClickGoToOriginal`

### New Component: `ReplyComposerBar.svelte`

A bar rendered above the `Composer` input when a reply is in progress.

**Props:**
```typescript
type ReplyComposerBarProps = {
    authorName: string;
    bodyPreview: string;
    onCancel: () => void;
};
```

**Rendering:**
- Background: `--bg-secondary`
- Text: "Replying to **{authorName}**" with a truncated preview of the original body
- Cancel button (✕) on the right side to clear the reply

### Updated Component: `MessageItem.svelte`

- If `message.replyContext` is non-null, render `<ReplyReference>` above the message body
- Add a **Reply** button to the floating action bar on message hover (alongside existing reaction button)
  - Icon: ↩ reply arrow
  - Clicking it calls `appState.startReply(message.id, message.authorName, bodyPreview)`
- A thin connecting line (2px, `--text-muted` at 30% opacity) extends from the `ReplyReference` down to the message body, similar to Discord's visual treatment

### Updated Component: `Composer.svelte`

- When `appState.replyingTo` is non-null, render `<ReplyComposerBar>` above the input field
- Pass `appState.replyingTo.messageId` to the API call when sending the message
- Clear `appState.replyingTo` after successful send

### Updated Component: `DmChatArea.svelte`

- Same reply reference and composer bar integration as `MessageItem.svelte` / `Composer.svelte`, using `replyToDirectMessageId` for the DM context

### Scroll-to-Original Behavior

When the user clicks a `ReplyReference`:
1. Find the original message in the loaded message list by ID
2. If found: scroll the message feed to that message and apply a brief highlight animation (`--accent` background at 15% opacity, fading out over 1.5s)
3. If not found (message is outside the loaded window): optionally fetch more history to find it, or show a tooltip "Message not in view" (defer deep-scroll to a follow-up)

## UI Design

### Reply Reference Bar (inside MessageItem)

```
┌──────────────────────────────────────────────────────────────┐
│ ╭──╮                                                         │
│ │⤷ │  🟢 Bob  Has anyone tried the new feature? It looks... │
│ ╰──╯                                                         │
│ ┃                                                            │
│ ┃  Alice                                  Today at 10:05 AM  │
│ ┃  I agree with this!                                        │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

- The `⤷` / connecting line is `--text-muted` at 30% opacity
- Bob's name is rendered in `--accent` color
- The body preview is truncated to a single line with `text-overflow: ellipsis`
- The entire reference bar is clickable (cursor: pointer, hover: brighten background)

### Reply Composer Bar (above Composer input)

```
┌──────────────────────────────────────────────────────────────┐
│  Replying to Bob                                         ✕   │
│  Has anyone tried the new feature? It looks really inte...   │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│  Message #general                                      ➤    │
└──────────────────────────────────────────────────────────────┘
```

- Background: `--bg-secondary`
- Author name: `--accent` bold
- Body preview: `--text-muted`, single line, truncated
- Cancel (✕): `--text-muted`, hover `--text-normal`

### Reply Action Button (in floating action bar)

The existing floating action bar on message hover currently shows an emoji reaction button. A reply button is added alongside it:

```
┌──────────────────┐
│  😀  ↩           │   ← Reply icon added
└──────────────────┘
```

### Styling

| Element | Style |
|---------|-------|
| Reply reference container | `--bg-secondary` background at 50% opacity, 4px border-radius, 6px padding, `cursor: pointer` |
| Connecting line | 2px solid `--text-muted` at 30% opacity, extends from reference to message body |
| Original author avatar | 16px circle, `margin-right: 6px` |
| Original author name | 13px, `--accent` color, 600 weight |
| Body preview text | 13px, `--text-muted`, `white-space: nowrap`, `overflow: hidden`, `text-overflow: ellipsis`, max-width: 400px |
| Deleted message text | 13px italic, `--text-muted` |
| Highlight animation | `--accent` background at 15% opacity, fades out over 1.5s (`@keyframes reply-highlight`) |
| Reply composer bar | `--bg-secondary` background, 8px padding, `border-bottom: 1px solid --border` |
| Reply action button | Same style as existing emoji button in floating action bar |

### Keyboard Support

| Key | Action |
|-----|--------|
| `Escape` | Cancel the current reply (clear `replyingTo`) when the composer is focused |

### Accessibility

- Reply reference bar has `role="button"` and `tabindex="0"` for keyboard navigation
- `aria-label="Reply to message from {authorName}: {bodyPreview}"`
- Deleted references have `aria-label="Original message was deleted"`
- Reply composer bar has `aria-label="Replying to {authorName}"`
- Cancel button has `aria-label="Cancel reply"`

## Acceptance Criteria

### AC-1: Send a Reply (Channel)
- [ ] A user can reply to an existing message in a server channel
- [ ] The POST request includes `replyToMessageId` and the API persists it on the `Message` entity
- [ ] The API returns `400 Bad Request` if the referenced message does not exist
- [ ] The API returns `400 Bad Request` if the referenced message is in a different channel
- [ ] The reply is broadcast via `ReceiveMessage` with a populated `replyContext`

### AC-2: Send a Reply (DM)
- [ ] A user can reply to an existing message in a DM conversation
- [ ] The POST request includes `replyToDirectMessageId` and the API persists it
- [ ] The API returns `400 Bad Request` if the referenced DM does not exist
- [ ] The API returns `400 Bad Request` if the referenced DM is in a different DM channel
- [ ] The reply is broadcast via `ReceiveDm` with a populated `replyContext`

### AC-3: View Reply Context (Channel Messages)
- [ ] `GET /channels/{channelId}/messages` includes `replyContext` on messages that are replies
- [ ] `replyContext` contains `messageId`, `authorName`, `authorAvatarUrl`, `authorUserId`, `bodyPreview` (max 100 chars), and `isDeleted`
- [ ] Non-reply messages have `replyContext: null`

### AC-4: View Reply Context (DM Messages)
- [ ] `GET /dm/channels/{channelId}/messages` includes `replyContext` on DMs that are replies
- [ ] `replyContext` contains the same fields as channel reply context
- [ ] Non-reply DMs have `replyContext: null`

### AC-5: Deleted Original Message Handling
- [ ] If the original message is deleted, `ReplyToMessageId` is set to `null` via `ON DELETE SET NULL`
- [ ] When `ReplyToMessageId` is `null` but the message was previously a reply, the API returns `isDeleted: true` in the reply context (if original not found)
- [ ] The frontend displays _"Original message was deleted"_ in muted italic text
- [ ] The deleted reply reference is not clickable

### AC-6: Reply Reference UI
- [ ] Replies display a compact reference bar above the message body
- [ ] The reference shows the original author's avatar (16px), name (`--accent`), and truncated body preview
- [ ] A thin connecting line visually links the reference to the reply body
- [ ] The reference bar follows the CODEC CRT phosphor-green theme

### AC-7: Click-to-Scroll Navigation
- [ ] Clicking a reply reference scrolls the message feed to the original message
- [ ] The original message is briefly highlighted with an accent background animation (1.5s fade-out)
- [ ] If the original message is not in the current loaded window, the system gracefully handles it (tooltip or no-op)

### AC-8: Reply Composer Bar
- [ ] Clicking the reply button on a message shows "Replying to {authorName}" above the composer
- [ ] The composer bar shows a truncated preview of the original message
- [ ] A cancel (✕) button clears the reply state
- [ ] Pressing `Escape` while the composer is focused cancels the reply
- [ ] After sending the reply, the composer bar is automatically cleared

### AC-9: Reply Action Button
- [ ] A reply icon (↩) appears in the floating action bar on message hover
- [ ] Clicking the reply icon activates the reply composer state for that message
- [ ] The reply button appears alongside the existing emoji reaction button

### AC-10: Reply with Mention (Optional Enhancement)
- [ ] When replying, the system can optionally @-mention the original author in the reply body
- [ ] The mention follows the existing `<@userId>` mention format
- [ ] The original author receives a `MentionReceived` notification via SignalR

### AC-11: DM Reply UI
- [ ] Reply functionality works identically in DM conversations
- [ ] The DM composer bar shows "Replying to {authorName}"
- [ ] DM reply references are clickable and scroll to the original

### AC-12: Database Integrity
- [ ] `ReplyToMessageId` is a nullable FK on the `Messages` table with `ON DELETE SET NULL`
- [ ] `ReplyToDirectMessageId` is a nullable FK on the `DirectMessages` table with `ON DELETE SET NULL`
- [ ] Appropriate indexes exist on both FK columns
- [ ] The migration is reversible (down removes the columns)

### AC-13: Accessibility
- [ ] Reply reference bars have `role="button"` and are keyboard-navigable
- [ ] Screen readers announce "Reply to message from {authorName}"
- [ ] Cancel button has an accessible label
- [ ] `prefers-reduced-motion` disables the highlight animation

## Dependencies

- **Prerequisite:** Existing messaging system (server channels and DMs)
- **Impacts:** `MessageItem.svelte`, `Composer.svelte`, `DmChatArea.svelte`, `MessageFeed.svelte`
- **Reuses:** Existing floating action bar on message hover, `AppState` pattern, `ApiClient` methods
- **Related:** Message editing/deletion (future) — deleting a message must handle `ON DELETE SET NULL` for replies; @-mentions (implemented) — replies can optionally include a mention of the original author

## Migration Plan

A single EF Core migration (`AddMessageReplies`) will:
1. Add a nullable `ReplyToMessageId` column (FK → `Messages.Id`, `ON DELETE SET NULL`) to the `Messages` table
2. Add a nullable `ReplyToDirectMessageId` column (FK → `DirectMessages.Id`, `ON DELETE SET NULL`) to the `DirectMessages` table
3. Add indexes on both new columns
4. All existing messages will have `null` for both columns (no data migration needed)

```sql
-- Illustrative SQL (SQLite syntax). The actual EF Core migration will
-- generate provider-appropriate DDL for the configured database.

-- Up
ALTER TABLE Messages ADD COLUMN ReplyToMessageId TEXT NULL
    REFERENCES Messages(Id) ON DELETE SET NULL;
CREATE INDEX IX_Message_ReplyToMessageId ON Messages(ReplyToMessageId);

ALTER TABLE DirectMessages ADD COLUMN ReplyToDirectMessageId TEXT NULL
    REFERENCES DirectMessages(Id) ON DELETE SET NULL;
CREATE INDEX IX_DirectMessage_ReplyToDirectMessageId ON DirectMessages(ReplyToDirectMessageId);

-- Down (SQLite table rebuild required; EF Core handles this)
```

## Task Breakdown

### API — Data model & migration
- [ ] Add `ReplyToMessageId` (nullable `Guid?`) property to `Message` entity in `Models/Message.cs`
- [ ] Add `ReplyToDirectMessageId` (nullable `Guid?`) property to `DirectMessage` entity in `Models/DirectMessage.cs`
- [ ] Configure self-referencing FK relationships and `ON DELETE SET NULL` in `CodecDbContext.OnModelCreating`
- [ ] Add indexes on `ReplyToMessageId` and `ReplyToDirectMessageId`
- [ ] Create and apply EF Core migration (`AddMessageReplies`)

### API — Reply context in message retrieval
- [ ] Create `ReplyContextDto` record for lightweight reply info
- [ ] Update `GET /channels/{channelId}/messages` in `ChannelsController` to include `replyContext` for each message (batch-load referenced messages in a single query)
- [ ] Update `GET /dm/channels/{channelId}/messages` in `DmController` to include `replyContext` for each DM
- [ ] Handle deleted/missing originals gracefully (`isDeleted: true`)

### API — Reply support in message posting
- [ ] Add `ReplyToMessageId` field to `CreateMessageRequest`
- [ ] Update `POST /channels/{channelId}/messages` to validate and persist `ReplyToMessageId` (same channel check, existence check)
- [ ] Include `replyContext` in the `ReceiveMessage` SignalR payload
- [ ] Add `ReplyToDirectMessageId` field to the DM message request model
- [ ] Update `POST /dm/channels/{channelId}/messages` to validate and persist `ReplyToDirectMessageId`
- [ ] Include `replyContext` in the `ReceiveDm` SignalR payload

### Web — Types & API client
- [ ] Add `ReplyContext` type to `models.ts`
- [ ] Add `replyContext: ReplyContext | null` field to `Message` and `DirectMessage` types
- [ ] Update `sendMessage()` in `ApiClient` to accept optional `replyToMessageId` parameter
- [ ] Update `sendDm()` in `ApiClient` to accept optional `replyToDirectMessageId` parameter

### Web — State management
- [ ] Add `replyingTo` reactive state to `AppState` (per-channel/DM context)
- [ ] Add `startReply(messageId, authorName, bodyPreview)` method to `AppState`
- [ ] Add `cancelReply()` method to `AppState`
- [ ] Clear `replyingTo` on channel/DM switch and after successful send

### Web — UI components
- [ ] Create `ReplyReference.svelte` component (clickable reply preview bar)
- [ ] Create `ReplyComposerBar.svelte` component ("Replying to" bar above composer)
- [ ] Add reply button (↩) to floating action bar in `MessageItem.svelte`
- [ ] Integrate `ReplyReference.svelte` into `MessageItem.svelte` (render above body when `replyContext` is non-null)
- [ ] Integrate `ReplyComposerBar.svelte` into `Composer.svelte` (render above input when `replyingTo` is active)
- [ ] Implement `Escape` key to cancel reply while composer is focused
- [ ] Implement click-to-scroll behavior in `MessageFeed.svelte` (scroll to original + highlight animation)
- [ ] Add reply reference and composer bar to `DmChatArea.svelte`
- [ ] Add highlight animation CSS (`@keyframes reply-highlight`)
- [ ] Add `prefers-reduced-motion` media query to disable highlight animation

### Documentation
- [ ] Update `ARCHITECTURE.md` with updated `ReceiveMessage` / `ReceiveDm` payloads and `ReplyContext` DTO
- [ ] Update `DATA.md` with new columns, FKs, indexes, and updated entity definitions
- [ ] Update `FEATURES.md` to track Replies feature progress (move from "Future" to "In Progress" or "Planned")
- [ ] Update `DESIGN.md` with reply reference bar, reply composer bar, and reply action button UI specification

## Open Questions

1. **Auto-mention on reply:** Should replying to a message automatically insert an `@mention` of the original author in the reply body? (Recommendation: no auto-insert; the user can manually @-mention if they want. Discord provides a toggle for this — Codec can add that later. For MVP, keep it simple.)
2. **Deep scroll:** When clicking a reply reference and the original message is outside the loaded message window (before the cursor), should the client fetch more history to scroll to it? (Recommendation: defer. For MVP, only scroll to messages already loaded in the DOM. Show a tooltip "Message is not in view" if the original is outside the loaded window. Full deep-scroll can be added in a follow-up.)
3. **Reply count indicator:** Should messages show a count of how many replies reference them? (Recommendation: defer. This requires an aggregation query or counter and adds UI complexity. It's more appropriate for a future threading/conversation-view feature.)
4. **Reply notifications:** Should the original author receive a dedicated notification when someone replies to their message (separate from @mentions)? (Recommendation: defer. For MVP, reply notifications rely on the existing @-mention system. A dedicated "Someone replied to your message" notification can be built as part of a broader Notifications feature.)
5. **Context menu reply:** Should right-clicking a message show a "Reply" option in the context menu? (Recommendation: yes, as a fast follow after the initial implementation. The floating action bar reply button covers the primary interaction.)
6. **Reply to image-only messages:** How should the reply reference display when the original message has an image but no text body? (Recommendation: show the author name and `[Image]` as the body preview, with a small thumbnail if feasible. For MVP, just show `[Image]` as text.)
7. **Reply in notification badge:** Should replies to the user's own messages contribute to unread/notification counts? (Recommendation: defer to the broader Notifications feature.)
