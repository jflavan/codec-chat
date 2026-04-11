Fix confirmed performance issues in the Codec Chat codebase.

## Fix 1: N+1 Query in GetMyServers (CRITICAL)
**File:** `apps/api/Codec.Api/Controllers/ServersController.cs:124-142`
**Issue:** Inside a foreach loop over user memberships, `IsOwnerAsync(m.ServerId, appUser.Id)` makes a DB query per server.
**Fix:** Batch-load ownership status before the loop:
```csharp
// Before the loop, get all server IDs where user is owner
var ownedServerIds = await _context.Servers
    .Where(s => memberServerIds.Contains(s.Id) && s.OwnerId == appUser.Id)
    .Select(s => s.Id)
    .ToHashSetAsync();

// In the loop, use: var isOwner = ownedServerIds.Contains(m.ServerId);
```

## Fix 2: Memory Leak on Screen Share (HIGH)
**File:** `apps/web/src/lib/state/voice-store.svelte.ts:528-531`
**Issue:** `track.addEventListener('ended', ...)` listener never removed. Accumulates over multiple screen shares.
**Fix:** Store the handler reference and remove it when stopping screen share:
```typescript
private _screenEndedHandler: (() => void) | null = null;

// When starting:
this._screenEndedHandler = () => {
    this.isScreenSharing = false;
    this.localScreenTrack = null;
};
track.addEventListener('ended', this._screenEndedHandler);

// When stopping (and in cleanup):
if (this._screenEndedHandler && this.localScreenTrack) {
    this.localScreenTrack.removeEventListener('ended', this._screenEndedHandler);
    this._screenEndedHandler = null;
}
```

## Fix 3: Additional N+1 Patterns
**File:** `apps/api/Codec.Api/Controllers/ServersController.cs`
**Lines:** ~897, ~988, ~1075
**Issue:** `GetHighestRolePositionAsync` and `IsOwnerAsync` called in member management loops.
**Fix:** Same batch-load pattern — pre-fetch all needed data before the loop.

## Quality Gate
```bash
cd apps/api/Codec.Api && dotnet build
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
dotnet test apps/api/Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj
cd apps/web && npm run check && npm test
```
All must pass.
