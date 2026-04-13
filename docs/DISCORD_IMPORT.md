# Discord Import

Codec supports importing an entire Discord server — structure, messages, emojis, and member mappings — into an existing Codec server. The import is initiated from the **Discord Import** tab in server settings and runs as a background job with real-time progress via SignalR. Messages are imported newest-first so users see recent conversations immediately, with history backfilled in the background. Live `ImportMessagesAvailable` SignalR events notify the frontend after each batch, enabling real-time message loading during import.

## Overview

The Discord import feature migrates:

- **Roles** with permission mapping (Discord bitmask to Codec `Permission` flags)
- **Categories** preserving order
- **Channels** with descriptions and category assignments
- **Per-channel permission overrides** (allow/deny bitmasks per role)
- **Custom emojis** (Discord CDN URLs used directly, same as attachments)
- **Members** with role assignments and Discord avatar URLs
- **Messages** with author info, first attachment, image URLs, and reply chains (only message types 0/regular and 19/reply are imported; system messages skipped)
- **Pinned messages** per channel

Imported messages display the original Discord author's name and avatar via `ImportedAuthorName`, `ImportedAuthorAvatarUrl`, and `ImportedDiscordUserId` fields on the `Message` entity, shown with an `ImportedAuthorBadge` in the UI.

### Data Model: Message Fields for Import

Three nullable fields are added to the `Message` entity:

- `ImportedAuthorName` (string?) -- Discord username, used for display when author hasn't claimed
- `ImportedAuthorAvatarUrl` (string?) -- Discord avatar URL, used for display when author hasn't claimed
- `ImportedDiscordUserId` (string?) -- Discord user snowflake ID, used for claim matching; cleared on claim

## How It Works (Wizard Flow)

The import wizard in server settings has 4 steps:

1. **Bot Setup** (`WizardStepBotSetup`) — Instructions for creating a Discord bot, enabling the required intents, and generating a bot token.
2. **Connect** (`WizardStepConnect`) — Enter the bot token and Discord guild (server) ID. The API validates the token by fetching guild info.
3. **Destination** (`WizardStepDestination`) — Confirm the target Codec server and review what will be imported.
4. **Progress** (`WizardStepProgress`) — Real-time progress bar showing each import stage (roles, categories, channels, emojis, members, messages, pins) via SignalR events.

## Prerequisites

### Discord Bot Setup

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications) and create a new application.
2. Navigate to **Bot** and click **Reset Token** to generate a bot token.
3. Enable the following **Privileged Gateway Intents**:
   - `SERVER MEMBERS` — required to fetch the member list
   - `MESSAGE CONTENT` — required to read message bodies
4. Navigate to **OAuth2 > URL Generator**, select the `bot` scope, and grant the following permissions:
   - Read Messages/View Channels
   - Read Message History
   - Manage Roles (to read role configurations)
   - Manage Channels (to read channel configurations)
5. Use the generated URL to invite the bot to your Discord server.
6. Copy the bot token and the Discord server ID (right-click server name > Copy Server ID with developer mode enabled).

## Import Pipeline

The import runs in this order:

| Stage | What's Imported | Notes |
|-------|----------------|-------|
| Roles | All server roles except @everyone; managed (bot) roles skipped | Permissions mapped via `DiscordPermissionMapper`; position and color preserved; @everyone permissions synced to Codec's existing @everyone role |
| Categories | Channel categories | Position preserved |
| Channels | Text and voice channels | Description, category assignment, and position preserved |
| Permission Overrides | Per-channel role overrides | Allow/deny bitmasks mapped to Codec permissions |
| Emojis | Custom emojis | Discord CDN URLs used directly (not downloaded); name and animated flag preserved |
| Members | Guild members | Discord username, avatar URL, and role assignments stored |
| Messages | Full message history per channel | Imported newest-first (`before` pagination) across up to 4 channels in parallel; only message types 0 (regular) and 19 (reply) are imported — system messages skipped; only the first attachment per message is imported (multiple attachments not supported); Discord CDN URLs used directly for attachments (no re-hosting); reply chains preserved via `PendingReply` backfill (see below); author info stored in `ImportedAuthorName`/`ImportedAuthorAvatarUrl`/`ImportedDiscordUserId`; `ImportMessagesAvailable` SignalR event with `{ channelId, count }` sent to `channel-{channelId}` group after each batch so the frontend reloads messages in real-time |
| Pins | Pinned messages per channel | Matched to imported messages via entity mappings |

## Media Re-hosting

After the text import completes (stages 1-9), the import transitions to a `RehostingMedia` phase:

- **Stage 10 — Emoji re-hosting:** Custom emojis are downloaded from Discord CDN and uploaded to Codec storage. Emojis larger than 512KB are compressed. Emojis are accessible in the server's emoji picker immediately after import.
- **Stage 11 — Attachment re-hosting:** Message image attachments (`image/jpeg`, `image/png`, `image/webp`, `image/gif`) are downloaded newest-first, resized if over 4096px, compressed if over 10MB, and uploaded to Codec storage. Non-image attachments and GIFs over 10MB are skipped.

The `ImportCompleted` SignalR event fires after stage 9, so users see "Import complete" immediately. Media re-hosting continues in the background. If re-hosting fails, a retry will pick up where it left off (already re-hosted images are skipped).

## Identity Claiming

After import, Discord members can claim their imported messages:

1. The member joins the Codec server.
2. They navigate to server settings and find the Discord Import tab.
3. They click "Claim" next to their Discord username.
4. If the user has a linked Discord account (`DiscordSubject` on User), the system verifies the Discord user ID matches.
5. On claim, all messages authored by that Discord user are updated: `AuthorUserId` is set to the Codec user, and `ImportedAuthorName`/`ImportedAuthorAvatarUrl`/`ImportedDiscordUserId` are cleared.

Endpoint: `POST /servers/{serverId}/discord-import/claim`

## Re-sync

After the initial import, you can re-sync to pick up new messages:

1. Open the Discord Import tab in server settings.
2. Click **Re-sync** and provide the bot token again.
3. The system creates a new `DiscordImport` record and incrementally imports only new messages from Discord.

**Incremental import:** For each channel, the system finds the newest already-imported Discord message ID (via `DiscordEntityMapping` joined with `Messages`) and uses Discord's `after` pagination parameter to fetch only messages newer than that point. Channels with no previously imported messages fall back to full newest-first (`before`) pagination. This same optimization applies to retries of failed imports — channels that were fully imported before the failure are skipped entirely.

Endpoint: `POST /servers/{serverId}/discord-import/resync`

## API Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| `POST` | `/servers/{serverId}/discord-import` | Start a new import (requires ManageServer permission) |
| `GET` | `/servers/{serverId}/discord-import` | Get import status (latest import for the server) |
| `POST` | `/servers/{serverId}/discord-import/resync` | Re-sync messages since last completed import |
| `DELETE` | `/servers/{serverId}/discord-import` | Cancel an in-progress import |
| `GET` | `/servers/{serverId}/discord-import/mappings` | List Discord-to-Codec user mappings |
| `POST` | `/servers/{serverId}/discord-import/claim` | Claim a Discord identity (link imported messages to Codec account) |

All endpoints require `[Authorize]`. Start, status, resync, cancel, and mappings require ManageServer permission. Claim requires server membership.

## SignalR Events

All events are sent to the `server-{serverId}` group.

| Event | Payload | Description |
|-------|---------|-------------|
| `ImportProgress` | `{ stage, completed, total, percentComplete }` | Progress update for each import stage (Roles, Categories, Channels, Emojis, Members, Messages, Pins) |
| `ImportCompleted` | `{ importedChannels, importedMessages, importedMembers, completedAt }` | Import finished successfully |
| `ImportFailed` | `{ errorMessage }` | Import failed with error details |
| `ImportMessagesAvailable` | `{ channelId, count }` | New batch of messages available in a channel (sent to `channel-{channelId}` group); frontend reloads messages on receipt |

## Permission Mapping

Discord permissions (64-bit bitmask) are mapped to Codec `Permission` flags:

| Discord Permission | Bit | Codec Permission |
|-------------------|-----|-----------------|
| View Channels | 1 << 10 | `ViewChannels` |
| Send Messages | 1 << 11 | `SendMessages` |
| Manage Channels | 1 << 4 | `ManageChannels` |
| Manage Guild | 1 << 5 | `ManageServer` |
| Manage Roles | 1 << 28 | `ManageRoles` |
| Kick Members | 1 << 2 | `KickMembers` |
| Ban Members | 1 << 1 | `BanMembers` |
| Manage Messages | 1 << 13 | `ManageMessages` |
| Attach Files | 1 << 15 | `AttachFiles` |
| Add Reactions | 1 << 6 | `AddReactions` |
| Mention Everyone | 1 << 17 | `MentionEveryone` |
| Connect (Voice) | 1 << 20 | `Connect` |
| Speak (Voice) | 1 << 21 | `Speak` |
| Mute Members | 1 << 22 | `MuteMembers` |
| Deafen Members | 1 << 23 | `DeafenMembers` |
| Administrator | 1 << 3 | `Administrator` |
| Embed Links | 1 << 14 | `EmbedLinks` |

