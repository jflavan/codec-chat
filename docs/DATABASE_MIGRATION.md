# Database Migration Plan: SQLite to PostgreSQL

## Executive Summary

Codec currently uses **SQLite** with **Entity Framework Core 10** — a pragmatic choice for rapid MVP iteration. As the application moves toward production readiness, SQLite introduces several limitations that make it unsuitable for an enterprise-grade, multi-user chat platform:

| Concern | SQLite Limitation | PostgreSQL Advantage |
|---------|-------------------|----------------------|
| **Concurrency** | Single-writer lock; WAL mode still serializes writes | MVCC — true concurrent reads and writes |
| **Scalability** | Single-file, single-node only | Horizontal read replicas, connection pooling, partitioning |
| **DateTimeOffset** | No native type; requires `DateTimeOffsetToStringConverter` (string storage) | Native `timestamptz` with proper ordering and arithmetic |
| **Full-text search** | Limited FTS5 extension | Built-in `tsvector`/`tsquery` with language-aware stemming |
| **JSON support** | Minimal | `jsonb` with indexing, operators, and path queries |
| **Connection pooling** | Not applicable (embedded) | `PgBouncer` or built-in Npgsql pooling for thousands of connections |
| **Backup & recovery** | File copy or Litestream | `pg_dump`, WAL archiving, PITR, managed cloud backups |
| **Observability** | Minimal | `pg_stat_statements`, `EXPLAIN ANALYZE`, rich ecosystem |
| **Cloud-managed options** | None | Azure Database for PostgreSQL, AWS RDS, Supabase, Neon |

**Recommendation:** Migrate to **PostgreSQL 16+** via the `Npgsql.EntityFrameworkCore.PostgreSQL` EF Core provider. PostgreSQL is the best fit for Codec's relational schema, real-time workload, and future growth (full-text search, partitioning, LISTEN/NOTIFY for scaled-out SignalR).

---

## Current State Assessment

### Database Provider
- **Provider:** `Microsoft.EntityFrameworkCore.Sqlite` 9.0.4
- **Connection string:** `Data Source=codec-dev.db` (file-based)
- **Auto-migration:** Applied on startup in Development mode

### Schema Summary (12 tables, 11 migrations)

| Entity | PK | Key Relationships | Indexes |
|--------|----|--------------------|---------|
| `Users` | `Id` (Guid) | → Messages, ServerMembers, Reactions, Friendships, DmChannelMembers, DirectMessages, ServerInvites | Unique on `GoogleSubject` |
| `Servers` | `Id` (Guid) | → Channels, ServerMembers, ServerInvites | — |
| `Channels` | `Id` (Guid) | → Server (FK), Messages | — |
| `Messages` | `Id` (Guid) | → Channel (FK), User (FK), Reactions, LinkPreviews, self-ref reply FK | `ChannelId`, `ReplyToMessageId` |
| `Reactions` | `Id` (Guid) | → Message (FK), User (FK) | Unique on `(MessageId, UserId, Emoji)` |
| `ServerMembers` | `(ServerId, UserId)` | → Server (FK), User (FK) | Composite PK |
| `Friendships` | `Id` (Guid) | → User (Requester FK), User (Recipient FK) | Unique on `(RequesterId, RecipientId)`, `RequesterId`, `RecipientId` |
| `DmChannels` | `Id` (Guid) | → DmChannelMembers, DirectMessages | — |
| `DmChannelMembers` | `(DmChannelId, UserId)` | → DmChannel (FK), User (FK) | `UserId`, `DmChannelId` |
| `DirectMessages` | `Id` (Guid) | → DmChannel (FK), User (FK), LinkPreviews, self-ref reply FK | `DmChannelId`, `AuthorUserId`, `ReplyToDirectMessageId` |
| `ServerInvites` | `Id` (Guid) | → Server (FK), User (FK) | Unique on `Code`, `ServerId` |
| `LinkPreviews` | `Id` (Guid) | → Message (FK?), DirectMessage (FK?) | `MessageId`, `DirectMessageId`, check constraint `CK_LinkPreview_SingleParent` |

