# Link Previews (Automatic Embeds) Feature Specification

This document describes the **Link Previews** feature for Codec — automatic visual embeds generated when a user sends a message containing a URL. The system fetches Open Graph and HTML meta tags from the linked website and renders a clickable preview card inline below the message.

## Overview

When a message is posted (in a server channel or DM), the API detects URLs in the message body, fetches metadata from each URL (title, description, image, site name), and stores the resulting preview data alongside the message. The frontend renders a compact, clickable embed card below the message text. This mirrors the behavior of link previews in Discord, Slack, and iMessage.

## Goals

- Automatically generate rich visual previews for URLs shared in messages
- Fetch Open Graph (`og:title`, `og:description`, `og:image`, `og:site_name`, `og:url`) and HTML meta tag fallbacks (`<title>`, `<meta name="description">`)
- Display a clickable embed card below the message body with title, description, thumbnail, and site name
- Ensure previews do not block message delivery — metadata fetching happens asynchronously after the message is persisted
- Protect against SSRF, excessive resource consumption, and malicious payloads

## Terminology

| Term | Definition |
|------|-----------|
| **Link Preview** | A visual card showing metadata (title, description, image) extracted from a URL |
| **Embed** | Synonym for link preview in the context of chat applications |
| **Open Graph (OG)** | A protocol (`og:*` meta tags) that enables web pages to declare structured metadata for rich previews |
| **Meta Tags** | HTML `<meta>` elements in a page's `<head>` used as fallback when OG tags are absent |
| **Unfurling** | The process of resolving a URL into a link preview |

## User Stories

### Sharing a Link
> As a user, I want to paste a URL into a message and have Codec automatically generate a visual preview so that other users can see what the link is about without opening it.

### Viewing a Link Preview
> As a user, I want to see a title, description, and thumbnail for shared links so that I can decide whether to click through.

### Clicking a Link Preview
> As a user, I want to click on a link preview card to open the URL in a new browser tab.

### Multiple Links in a Message
> As a user, I want all links in my message to generate previews (up to a reasonable limit) so that shared resources are all visible.

### Failed Preview
> As a user, if a link cannot be previewed (e.g., the site blocks scrapers or is unreachable), I want the URL to still be a clickable hyperlink in the message body without a broken embed card.

## Data Model

### LinkPreview Entity

A link preview is an optional child of a `Message` or `DirectMessage`. One message may have zero or more link previews (capped at 5).

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique link preview identifier |
| `MessageId` | Guid? (FK → Message) | Reference to a server channel message (nullable) |
| `DirectMessageId` | Guid? (FK → DirectMessage) | Reference to a DM message (nullable) |
| `Url` | string (2048 max) | The original URL found in the message body |
| `Title` | string? (512 max) | Page title from `og:title` or `<title>` |
| `Description` | string? (1024 max) | Page description from `og:description` or `<meta name="description">` |
| `ImageUrl` | string? (2048 max) | Thumbnail URL from `og:image` |
| `SiteName` | string? (256 max) | Site name from `og:site_name` |
| `CanonicalUrl` | string? (2048 max) | Canonical URL from `og:url` (used as the click-through target if present) |
| `FetchedAt` | DateTimeOffset | When metadata was fetched |
| `Status` | LinkPreviewStatus (enum) | `Pending`, `Success`, `Failed` |

**Constraints:**
- Exactly one of `MessageId` or `DirectMessageId` must be non-null (check constraint)
- Maximum 5 link previews per message (enforced at application level)

### LinkPreviewStatus Enum

```csharp
public enum LinkPreviewStatus
{
    Pending = 0,   // Metadata fetch has not completed yet
    Success = 1,   // Metadata was successfully fetched
    Failed = 2     // Fetch failed (timeout, unreachable, no metadata)
}
```

### Schema Diagram

```
┌─────────────┐       ┌─────────────────┐       ┌────────────────┐
│  Message    │       │  LinkPreview    │       │ DirectMessage  │
│─────────────│       │─────────────────│       │────────────────│
│ Id (PK) ────│──────►│ MessageId (FK?) │       │ Id (PK) ───────│──┐
│ Body        │       │ DirectMsgId(FK?)│◄──────│ Body           │  │
│ ChannelId   │       │ Url             │       │ DmChannelId    │  │
│ CreatedAt   │       │ Title           │       │ CreatedAt      │  │
└─────────────┘       │ Description     │       └────────────────┘  │
                      │ ImageUrl        │                           │
                      │ SiteName        │                           │
                      │ CanonicalUrl    │                           │
                      │ FetchedAt       │                           │
                      │ Status          │                           │
                      └─────────────────┘                           │
                              │                                     │
                              └─────────────────────────────────────┘
```

