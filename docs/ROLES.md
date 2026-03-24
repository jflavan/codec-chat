# Roles & Permissions

Codec uses a custom role system with granular permissions. Each server has its own set of roles with configurable permission bitmasks. Three system roles (Owner, Admin, Member) are created by default and cannot be deleted.

## Custom Roles

Server administrators can create custom roles with any combination of permissions. Roles are ordered by position (lower position = higher rank). Members can only manage roles below their own position.

### Role Properties

| Property | Description |
|---|---|
| Name | Display name (unique per server, max 100 chars) |
| Color | Hex color for the role badge (e.g. `#f0b232`) |
| Position | Hierarchy position (0 = highest). Owner is always 0 |
| Permissions | Bitmask of granted permissions |
| Hoisted | Whether members with this role appear in a separate group in the member sidebar |
| Mentionable | Whether the role can be @mentioned |

## Permission Flags

| Permission | Flag | Description |
|---|---|---|
| View Channels | `1 << 0` | View text and voice channels |
| Manage Channels | `1 << 1` | Create, edit, delete channels |
| Manage Server | `1 << 2` | Edit server name, description, icon |
| Manage Roles | `1 << 3` | Create, edit, delete, assign roles |
| Manage Emojis | `1 << 4` | Upload, rename, delete custom emojis |
| View Audit Log | `1 << 5` | View the server audit log |
| Create Invites | `1 << 6` | Create invite links |
| Manage Invites | `1 << 7` | Revoke invites created by others |
| Kick Members | `1 << 10` | Remove members from the server |
| Ban Members | `1 << 11` | Ban members (reserved for future use) |
| Send Messages | `1 << 20` | Send messages in text channels |
| Embed Links | `1 << 21` | Embed links in messages |
| Attach Files | `1 << 22` | Upload images and files |
| Add Reactions | `1 << 23` | Add emoji reactions to messages |
| Mention Everyone | `1 << 24` | Use @here mentions |
| Manage Messages | `1 << 25` | Delete messages by other users |
| Pin Messages | `1 << 26` | Pin and unpin messages |
| Connect | `1 << 30` | Connect to voice channels |
| Speak | `1 << 31` | Speak in voice channels |
| Mute Members | `1 << 32` | Server-mute other users in voice |
| Deafen Members | `1 << 33` | Server-deafen other users in voice |
| Administrator | `1 << 40` | Grants all permissions. Bypasses all checks |

## Default System Roles

### Owner (Position 0)
The server creator. Has the **Administrator** permission which grants all permissions. Cannot be assigned to other members. There is exactly one Owner per server.

### Admin (Position 1)
Has all management permissions except Administrator. Cannot manage the Owner role or kick the Owner. Default color: `#f0b232`.

### Member (Position 2+)
The default role assigned when joining via invite. Has basic permissions: View Channels, Send Messages, Embed Links, Attach Files, Add Reactions, Create Invites, Connect, Speak.

## Global Admin

A platform-wide role set via the `GlobalAdmin:Email` configuration value. Global Admins bypass all server-level permission checks and can manage all servers regardless of membership. This role is independent of server-level roles.

## Role Hierarchy Rules

- Members can only manage roles with a higher position number (lower rank) than their own
- Members cannot assign roles equal to or higher than their own
- Members cannot grant permissions they don't have (unless they have Administrator)
- System roles (Owner, Admin, Member) cannot be deleted or renamed
- Deleting a custom role moves its members to the default Member role