### SQLite-Specific Workarounds in Current Code

1. **`DateTimeOffsetToStringConverter`** — All `DateTimeOffset` properties are stored as ISO 8601 strings because SQLite lacks a native temporal type. This is applied globally in `OnModelCreating`. PostgreSQL's `timestamptz` eliminates this entirely.

2. **Unique constraint race in `UserService.GetOrCreateUserAsync`** — A try/catch intercepts the SQLite unique constraint exception to handle concurrent first-login scenarios. PostgreSQL supports `ON CONFLICT` (upsert) natively; EF Core's `ExecuteUpdate`/`ExecuteDelete` or raw SQL can leverage this.

3. **No connection pooling** — SQLite is embedded and single-connection. The API creates a new `CodecDbContext` per request with no pool. PostgreSQL + Npgsql provides built-in connection pooling.

4. **Check constraint syntax** — The `CK_LinkPreview_SingleParent` constraint uses double-quoted column names (`"MessageId"`). PostgreSQL uses the same quoting convention, so this is compatible.

### Data Access Patterns

- Controllers directly inject `CodecDbContext` (no repository layer)
- `AsNoTracking()` used for read-only queries
- Batch loading via separate queries + in-memory dictionary assembly (reactions, link previews, mention lookups, reply contexts)
- `GetOrCreateUserAsync` called on every authenticated request (upsert pattern)
- Fire-and-forget background tasks for link preview extraction using `IServiceScopeFactory`
- Manual role-based authorization via `ServerMembers` queries

---

## Target Architecture

### Technology Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| **Database** | PostgreSQL 16+ | MVCC concurrency, native `timestamptz`, full-text search, `jsonb`, LISTEN/NOTIFY, mature ecosystem |
| **EF Core Provider** | `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x | First-class PostgreSQL support, Guid mapping to `uuid`, `timestamptz` mapping, array/jsonb support |
| **Connection pooling** | Npgsql built-in (default 100 connections) | Efficient connection reuse; optionally add PgBouncer for very high connection counts |
| **Local development** | Docker Compose (PostgreSQL container) | Reproducible environment, no global install required |
| **Production hosting** | Azure Database for PostgreSQL Flexible Server (or AWS RDS, Supabase) | Managed backups, high availability, security patching |
| **Migrations** | EF Core code-first migrations (new baseline) | Clean slate for PostgreSQL; SQLite migrations archived |

### Connection String (Production Template)

```
Host=<host>;Port=5432;Database=codec;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=false;
```

> Secrets must be loaded from environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager). Never hardcode credentials.

---

## Implementation Plan

### Phase 1: Infrastructure & Provider Swap

**Goal:** Replace the SQLite provider with PostgreSQL. Application code changes should be minimal — EF Core abstracts the provider.

#### 1.1 Add PostgreSQL NuGet Package

Replace `Microsoft.EntityFrameworkCore.Sqlite` with `Npgsql.EntityFrameworkCore.PostgreSQL` in the `.csproj`:

```xml
<!-- Remove -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.4" />

<!-- Add -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
```

#### 1.2 Update `Program.cs` Provider Registration

```csharp
// Before (SQLite)
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(connectionString);
});

// After (PostgreSQL)
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString);
});
```

#### 1.3 Update Connection Strings

**`appsettings.Development.json`:**
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=codec_dev;Username=codec;Password=codec_dev_pass;Include Error Detail=true"
  }
}
```

**`appsettings.json` (production template):**
```json
{
  "ConnectionStrings": {
    "Default": ""
  }
}
```

> The production connection string should be injected via environment variable `ConnectionStrings__Default` or a secrets manager.

#### 1.4 Remove the `DateTimeOffsetToStringConverter`