### Indexes

```
IX_LinkPreview_MessageId          — fast retrieval of previews for a server message
IX_LinkPreview_DirectMessageId    — fast retrieval of previews for a DM
```

### Relationships

```csharp
// In CodecDbContext.OnModelCreating
modelBuilder.Entity<LinkPreview>(entity =>
{
    entity.HasOne<Message>()
        .WithMany(m => m.LinkPreviews)
        .HasForeignKey(lp => lp.MessageId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne<DirectMessage>()
        .WithMany(dm => dm.LinkPreviews)
        .HasForeignKey(lp => lp.DirectMessageId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasIndex(lp => lp.MessageId);
    entity.HasIndex(lp => lp.DirectMessageId);
});
```

## API Changes

### Updated Message Responses

All endpoints that return messages will include a `linkPreviews` array:

**Server channel message:**
```json
{
  "id": "guid",
  "authorName": "Alice",
  "body": "Check this out https://example.com",
  "createdAt": "2026-02-12T10:00:00Z",
  "channelId": "guid",
  "reactions": [],
  "linkPreviews": [
    {
      "url": "https://example.com",
      "title": "Example Domain",
      "description": "This domain is for use in illustrative examples.",
      "imageUrl": "https://example.com/og-image.png",
      "siteName": "Example",
      "canonicalUrl": "https://example.com"
    }
  ]
}
```

**DM message:**
```json
{
  "id": "guid",
  "dmChannelId": "guid",
  "authorUserId": "guid",
  "authorName": "Alice",
  "body": "Look at this https://example.com",
  "createdAt": "2026-02-12T10:00:00Z",
  "linkPreviews": [
    {
      "url": "https://example.com",
      "title": "Example Domain",
      "description": "This domain is for use in illustrative examples.",
      "imageUrl": null,
      "siteName": null,
      "canonicalUrl": null
    }
  ]
}
```

### New Internal Service: `LinkPreviewService`

A background service that handles URL extraction and metadata fetching. This is **not** a new API endpoint — it is triggered internally after a message is posted.

#### Flow

```
User posts message
       │
       ▼
POST /channels/{id}/messages  (or POST /dm/channels/{id}/messages)
       │
       ├──► 1. Persist message to DB
       ├──► 2. Broadcast message via SignalR (with empty linkPreviews)
       └──► 3. Queue background link preview fetch
                   │
                   ▼
            LinkPreviewService
                   │
                   ├──► Extract URLs from message body (regex, max 5)
                   ├──► For each URL:
                   │       ├──► Validate URL (HTTPS/HTTP, not private IP)
                   │       ├──► HTTP GET with timeout (5s) and size limit (512 KB)
                   │       ├──► Parse <head> for OG tags and meta fallbacks
                   │       └──► Store LinkPreview entity (Status = Success/Failed)
                   │
                   └──► Broadcast LinkPreviewsReady event via SignalR
```

### New SignalR Events

| Event | Payload | Delivered To |
|-------|---------|-------------|
| `LinkPreviewsReady` | `{ messageId, channelId?, dmChannelId?, linkPreviews: [...] }` | Channel group or DM channel group |

This event is sent after the background fetch completes, allowing the frontend to update the message with preview data. Only previews with `Status = Success` are included in the payload.

## Service Implementation

### URL Extraction

Extract URLs from the message body using a regex pattern. Only `http://` and `https://` URLs are considered.

```csharp
private static readonly Regex UrlRegex = new(
    @"https?://[^\s<>\""')\]]+",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

public static IReadOnlyList<string> ExtractUrls(string body, int maxUrls = 5)
{
    return UrlRegex.Matches(body)
        .Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?'))
        .Distinct()
        .Take(maxUrls)
        .ToList();
}
```

### Metadata Fetching

