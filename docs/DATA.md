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
       │                 └─────────────┘    │  │
       │                                    │  │
       │                 ┌─────────────┐    │  │
       └─────────────────┤  Channel    │◄───┘  │
                         │─────────────│       │
                         │ Id (PK)     │       │
                         │ ServerId    │       │
                         │ Name        │       │
                         │ CreatedAt   │       │
                         └─────────────┘       │
```

### Entity Definitions

#### User
Represents an authenticated user in the system.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique user identifier |
| `GoogleSubject` | string (unique) | Google user ID for authentication |
| `DisplayName` | string | User's display name |
| `Email` | string | User's email address |
| `AvatarUrl` | string? | Profile picture URL |
| `CreatedAt` | DateTimeOffset | Account creation timestamp |
| `UpdatedAt` | DateTimeOffset | Last profile update timestamp |

**Relationships:**
- One-to-many with `ServerMember`
- One-to-many with `Message`

**Notes:**
- `GoogleSubject` is the primary link to Google identity
- Auto-created on first sign-in
- Profile fields (DisplayName, Email, AvatarUrl) updated on each sign-in
- `AvatarUrl` is optional (nullable)

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

#### ServerMember
Join table linking users to servers with role information.

| Column | Type | Description |
|--------|------|-------------|
| `ServerId` | Guid (PK, FK) | Reference to Server |
| `UserId` | Guid (PK, FK) | Reference to User |
| `Role` | ServerRole (enum) | User's role in the server |
| `JoinedAt` | DateTimeOffset | When user joined |

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
| `CreatedAt` | DateTimeOffset | Message timestamp |

**Relationships:**
- Many-to-one with `Channel`
- Many-to-one with `User` (optional)

**Notes:**
- `AuthorUserId` is nullable for system messages
- `AuthorName` is a snapshot (denormalized) for performance
- `Body` is plain text (future: rich text/markdown)

## Database Context

```csharp
public class CodecDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ServerMember> ServerMembers => Set<ServerMember>();

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
- Direct message channels (1-on-1 chat)
- Message reactions (emoji)
- File attachments metadata
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
