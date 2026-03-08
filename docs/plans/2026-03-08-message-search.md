# Message Search Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add full-text message search with filters (author, date range, has:image/link), a right-side results panel with pagination, and jump-to-message from search results.

**Architecture:** PostgreSQL trigram search (`pg_trgm` + GIN indexes) on `Messages.Body` and `DirectMessages.Body`. Two new search endpoints (server and DM). A new "around" message-loading mode for jump-to-message. Frontend search panel slides in from the right, overlaying/pushing the member list.

**Tech Stack:** ASP.NET Core 10 (C# 14), EF Core, PostgreSQL 16 + pg_trgm, SvelteKit + Svelte 5 runes, TypeScript

---

### Task 1: Database Migration — Enable pg_trgm + GIN Indexes

**Files:**
- Create: `apps/api/Codec.Api/Migrations/20260308000000_AddTrigramSearchIndexes.cs`

**Step 1: Create the migration file**

Note: `dotnet ef` is unavailable in non-interactive shell. Write the migration manually following the existing pattern (see `20260306033006_AddCustomEmoji.cs`).

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable pg_trgm extension for trigram-based search
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // GIN trigram index on Messages.Body for fast ILIKE search
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Messages_Body_Trgm\" ON \"Messages\" USING gin (\"Body\" gin_trgm_ops);");

            // GIN trigram index on DirectMessages.Body for fast ILIKE search
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_DirectMessages_Body_Trgm\" ON \"DirectMessages\" USING gin (\"Body\" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DirectMessages_Body_Trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Messages_Body_Trgm\";");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
        }
    }
}
```

**Step 2: Verify migration applies**

Run: `cd apps/api/Codec.Api && dotnet run`

The API auto-migrates in development. Check logs for migration application. If `CONCURRENTLY` fails inside a transaction, remove that keyword — EF migrations run inside a transaction by default. In that case, use `migrationBuilder.Sql("...", suppressTransaction: true)` or drop `CONCURRENTLY`.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Migrations/20260308000000_AddTrigramSearchIndexes.cs
git commit -m "feat: add pg_trgm extension and GIN trigram indexes for message search"
```

---

### Task 2: Search Request Model

**Files:**
- Create: `apps/api/Codec.Api/Models/SearchMessagesRequest.cs`

**Step 1: Create the request DTO**

```csharp
namespace Codec.Api.Models;

public class SearchMessagesRequest
{
    public required string Q { get; init; }
    public Guid? ChannelId { get; init; }
    public Guid? AuthorId { get; init; }
    public DateTimeOffset? Before { get; init; }
    public DateTimeOffset? After { get; init; }
    public string? Has { get; init; } // "image", "link"
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
```

**Step 2: Commit**

```bash
git add apps/api/Codec.Api/Models/SearchMessagesRequest.cs
git commit -m "feat: add SearchMessagesRequest DTO"
```

---

### Task 3: Server Message Search Endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs`

**Context:** Follow the pattern in `ChannelsController.GetMessages` for response shape (batch-loading reactions, link previews, mentions, reply context). Follow `UsersController.SearchUsers` for search input validation.

**Step 1: Read existing files for reference**

Read these files to understand the exact patterns:
- `apps/api/Codec.Api/Controllers/ChannelsController.cs` (lines 28-212 for GetMessages)
- `apps/api/Codec.Api/Controllers/UsersController.cs` (lines 99-160 for SearchUsers)

**Step 2: Add the SearchMessages endpoint to ServersController**

Add a `[HttpGet("{serverId}/search")]` action. Key logic:

```csharp
[HttpGet("{serverId}/search")]
public async Task<IActionResult> SearchMessages(Guid serverId, [FromQuery] SearchMessagesRequest request)
{
    // 1. Validate: Q must be at least 2 chars
    if (string.IsNullOrWhiteSpace(request.Q) || request.Q.Trim().Length < 2)
        return BadRequest(new { error = "Search query must be at least 2 characters." });

    // 2. Clamp page/pageSize
    var page = Math.Max(1, request.Page);
    var pageSize = Math.Clamp(request.PageSize, 1, 50);

    // 3. Auth: get current user, ensure server member
    var appUser = await userService.GetOrCreateUserAsync(User);
    await userService.EnsureMemberAsync(serverId, appUser.Id);

    // 4. Get channel IDs the user can access in this server
    var accessibleChannelIds = await db.Channels
        .Where(c => c.ServerId == serverId)
        .Select(c => c.Id)
        .ToListAsync();

    // 5. If channelId filter specified, validate it's in accessible channels
    if (request.ChannelId.HasValue && !accessibleChannelIds.Contains(request.ChannelId.Value))
        return NotFound();

    var channelFilter = request.ChannelId.HasValue
        ? new List<Guid> { request.ChannelId.Value }
        : accessibleChannelIds;

    // 6. Build query
    var query = db.Messages
        .Where(m => channelFilter.Contains(m.ChannelId))
        .Where(m => EF.Functions.ILike(m.Body, $"%{request.Q}%"));

    if (request.AuthorId.HasValue)
        query = query.Where(m => m.AuthorUserId == request.AuthorId.Value);
    if (request.Before.HasValue)
        query = query.Where(m => m.CreatedAt < request.Before.Value);
    if (request.After.HasValue)
        query = query.Where(m => m.CreatedAt > request.After.Value);
    if (request.Has == "image")
        query = query.Where(m => m.ImageUrl != null);
    if (request.Has == "link")
        query = query.Where(m => db.LinkPreviews.Any(lp => lp.MessageId == m.Id));

    // 7. Get total count
    var totalCount = await query.CountAsync();

    // 8. Fetch page
    var messages = await query
        .OrderByDescending(m => m.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(m => new
        {
            m.Id, m.ChannelId, m.AuthorName, m.AuthorUserId,
            m.Body, m.ImageUrl, m.CreatedAt, m.EditedAt
        })
        .ToListAsync();

    // 9. Batch-load related data (reactions, link previews, mentions, reply context)
    //    Follow the EXACT pattern from ChannelsController.GetMessages
    //    Also load channel names for server-wide results
    var messageIds = messages.Select(m => m.Id).ToList();

    // ... (batch-load reactions, link previews, mentions, reply context, channel names, author avatars)
    // Mirror ChannelsController.GetMessages lines ~100-200 exactly

    // 10. Build response
    return Ok(new
    {
        totalCount,
        page,
        pageSize,
        results = messages.Select(m => new { /* enriched message DTO with channelName */ })
    });
}
```

Important: For the batch-loading of reactions, link previews, mentions, reply context, and author avatars — copy the exact pattern from `ChannelsController.GetMessages`. Also add `channelName` by joining against the Channels table.

**Step 3: Test manually**

Start the API, send a search request:
```bash
curl -H "Authorization: Bearer <token>" "http://localhost:5050/api/servers/{serverId}/search?q=hello"
```

Expected: 200 with `{ totalCount, page, pageSize, results: [...] }`

**Step 4: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat: add server message search endpoint with filters"
```

---

### Task 4: DM Message Search Endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/DmController.cs`

**Context:** Follow the same pattern as Task 3, but for DirectMessages. Authorization uses `EnsureDmParticipantAsync`. When `dmChannelId` is omitted, search all DM channels the user participates in.

**Step 1: Read DmController for reference**

Read `apps/api/Codec.Api/Controllers/DmController.cs` for the DM authorization and response patterns.

**Step 2: Add the SearchDmMessages endpoint**

Add `[HttpGet("search")]` to DmController:

```csharp
[HttpGet("search")]
public async Task<IActionResult> SearchMessages([FromQuery] SearchMessagesRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Q) || request.Q.Trim().Length < 2)
        return BadRequest(new { error = "Search query must be at least 2 characters." });

    var page = Math.Max(1, request.Page);
    var pageSize = Math.Clamp(request.PageSize, 1, 50);

    var appUser = await userService.GetOrCreateUserAsync(User);

    // Get DM channel IDs the user participates in
    var accessibleDmChannelIds = await db.DmChannelMembers
        .Where(m => m.UserId == appUser.Id)
        .Select(m => m.DmChannelId)
        .ToListAsync();

    if (request.ChannelId.HasValue)
    {
        if (!accessibleDmChannelIds.Contains(request.ChannelId.Value))
            return NotFound();
        accessibleDmChannelIds = [request.ChannelId.Value];
    }

    var query = db.DirectMessages
        .Where(m => accessibleDmChannelIds.Contains(m.DmChannelId))
        .Where(m => EF.Functions.ILike(m.Body, $"%{request.Q}%"));

    // Apply same filters as server search (AuthorId, Before, After, Has)
    // ... (same pattern as Task 3)

    var totalCount = await query.CountAsync();

    var messages = await query
        .OrderByDescending(m => m.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(m => new
        {
            m.Id, m.DmChannelId, m.AuthorName, m.AuthorUserId,
            m.Body, m.ImageUrl, m.CreatedAt, m.EditedAt, m.MessageType
        })
        .ToListAsync();

    // Batch-load reactions, link previews, reply context
    // Follow DmController.GetMessages pattern

    return Ok(new { totalCount, page, pageSize, results = /* enriched DTOs */ });
}
```