```csharp
public class LinkPreviewService
{
    private readonly HttpClient _httpClient;
    private const int TimeoutSeconds = 5;
    private const int MaxResponseBytes = 512 * 1024; // 512 KB

    public async Task<LinkPreviewResult?> FetchMetadataAsync(string url)
    {
        // 1. Validate URL (scheme, host not private IP)
        if (!IsAllowedUrl(url)) return null;

        // 2. HTTP GET with timeout and size limit
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "CodecBot/1.0 (+https://codec.chat)");
        request.Headers.Add("Accept", "text/html");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        if (!response.IsSuccessStatusCode) return null;

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.StartsWith("text/html")) return null;

        // 3. Read limited body
        var body = await ReadLimitedAsync(response.Content, MaxResponseBytes, cts.Token);

        // 4. Parse OG tags and meta fallbacks
        return ParseMetadata(body, url);
    }
}
```

### SSRF Protection

All outbound requests must be validated to prevent Server-Side Request Forgery:

```csharp
private static bool IsAllowedUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    // Only HTTP and HTTPS
    if (uri.Scheme is not ("http" or "https"))
        return false;

    // Block private/internal network ranges
    if (IPAddress.TryParse(uri.Host, out var ip))
    {
        if (ip.IsLoopback() || ip.IsPrivate() || ip.IsLinkLocal())
            return false;
    }

    // Block well-known internal hostnames
    var host = uri.Host.ToLowerInvariant();
    if (host is "localhost" or "metadata.google.internal"
        || host.EndsWith(".local")
        || host.EndsWith(".internal"))
        return false;

    return true;
}
```

**Additional safeguards:**
- DNS resolution is checked against private IP ranges before connecting (using a custom `SocketsHttpHandler` with `ConnectCallback`)
- Redirects are limited to **3 hops** maximum
- Response body is capped at **512 KB** to prevent memory exhaustion
- Fetch timeout is **5 seconds** per URL
- A dedicated `HttpClient` with `PooledConnectionLifetime` is used (no cookie jar, no credential sharing)

### Metadata Parsing

Parse HTML `<head>` to extract Open Graph tags with fallbacks:

| Priority | Source | Maps To |
|----------|--------|---------|
| 1 | `<meta property="og:title">` | `Title` |
| 2 | `<title>` | `Title` (fallback) |
| 1 | `<meta property="og:description">` | `Description` |
| 2 | `<meta name="description">` | `Description` (fallback) |
| 1 | `<meta property="og:image">` | `ImageUrl` |
| 1 | `<meta property="og:site_name">` | `SiteName` |
| 1 | `<meta property="og:url">` | `CanonicalUrl` |

Parsing should use a lightweight HTML parser (e.g., `AngleSharp` or regex for just `<meta>` / `<title>` tags within `<head>`) — full DOM parsing of the body is unnecessary.

### Caching (Future Enhancement)

For frequently shared URLs, a short-lived cache (e.g., 1 hour in-memory or distributed cache) can prevent redundant fetches. Deferred from the initial implementation.

## Frontend Changes

### Updated Types

```typescript
/** Link preview metadata for a URL in a message. */
export type LinkPreview = {
    url: string;
    title: string | null;
    description: string | null;
    imageUrl: string | null;
    siteName: string | null;
    canonicalUrl: string | null;
};

/** Chat message in a channel (updated). */
export type Message = {
    id: string;
    authorName: string;
    body: string;
    createdAt: string;
    channelId: string;
    authorUserId?: string | null;
    authorAvatarUrl?: string | null;
    reactions: Reaction[];
    linkPreviews: LinkPreview[];
};

/** Direct message in a DM conversation (updated). */
export type DirectMessage = {
    id: string;
    dmChannelId: string;
    authorUserId: string;
    authorName: string;
    body: string;
    createdAt: string;
    authorAvatarUrl?: string | null;
    linkPreviews: LinkPreview[];
};
```

### New SignalR Event Handler

```typescript
// In ChatHubService
onLinkPreviewsReady(callback: (data: {
    messageId: string;
    channelId?: string;
    dmChannelId?: string;
    linkPreviews: LinkPreview[];
}) => void): void;
```

### New Component: `LinkPreviewCard.svelte`

A reusable component rendered below the message body for each successful link preview.

**Props:**
```typescript
type LinkPreviewCardProps = {
    preview: LinkPreview;
};
```

