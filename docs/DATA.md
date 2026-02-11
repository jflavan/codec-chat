# Data layer

## Decision
Codec uses SQLite with EF Core for the initial persistence layer. This keeps local setup minimal while we iterate on the schema.

## Storage
- Default database: codec.db (production placeholder)
- Development database: codec-dev.db

## Initial entities
- Server: collection of channels
- Channel: belongs to a server
- Message: belongs to a channel, stores author name, body, and optional user link
- User: maps Google subject to profile data
- ServerMember: join table between servers and users with a role

## Next steps
- Define role permissions and moderation rules
- Decide on production database path and migration strategy

## Migrations
Run these from apps/api/Codec.Api:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## Seed data
In development, the API seeds a default server, a couple channels, and sample messages if the database is empty.
