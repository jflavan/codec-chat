# Discord Server Import

> **Implementation divergences (2026-04-12):** The final implementation differs from this original spec in several ways:
> - **No attachment re-hosting** -- Discord CDN URLs are used directly for message attachments (no download/re-upload to Codec file storage). Emojis are still downloaded.
> - **Newest-first pagination** -- Messages are imported newest-first using `before` cursor pagination (not oldest-first as originally specified), so users see recent messages immediately.
> - **Live channel updates** -- An `ImportMessagesAvailable` SignalR event is sent to `channel-{channelId}` groups after each message batch, enabling real-time message loading in the frontend during import.
> - **Parallel channel imports (4 concurrent)** -- Message history is fetched for up to 4 channels concurrently via `SemaphoreSlim`.
> - **Global rate limiting** -- A `TokenBucketRateLimiter` (50 tokens/sec) enforces Discord's API rate limit across all parallel imports, replacing the original ~40 req/sec cap.
> - **Performance** -- 371K messages imported in ~20 minutes, 98+ msgs/API call, ~10% 429 rate.

Import Discord server content into Codec via a Discord bot, run as a background job in the API.

## Scope

Full migration: server structure (categories, channels, roles, permission overrides), message history (with attachments, reactions, replies, pins), custom emojis, and member mappings. Admin-triggered, one-shot with optional re-sync.

## Data Model

### New Entities

**`DiscordImport`** -- tracks each import job.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ServerId | Guid | FK to Server |
| DiscordGuildId | string | Discord snowflake |
| BotToken | string | Encrypted at rest via Data Protection API. Cleared after import completes. |
| Status | enum | Pending, InProgress, Completed, Failed, Cancelled |
| StartedAt | DateTimeOffset? | |
| CompletedAt | DateTimeOffset? | |
| ErrorMessage | string? | Populated on failure |
| ImportedChannels | int | Progress counter |
| ImportedMessages | int | Progress counter |
| ImportedMembers | int | Progress counter |
| LastSyncedAt | DateTimeOffset? | For re-sync: timestamp of newest imported message |
| InitiatedByUserId | Guid | FK to User |

**`DiscordUserMapping`** -- maps Discord users to Codec users for the claim flow.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ServerId | Guid | FK to Server |
| DiscordUserId | string | Discord snowflake |
| DiscordUsername | string | Display name at time of import |
| DiscordAvatarUrl | string? | Avatar URL at time of import |
| CodecUserId | Guid? | FK to User, null until claimed |
| ClaimedAt | DateTimeOffset? | |

**`DiscordEntityMapping`** -- maps Discord entities to Codec entities for re-sync and deduplication.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| DiscordImportId | Guid | FK to DiscordImport |
| ServerId | Guid | FK to Server |
| DiscordEntityId | string | Discord snowflake |
| EntityType | enum | Role, Category, Channel, Message, Emoji, PinnedMessage |
| CodecEntityId | Guid | The corresponding Codec record's ID |

Unique constraint on `(ServerId, DiscordEntityId, EntityType)`.

### Changes to Existing Models

**`Message`** -- two new nullable fields:

- `ImportedAuthorName` (string?) -- Discord username, used for display when author hasn't claimed
- `ImportedAuthorAvatarUrl` (string?) -- Discord avatar, used for display when author hasn't claimed

No changes to Server, Channel, ChannelCategory, ServerRoleEntity, Reaction, CustomEmoji, PinnedMessage, or ChannelPermissionOverride. Imported data uses existing models directly.

## Import Pipeline

### Architecture

Background job using `IHostedService` with a `Channel<DiscordImportJob>` queue (producer/consumer pattern).

### Flow

1. Admin calls `POST /servers/{serverId}/discord-import` with `{ botToken, discordGuildId }`.
2. Controller validates the bot token against Discord API (`GET /guilds/{guildId}`), creates a `DiscordImport` record (status: Pending), enqueues the job, returns `202 Accepted` with the import ID.
3. Background worker picks up the job and imports in order:
   1. **Roles** -- create `ServerRoleEntity` records, map Discord role IDs to Codec role IDs. `@everyone` maps to Codec's existing `@everyone` system role (permissions updated, not duplicated).
   2. **Categories** -- create `ChannelCategory` records, map IDs.
   3. **Channels** -- create `Channel` records with correct category and position, map IDs.
   4. **Channel permission overrides** -- create `ChannelPermissionOverride` records using role ID mapping.
   5. **Custom emojis** -- download images to Codec file storage, create `CustomEmoji` records.
   6. **Members** -- create `DiscordUserMapping` records (no Codec User records created).
   7. **Messages** (per channel, paginated oldest-first) -- create `Message` records with `ImportedAuthorName`/`ImportedAuthorAvatarUrl`, download attachments to Codec file storage, create `Reaction` records.
   8. **Pinned messages** -- create `PinnedMessage` records.