**Rendering logic:**
- If `title` is null, do not render the card (graceful no-op)
- Title is rendered as a clickable link (opens `canonicalUrl ?? url` in a new tab with `rel="noopener noreferrer"`)
- Description is truncated to 300 characters with ellipsis
- Thumbnail image (`imageUrl`) is rendered on the right side of the card (max 80×80px, object-fit cover)
- Site name is rendered above the title in `--text-muted`
- The entire card has a left accent border (`--accent`, 3px)

### Component Integration

`LinkPreviewCard` is rendered in both `MessageItem.svelte` and `DmChatArea.svelte`:

```svelte
<!-- Inside MessageItem.svelte, below the message body -->
{#if message.linkPreviews?.length}
    <div class="link-previews">
        {#each message.linkPreviews as preview}
            <LinkPreviewCard {preview} />
        {/each}
    </div>
{/if}
```

### State Management

In `AppState`:
- `onLinkPreviewsReady` callback updates the matching message's `linkPreviews` array in-place
- Messages initially arrive with `linkPreviews: []` and are patched when the `LinkPreviewsReady` event fires
- No separate state is needed — previews are stored directly on the message objects

### Message Body URL Rendering

URLs in the message body text should be rendered as clickable hyperlinks using a simple text-to-link transformation:

```typescript
// In a utility function or within MessageItem
function linkifyText(body: string): string {
    return body.replace(
        /https?:\/\/[^\s<>"')\]]+/gi,
        (url) => `<a href="${url}" target="_blank" rel="noopener noreferrer">${url}</a>`
    );
}
```

**Security note:** The `linkifyText` function must sanitize the URL to prevent XSS. Only `http://` and `https://` URLs are matched by the regex above, and the URL is used as-is (no user-supplied HTML). The message body should already be escaped before `linkifyText` is applied, so the only injected HTML is the `<a>` wrapper around verified URLs.

## UI Design

### Link Preview Card Layout

```
┌──────────────────────────────────────────────────────────────┐
│ ┌─accent-border (3px, --accent)                              │
│ │                                                            │
│ │  example.com                          ┌──────────────┐     │
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
| Site name | 12px, `--text-muted`, uppercase |
| Title | 15px, 600 weight, `--accent` color, hover underline, clickable |
| Description | 13px, `--text-normal`, max 3 lines with `-webkit-line-clamp` |
| Thumbnail | 80×80px, 4px border-radius, `object-fit: cover`, right-aligned |
| Card spacing | 8px margin-top below message body, 4px gap between multiple preview cards |
| Loading state | Subtle shimmer/pulse animation on a placeholder card while `LinkPreviewsReady` is pending (optional, can show nothing until ready) |

### Responsive Behavior

| Breakpoint | Behavior |
|-----------|----------|
| ≥ 600px | Side-by-side layout: text left, thumbnail right |
| < 600px | Stacked layout: thumbnail above text (full width, max-height 160px) |

## Security Considerations

### SSRF Prevention (Critical)
- **Block private IP ranges:** 127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, fc00::/7, ::1
- **Block internal hostnames:** `localhost`, `*.local`, `*.internal`, `metadata.google.internal`
- **DNS rebinding protection:** Resolve DNS before connecting and validate the resolved IP against the blocklist using a custom `SocketsHttpHandler.ConnectCallback`
- **Redirect limiting:** Maximum 3 redirects; revalidate each redirect target against the blocklist

### Resource Protection
- **Response size limit:** 512 KB max — prevents memory exhaustion from large pages
- **Timeout:** 5 seconds per URL — prevents slow-loris attacks
- **Max URLs per message:** 5 — prevents abuse via messages with many URLs
- **No credential forwarding:** The `HttpClient` does not send cookies or authentication headers
- **User-Agent identification:** Requests include `CodecBot/1.0` user-agent for transparency

### XSS Prevention
- All preview metadata (title, description, site name) is treated as untrusted text
- The frontend renders metadata using `.textContent` or Svelte's default text escaping (no `{@html}` for metadata)
- `imageUrl` is rendered via `<img src>` — only `https://` URLs are allowed (strip `http://` images or proxy them)
- Link `href` values are validated to be `https://` or `http://` before rendering (no `javascript:` URIs)
- The `linkifyText` utility only wraps regex-matched `https?://` URLs — it does not parse arbitrary HTML

