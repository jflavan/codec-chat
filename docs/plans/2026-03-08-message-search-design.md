# Message Search Design

## Scope

- Search within the current channel by default, with a toggle to search server-wide (all channels the user has access to)
- Separate search for DM channels (scoped to current DM conversation, or across all DMs)

## Search Backend

- PostgreSQL trigram search using `pg_trgm` extension with GIN indexes on `Message.Body` and `DirectMessage.Body`
- Case-insensitive substring matching via `ILIKE` backed by trigram index
- Results ordered by trigram similarity with `CreatedAt` as tiebreaker

## Filters

- **Text query** (required) — substring match against message body
- **Author** — filter by user ID
- **Date range** — `before` and `after` timestamps
- **Has** — `image` (ImageUrl not null), `link` (LinkPreviews count > 0)

## API Endpoints

### Server message search

```
GET /api/servers/{serverId}/search?q=&channelId=&authorId=&before=&after=&has=&page=&pageSize=
```

- `channelId` optional — omit for server-wide search
- Only returns messages from channels the user is a member of

### DM message search

```
GET /api/dm/search?q=&dmChannelId=&authorId=&before=&after=&has=&page=&pageSize=
```

- `dmChannelId` optional — omit to search across all DMs
- Only returns messages from DM conversations the user participates in

### Response shape

```json
{
  "results": [
    {
      "id": "guid",
      "authorName": "string",
      "authorAvatarUrl": "string?",
      "authorUserId": "guid?",
      "body": "string",
      "imageUrl": "string?",
      "createdAt": "datetime",
      "editedAt": "datetime?",
      "channelId": "guid",
      "channelName": "string",
      "reactions": [],
      "linkPreviews": [],
      "mentions": [],
      "replyContext": null
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 25
}
```

### Jump-to-message support

```
GET /api/channels/{channelId}/messages?around={messageId}&limit=50
GET /api/dm/{dmChannelId}/messages?around={messageId}&limit=50
```

Returns 25 messages before + target message + 25 after. Enables loading messages around an arbitrary point in history.

## UI

### Search bar
- Magnifying glass icon in the channel header area
- Expands to text input on click

### Search panel
- Slides in from the right side, overlaying or pushing the member list
- Contains search input, filter bar, and results list

### Filter bar
- Below search input
- Dropdowns/chips for: author, date range, has-filters (image, link)

### Results list
- Compact message cards: author avatar, name, timestamp, channel name (if server-wide), body with highlighted matching text
- Pagination at bottom: "Page 1 of N" with prev/next buttons
- Default page size: 25

### Click-to-jump behavior
- Clicking a result scrolls the main chat feed to that message
- Brief highlight animation (yellow fade) on the target message
- Search panel stays open

## Database Migration

1. Enable `pg_trgm` extension: `CREATE EXTENSION IF NOT EXISTS pg_trgm`
2. Add GIN trigram index on `Messages.Body`
3. Add GIN trigram index on `DirectMessages.Body`