Discord permissions without a Codec equivalent (e.g., Manage Webhooks, Create Invites) are silently dropped.

## Architecture Notes

- **Background worker** (`DiscordImportWorker`) — hosted service that reads from a `Channel<Guid>` queue and processes imports sequentially.
- **Channel\<T\> queue** — `System.Threading.Channels.Channel<Guid>` registered as a singleton; the controller writes import IDs and the worker reads them.
- **Parallel channel imports** — message history is fetched for up to 4 channels concurrently (`MaxParallelChannels = 4` in `DiscordImportService`).
- **Media re-hosting (`DiscordMediaRehostService`)** — after text import completes, emojis and image attachments (`image/jpeg`, `image/png`, `image/webp`, `image/gif`) are downloaded from Discord CDN, processed via SkiaSharp, and uploaded to Codec storage. Images over 10MB are resized (max 4096px) and compressed; emojis over 512KB are compressed; PNGs that exceed size limits are converted to WebP; GIFs over size limits are skipped. 10 consecutive failures abort the re-hosting phase, but all text import data is preserved. The process is idempotent — retries skip already re-hosted images. Non-image file attachments (`FileUrl`) and member avatars (`DiscordAvatarUrl`, `ImportedAuthorAvatarUrl`) are not re-hosted and continue to reference Discord CDN URLs.
- **Reply backfill (`PendingReply`)** — since messages are imported newest-first, a reply may reference a message that hasn't been imported yet. These are tracked as `PendingReply` entity mappings in `DiscordEntityMapping`. After all messages in a channel are imported, a final backfill pass resolves pending replies by looking up the now-imported parent messages.
- **Rate limiting** — `DiscordRateLimitHandler` is a `DelegatingHandler` on the Discord API `HttpClient` that reads `X-RateLimit-*` response headers (including per-endpoint `X-RateLimit-Bucket` tracking) and delays requests when limits are hit. A global `TokenBucketRateLimiter` (50 tokens/sec) coordinates rate limiting across all parallel channel imports.
- **Partial visibility** — imported data is visible immediately as each batch completes. Users can browse channels and read messages during an in-progress import.
- **Live channel updates** — `ImportMessagesAvailable` SignalR event is sent to the `channel-{channelId}` group after each message batch, so the frontend can reload and display messages as they are imported.
- **Bot token security** — tokens are encrypted at rest using ASP.NET Core Data Protection (`IDataProtectionProvider`) and cleared after import completes.
- **Scoped services** — the worker creates a new DI scope per import to get fresh `CodecDbContext` and `DiscordApiClient` instances.
- **Entity mappings** — `DiscordEntityMapping` records track the relationship between Discord IDs and Codec IDs for roles, categories, channels, messages, emojis, and pins, enabling re-sync and cross-reference.

## Performance Characteristics

Benchmarked on a real Discord server import:

| Metric | Value |
|--------|-------|
| Total messages imported | 371,000 |
| Wall-clock time | ~20 minutes |
| Messages per API call | 98+ (Discord returns up to 100) |
| Discord API 429 rate | ~10% of requests |
| Parallel channels | 4 concurrent |
| Pagination direction | Newest-first (`before` cursor) |

The newest-first strategy means users see recent messages within seconds of import start. The global `TokenBucketRateLimiter` keeps 429 rates manageable while maximizing throughput across parallel channel imports.

## Limitations

- **Only first attachment per message** — if a Discord message has multiple attachments, only the first is imported; additional attachments are dropped.
- **Only regular messages and replies** — only Discord message types 0 (regular) and 19 (reply) are imported. System messages (joins, boosts, pins, etc.) are skipped.
- **Reactions not imported** — message reactions are not included in the import.
- **Discord CDN URLs may break (non-image attachments and avatars only)** — non-image file attachments (`FileUrl`) still reference Discord CDN URLs and may expire if the source Discord server is deleted. Member avatars (`DiscordAvatarUrl`, `ImportedAuthorAvatarUrl`) also use Discord CDN URLs, though these are public/unsigned and low risk. Custom emojis and image attachments are re-hosted to Codec storage and are not affected.
- **Re-sync is incremental for messages** — re-sync uses `after` pagination from the newest already-imported Discord message ID per channel, fetching only new messages from the Discord API. Other entities (roles, categories, channels, members) are still re-fetched but deduplicated via `DiscordEntityMapping` checks.
- **Managed roles skipped** — bot integration roles (marked as `managed` by Discord) are not imported.
