# Admin Purge Channel Messages — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow global admins to bulk-delete all messages from a text channel via Server Settings UI.

**Architecture:** New `DELETE /channels/{channelId}/messages` API endpoint (global admin only) that bulk-deletes messages with `ExecuteDeleteAsync`. A `ChannelPurged` SignalR event notifies connected clients to clear their message list. Frontend adds a "Purge" button per channel in the existing Server Settings panel.

**Tech Stack:** ASP.NET Core 10 (C#), SvelteKit/Svelte 5 (TypeScript), SignalR, EF Core, PostgreSQL

---

### Task 1: API — Add PurgeChannelMessages endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs` (add new method after line 484)

**Step 1: Add the PurgeChannelMessages endpoint**

Insert after the existing `DeleteMessage` method (line 484):

```csharp
/// <summary>
/// Deletes all messages in a channel. Requires global admin.
/// Cascade-deletes associated reactions and link previews.
/// </summary>
[HttpDelete("{channelId:guid}/messages")]
public async Task<IActionResult> PurgeChannelMessages(Guid channelId)
{
    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
    if (channel is null)
    {
        return NotFound(new { error = "Channel not found." });
    }

    var appUser = await userService.GetOrCreateUserAsync(User);
    if (!appUser.IsGlobalAdmin)
    {
        return Forbid();
    }

    await db.LinkPreviews
        .Where(lp => lp.Message.ChannelId == channelId)
        .ExecuteDeleteAsync();

    await db.Reactions
        .Where(r => r.Message.ChannelId == channelId)
        .ExecuteDeleteAsync();

    await db.Messages
        .Where(m => m.ChannelId == channelId)
        .ExecuteDeleteAsync();

    await chatHub.Clients.Group(channelId.ToString()).SendAsync("ChannelPurged", new
    {
        ChannelId = channelId
    });

    return NoContent();
}
```

**Step 2: Verify the API builds**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ChannelsController.cs
git commit -m "feat(api): add PurgeChannelMessages endpoint for global admins"
```

---

### Task 2: Frontend — Add ChannelPurged event type and SignalR wiring

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts` (add event type after line 74, add callback after line 206, add registration after line 315)

**Step 1: Add the ChannelPurgedEvent type**

In `chat-hub.ts`, after the `MessageDeletedEvent` type (line 74), add:

```typescript
export type ChannelPurgedEvent = {
	channelId: string;
};
```

**Step 2: Add the callback to SignalRCallbacks**

In the `SignalRCallbacks` type, after `onChannelDeleted` (line 206), add:

```typescript
onChannelPurged?: (event: ChannelPurgedEvent) => void;
```

**Step 3: Register the handler in the start method**

After the `onChannelDeleted` registration block (line 315), add:

```typescript
if (callbacks.onChannelPurged) {
    connection.on('ChannelPurged', callbacks.onChannelPurged);
}
```

**Step 4: Commit**

```bash
git add apps/web/src/lib/services/chat-hub.ts
git commit -m "feat(web): add ChannelPurged SignalR event type and wiring"
```

---

### Task 3: Frontend — Add purgeChannel to API client

**Files:**
- Modify: `apps/web/src/lib/api/client.ts` (add method after deleteMessage at line 330)

**Step 1: Add purgeChannel method**

After `deleteMessage` (line 330), add:

```typescript
purgeChannel(token: string, channelId: string): Promise<void> {
    return this.requestVoid(
        `${this.baseUrl}/channels/${encodeURIComponent(channelId)}/messages`,
        { method: 'DELETE', headers: this.headers(token) }
    );
}
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat(web): add purgeChannel API client method"
```

---

### Task 4: Frontend — Add purgeChannel to AppState + SignalR handler

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`
  - Add `isPurgingChannel` state near line 58
  - Add `purgeChannel()` method near the `deleteMessage` method (line 1079)
  - Add `onChannelPurged` callback in SignalR handlers (near line 2094)

**Step 1: Add isPurgingChannel state**

Near the other message state declarations (around line 58), add:

```typescript
isPurgingChannel = $state(false);
```

**Step 2: Add purgeChannel method**

After the `deleteMessage` method (line 1079), add:

```typescript
async purgeChannel(channelId: string): Promise<void> {
    if (!this.idToken) return;
    this.isPurgingChannel = true;
    try {
        await this.api.purgeChannel(this.idToken, channelId);
        if (!this.hub.isConnected && channelId === this.selectedChannelId) {
            this.messages = [];
            this.hasMoreMessages = false;
        }
    } catch (e) {
        this.setError(e);
    } finally {
        this.isPurgingChannel = false;
    }
}
```

**Step 3: Add ChannelPurged SignalR handler**

In the SignalR callbacks object, after `onMessageDeleted` (line 2094), add:

```typescript
onChannelPurged: (event) => {
    if (event.channelId === this.selectedChannelId) {
        this.messages = [];
        this.hasMoreMessages = false;
    }
},
```

**Step 4: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat(web): add purgeChannel state method and ChannelPurged handler"
```

---

### Task 5: Frontend — Add Purge button to Server Settings UI

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerSettings.svelte`
  - Add `confirmPurgeChannelId` state (near line 11)
  - Add `handlePurgeChannel` function (near line 57)
  - Add Purge button in channel row (near line 243, before the delete button)

**Step 1: Add state for purge confirmation**

Near the other confirmation state (line 11), add:

```typescript
let confirmPurgeChannelId = $state<string | null>(null);
```

**Step 2: Add handler function**

After `handleDeleteChannel` (line 57), add:

```typescript
async function handlePurgeChannel(channelId: string) {
    await app.purgeChannel(channelId);
    confirmPurgeChannelId = null;
}
```

**Step 3: Add Purge button in channel display row**

In the `{#each app.channels as channel}` loop, inside `.channel-display`, add a Purge button before the existing Delete button (before line 244). Only visible when `app.isGlobalAdmin` and the channel type is text:

```svelte
{#if app.isGlobalAdmin && channel.type === 'text'}
    {#if confirmPurgeChannelId === channel.id}
        <span class="danger-warning-inline">Delete all messages?</span>
        <button
            type="button"
            class="btn-danger-sm"
            disabled={app.isPurgingChannel}
            onclick={() => handlePurgeChannel(channel.id)}
        >
            {app.isPurgingChannel ? 'Purging...' : 'Confirm'}
        </button>
        <button
            type="button"
            class="btn-secondary-sm"
            disabled={app.isPurgingChannel}
            onclick={() => (confirmPurgeChannelId = null)}
        >
            Cancel
        </button>
    {:else}
        <button
            type="button"
            class="btn-danger-sm"
            onclick={() => {
                confirmPurgeChannelId = channel.id;
                confirmDeleteChannelId = null;
            }}
        >
            Purge
        </button>
    {/if}
{/if}
```

**Step 4: Add CSS for the inline danger warning**

Add this CSS rule in the `<style>` block:

```css
.danger-warning-inline {
    color: var(--danger);
    font-size: 12px;
    white-space: nowrap;
}
```

**Step 5: Verify the frontend builds**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 6: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerSettings.svelte
git commit -m "feat(web): add Purge button for global admins in Server Settings"
```

---

### Task 6: Manual smoke test

**Step 1: Start the stack**

```bash
docker compose up -d postgres azurite
cd apps/api/Codec.Api && dotnet run &
cd apps/web && npm run dev &
```

**Step 2: Test the flow**

1. Log in as a global admin
2. Open a server with messages in a text channel
3. Open Server Settings
4. Click "Purge" on a text channel
5. Confirm the inline prompt
6. Verify messages are cleared from the chat area
7. Verify the channel still exists with no messages

**Step 3: Verify non-admin cannot see button**

1. Log in as a regular member
2. Open Server Settings (if they have access) — Purge button should not appear
