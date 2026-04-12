# Discord Import

Codec supports importing an entire Discord server — structure, messages, emojis, and member mappings — into an existing Codec server. The import is initiated from the **Discord Import** tab in server settings and runs as a background job with real-time progress via SignalR.

## Overview

The Discord import feature migrates:

- **Roles** with permission mapping (Discord bitmask to Codec `Permission` flags)
- **Categories** preserving order
- **Channels** with descriptions and category assignments
- **Per-channel permission overrides** (allow/deny bitmasks per role)
- **Custom emojis** (downloaded from Discord CDN)
- **Members** with role assignments and Discord avatar URLs
- **Messages** with author info, attachments, image URLs, and reply chains
- **Pinned messages** per channel

Imported messages display the original Discord author's name and avatar via `ImportedAuthorName` and `ImportedAuthorAvatarUrl` fields on the `Message` entity, shown with an `ImportedAuthorBadge` in the UI.

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
| Roles | All server roles except @everyone | Permissions mapped via `DiscordPermissionMapper`; position and color preserved |
| Categories | Channel categories | Position preserved |
| Channels | Text channels | Description, category assignment, and position preserved |
| Permission Overrides | Per-channel role overrides | Allow/deny bitmasks mapped to Codec permissions |
| Emojis | Custom emojis | Downloaded from Discord CDN; name and animated flag preserved |
| Members | Guild members | Discord username, avatar URL, and role assignments stored |
| Messages | Full message history per channel | Imported in parallel (up to 4 channels); attachments stored as image/file URLs; reply chains preserved; author info stored in `ImportedAuthorName`/`ImportedAuthorAvatarUrl` |
| Pins | Pinned messages per channel | Matched to imported messages via entity mappings |

## Identity Claiming

After import, Discord members can claim their imported messages:

1. The member joins the Codec server.
2. They navigate to server settings and find the Discord Import tab.
3. They click "Claim" next to their Discord username.
4. If the user has a linked Discord account (`DiscordSubject` on User), the system verifies the Discord user ID matches.
5. On claim, all messages authored by that Discord user are updated: `AuthorUserId` is set to the Codec user, and `ImportedAuthorName`/`ImportedAuthorAvatarUrl` are cleared.

Endpoint: `POST /servers/{serverId}/discord-import/claim`

## Re-sync

After the initial import, you can pull new messages posted since the last import:

1. Open the Discord Import tab in server settings.
2. Click **Re-sync** and provide the bot token again.
3. The system creates a new `DiscordImport` record with `LastSyncedAt` set from the previous completed import.
4. Only messages newer than `LastSyncedAt` are fetched.

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
- **Discord CDN URLs** — emoji images are downloaded from `cdn.discordapp.com` and stored locally (or in blob storage); message attachment URLs reference the original Discord CDN.
- **Rate limiting** — `DiscordRateLimitHandler` is a `DelegatingHandler` on the Discord API `HttpClient` that reads `X-RateLimit-*` response headers and delays requests when limits are hit.
- **Bot token security** — tokens are encrypted at rest using ASP.NET Core Data Protection (`IDataProtectionProvider`) and cleared after import completes.
- **Scoped services** — the worker creates a new DI scope per import to get fresh `CodecDbContext` and `DiscordApiClient` instances.
- **Entity mappings** — `DiscordEntityMapping` records track the relationship between Discord IDs and Codec IDs for roles, categories, channels, messages, emojis, and pins, enabling re-sync and cross-reference.
