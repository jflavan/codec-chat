# Discord Media Re-hosting Design

**Date:** 2026-04-12
**Context:** PR #160 stores Discord CDN URLs directly for imported message attachments, emojis, and avatars. Attachment URLs are signed with expiry tokens (~24h TTL) and will return 403 after expiry. This spec adds a post-text-import background pass to download and re-host images to Codec's own storage.

## Scope

- Re-host imported **custom emojis** and **message image attachments** to Codec storage
- Emojis re-hosted first, then attachments (newest-first)
- Images over size limits are compressed via resize + quality reduction
- Non-image files and unsupported image types are skipped
- Avatars are NOT re-hosted (public unsigned URLs, decorative only)
- The re-hosting pass is idempotent — retries skip already-completed work

## Pipeline Changes

The existing 9-stage import pipeline extends to 11 stages:

| Stage | Name | Description |
|-------|------|-------------|
| 1-9 | (existing) | Roles, categories, channels, permissions, emojis, members, messages, pins, reply backfill |
| — | Milestone | `ImportCompleted` SignalR event fires. Status transitions to `RehostingMedia`. |
| 10 | Emoji re-hosting | Download, compress if needed, upload to Codec storage, update `CustomEmoji.ImageUrl` |
| 11 | Attachment re-hosting | Download message images newest-first, resize/compress if needed, upload to Codec storage, update `Message.ImageUrl` |
| — | Done | Status transitions to `Completed` |

### New status enum value

`DiscordImportStatus.RehostingMedia` — set after stage 9, before stages 10-11. Indicates text import succeeded and media re-hosting is in progress.

### Retry / resync behavior

On re-launch of a failed or cancelled import, `RunImportAsync` runs all stages. Each stage is idempotent:

- **Stages 1-9:** Skip entities that already have a `DiscordEntityMapping` row (existing behavior)
- **Stage 10:** Skip emojis whose `ImageUrl` does not contain `cdn.discordapp.com`
- **Stage 11:** Skip messages whose `ImageUrl` does not contain `cdn.discordapp.com`

This means a retry picks up wherever it left off with no duplicate work.

### Bot token lifecycle

The encrypted bot token is retained through the `RehostingMedia` phase. It is nulled out only when the import reaches `Completed`, `Failed`, or `Cancelled`. This is needed because a retry of a failed import re-runs all stages, and stages 1-9 require the bot token for Discord API calls. CDN downloads themselves do not require the bot token (auth is in the signed URL parameters).

### Cancellation

Same as today. The cancellation token is checked throughout stages 10-11. If cancelled during re-hosting, status is set to `Cancelled` and the bot token is cleared.

## New Dependency

**`SkiaSharp`** (MIT) + **`SkiaSharp.NativeAssets.Linux`** (MIT) added to `Codec.Api.csproj`. SkiaSharp is the .NET binding for Google's Skia graphics library, maintained by Microsoft. Used for reading image metadata, resizing, quality reduction, and format conversion. Supports JPEG, PNG, WebP, and GIF natively via built-in codecs.

## New Service: DiscordMediaRehostService

Responsible for downloading a Discord CDN image, processing it, and uploading it to Codec storage. Injected into `DiscordImportService`.

### Interface

```csharp
public enum RehostOutcome { Success, Skipped, Failed }

public record RehostResult(RehostOutcome Outcome, string? Url = null);

public class DiscordMediaRehostService
{
    public virtual Task<RehostResult> RehostImageAsync(
        string discordCdnUrl,
        string storageContainer,  // "images" or "emojis"
        long maxFileSize,         // 10MB for attachments, 512KB for emojis
        int? maxDimensionPx,      // 4096 for attachments, null for emojis
        CancellationToken ct);
}
```

### Dependencies