PostgreSQL natively supports `timestamptz`, which maps to `DateTimeOffset` automatically. Remove the global converter from `CodecDbContext.OnModelCreating`:

```csharp
// DELETE this entire block from OnModelCreating:
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
```

Also remove the `using Microsoft.EntityFrameworkCore.Storage.ValueConversion;` import if no longer needed.

#### 1.5 Add Docker Compose for Local PostgreSQL

Create `docker-compose.yml` at the repository root:

```yaml
services:
  postgres:
    image: postgres:16-alpine
    container_name: codec-postgres
    environment:
      POSTGRES_USER: codec
      POSTGRES_PASSWORD: codec_dev_pass
      POSTGRES_DB: codec_dev
    ports:
      - "5432:5432"
    volumes:
      - codec-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U codec -d codec_dev"]
      interval: 5s
      timeout: 3s
      retries: 5

volumes:
  codec-pgdata:
```

#### 1.6 Generate a Fresh PostgreSQL Migration Baseline

Since the SQLite migrations contain SQLite-specific SQL and type mappings, create a clean baseline migration for PostgreSQL rather than attempting to reuse them:

```bash
# Archive SQLite migrations (keep for reference)
mkdir -p apps/api/Codec.Api/Migrations/_sqlite_archive
mv apps/api/Codec.Api/Migrations/*.cs apps/api/Codec.Api/Migrations/_sqlite_archive/

# Start PostgreSQL
docker compose up -d postgres

# Generate fresh baseline migration
cd apps/api/Codec.Api
dotnet ef migrations add InitialPostgresBaseline

# Apply the migration
dotnet ef database update
```

#### 1.7 Update `UserService` Race Condition Handling

The current `GetOrCreateUserAsync` catches a `DbUpdateException` for SQLite unique constraint violations. Update the catch to handle PostgreSQL's unique violation instead (Npgsql throws `PostgresException` with `SqlState = "23505"`):

```csharp
catch (DbUpdateException ex) when (
    ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
{
    // Concurrent insert race — re-fetch the user created by the other request.
    return await db.Users.FirstAsync(u => u.GoogleSubject == googleSubject);
}
```

---

### Phase 2: Schema Optimizations for PostgreSQL

**Goal:** Leverage PostgreSQL-specific capabilities to improve performance and prepare for scale.

#### 2.1 Add Missing Indexes for High-Traffic Queries

Based on the current query patterns in the controllers, add these indexes that are critical for performance at scale:

```csharp
// Message retrieval is the hottest query path — compound index for channel + time ordering
modelBuilder.Entity<Message>()
    .HasIndex(m => new { m.ChannelId, m.CreatedAt });

// DM message retrieval — compound index for DM channel + time ordering (cursor pagination)
modelBuilder.Entity<DirectMessage>()
    .HasIndex(dm => new { dm.DmChannelId, dm.CreatedAt });

// Server member lookup by user (used on every authenticated request via IsMemberAsync)
modelBuilder.Entity<ServerMember>()
    .HasIndex(m => m.UserId);

// Friendship lookup — the API checks both directions, so index both
// (Already exists for RequesterId and RecipientId individually — verified)

// User search — used by GET /users/search
modelBuilder.Entity<User>()
    .HasIndex(u => u.Email);
```

#### 2.2 Configure Column Types for PostgreSQL

Add explicit PostgreSQL column type mappings where the defaults may not be optimal:

```csharp
// Use citext (case-insensitive text) for user search columns if the citext extension is enabled
// Otherwise, rely on ILIKE queries which PostgreSQL handles efficiently

// Explicitly set string max lengths where not already constrained
modelBuilder.Entity<Message>()
    .Property(m => m.Body)
    .HasMaxLength(4000);

modelBuilder.Entity<DirectMessage>()
    .Property(dm => dm.Body)
    .HasMaxLength(4000);

modelBuilder.Entity<Message>()
    .Property(m => m.AuthorName)
    .HasMaxLength(128);

modelBuilder.Entity<DirectMessage>()
    .Property(dm => dm.AuthorName)
    .HasMaxLength(128);

modelBuilder.Entity<Server>()
    .Property(s => s.Name)
    .HasMaxLength(100);

modelBuilder.Entity<Channel>()
    .Property(c => c.Name)
    .HasMaxLength(100);

modelBuilder.Entity<Reaction>()
    .Property(r => r.Emoji)
    .HasMaxLength(32);

modelBuilder.Entity<ServerInvite>()
    .Property(i => i.Code)
    .HasMaxLength(16);
```