4. Progress updates pushed via SignalR to `server-{serverId}` group after each stage and every ~100 messages.

### Discord Rate Limiting

A `DelegatingHandler` on a named `HttpClient("discord")` that reads `X-RateLimit-Remaining` and `Retry-After` response headers, with a global cap of ~40 req/sec.

### Re-sync

When triggered again on the same guild:
- Roles, categories, channels matched by Discord ID via `DiscordEntityMapping`; existing records skipped, new ones created.
- Messages fetched only after `LastSyncedAt`.
- Admin must provide a fresh bot token (previous one was cleared).

### Failure Handling

On failure, status set to `Failed` with error message. Data imported so far is kept (no rollback). Admin can re-trigger to resume.

## API Endpoints

All endpoints require `ManageServer` permission on the target server.

| Method | Route | Purpose | Response |
|--------|-------|---------|----------|
| POST | `/servers/{serverId}/discord-import` | Start import | 202 with import ID |
| GET | `/servers/{serverId}/discord-import` | Get latest import status and progress | 200 |
| POST | `/servers/{serverId}/discord-import/resync` | Trigger re-sync | 202 |
| DELETE | `/servers/{serverId}/discord-import` | Cancel in-progress import | 204 |
| GET | `/servers/{serverId}/discord-import/mappings` | List Discord user mappings | 200 |
| POST | `/servers/{serverId}/discord-import/claim` | Claim a Discord identity | 200 |

### Request/Response Bodies

**Start import request:**
```json
{ "botToken": "string", "discordGuildId": "string" }
```

**Import status response:**
```json
{
  "id": "guid",
  "status": "InProgress",
  "importedChannels": 12,
  "importedMessages": 4500,
  "importedMembers": 85,
  "startedAt": "2026-04-11T...",
  "completedAt": null,
  "errorMessage": null
}
```

**Claim request:**
```json
{ "discordUserId": "string" }
```

## SignalR Events

Sent to `server-{serverId}` group:

- `ImportProgress` -- `{ stage: string, completed: int, total: int, percentComplete: float }`
- `ImportCompleted` -- `{ importedChannels: int, importedMessages: int, importedMembers: int }`
- `ImportFailed` -- `{ errorMessage: string }`

## Permission Mapping

Static translation from Discord permission bits to Codec `Permission` enum:

| Discord | Codec |
|---------|-------|
| VIEW_CHANNEL | ViewChannels |
| SEND_MESSAGES | SendMessages |
| MANAGE_CHANNELS | ManageChannels |
| MANAGE_GUILD | ManageServer |
| MANAGE_ROLES | ManageRoles |
| KICK_MEMBERS | KickMembers |
| BAN_MEMBERS | BanMembers |
| MANAGE_MESSAGES | ManageMessages |
| ATTACH_FILES | AttachFiles |
| ADD_REACTIONS | AddReactions |
| MENTION_EVERYONE | MentionEveryone |
| CONNECT | Connect |
| SPEAK | Speak |
| MUTE_MEMBERS | MuteMembers |
| DEAFEN_MEMBERS | DeafenMembers |
| ADMINISTRATOR | Administrator |

Unmapped Discord permissions silently dropped.

## Security

- **Bot token**: encrypted at rest using ASP.NET Data Protection API. Never returned in API responses. Cleared after import completes.
- **Authorization**: all import endpoints require `ManageServer` permission.
- **Claim validation**: if the claiming user has linked their Discord account (via `DiscordSubject` on User), the claim is verified automatically by matching `DiscordSubject` to `DiscordUserMapping.DiscordUserId`. Otherwise trust-based; admins can revoke claims.

## Frontend

### Server Settings -- "Import from Discord" Tab

- Form: bot token input + Discord guild ID input + "Start Import" button
- During import: progress bar with stage label and message count
- After completion: summary stats, "Re-sync" button
- Cancel button while in progress

### Message Display

When `ImportedAuthorName` is set and `AuthorUser` is null:
- Show imported name and avatar
- Subtle "imported" badge on the message

### Claim Flow

- In server member list or via a banner: unclaimed Discord users listed
- "This is me" button triggers claim endpoint
- On claim: update all `Message` records where `ImportedAuthorName` matches the mapping's `DiscordUsername` and `AuthorUserId` is null -- set `AuthorUserId` to the claiming user's ID and clear `ImportedAuthorName`/`ImportedAuthorAvatarUrl`. Also update `DiscordUserMapping.CodecUserId` and `ClaimedAt`.

## Testing

- **Unit tests**: permission mapping logic, rate limit handler, entity mapping deduplication
- **Integration tests**: full import pipeline with mocked Discord API responses (using Testcontainers for DB)
- **Frontend tests**: import settings tab rendering, progress bar updates, claim flow
