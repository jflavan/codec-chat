# Data Layer

This document describes Codec's data persistence strategy, database schema, and migration approach.

## Technology Choice

**Decision:** Codec uses **SQLite** with **Entity Framework Core 9** for persistence.

### Rationale
- ✅ **Simple local setup** - No database server required for development
- ✅ **Zero configuration** - File-based database (codec-dev.db)
- ✅ **Fast iteration** - Quick schema changes during MVP phase
- ✅ **EF Core support** - Full ORM capabilities with migrations
- ✅ **Portable** - Easy to share development databases
- ✅ **Production ready** - Suitable for small-to-medium deployments

### Production Considerations
SQLite is suitable for initial production deployments with moderate traffic. For larger scale:
- **PostgreSQL** - Best for high concurrency and complex queries
- **Azure SQL** - For Azure-hosted deployments
- **MySQL/MariaDB** - Alternative open-source option

EF Core migrations make database provider switches straightforward.

## Database Files

| Environment | File | Purpose |
|------------|------|---------|
| Production | `codec.db` | Main production database |
| Development | `codec-dev.db` | Local development database |
| Testing | `:memory:` | In-memory for tests |

## Entity Model

### Database Schema

```
┌─────────────┐
│   User      │
│─────────────│
│ Id (PK)     │◄───┐
│ GoogleSub   │    │
│ DisplayName │    │
│ Nickname    │    │
│ Email       │    │
│ AvatarUrl   │    │
│ CreatedAt   │    │
└─────────────┘    │
                   │
┌─────────────┐    │     ┌──────────────┐
│   Server    │    │     │ ServerMember │
│─────────────│    │     │──────────────│
│ Id (PK)     │◄───┼─────┤ ServerId (FK)│
│ Name        │    │     │ UserId (FK)  │├──┐
│ CreatedAt   │    │     │ Role         │   │
└──────┬──────┘    │     │ JoinedAt     │   │
       │           │     └──────────────┘   │
       │           │                        │
       │           │                        │
       │           │     ┌─────────────┐    │
       │           │     │  Message    │    │
       │           │     │─────────────│    │
       │           │     │ Id (PK)     │    │
       │           │     │ ChannelId   │────┼──┐
       │           └─────┤ AuthorId    │    │  │
       │                 │ AuthorName  │    │  │
       │                 │ Body        │    │  │
       │                 │ CreatedAt   │    │  │
       │                 └──────┬──────┘    │  │
       │                        │           │  │
       │                 ┌──────┴──────┐    │  │
       │                 │  Reaction   │    │  │
       │                 │─────────────│    │  │
       │                 │ Id (PK)     │    │  │
       │                 │ MessageId   │────┘  │
       │                 │ UserId      │───────┼──┐
       │                 │ Emoji       │       │  │
       │                 │ CreatedAt   │       │  │
       │                 └─────────────┘       │  │
       │                                       │  │
       │                 ┌─────────────┐    │  │
       └─────────────────┤  Channel    │◄───┘  │
                         │─────────────│       │
                         │ Id (PK)     │       │
                         │ ServerId    │       │
                         │ Name        │       │
                         │ CreatedAt   │       │
                         └─────────────┘       │
                                               │
┌─────────────┐         ┌─────────────┐       │
│   User      │         │  Friendship │       │
│ (Requester) │         │─────────────│       │
│─────────────│         │ Id (PK)     │       │
│ Id ─────────│───────►│ RequesterId │       │
└─────────────┘         │ RecipientId │◄───────┘
                         │ Status      │
                         │ CreatedAt   │
                         │ UpdatedAt   │
                         └─────────────┘

┌──────────────┐       ┌───────────────┐
│ ServerInvite │       │   Server      │
│──────────────│       │               │
│ Id (PK)      │       │               │
│ ServerId (FK)│───────│               │
│ Code (unique)│       │               │
│ CreatedBy(FK)│──┐    │               │
│ ExpiresAt    │  │    └───────────────┘
│ MaxUses      │  │
│ UseCount     │  └──────► User
│ CreatedAt    │
└──────────────┘

┌─────────────┐       ┌─────────────────┐       ┌─────────────┐
│   User      │       │ DmChannelMember │       │  DmChannel  │
│─────────────│       │─────────────────│       │─────────────│
│ Id ─────────│──────►│ UserId (PK,FK)  │       │ Id ─────────│
│ DisplayName │       │ DmChannelId     │◄──────│ CreatedAt   │
│ Nickname    │       │ IsOpen          │       └──────┬──────┘
│ AvatarUrl   │       │ JoinedAt        │              │
└─────────────┘       │ JoinedAt        │              │
                      └─────────────────┘              │
                                                       │
                      ┌─────────────────┐              │
                      │ DirectMessage   │              │
                      │─────────────────│              │
                      │ Id (PK)         │              │
                      │ DmChannelId (FK)│──────────────┘
                      │ AuthorUserId(FK)│──────► User
                      │ AuthorName      │
                      │ Body            │
                      │ CreatedAt       │
                      └─────────────────┘

┌─────────────┐       ┌─────────────────┐       ┌────────────────┐
│   Message   │       │   LinkPreview   │       │ DirectMessage  │
│─────────────│       │─────────────────│       │────────────────│
│ Id ─────────│─────►│ MessageId (FK?) │       │ Id ───────────│──┐
│ Body        │       │ DirectMsgId(FK?)│◄──────│ Body           │  │
│ ChannelId   │       │ Url             │       │ DmChannelId    │  │
│ CreatedAt   │       │ Title           │       │ CreatedAt      │  │
└─────────────┘       │ Description     │       └────────────────┘  │
                      │ ImageUrl        │                          │
                      │ SiteName        │                          │
                      │ CanonicalUrl    │                          │
                      │ FetchedAt       │                          │
                      │ Status          │                          │
                      └─────────────────┘                          │
                              │                                    │
                              └────────────────────────────────────┘
```