### Content Policy
- Only `text/html` responses are parsed for metadata (skip PDFs, images, etc.)
- Image URLs from `og:image` are validated to use `https://` scheme
- No JavaScript execution — metadata is extracted from static HTML only

## Acceptance Criteria

### AC-1: URL Detection
- [ ] URLs in message bodies (`http://` and `https://`) are detected after the message is posted
- [ ] A maximum of 5 URLs per message are processed
- [ ] Non-URL text is not affected

### AC-2: Metadata Fetching
- [ ] Open Graph tags (`og:title`, `og:description`, `og:image`, `og:site_name`, `og:url`) are extracted
- [ ] HTML `<title>` and `<meta name="description">` are used as fallbacks when OG tags are missing
- [ ] Fetching times out after 5 seconds per URL
- [ ] Response bodies larger than 512 KB are truncated before parsing
- [ ] Only `text/html` responses are parsed

### AC-3: SSRF Protection
- [ ] URLs pointing to private IP ranges are blocked (127.x, 10.x, 172.16–31.x, 192.168.x, link-local)
- [ ] URLs pointing to `localhost`, `*.local`, and `*.internal` are blocked
- [ ] DNS resolution is validated against private IP ranges before connecting
- [ ] Redirects are limited to 3 hops and each hop is revalidated

### AC-4: Link Preview Storage
- [ ] Link preview entities are persisted in the database with appropriate foreign keys
- [ ] Previews with `Status = Success` include title (at minimum) and optional description, image, site name
- [ ] Previews with `Status = Failed` are stored but not returned to clients

### AC-5: Real-time Delivery
- [ ] Messages are delivered immediately via SignalR with `linkPreviews: []`
- [ ] After metadata is fetched, `LinkPreviewsReady` event is broadcast to the appropriate group
- [ ] The frontend patches the message's `linkPreviews` array when the event is received
- [ ] Preview cards appear without requiring a page refresh

### AC-6: Link Preview Card UI
- [ ] Preview cards render below the message body with a left accent border
- [ ] Title is a clickable link that opens in a new tab (`target="_blank"`, `rel="noopener noreferrer"`)
- [ ] Description is truncated to 300 characters
- [ ] Thumbnail image is displayed when `imageUrl` is present (80×80px)
- [ ] Site name is displayed above the title
- [ ] Cards are responsive (side-by-side ≥ 600px, stacked < 600px)

### AC-7: Message Body URLs
- [ ] URLs in the message body are rendered as clickable hyperlinks
- [ ] Links open in a new tab with `rel="noopener noreferrer"`
- [ ] Non-URL text is properly escaped (no XSS)

### AC-8: Graceful Degradation
- [ ] If metadata fetch fails, the URL is still clickable in the message body (no broken embed card)
- [ ] If a site returns no OG tags and no `<title>`, no preview card is rendered
- [ ] Network errors and timeouts are handled gracefully without affecting message delivery

### AC-9: DM Link Previews
- [ ] Link previews work in DM conversations using the same flow
- [ ] `LinkPreviewsReady` events are delivered to the DM channel group
- [ ] DM messages include `linkPreviews` in their response and SignalR payload

### AC-10: Database Integrity
- [ ] Each `LinkPreview` references exactly one of `MessageId` or `DirectMessageId`
- [ ] Deleting a message cascades to delete its link previews
- [ ] Appropriate indexes exist for efficient queries

## Dependencies

- **Prerequisite:** Existing message posting flow (server channels and DMs)
- **New NuGet package:** `AngleSharp` (lightweight HTML parser) — or use regex-based parsing for `<meta>` tags within `<head>` to avoid the dependency
- **Reuses:** Existing SignalR infrastructure, `ChatHubService`, message state management
- **Related:** Rich text / markdown rendering (future) — link previews complement but do not depend on markdown support

## Migration Plan

A single EF Core migration (`AddLinkPreviews`) will:
1. Create the `LinkPreviews` table with all columns
2. Add foreign keys to `Messages` and `DirectMessages` (both nullable, cascade delete)
3. Add check constraint ensuring exactly one parent reference is non-null
4. Add indexes on `MessageId` and `DirectMessageId`

## Task Breakdown

