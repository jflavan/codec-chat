# Roles

Codec uses a three-tier role system at the server level, plus a platform-wide Global Admin role.

## Permission Matrix

| Capability | Member | Admin | Owner | Global Admin |
|---|:---:|:---:|:---:|:---:|
| Send messages | Yes | Yes | Yes | Yes |
| React to messages | Yes | Yes | Yes | Yes |
| Create channels | — | Yes | Yes | Yes |
| Edit/delete channels | — | Yes | Yes | Yes |
| Create/revoke invites | — | Yes | Yes | Yes |
| Kick members | — | Members only | All except self | Yes |
| Update server name | — | Yes | Yes | Yes |
| Upload/delete server icon | — | Yes | Yes | Yes |
| Delete server | — | — | Yes | Yes |

## Roles

### Member

The default role assigned when a user joins a server via an invite link. Members can send messages, react to messages, and read channel history.

### Admin

Admins have full management access to a server's channels and invites, and can kick Members. Admins cannot kick other Admins or the Owner. Only the server Owner can promote a member to Admin.

### Owner

The user who created the server. Owners have all Admin permissions plus the ability to delete the server and manage Admins. There is exactly one Owner per server.

### Global Admin

A platform-wide role set via the `GlobalAdmin:Email` configuration value. Global Admins can see and manage all servers regardless of membership, and can delete any server, channel, or message. This role is independent of server-level roles.