#### 2.3 Configure UUID Generation

PostgreSQL has native `uuid` support. EF Core + Npgsql will map `Guid` to `uuid` automatically. Optionally, configure `uuid_generate_v4()` as the database default for primary keys:

```csharp
// In OnModelCreating — apply to all Guid PKs
modelBuilder.HasPostgresExtension("uuid-ossp");

// Or use the newer gen_random_uuid() available in PostgreSQL 13+
// (no extension needed)
```

> Since the application already generates `Guid.NewGuid()` in C#, database-side UUID generation is optional. It is useful as a safety net for direct SQL inserts.

---

### Phase 3: Performance & Observability Enhancements

**Goal:** Implement connection pooling best practices, query optimization, and monitoring.

#### 3.1 Configure Npgsql Connection Pooling

Npgsql provides built-in connection pooling. Configure it in `Program.cs`:

```csharp
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.CommandTimeout(30);
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });
});
```

Connection pool size is controlled via the connection string:

```
Host=...;...;Minimum Pool Size=5;Maximum Pool Size=100;Connection Idle Lifetime=300;
```

#### 3.2 Add Health Check for PostgreSQL

Replace the basic health endpoint with an actual database connectivity check:

```csharp
// In Program.cs
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!,
        name: "postgresql",
        tags: ["db", "ready"]);
```

This requires the `AspNetCore.HealthChecks.NpgSql` NuGet package.

#### 3.3 Enable EF Core Query Logging (Development)

```csharp
// In Program.cs or DbContext configuration (Development only)
options.UseNpgsql(connectionString)
    .EnableSensitiveDataLogging()  // Shows parameter values in logs
    .EnableDetailedErrors();       // More detailed error messages
```

#### 3.4 Add Database Metrics

For production observability, configure the Npgsql OpenTelemetry integration:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddNpgsqlInstrumentation();
    });
```

---

### Phase 4: Data Migration (Existing Data)

**Goal:** Migrate existing SQLite data to PostgreSQL for any non-greenfield environment.

> If this is a greenfield deployment (no production data yet), skip this phase. PostgreSQL starts with an empty database seeded by `SeedData`.

#### 4.1 Data Export from SQLite

```bash
# Export each table as SQL INSERT statements
sqlite3 codec.db ".mode insert" ".output users.sql" "SELECT * FROM Users;"
sqlite3 codec.db ".mode insert" ".output servers.sql" "SELECT * FROM Servers;"
# ... repeat for each table
```

#### 4.2 Transform DateTimeOffset Values

Since SQLite stored `DateTimeOffset` as ISO 8601 strings, the exported strings must be cast to `timestamptz` during import:

```sql
-- Example: transform string timestamps during import
INSERT INTO "Users" ("Id", "GoogleSubject", "DisplayName", "CreatedAt", "UpdatedAt")
VALUES (
    '550e8400-e29b-41d4-a716-446655440000',
    '1234567890',
    'Test User',
    '2026-02-11T06:32:04+00:00'::timestamptz,
    '2026-02-11T06:32:04+00:00'::timestamptz
);
```

#### 4.3 Migration Script Approach

For a more robust migration, use a one-time C# console app or script:

```bash
# Pseudocode — read from SQLite, write to PostgreSQL
1. Open SQLite connection (read-only)
2. Open PostgreSQL connection
3. For each table in dependency order:
   a. Read all rows from SQLite
   b. Bulk insert into PostgreSQL using Npgsql COPY