### Entity Definitions

#### User
Represents an authenticated user in the system.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique user identifier |
| `GoogleSubject` | string (unique) | Google user ID for authentication |
| `DisplayName` | string | User's display name (from Google) |
| `Nickname` | string? (max 32) | User-chosen display name override. `null` = use Google display name |
| `Email` | string | User's email address |
| `AvatarUrl` | string? | Google profile picture URL |
| `CustomAvatarPath` | string? | Relative path to a user-uploaded avatar file |
| `CreatedAt` | DateTimeOffset | Account creation timestamp |
| `UpdatedAt` | DateTimeOffset | Last profile update timestamp |

**Relationships:**
- One-to-many with `ServerMember`
- One-to-many with `Message`
- One-to-many with `Friendship` (as Requester, via `SentFriendRequests`)
- One-to-many with `Friendship` (as Recipient, via `ReceivedFriendRequests`)
- One-to-many with `DmChannelMember` (DM conversations the user participates in)
- One-to-many with `DirectMessage` (DM messages authored by the user)
- One-to-many with `ServerInvite` (invites created by the user, via `CreatedInvites`)

**Notes:**
- `GoogleSubject` is the primary link to Google identity
- Auto-created on first sign-in
- Profile fields (DisplayName, Email, AvatarUrl) updated on each sign-in
- `Nickname` is user-chosen and persists across sign-ins; effective display name resolves as `Nickname ?? DisplayName`
- `AvatarUrl` is the Google profile picture (always updated on sign-in)
- `CustomAvatarPath` is `null` when using the default Google avatar; non-null when the user uploads a custom avatar
- The effective avatar URL uses the fallback chain: custom upload → Google profile picture

#### Server
Top-level organizational unit (equivalent to Discord servers).

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique server identifier |
| `Name` | string | Server display name |
| `CreatedAt` | DateTimeOffset | Server creation timestamp |

**Relationships:**
- One-to-many with `Channel`
- One-to-many with `ServerMember`
- One-to-many with `ServerInvite`

#### ServerMember
Join table linking users to servers with role information.