### API — Data model & migration
- [ ] Create `LinkPreview` entity and `LinkPreviewStatus` enum in `Models/`
- [ ] Add `LinkPreviews` navigation property to `Message` and `DirectMessage` entities
- [ ] Add `LinkPreviews` DbSet to `CodecDbContext` and configure relationships, indexes, and check constraint
- [ ] Create and apply EF Core migration (`AddLinkPreviews`)

### API — LinkPreviewService
- [ ] Create `Services/LinkPreviewService.cs` with URL extraction, SSRF validation, HTTP fetching, and HTML parsing
- [ ] Register `HttpClient` with `SocketsHttpHandler` (DNS rebinding protection, redirect limits, no cookies)
- [ ] Implement Open Graph + meta tag parsing with fallback chain
- [ ] Add unit tests for URL extraction, SSRF validation, and metadata parsing

### API — Integration with message posting
- [ ] After persisting a message, queue link preview fetching (e.g., `Task.Run` with fire-and-forget for MVP, or `IHostedService` / `Channel<T>` for production)
- [ ] After fetching completes, persist `LinkPreview` entities and broadcast `LinkPreviewsReady` via SignalR
- [ ] Include `linkPreviews` in `GET /channels/{channelId}/messages` and `GET /dm/channels/{channelId}/messages` responses
- [ ] Include `linkPreviews: []` in the initial `ReceiveMessage` and `ReceiveDm` SignalR payloads

### Web — Types & API client
- [ ] Add `LinkPreview` type to `models.ts`
- [ ] Add `linkPreviews: LinkPreview[]` field to `Message` and `DirectMessage` types
- [ ] Ensure API client methods for fetching messages include `linkPreviews` in the response mapping

### Web — SignalR & state
- [ ] Add `LinkPreviewsReady` event handler to `ChatHubService`
- [ ] Add `onLinkPreviewsReady` callback in `AppState.startSignalR()` to patch messages in-place
- [ ] Ensure newly received messages initialize with `linkPreviews: []`

### Web — UI components
- [ ] Create `LinkPreviewCard.svelte` component (accent border, title link, description, thumbnail, site name)
- [ ] Integrate `LinkPreviewCard` into `MessageItem.svelte` (below message body, above reactions)
- [ ] Integrate link previews into DM message rendering (`DmChatArea.svelte`)
- [ ] Add `linkifyText` utility to render URLs in message bodies as clickable hyperlinks
- [ ] Add responsive styles for preview cards (side-by-side → stacked)

### Documentation
- [ ] Update `ARCHITECTURE.md` with LinkPreview entity, SignalR events, and service description
- [ ] Update `DATA.md` with LinkPreview schema, indexes, and entity definition
- [ ] Update `FEATURES.md` to track Link Previews feature progress
- [ ] Update `DESIGN.md` with Link Preview Card UI specification
- [ ] Update `PLAN.md` with Link Previews task breakdown

## Open Questions

1. **Caching:** Should fetched metadata be cached to avoid re-fetching the same URL across messages? (Recommendation: defer from initial implementation. Add an in-memory or distributed cache in a follow-up when usage patterns are clear.)
2. **Image proxying:** Should `og:image` URLs be proxied through the Codec API to prevent mixed-content issues and user IP leakage? (Recommendation: defer. Initially render `https://` images directly. Add a proxy endpoint in a follow-up for privacy and reliability.)
3. **Video embeds:** Should YouTube/Vimeo links get special treatment with inline video players? (Recommendation: defer. Standard OG metadata renders a title/description/thumbnail card. Inline video can come later as a progressive enhancement.)
4. **Rate limiting:** Should link preview fetching be rate-limited per user to prevent abuse? (Recommendation: yes, but defer the implementation. The 5-URL-per-message cap and 5-second timeout provide baseline protection. Per-user rate limiting can be added when needed.)
5. **Background processing:** Should link preview fetching use a proper background queue (`IHostedService` with `Channel<T>`) or simple fire-and-forget (`Task.Run`)? (Recommendation: start with fire-and-forget for MVP simplicity. Migrate to `Channel<T>` or a hosted service when scaling requires it.)
6. **AngleSharp dependency:** Should we add `AngleSharp` for robust HTML parsing, or use regex-based extraction for `<meta>` and `<title>` tags? (Recommendation: use regex for MVP since we only need tags within `<head>`. Consider `AngleSharp` if parsing needs grow.)