4. Validate row counts match
5. Verify foreign key integrity
```

**Table insertion order** (respecting FK dependencies):
1. `Users`
2. `Servers`
3. `Channels`
4. `ServerMembers`
5. `Messages`
6. `Reactions`
7. `Friendships`
8. `DmChannels`
9. `DmChannelMembers`
10. `DirectMessages`
11. `ServerInvites`
12. `LinkPreviews`

#### 4.4 Post-Migration Validation

```sql
-- Verify row counts
SELECT 'Users' AS table_name, COUNT(*) FROM "Users"
UNION ALL
SELECT 'Servers', COUNT(*) FROM "Servers"
UNION ALL
SELECT 'Channels', COUNT(*) FROM "Channels"
UNION ALL
SELECT 'Messages', COUNT(*) FROM "Messages"
UNION ALL
SELECT 'Reactions', COUNT(*) FROM "Reactions"
UNION ALL
SELECT 'Friendships', COUNT(*) FROM "Friendships"
UNION ALL
SELECT 'DmChannels', COUNT(*) FROM "DmChannels"
UNION ALL
SELECT 'DmChannelMembers', COUNT(*) FROM "DmChannelMembers"
UNION ALL
SELECT 'DirectMessages', COUNT(*) FROM "DirectMessages"
UNION ALL
SELECT 'ServerInvites', COUNT(*) FROM "ServerInvites"
UNION ALL
SELECT 'LinkPreviews', COUNT(*) FROM "LinkPreviews";

-- Verify FK integrity
SELECT COUNT(*) FROM "Messages" m
LEFT JOIN "Channels" c ON m."ChannelId" = c."Id"
WHERE c."Id" IS NULL;
-- Should return 0
```

---

### Phase 5: Future PostgreSQL-Native Features

**Goal:** Leverage PostgreSQL-specific capabilities that were impossible with SQLite.

#### 5.1 Full-Text Search for Messages

Add a `tsvector` column to `Messages` and `DirectMessages` for efficient full-text search:

```sql
-- Add GIN-indexed tsvector column
ALTER TABLE "Messages" ADD COLUMN "SearchVector" tsvector
    GENERATED ALWAYS AS (to_tsvector('english', "Body")) STORED;

CREATE INDEX "IX_Messages_SearchVector" ON "Messages" USING GIN ("SearchVector");

-- Query
SELECT * FROM "Messages"
WHERE "SearchVector" @@ plainto_tsquery('english', 'search terms')
ORDER BY ts_rank("SearchVector", plainto_tsquery('english', 'search terms')) DESC;
```

#### 5.2 Table Partitioning for Messages

As message volume grows, partition the `Messages` table by time range:

```sql
-- Convert to a partitioned table (requires recreation)
CREATE TABLE "Messages_partitioned" (
    "Id" uuid NOT NULL,
    "ChannelId" uuid NOT NULL,
    "AuthorUserId" uuid,
    "Body" text NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    PRIMARY KEY ("Id", "CreatedAt")
) PARTITION BY RANGE ("CreatedAt");

-- Create monthly partitions
CREATE TABLE "Messages_2026_01" PARTITION OF "Messages_partitioned"
    FOR VALUES FROM ('2026-01-01') TO ('2026-02-01');
CREATE TABLE "Messages_2026_02" PARTITION OF "Messages_partitioned"
    FOR VALUES FROM ('2026-02-01') TO ('2026-03-01');
-- Automate with pg_partman extension
```

#### 5.3 LISTEN/NOTIFY for Scaled-Out SignalR

When running multiple API instances behind a load balancer, replace the default in-memory SignalR backplane with PostgreSQL LISTEN/NOTIFY or Redis:

```csharp
// Option A: Redis backplane (recommended for SignalR)
builder.Services.AddSignalR().AddStackExchangeRedis(connectionString);

