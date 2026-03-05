# Admin Purge Channel Messages — Design

## Summary

Allow global admins to delete all messages from a text channel while keeping the channel itself intact. Accessible from the existing Server Settings panel.

## API

**Endpoint:** `DELETE /channels/{channelId}/messages`

- **Auth:** Global admin only (`appUser.IsGlobalAdmin`). Returns 403 otherwise.
- **Behavior:** Bulk-deletes all messages in the channel. EF Core cascade removes associated reactions and link previews.
- **Implementation:** `db.Messages.Where(m => m.ChannelId == channelId).ExecuteDeleteAsync()`
- **Response:** `204 No Content`
- **SignalR:** Broadcasts `ChannelPurged` with `{ ChannelId }` to `channel-{channelId}` group.

## Frontend

### AppState

- `purgeChannel(channelId: string)` — calls the new API endpoint.
- `isPurgingChannel: boolean` — loading state.
- SignalR handler for `ChannelPurged` — clears `messages` array if the purged channel is selected.

### API Client

- `purgeChannel(token, channelId)` — `DELETE /channels/{channelId}/messages`

### UI (ServerSettings.svelte)

- "Purge" button (danger-sm style) per text channel, visible only when `app.isGlobalAdmin`.
- Inline confirm/cancel matching the existing delete-channel pattern.
- Confirmation text: "This will permanently delete all messages in this channel."

## Decisions

- Global admin only (not server Owner/Admin) per user requirement.
- Channel persists after purge — only messages removed.
- Single bulk SQL delete for performance (no per-message SignalR events).
- One `ChannelPurged` event replaces N `MessageDeleted` events.