| Column | Type | Description |
|--------|------|-------------|
| `ServerId` | Guid (PK, FK) | Reference to Server |
| `UserId` | Guid (PK, FK) | Reference to User |
| `Role` | ServerRole (enum) | User's role in the server |
| `JoinedAt` | DateTimeOffset | When user joined |
| `CustomAvatarPath` | string? | Relative path to a server-specific avatar file |

**Composite Primary Key:** (`ServerId`, `UserId`)

**Relationships:**
- Many-to-one with `Server`
- Many-to-one with `User`

**ServerRole Enum:**
```csharp
public enum ServerRole
{
    Member = 0,  // Default role, basic permissions
    Admin = 1,   // Administrative permissions
    Owner = 2    // Full control, server creator
}
```

#### Channel
Text communication channel within a server.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique channel identifier |
| `ServerId` | Guid (FK) | Reference to parent Server |
| `Name` | string | Channel display name |
| `CreatedAt` | DateTimeOffset | Channel creation timestamp |

**Relationships:**
- Many-to-one with `Server`
- One-to-many with `Message`

#### Message
Individual chat message in a channel.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique message identifier |
| `ChannelId` | Guid (FK) | Reference to Channel |
| `AuthorUserId` | Guid? (FK) | Reference to User (nullable) |
| `AuthorName` | string | Display name snapshot |
| `Body` | string | Message content |
| `ImageUrl` | string? | URL of an uploaded image attachment (`null` if text-only) |
| `CreatedAt` | DateTimeOffset | Message timestamp |

**Relationships:**
- Many-to-one with `Channel`
- Many-to-one with `User` (optional)
- One-to-many with `Reaction`

**Notes:**
- `AuthorUserId` is nullable for system messages
- `AuthorName` is a snapshot (denormalized) for performance
- `Body` is plain text (future: rich text/markdown)
- A message may have `ImageUrl` only (no body text), body only, or both

#### Reaction
Emoji reaction on a message by a specific user.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique reaction identifier |
| `MessageId` | Guid (FK) | Reference to Message |
| `UserId` | Guid (FK) | Reference to User |
| `Emoji` | string | Emoji character (e.g. 👍) |
| `CreatedAt` | DateTimeOffset | Reaction timestamp |

**Unique Constraint:** (`MessageId`, `UserId`, `Emoji`) — one reaction per emoji per user per message

**Relationships:**
- Many-to-one with `Message` (cascade delete)
- Many-to-one with `User` (cascade delete)

#### Friendship
Relationship between two users (friend request or confirmed friendship).

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique friendship identifier |
| `RequesterId` | Guid (FK) | Reference to User who sent the request |
| `RecipientId` | Guid (FK) | Reference to User who received the request |
| `Status` | FriendshipStatus (enum) | Current state of the relationship |
| `CreatedAt` | DateTimeOffset | When the request was sent |
| `UpdatedAt` | DateTimeOffset | When the status last changed |

**Unique Constraint:** (`RequesterId`, `RecipientId`) — one friendship record per user pair

**Relationships:**
- Many-to-one with `User` (as Requester, restrict delete)
- Many-to-one with `User` (as Recipient, restrict delete)

**FriendshipStatus Enum:**
```csharp
public enum FriendshipStatus
{
    Pending = 0,    // Request sent, awaiting response
    Accepted = 1,   // Both users are friends
    Declined = 2    // Recipient declined the request
}
```

**Notes:**
- Before inserting, both `(RequesterId, RecipientId)` and `(RecipientId, RequesterId)` are checked to prevent duplicate relationships
- Self-friendship is prevented at the API level (`RequesterId` must differ from `RecipientId`)
- Declined requests retain the record (status changes to `Declined`); cancelled requests delete the record

#### ServerInvite
Invite code for joining a server. Owners and Admins can create and manage invite codes.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique invite identifier |
| `ServerId` | Guid (FK) | Reference to Server |
| `Code` | string (unique) | Short alphanumeric invite code (8 characters) |
| `CreatedByUserId` | Guid (FK) | Reference to User who created the invite |
| `ExpiresAt` | DateTimeOffset? | When the invite expires (`null` = never) |
| `MaxUses` | int? | Maximum number of uses (`null` or `0` = unlimited) |
| `UseCount` | int | How many times this invite has been used |
| `CreatedAt` | DateTimeOffset | Invite creation timestamp |

