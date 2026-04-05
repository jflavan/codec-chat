# Account Deletion Design

## Overview

Allow users to permanently delete their accounts. Messages are anonymized (attributed to "Deleted User"), the user row is hard-deleted, and cascading rules clean up related data. Deletion is immediate with password re-authentication and a typed confirmation ("DELETE").

## API

### `DELETE /me` (UsersController)

**Request body:**

```json
{
  "password": "string",            // required for email/password users
  "googleCredential": "string",    // required for Google-only users (no password set)
  "confirmationText": "string"     // must be exactly "DELETE"
}
```

**Validation:**

- `confirmationText` must equal `"DELETE"` (case-sensitive). Return 400 if not.
- Email/password users: verify `password` against stored bcrypt hash. Return 401 if wrong.
- Google-only users (no password hash): validate `googleCredential` as a Google ID token against Google JWKS. Return 401 if invalid.
- GitHub/Discord OAuth users have passwords (OAuth linking requires an existing account), so they use the `password` field.
- If user owns any servers: return 400 with `{ "ownedServers": [{ "id", "name" }] }` listing servers that need ownership transferred first.

**Success:** 200 OK with empty body.

## Deletion Logic

A `DeleteAccountAsync(Guid userId)` method on `UserService`, executed in a single database transaction:

1. **Pre-check:** Query `ServerMember` for rows where user has the Owner role. If any exist, abort with the list of owned servers.

2. **Revoke sessions:** Delete all `RefreshToken` rows for the user.

3. **Force-disconnect SignalR:** Use `IHubContext<ChatHub>` to remove user from all groups and close active connections. Clean up `PresenceTracker` state.

4. **Clean up `Restrict` FK entities** (these would block the user row deletion):
   - Delete all `Friendship` rows where user is requester or recipient
   - Delete all `VoiceCall` rows where user is caller or recipient
   - Set `UploadedByUserId = null` on `CustomEmoji` rows
   - Set `CreatedByUserId = null` on `Webhook` rows
   - Set `CreatedByUserId = null` on `ServerInvite` rows
   - Delete `AdminAction` rows where user is actor
   - Set `CreatedByUserId = null` on `SystemAnnouncement` rows
   - Set `ReporterId = null` on `Report` rows filed by user

5. **Anonymize messages:**
   - Set `AuthorUserId = null` on all `Message` rows by this user
   - Set `AuthorUserId = null` on all `DirectMessage` rows by this user

6. **Delete user row.** EF cascade handles: `ServerMember`, `ServerMemberRole`, `Reaction`, `DmChannelMember`, `PresenceState`, `VoiceState`, `PushSubscription`, `ChannelNotificationOverride`, `BannedMember`, `RefreshToken`.

7. **Broadcast real-time events:** After commit, notify affected servers and DM channels that the user has been removed (so UI updates member lists and presence).

## Frontend

### Account Settings (`AccountSettings.svelte`)

Add a danger-styled "Delete Account" section at the bottom of the existing account settings page:

- Warning text: "This will permanently delete your account. Your messages will remain but show as 'Deleted User'."
- Red "Delete Account" button.

### Confirmation Modal

Clicking the button opens a modal with:

1. Detailed warning explaining consequences (messages anonymized, memberships removed, friendships removed, action is irreversible).
2. **Server ownership block:** If the user owns servers, show a list of those servers with a note to transfer ownership first. The delete button is not shown.
3. Password input (or Google re-auth button for Google-only users).
4. Text input: "Type DELETE to confirm."
5. "Delete My Account" button — disabled until password is provided and confirmation text matches "DELETE".

### Post-Deletion

- Clear auth state (tokens, user data) the same way sign-out does.
- Redirect to the login page.

### Error Handling

- Wrong password: inline error on the password field.
- Owns servers: display the list of servers needing ownership transfer.
- Generic failure: inline error message in the modal.

## Message Display

When `AuthorUserId` is null on a message, the frontend displays "Deleted User" as the author name with a default/ghost avatar. This applies to both server messages and DMs.

## Testing

### API Unit Tests (`Codec.Api.Tests`)

- Returns 400 if `confirmationText` is not "DELETE"
- Returns 400 if user owns a server (includes server list in response)
- Returns 401 if password is incorrect
- Successful deletion removes user row from database
- Successful deletion sets `AuthorUserId = null` on user's messages
- Successful deletion removes friendships, reactions, server memberships

### Frontend Tests (`apps/web`)

- Delete button disabled until password and "DELETE" are both provided
- Server ownership warning blocks the delete flow
- Successful deletion clears auth state and redirects to login

## Out of Scope

- Grace period / undo window (deletion is immediate)
- Admin-initiated account deletion (admin can already disable accounts)
- Batch data export before deletion
- Email notification of deletion