- `HttpClient` (registered with `DiscordRateLimitHandler` for 50 req/sec throttling — CDN downloads don't need bot token auth but share the global rate limit budget with API calls, which is fine since stages 1-9 are complete before stages 10-11 begin)
- `IFileStorageService` (Local or AzureBlob, depending on config)

### Processing logic

1. **Download** the image from the Discord CDN URL via the rate-limited `HttpClient`.
2. **Validate** content type from the HTTP response. Only process: `image/jpeg`, `image/png`, `image/webp`, `image/gif`. Return `RehostResult(Skipped)` for anything else.
3. **Check file size and dimensions** using SkiaSharp (load image metadata without full decode when possible via `SKCodec`).
4. **Resize** if dimensions exceed `maxDimensionPx` on the longest side. Resize proportionally, maintaining aspect ratio.
5. **Compress** if file size exceeds `maxFileSize`:
   - For JPEG/WebP: encode at quality 85 first; if still over the limit, iterate from quality 75 down to 25 in steps of 10 until under the limit.
   - For PNG: convert to WebP with lossy compression (PNGs can't be quality-reduced meaningfully as PNG).
   - For GIF: if over `maxFileSize` after any applicable resize, return `RehostResult(Skipped)`. GIF processing is expensive and unreliable.
6. **Upload** to the specified storage container via `IFileStorageService`. Use the same content-addressed path pattern as `ImageUploadService`: `import/{sha256-hash-prefix}{ext}`. The `import/` prefix distinguishes re-hosted media from user-uploaded media.
7. **Return** a `RehostResult` with `Outcome = Success` and the new Codec-hosted URL, `Skipped` for unsupported or oversized-GIF cases, or `Failed` on download/processing errors.

### Rate limiting

Downloads go through the same `HttpClient` pipeline registered with `DiscordRateLimitHandler`, so the existing 50 req/sec global token bucket and per-bucket reset tracking apply automatically. No additional rate limiting logic is needed in this service.

## Stage 10: Emoji Re-hosting

Query all `CustomEmoji` rows in the server that were created by this import (join through `DiscordEntityMapping` where `EntityType == Emoji`).

For each emoji:

1. Skip if `ImageUrl` does not contain `cdn.discordapp.com` (already re-hosted on a previous run).
2. Call `DiscordMediaRehostService.RehostImageAsync` with `storageContainer: "emojis"`, `maxFileSize: 512KB`, `maxDimensionPx: null`.
3. If the result is `Success`, update `CustomEmoji.ImageUrl` to the new Codec URL.
4. If the result is `Skipped` or `Failed` (unsupported type or GIF too large), log a warning and leave the Discord CDN URL in place.
5. Save in batches (every 10 emojis).

### Emoji naming

The existing import code (stage 5) already handles name deduplication with a `_1`, `_2` suffix pattern when conflicts are detected. No changes needed here — emojis are already named and accessible from the server's custom emoji picker after stage 5. Stage 10 only updates the `ImageUrl`.

### Emoji size limit

The existing `CustomEmojiService` enforces a 512KB limit. Emojis imported from Discord may exceed this if they were uploaded under Discord's more generous limits. Stage 10 compresses these down to 512KB using SkiaSharp quality reduction. If an emoji can't be compressed below 512KB (unlikely for emoji-sized images), it is skipped.

## Stage 11: Attachment Re-hosting

Query all messages in the server's channels where `ImageUrl LIKE 'https://cdn.discordapp.com%'`, ordered by `CreatedAt DESC` (newest first). Process in batches of 50.

For each message:

1. Call `DiscordMediaRehostService.RehostImageAsync` with `storageContainer: "images"`, `maxFileSize: 10MB`, `maxDimensionPx: 4096`.
2. If the result is `Success`, update `Message.ImageUrl` to the new Codec URL.
3. If the result is `Skipped` (unsupported type, oversized GIF) or `Failed` (download error):
   - Set `Message.ImageUrl = null` — the preview is lost but message text is preserved.
   - Log a warning with the message ID and original URL.
4. Broadcast `ImportProgress` via SignalR every 50 messages with stage name and completion count.

### Non-image FileUrl attachments

Messages with `FileUrl` pointing to Discord CDN (documents, videos, archives) are **not re-hosted**. The `FileUrl` remains populated so users can see the filename, but the download link will eventually 404 when the Discord CDN URL expires. This is an accepted trade-off per the scope decision to only re-host supported image types.

## Error Handling

### Per-image resilience

Each image is processed independently. If a download fails (network error, 403, timeout), log a warning and move to the next image. A single image failure does not fail the import.

### Systemic failure detection

If 10 consecutive downloads fail, treat it as a systemic issue (e.g., storage service down, bot token revoked, network outage). Set the import status to `Failed` with an error message indicating media re-hosting failed. The text import data is preserved. The user can retry later.

### Status on failure during re-hosting

If the import fails during stages 10-11, the status is set to `Failed` with an error message noting that text import succeeded but media re-hosting failed. On retry, stages 1-9 skip (all entities already mapped), and stages 10-11 resume from where they left off (idempotent URL checks).

## SignalR Progress Events

Stages 10-11 use the existing `ImportProgress` event format:

```json
{ "stage": "Re-hosting emojis", "completed": 15, "total": 30, "percentComplete": 97.5 }
{ "stage": "Re-hosting images (142/500)", "completed": 142, "total": 500, "percentComplete": 98.0 }
```

The `ImportCompleted` event fires after stage 9 (before re-hosting begins) so the frontend can show "Import complete — optimizing media..." or similar. A second `ImportCompleted` does NOT fire after stages 10-11; instead the status transition to `Completed` is picked up by the existing polling/SignalR mechanisms.

## Frontend Changes

Minimal. The frontend already shows import progress by stage name and handles the `RehostingMedia` status as a variant of "in progress":

- `ServerDiscordImport.svelte`: When status is `RehostingMedia`, show "Import complete. Re-hosting media..." with the progress bar still active.
- `WizardStepProgress.svelte`: Same treatment — the wizard can close after `ImportCompleted` fires, and the settings panel shows the ongoing re-hosting progress.
- `models.ts`: Add `RehostingMedia` to the `DiscordImport` status type.

## Files Changed (estimated)

### New files
- `apps/api/Codec.Api/Services/DiscordMediaRehostService.cs`

### Modified files
- `apps/api/Codec.Api/Codec.Api.csproj` — add `SkiaSharp` and `SkiaSharp.NativeAssets.Linux` packages
- `apps/api/Codec.Api/Services/DiscordImportService.cs` — add stages 10-11, inject `DiscordMediaRehostService`
- `apps/api/Codec.Api/Models/DiscordImport.cs` — add `RehostingMedia` to status enum
- `apps/api/Codec.Api/Program.cs` — register `DiscordMediaRehostService`
- `apps/web/src/lib/types/models.ts` — add `RehostingMedia` status
- `apps/web/src/lib/components/discord-import/WizardStepProgress.svelte` — handle `RehostingMedia` status
- `apps/web/src/lib/components/server-settings/ServerDiscordImport.svelte` — handle `RehostingMedia` status