// Option B: PostgreSQL LISTEN/NOTIFY (no additional infrastructure)
// Requires a custom IHubLifetimeManager implementation
```

#### 5.4 Read Replicas for Query Offloading

For high-traffic read operations (message retrieval, member listing), configure a read replica:

```csharp
// Register separate read/write contexts
builder.Services.AddDbContext<CodecDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddDbContext<CodecReadOnlyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ReadReplica"))
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

---

## Rollback Strategy

If issues are encountered post-migration:

1. **Phase 1 rollback:** Revert the NuGet package to `Microsoft.EntityFrameworkCore.Sqlite`, restore the `DateTimeOffsetToStringConverter`, and revert connection strings. The SQLite database file is unchanged.

2. **Phase 4 rollback (data migration):** The original SQLite database is read-only during migration — it is not modified. Simply revert the provider configuration to resume using SQLite.

3. **Connection string swap:** Since the only code changes are in `Program.cs` provider registration and `CodecDbContext.OnModelCreating`, a rollback is a two-file revert.

---

## Environment-Specific Configuration

| Environment | Database | Connection String Source | Migrations |
|-------------|----------|--------------------------|------------|
| **Local dev** | PostgreSQL in Docker Compose | `appsettings.Development.json` | Auto-applied on startup |
| **CI/Test** | PostgreSQL in Docker (ephemeral) | GitHub Actions secrets / env vars | Applied in test setup |
| **Staging** | Azure Database for PostgreSQL (Flexible Server) | Azure Key Vault | Applied via `dotnet ef database update` in CI/CD pipeline |
| **Production** | Azure Database for PostgreSQL (Flexible Server, HA) | Azure Key Vault | Applied via `dotnet ef database update` in release pipeline with DBA approval |

---

## Testing Strategy

### Unit Tests
- Mock `CodecDbContext` with EF Core's in-memory provider or `Testcontainers.PostgreSql` for realistic integration tests
- Verify all query patterns work identically after the provider swap

### Integration Tests
- Use `Testcontainers.PostgreSql` NuGet package to spin up a disposable PostgreSQL container per test run
- Run the full EF Core migration against a real PostgreSQL instance
- Validate seed data, FK constraints, check constraints, and unique indexes

### Smoke Tests
- After deployment, verify:
  - User sign-in and `GetOrCreateUserAsync` flow
  - Message posting and retrieval (the highest-traffic path)
  - Real-time SignalR message delivery
  - DM creation and messaging
  - Friend request flow
  - Image upload and link preview extraction

---

## Estimated Scope of Changes

| File | Change Type | Description |
|------|-------------|-------------|
| `Codec.Api.csproj` | Modify | Swap SQLite → PostgreSQL NuGet package |
| `Program.cs` | Modify | `UseSqlite()` → `UseNpgsql()`, add retry policy, health check |
| `CodecDbContext.cs` | Modify | Remove `DateTimeOffsetToStringConverter`, add column type annotations, add compound indexes |
| `UserService.cs` | Modify | Update unique constraint exception handling for PostgreSQL |
| `appsettings.json` | Modify | Update connection string template |
| `appsettings.Development.json` | Modify | PostgreSQL local dev connection string |
| `docker-compose.yml` | Create | PostgreSQL container for local development |
| `Migrations/` | Regenerate | Fresh `InitialPostgresBaseline` migration; archive SQLite migrations |

**Total files changed: ~8** — The migration is narrowly scoped because EF Core abstracts the provider. No controller or model changes are required.

---

## References

- [Npgsql EF Core Provider Documentation](https://www.npgsql.org/efcore/)
- [EF Core — Database Provider Switching](https://learn.microsoft.com/en-us/ef/core/providers/)
- [PostgreSQL DateTimeOffset Mapping](https://www.npgsql.org/doc/types/datetime.html)
- [PostgreSQL Full-Text Search](https://www.postgresql.org/docs/current/textsearch.html)
- [PostgreSQL Table Partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/)
