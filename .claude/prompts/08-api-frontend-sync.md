Verify API-frontend type contract consistency.

## Check 1: C# Models vs TypeScript Types
Compare every C# model/DTO in `apps/api/Codec.Api/Models/` with TypeScript types in `apps/web/src/lib/types/models.ts`.

For each C# class:
1. Find its TypeScript counterpart
2. Compare property names (C# PascalCase → TS camelCase)
3. Compare types (string, int, bool, DateTime → string, number, boolean, string)
4. Compare nullability (C#? → TS | null)
5. Check for missing properties in either direction

## Check 2: API Endpoints vs Client Methods
Compare controller actions with `apps/web/src/lib/api/client.ts` methods:
1. Every API endpoint should have a corresponding client method
2. Request/response types should match
3. HTTP methods should match (GET, POST, PUT, DELETE, PATCH)

## Check 3: SignalR Events
Compare ChatHub.cs `SendAsync` calls with chat-hub.ts event subscriptions:
1. Every server-side event should have a client-side handler
2. Every client-side subscription should have a matching server-side emission
3. Payload shapes should match

## Check 4: Enum Consistency
Compare C# enums with TypeScript enums/unions:
- ChannelType
- MemberRole (if exists)
- Any other enum types

## Fix Process
For each mismatch:
1. Check git log to determine which side is authoritative (most recent change)
2. Update the stale side
3. Verify all usages compile

## Quality Gate
```bash
cd apps/web && npm run check && npm test
cd apps/api/Codec.Api && dotnet build
```