**Unique Constraint:** `Code` — each invite code must be globally unique

**Relationships:**
- Many-to-one with `Server`
- Many-to-one with `User` (as creator)

**Notes:**
- Codes are generated using `System.Security.Cryptography.RandomNumberGenerator` for cryptographic randomness
- Expired invites are filtered out when listing but not automatically deleted
- `UseCount` is incremented atomically when a user joins via the code

#### DmChannel
A private 1-on-1 conversation channel, not attached to any server.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique DM channel identifier |
| `CreatedAt` | DateTimeOffset | When the conversation was started |

**Relationships:**
- One-to-many with `DmChannelMember`
- One-to-many with `DirectMessage`

**Notes:**
- Each `DmChannel` has exactly two `DmChannelMember` entries (enforced at API level)
- Only one `DmChannel` may exist for a given pair of users (enforced by application logic)

#### DmChannelMember
Join table linking users to DM channels.

| Column | Type | Description |
|--------|------|-------------|
| `DmChannelId` | Guid (PK, FK) | Reference to DmChannel |
| `UserId` | Guid (PK, FK) | Reference to User |
| `IsOpen` | bool | Whether the conversation appears in the user's sidebar (default: `true`) |
| `JoinedAt` | DateTimeOffset | When the user was added to the conversation |

**Composite Primary Key:** (`DmChannelId`, `UserId`)

**Relationships:**
- Many-to-one with `DmChannel`
- Many-to-one with `User`

**Notes:**
- `IsOpen` controls visibility — closing a conversation sets this to `false` without deleting data
- Sending a new message re-opens the conversation for both participants

#### DirectMessage
Individual message within a DM conversation.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique message identifier |
| `DmChannelId` | Guid (FK) | Reference to DmChannel |
| `AuthorUserId` | Guid (FK) | Reference to User |
| `AuthorName` | string | Display name snapshot (denormalized) |
| `Body` | string | Message content (plain text) |
| `ImageUrl` | string? | URL of an uploaded image attachment (`null` if text-only) |
| `CreatedAt` | DateTimeOffset | Message timestamp |

**Relationships:**
- Many-to-one with `DmChannel`
- Many-to-one with `User`

**Notes:**
- `AuthorName` is a snapshot (denormalized) for performance, matching the server `Message` entity pattern
- Follows the same structure as server channel messages
- A message may have `ImageUrl` only (no body text), body only, or both

#### LinkPreview
URL metadata extracted from a message body via Open Graph and HTML meta tag parsing.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique link preview identifier |
| `MessageId` | Guid? (FK) | Reference to a server channel Message (nullable) |
| `DirectMessageId` | Guid? (FK) | Reference to a DirectMessage (nullable) |
| `Url` | string (2048 max) | The original URL found in the message body |
| `Title` | string? (512 max) | Page title from `og:title` or `<title>` |
| `Description` | string? (1024 max) | Page description from `og:description` or `<meta name="description">` |
| `ImageUrl` | string? (2048 max) | Thumbnail URL from `og:image` (HTTPS only) |
| `SiteName` | string? (256 max) | Site name from `og:site_name` |
| `CanonicalUrl` | string? (2048 max) | Canonical URL from `og:url` (click-through target if present) |
| `FetchedAt` | DateTimeOffset | When metadata was fetched |
| `Status` | LinkPreviewStatus (enum) | `Pending`, `Success`, `Failed` |

**Check Constraint:** Exactly one of `MessageId` or `DirectMessageId` must be non-null

**Relationships:**
- Many-to-one with `Message` (cascade delete)
- Many-to-one with `DirectMessage` (cascade delete)

**LinkPreviewStatus Enum:**
```csharp
public enum LinkPreviewStatus
{
    Pending = 0,   // Metadata fetch has not completed yet
    Success = 1,   // Metadata was successfully fetched
    Failed = 2     // Fetch failed (timeout, unreachable, no metadata)
}
```