Note: Use `request.ChannelId` as the DM channel filter (reuse the same DTO; the field name is `ChannelId` but semantically it's `DmChannelId` here). Or add a `DmChannelId` property to the DTO if clarity is preferred.

**Step 3: Test manually**

```bash
curl -H "Authorization: Bearer <token>" "http://localhost:5050/api/dm/search?q=hello"
```

**Step 4: Commit**

```bash
git add apps/api/Codec.Api/Controllers/DmController.cs
git commit -m "feat: add DM message search endpoint with filters"
```

---

### Task 5: Around-Message Endpoint (Channels)

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs`

**Context:** The existing `GetMessages` action uses cursor-based pagination (`before` + `limit`). Add an `around` query parameter that loads messages centered on a target message ID.

**Step 1: Modify GetMessages to support `around` parameter**

Add an optional `Guid? around` parameter. When provided:

```csharp
// Inside GetMessages, after authorization:
if (around.HasValue)
{
    var targetMessage = await db.Messages
        .Where(m => m.ChannelId == channelId && m.Id == around.Value)
        .Select(m => new { m.Id, m.CreatedAt })
        .FirstOrDefaultAsync();

    if (targetMessage is null) return NotFound();

    var half = limit / 2;

    var beforeMessages = await db.Messages
        .Where(m => m.ChannelId == channelId && m.CreatedAt < targetMessage.CreatedAt)
        .OrderByDescending(m => m.CreatedAt)
        .Take(half)
        .Select(m => new { m.Id, m.ChannelId, m.AuthorName, m.AuthorUserId, m.Body, m.ImageUrl, m.CreatedAt, m.EditedAt })
        .ToListAsync();

    var afterMessages = await db.Messages
        .Where(m => m.ChannelId == channelId && m.CreatedAt > targetMessage.CreatedAt)
        .OrderBy(m => m.CreatedAt)
        .Take(half)
        .Select(m => new { m.Id, m.ChannelId, m.AuthorName, m.AuthorUserId, m.Body, m.ImageUrl, m.CreatedAt, m.EditedAt })
        .ToListAsync();

    var target = await db.Messages
        .Where(m => m.Id == around.Value)
        .Select(m => new { m.Id, m.ChannelId, m.AuthorName, m.AuthorUserId, m.Body, m.ImageUrl, m.CreatedAt, m.EditedAt })
        .FirstAsync();

    var allMessages = beforeMessages
        .Reverse<dynamic>()
        .Append(target)
        .Concat(afterMessages)
        .ToList();

    // Batch-load reactions, link previews, mentions, reply context for allMessages
    // (same pattern as existing GetMessages)

    return Ok(new
    {
        hasMoreBefore = beforeMessages.Count == half,
        hasMoreAfter = afterMessages.Count == half,
        messages = /* enriched DTOs */
    });
}
// ... existing cursor-based logic continues below
```

**Step 2: Test manually**

```bash
curl -H "Authorization: Bearer <token>" \
  "http://localhost:5050/api/channels/{channelId}/messages?around={messageId}&limit=50"
```

Expected: 200 with messages centered on the target, plus `hasMoreBefore`/`hasMoreAfter`.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ChannelsController.cs
git commit -m "feat: add around-message loading for jump-to-message"
```

---

### Task 6: Around-Message Endpoint (DMs)

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/DmController.cs`

**Step 1: Add `around` parameter to DM GetMessages**

Same pattern as Task 5, but for `DirectMessages` and `DmChannelId`. Follow the DM authorization pattern (`EnsureDmParticipantAsync`).

**Step 2: Test manually**

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/DmController.cs
git commit -m "feat: add around-message loading for DM jump-to-message"
```

---

### Task 7: Frontend Types

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`

**Step 1: Add search-related types**

```typescript
export type SearchResult = Message & {
  channelName?: string;
};

export type PaginatedSearchResults = {
  totalCount: number;
  page: number;
  pageSize: number;
  results: SearchResult[];
};

export type AroundMessages = {
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
  messages: Message[];
};

export type SearchFilters = {
  channelId?: string;
  authorId?: string;
  before?: string;
  after?: string;
  has?: 'image' | 'link';
  page?: number;
  pageSize?: number;
};
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/types/models.ts
git commit -m "feat: add search-related TypeScript types"
```

---

### Task 8: Frontend API Client Methods

**Files:**
- Modify: `apps/web/src/lib/api/client.ts`

**Step 1: Read the existing API client**

Read `apps/web/src/lib/api/client.ts` to understand the exact method pattern (how params are built, how requests are made).

**Step 2: Add search and around-message methods**

```typescript
async searchServerMessages(
  idToken: string,
  serverId: string,
  query: string,
  filters: SearchFilters = {}
): Promise<PaginatedSearchResults> {
  const params = new URLSearchParams({ q: query });
  if (filters.channelId) params.set('channelId', filters.channelId);
  if (filters.authorId) params.set('authorId', filters.authorId);
  if (filters.before) params.set('before', filters.before);
  if (filters.after) params.set('after', filters.after);
  if (filters.has) params.set('has', filters.has);
  if (filters.page) params.set('page', String(filters.page));
  if (filters.pageSize) params.set('pageSize', String(filters.pageSize));
  return this.request<PaginatedSearchResults>(
    idToken, `/api/servers/${serverId}/search?${params}`
  );
}

async searchDmMessages(
  idToken: string,
  query: string,
  filters: SearchFilters = {}
): Promise<PaginatedSearchResults> {
  const params = new URLSearchParams({ q: query });
  if (filters.channelId) params.set('dmChannelId', filters.channelId);
  if (filters.authorId) params.set('authorId', filters.authorId);
  if (filters.before) params.set('before', filters.before);
  if (filters.after) params.set('after', filters.after);
  if (filters.has) params.set('has', filters.has);
  if (filters.page) params.set('page', String(filters.page));
  if (filters.pageSize) params.set('pageSize', String(filters.pageSize));
  return this.request<PaginatedSearchResults>(idToken, `/api/dm/search?${params}`);
}

async getMessagesAround(
  idToken: string,
  channelId: string,
  messageId: string,
  limit: number = 50
): Promise<AroundMessages> {
  return this.request<AroundMessages>(
    idToken, `/api/channels/${channelId}/messages?around=${messageId}&limit=${limit}`
  );
}

async getDmMessagesAround(
  idToken: string,
  dmChannelId: string,
  messageId: string,
  limit: number = 50
): Promise<AroundMessages> {
  return this.request<AroundMessages>(
    idToken, `/api/dm/${dmChannelId}/messages?around=${messageId}&limit=${limit}`
  );
}
```

**Step 3: Add imports for new types at top of file**

Make sure `PaginatedSearchResults`, `AroundMessages`, `SearchFilters` are imported from `../types/models`.

**Step 4: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat: add search and around-message API client methods"
```

---

### Task 9: AppState — Search State and Actions

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Read the current AppState**

Read `apps/web/src/lib/state/app-state.svelte.ts` to understand existing state fields and method patterns.

**Step 2: Add search state fields**

```typescript
// Search state
isSearchOpen = $state(false);
searchQuery = $state('');
searchFilters = $state<SearchFilters>({});
searchResults = $state<PaginatedSearchResults | null>(null);
isSearching = $state(false);
highlightedMessageId = $state<string | null>(null);
```

**Step 3: Add search actions**

```typescript
toggleSearch(): void {
  this.isSearchOpen = !this.isSearchOpen;
  if (!this.isSearchOpen) {
    this.searchQuery = '';
    this.searchFilters = {};
    this.searchResults = null;
  }
}

async searchMessages(query: string, filters: SearchFilters = {}): Promise<void> {
  if (!this.idToken || query.trim().length < 2) {
    this.searchResults = null;
    return;
  }

  this.searchQuery = query;
  this.searchFilters = filters;
  this.isSearching = true;

  try {
    if (this.selectedServerId) {
      this.searchResults = await this.api.searchServerMessages(
        this.idToken,
        this.selectedServerId,
        query,
        { ...filters, channelId: filters.channelId ?? this.selectedChannelId ?? undefined }
      );
    } else if (this.selectedDmChannelId) {
      this.searchResults = await this.api.searchDmMessages(
        this.idToken,
        query,
        { ...filters, channelId: this.selectedDmChannelId }
      );
    }
  } catch (e) {
    this.setError(e);
  } finally {
    this.isSearching = false;
  }
}

async searchPage(page: number): Promise<void> {
  await this.searchMessages(this.searchQuery, { ...this.searchFilters, page });
}

async jumpToMessage(messageId: string, channelId: string): Promise<void> {
  if (!this.idToken) return;

  try {
    // Determine if this is a DM or channel message based on current context
    const isDm = !!this.selectedDmChannelId;

    const result = isDm
      ? await this.api.getDmMessagesAround(this.idToken, channelId, messageId)
      : await this.api.getMessagesAround(this.idToken, channelId, messageId);

    // If we need to switch channels first
    if (!isDm && this.selectedChannelId !== channelId) {
      await this.selectChannel(channelId);
    }

    // Replace messages with the around-window
    if (isDm) {
      this.dmMessages = result.messages;
    } else {
      this.messages = result.messages;
      this.hasMoreMessages = result.hasMoreBefore;
    }

    // Highlight the target message
    this.highlightedMessageId = messageId;
    setTimeout(() => { this.highlightedMessageId = null; }, 2000);
  } catch (e) {
    this.setError(e);
  }
}
```

**Step 4: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat: add search state and actions to AppState"
```

---

### Task 10: Search Bar in Channel Header

**Files:**
- Modify: `apps/web/src/lib/components/channel-header/ChannelHeader.svelte` (or equivalent header component)

**Step 1: Read the header component**

Read the channel header component to understand its layout and where to add the search button.

**Step 2: Add search toggle button**

Add a magnifying glass icon button to the header. On click, call `app.toggleSearch()`.

```svelte
<button
  class="search-btn"
  onclick={() => app.toggleSearch()}
  title="Search messages"
  aria-label="Search messages"
>
  <!-- SVG magnifying glass icon -->
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
    <circle cx="11" cy="11" r="8"/>
    <path d="m21 21-4.3-4.3"/>
  </svg>
</button>
```

Style it to match existing header buttons.

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/channel-header/ChannelHeader.svelte
git commit -m "feat: add search toggle button to channel header"
```

---

### Task 11: Search Panel Component

**Files:**
- Create: `apps/web/src/lib/components/search/SearchPanel.svelte`

**Step 1: Read ChatArea.svelte for layout context**

Read `apps/web/src/lib/components/chat/ChatArea.svelte` (or equivalent) to understand how the panel should integrate.

**Step 2: Create SearchPanel.svelte**

Build the right-side search panel with:
- Search text input at top (debounced, triggers search on typing)
- Filter bar below input (author dropdown, date pickers, has-filter chips)
- Results list (compact message cards with author, timestamp, channel name, highlighted body)
- Pagination at bottom ("Page N of M" with prev/next)
- Close button (X) in top-right corner

Key behaviors:
- Debounce search input (300ms) before firing API call
- Show loading spinner while `app.isSearching` is true
- Show "No results" when search completes with 0 results
- Show result count ("N results" or "Page 1 of M")
- Highlight matching text in result body using the search query

```svelte
<script lang="ts">
  import { getAppState } from '$lib/state/app-state.svelte';

  const app = getAppState();

  let searchInput = $state('');
  let debounceTimer: ReturnType<typeof setTimeout>;
  let searchScope = $state<'channel' | 'server'>('channel');

  function onSearchInput(value: string) {
    searchInput = value;
    clearTimeout(debounceTimer);
    if (value.trim().length < 2) {
      app.searchResults = null;
      return;
    }
    debounceTimer = setTimeout(() => {
      const filters = searchScope === 'server'
        ? { ...app.searchFilters, channelId: undefined }
        : app.searchFilters;
      app.searchMessages(value, filters);
    }, 300);
  }

  function jumpTo(messageId: string, channelId: string) {
    app.jumpToMessage(messageId, channelId);
  }
</script>
```

Layout: fixed-width panel (340px), full height, with the CRT/phosphor-green design tokens.

**Step 3: Integrate SearchPanel into ChatArea layout**

Modify ChatArea.svelte to render `<SearchPanel />` conditionally when `app.isSearchOpen` is true. Position it as a right-side panel (flex layout: chat area takes remaining space, search panel takes 340px).

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/search/SearchPanel.svelte
git add apps/web/src/lib/components/chat/ChatArea.svelte
git commit -m "feat: add search panel component with debounced input and results"
```

---

### Task 12: Search Result Item Component

**Files:**
- Create: `apps/web/src/lib/components/search/SearchResultItem.svelte`

**Step 1: Create SearchResultItem.svelte**

Compact message card showing:
- Author avatar (small, 24px) + author name + timestamp
- Channel name (if server-wide search, shown as chip/badge)
- Message body with search query highlighted (bold or background color)
- Clickable — calls `jumpTo(message.id, message.channelId)`

Highlight logic: split the body text by the search query (case-insensitive), wrap matches in `<mark>` tags.

```svelte
<script lang="ts">
  import type { SearchResult } from '$lib/types/models';

  let { result, query, onJump }: {
    result: SearchResult;
    query: string;
    onJump: (messageId: string, channelId: string) => void;
  } = $props();

  function highlightMatches(text: string, q: string): string {
    if (!q) return text;
    const escaped = q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return text.replace(new RegExp(`(${escaped})`, 'gi'), '<mark>$1</mark>');
  }
</script>

<button class="search-result" onclick={() => onJump(result.id, result.channelId)}>
  <div class="result-header">
    <span class="author">{result.authorName}</span>
    {#if result.channelName}
      <span class="channel-badge">#{result.channelName}</span>
    {/if}
    <time>{new Date(result.createdAt).toLocaleDateString()}</time>
  </div>
  <p class="result-body">{@html highlightMatches(result.body, query)}</p>
</button>
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/components/search/SearchResultItem.svelte
git commit -m "feat: add search result item component with text highlighting"
```

---

### Task 13: Search Filter Bar Component

**Files:**
- Create: `apps/web/src/lib/components/search/SearchFilterBar.svelte`

**Step 1: Create SearchFilterBar.svelte**

Filter controls:
- **Scope toggle**: "This Channel" / "Server" (or "This DM" / "All DMs")
- **Author**: text input that searches server members (reuse user search), shows dropdown
- **Date range**: "Before" and "After" date inputs (`<input type="date">`)
- **Has**: clickable chips for "Image" and "Link" (toggle on/off)

When any filter changes, call `app.searchMessages(app.searchQuery, updatedFilters)`.

**Step 2: Integrate into SearchPanel**

Render `<SearchFilterBar />` between the search input and results list in SearchPanel.svelte.

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/search/SearchFilterBar.svelte
git add apps/web/src/lib/components/search/SearchPanel.svelte
git commit -m "feat: add search filter bar with author, date, and has-filters"
```

---

### Task 14: Jump-to-Message Highlight in MessageFeed

**Files:**
- Modify: `apps/web/src/lib/components/chat/MessageFeed.svelte` (or equivalent)
- Modify: `apps/web/src/lib/components/chat/MessageItem.svelte` (or equivalent)

**Step 1: Read MessageFeed and MessageItem**

Read both components to understand how messages render and scroll.

**Step 2: Add scroll-to and highlight behavior**

In MessageFeed: when `app.highlightedMessageId` changes, scroll to that message element using `element.scrollIntoView({ behavior: 'smooth', block: 'center' })`.

```svelte
$effect(() => {
  if (app.highlightedMessageId) {
    const el = document.getElementById(`message-${app.highlightedMessageId}`);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }
});
```

In MessageItem: add the `id` attribute and conditional highlight class:

```svelte
<div
  id="message-{message.id}"
  class="message-item"
  class:highlighted={app.highlightedMessageId === message.id}
>
```

Add highlight animation CSS:

```css
.message-item.highlighted {
  animation: highlight-fade 2s ease-out;
}

@keyframes highlight-fade {
  0% { background-color: rgba(0, 255, 102, 0.15); }
  100% { background-color: transparent; }
}
```

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageFeed.svelte
git add apps/web/src/lib/components/chat/MessageItem.svelte
git commit -m "feat: add jump-to-message scroll and highlight animation"
```

---

### Task 15: Integration Testing and Polish

**Step 1: Test full flow end-to-end**

Run both API and web dev server:
```bash
cd apps/api/Codec.Api && dotnet run &
cd apps/web && npm run dev &
```

Test these scenarios:
1. Click search icon in header — panel opens
2. Type query (2+ chars) — results appear after debounce
3. Toggle "This Channel" / "Server" scope — results update
4. Apply author filter — results narrow
5. Apply date range — results narrow
6. Apply "has: image" filter — only image messages shown
7. Click a result — chat scrolls to message, highlight fades
8. Click a result from a different channel (server-wide) — channel switches, then scrolls
9. Pagination — prev/next works, page count is correct
10. Close panel — state resets
11. Search in DM context — DM search endpoint is used

**Step 2: Run svelte-check**

```bash
cd apps/web && npm run check
```

Fix any TypeScript or Svelte errors.

**Step 3: Run API build**

```bash
cd apps/api/Codec.Api && dotnet build
```

Fix any C# compilation errors.

**Step 4: Final commit**

```bash
git add -A
git commit -m "fix: polish search feature and fix integration issues"
```