**Notes:**
- Maximum 5 link previews per message (enforced at application level)
- Fetched asynchronously after message posting via `LinkPreviewService`
- Only previews with `Status = Success` are returned to clients
- SSRF-safe: URL validation + DNS rebinding check via `SocketsHttpHandler.ConnectCallback`

## Database Context

```csharp
public class CodecDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ServerMember> ServerMembers => Set<ServerMember>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<DmChannel> DmChannels => Set<DmChannel>();
    public DbSet<DmChannelMember> DmChannelMembers => Set<DmChannelMember>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<ServerInvite> ServerInvites => Set<ServerInvite>();
    public DbSet<LinkPreview> LinkPreviews => Set<LinkPreview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure composite key for ServerMember
        modelBuilder.Entity<ServerMember>()
            .HasKey(m => new { m.ServerId, m.UserId });

        // Configure relationships and indexes
        // ... (see actual implementation)

        // SQLite does not natively support DateTimeOffset ordering.
        // Store as ISO 8601 strings so ORDER BY works correctly.
        var dateTimeOffsetConverter = new DateTimeOffsetToStringConverter();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }
            }
        }
    }
}
```

> **SQLite DateTimeOffset Handling:** SQLite does not natively support `DateTimeOffset` in `ORDER BY` clauses. A `DateTimeOffsetToStringConverter` is applied to all `DateTimeOffset` properties, storing them as ISO 8601 strings. This ensures correct ordering and filtering without requiring raw SQL workarounds.

## Migrations

Entity Framework Core migrations track database schema changes over time.

### Creating Migrations

From `apps/api/Codec.Api/`:

```bash
# Install EF Core CLI tools (once)
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"

# Create a new migration
dotnet ef migrations add MigrationName

# Review the generated migration in Migrations/ folder
```

### Applying Migrations

**Development (Automatic):**
```csharp
// In Program.cs - auto-applies on startup
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
    db.Database.Migrate(); // Apply pending migrations
}
```

**Production (Manual):**
```bash
# Apply migrations explicitly
cd apps/api/Codec.Api
dotnet ef database update

# Or use SQL script for DBA review
dotnet ef migrations script > migration.sql
```

### Rollback Migrations

```bash
# Rollback to specific migration
dotnet ef database update PreviousMigrationName

# Remove last migration (if not applied)
dotnet ef migrations remove
```

### Migration Best Practices

✅ **Do:**
- Create descriptive migration names: `AddUserAvatarUrl`, `CreateServerMemberTable`
- Review generated migrations before applying
- Test migrations on a copy of production data
- Include both Up and Down methods
- Keep migrations small and focused

❌ **Don't:**
- Modify applied migrations (create new ones instead)
- Delete migration files from source control
- Skip testing migration rollbacks

## Seed Data

Development environment includes seed data for testing.

**Location:** `apps/api/Codec.Api/Data/SeedData.cs`

### Default Seeded Data

When database is empty, seeds:

**Users:**
- Avery (Owner)
- Morgan (Admin)
- Rae (Member)

**Server:**
- "Codec HQ"

**Channels:**
- #build-log
- #announcements

**Messages:**
- Initial welcome messages in each channel

**Memberships:**
- All three users joined to Codec HQ with appropriate roles

### Seed Data Execution

```csharp
// In Program.cs - runs after migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(db); // Only if database empty
}
```

**Note:** Seed data only runs if `db.Servers.AnyAsync()` returns false.

## Indexes and Performance

### Current Indexes

Primary keys are automatically indexed. Additional indexes:

```csharp
// User lookup by Google subject (frequent)
modelBuilder.Entity<User>()
    .HasIndex(u => u.GoogleSubject)
    .IsUnique();

// Server membership queries (frequent)
modelBuilder.Entity<ServerMember>()
    .HasIndex(m => m.UserId);

modelBuilder.Entity<ServerMember>()
    .HasIndex(m => m.ServerId);

// Channel messages (frequent range queries)
modelBuilder.Entity<Message>()
    .HasIndex(m => m.ChannelId);

// Reaction uniqueness (one reaction per emoji per user per message)
modelBuilder.Entity<Reaction>()
    .HasIndex(r => new { r.MessageId, r.UserId, r.Emoji })
    .IsUnique();

// Reaction lookup by user
modelBuilder.Entity<Reaction>()
    .HasIndex(r => r.UserId);

// Friendship: unique pair constraint (one relationship per user pair)
modelBuilder.Entity<Friendship>()
    .HasIndex(f => new { f.RequesterId, f.RecipientId })
    .IsUnique();

// Friendship: fast lookup by requester
modelBuilder.Entity<Friendship>()
    .HasIndex(f => f.RequesterId);

// Friendship: fast lookup by recipient
modelBuilder.Entity<Friendship>()
    .HasIndex(f => f.RecipientId);

// DmChannelMember: fast lookup of a user's DM conversations
modelBuilder.Entity<DmChannelMember>()
    .HasIndex(m => m.UserId);

// DmChannelMember: fast lookup of members in a DM channel
modelBuilder.Entity<DmChannelMember>()
    .HasIndex(m => m.DmChannelId);

// DirectMessage: fast retrieval of messages in a conversation
modelBuilder.Entity<DirectMessage>()
    .HasIndex(m => m.DmChannelId);

// DirectMessage: fast lookup of messages by author
modelBuilder.Entity<DirectMessage>()
    .HasIndex(m => m.AuthorUserId);

// ServerInvite: unique code for fast lookup when joining
modelBuilder.Entity<ServerInvite>()
    .HasIndex(i => i.Code)
    .IsUnique();

// ServerInvite: fast listing of invites for a server
modelBuilder.Entity<ServerInvite>()
    .HasIndex(i => i.ServerId);

// LinkPreview: fast retrieval of previews for a server message
modelBuilder.Entity<LinkPreview>()
    .HasIndex(lp => lp.MessageId);

// LinkPreview: fast retrieval of previews for a DM
modelBuilder.Entity<LinkPreview>()
    .HasIndex(lp => lp.DirectMessageId);
```

### Query Patterns

**Optimized queries use:**
- `AsNoTracking()` for read-only operations
- Projection to DTOs to avoid loading full entities
- Explicit `Include()` for related data

```csharp
// Good: Efficient projection
var messages = await db.Messages
    .AsNoTracking()
    .Where(m => m.ChannelId == channelId)
    .OrderBy(m => m.CreatedAt)
    .Select(m => new { m.Id, m.Body, m.AuthorName, m.CreatedAt })
    .ToListAsync();
```

## Future Schema Changes

### Near-term Additions
- ~~Link preview metadata~~ → ✅ implemented (`LinkPreview` entity)
- File attachments metadata (non-image files)
- User preferences/settings

### Long-term Additions
- Voice channel metadata
- Custom roles and permissions
- Audit logs
- Message search indexes (full-text)
- Analytics and metrics tables

## Backup and Recovery

### Development
```bash
# Backup
cp apps/api/Codec.Api/codec-dev.db codec-dev.backup.db

# Restore
cp codec-dev.backup.db apps/api/Codec.Api/codec-dev.db
```

### Production

**Automated backup strategy:**
- SQLite: Use Litestream for continuous replication
- PostgreSQL: Use pg_dump or cloud provider backups
- Azure SQL: Built-in automated backups

**Backup frequency:**
- Full backup: Daily
- Incremental: Hourly
- Retention: 30 days minimum

## Connection String Configuration

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=codec-dev.db"
  }
}
```

**SQLite options:**
```
Data Source=codec.db;Cache=Shared;Mode=ReadWriteCreate;
```

**Future PostgreSQL:**
```
Host=localhost;Database=codec;Username=codecuser;Password=***;
```

## References

- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [SQLite Documentation](https://www.sqlite.org/docs.html)
- [Migrations Overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [Query Performance](https://learn.microsoft.com/en-us/ef/core/performance/)
