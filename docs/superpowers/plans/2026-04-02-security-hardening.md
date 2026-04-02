# Security Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix SignalR authorization gaps, add hub rate limiting, tighten CSP/security headers, close input validation gaps, and add missing audit logging for channel permission overrides.

**Architecture:** All changes are in the existing API and web projects. SignalR authorization fixes add membership checks to existing hub methods. Rate limiting uses a custom `IHubFilter`. CSP and header changes are in the SvelteKit hooks middleware. Audit logging extends the existing `AuditService` pattern.

**Tech Stack:** ASP.NET Core 10, SignalR, EF Core, SvelteKit, xUnit + FluentAssertions + Moq

---

### Task 1: Add authorization to `JoinServer` and `LeaveServer`

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs:93-105`
- Test: `apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ChatHubTests.cs`:

```csharp
[Fact]
public async Task JoinServer_ValidMember_JoinsGroup()
{
    var hub = CreateHub();
    await hub.JoinServer(_testServer.Id.ToString());
    _mockGroups.Verify(
        g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
        Times.Once);
}

[Fact]
public async Task JoinServer_InvalidGuid_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.JoinServer("not-a-guid");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid server ID.");
}

[Fact]
public async Task JoinServer_NonMember_ThrowsHubException()
{
    var nonMemberServer = new Server { Id = Guid.NewGuid(), Name = "Other Server" };
    _db.Servers.Add(nonMemberServer);
    await _db.SaveChangesAsync();

    var hub = CreateHub();
    var act = () => hub.JoinServer(nonMemberServer.Id.ToString());
    await act.Should().ThrowAsync<HubException>().WithMessage("Not a member of this server.");
}

[Fact]
public async Task JoinServer_ServerNotFound_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.JoinServer(Guid.NewGuid().ToString());
    await act.Should().ThrowAsync<HubException>().WithMessage("Server not found.");
}

[Fact]
public async Task LeaveServer_InvalidGuid_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.LeaveServer("not-a-guid");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid server ID.");
}

[Fact]
public async Task LeaveServer_ValidId_LeavesGroup()
{
    var hub = CreateHub();
    await hub.LeaveServer(_testServer.Id.ToString());
    _mockGroups.Verify(
        g => g.RemoveFromGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
        Times.Once);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.JoinServer|FullyQualifiedName~ChatHubTests.LeaveServer" --no-restore`

Expected: FAIL — current methods have no validation.

- [ ] **Step 3: Implement authorization checks**

Replace `JoinServer` and `LeaveServer` in `ChatHub.cs` (lines 93-105):

```csharp
public async Task JoinServer(string serverId)
{
    if (!Guid.TryParse(serverId, out var serverGuid))
        throw new HubException("Invalid server ID.");

    var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

    var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverGuid);
    if (!serverExists)
        throw new HubException("Server not found.");

    var isMember = appUser.IsGlobalAdmin || await db.ServerMembers.AsNoTracking()
        .AnyAsync(m => m.ServerId == serverGuid && m.UserId == appUser.Id);
    if (!isMember)
        throw new HubException("Not a member of this server.");

    await Groups.AddToGroupAsync(Context.ConnectionId, $"server-{serverGuid}");
}

public async Task LeaveServer(string serverId)
{
    if (!Guid.TryParse(serverId, out var serverGuid))
        throw new HubException("Invalid server ID.");

    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"server-{serverGuid}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.JoinServer|FullyQualifiedName~ChatHubTests.LeaveServer" --no-restore`

Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Hubs/ChatHub.cs apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs
git commit -m "fix(security): add membership check to JoinServer/LeaveServer hub methods"
```

---

### Task 2: Add authorization to `JoinDmChannel`

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs:161-164`
- Test: `apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ChatHubTests.cs`:

```csharp
[Fact]
public async Task JoinDmChannel_ValidMember_JoinsGroup()
{
    var hub = CreateHub();
    await hub.JoinDmChannel(_dmChannel.Id.ToString());
    _mockGroups.Verify(
        g => g.AddToGroupAsync(_connectionId, $"dm-{_dmChannel.Id}", default),
        Times.Once);
}

[Fact]
public async Task JoinDmChannel_InvalidGuid_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.JoinDmChannel("not-a-guid");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid DM channel ID.");
}

[Fact]
public async Task JoinDmChannel_NonMember_ThrowsHubException()
{
    var otherDm = new DmChannel { Id = Guid.NewGuid() };
    _db.DmChannels.Add(otherDm);
    await _db.SaveChangesAsync();

    var hub = CreateHub();
    var act = () => hub.JoinDmChannel(otherDm.Id.ToString());
    await act.Should().ThrowAsync<HubException>().WithMessage("Not a member of this DM channel.");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.JoinDmChannel" --no-restore`

Expected: FAIL — current method has no validation.

- [ ] **Step 3: Implement authorization check**

Replace `JoinDmChannel` in `ChatHub.cs` (lines 161-164):

```csharp
public async Task JoinDmChannel(string dmChannelId)
{
    if (!Guid.TryParse(dmChannelId, out var dmChannelGuid))
        throw new HubException("Invalid DM channel ID.");

    var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
    var isMember = await db.DmChannelMembers.AsNoTracking()
        .AnyAsync(m => m.DmChannelId == dmChannelGuid && m.UserId == appUser.Id);
    if (!isMember)
        throw new HubException("Not a member of this DM channel.");

    await Groups.AddToGroupAsync(Context.ConnectionId, $"dm-{dmChannelGuid}");
}
```

Also update `LeaveDmChannel` (lines 169-172) with GUID validation:

```csharp
public async Task LeaveDmChannel(string dmChannelId)
{
    if (!Guid.TryParse(dmChannelId, out var dmChannelGuid))
        throw new HubException("Invalid DM channel ID.");

    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"dm-{dmChannelGuid}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.JoinDmChannel" --no-restore`

Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Hubs/ChatHub.cs apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs
git commit -m "fix(security): add membership check to JoinDmChannel hub method"
```

---

### Task 3: Add validation to typing methods and truncate displayName

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs:145-190`
- Test: `apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ChatHubTests.cs`:

```csharp
[Fact]
public async Task StartTyping_InvalidChannelId_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.StartTyping("not-a-guid", "User");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid channel ID.");
}

[Fact]
public async Task StartTyping_TruncatesLongDisplayName()
{
    var hub = CreateHub();
    var longName = new string('A', 200);
    await hub.StartTyping(_textChannel.Id.ToString(), longName);
    _mockClients.Verify(c => c.OthersInGroup(_textChannel.Id.ToString()), Times.Once);
    _mockOthersProxy.Verify(
        p => p.SendCoreAsync("UserTyping",
            It.Is<object?[]>(args => args.Length == 2 && ((string)args[1]!).Length == 100),
            default),
        Times.Once);
}

[Fact]
public async Task StartDmTyping_InvalidChannelId_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.StartDmTyping("not-a-guid", "User");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid DM channel ID.");
}

[Fact]
public async Task StopTyping_InvalidChannelId_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.StopTyping("not-a-guid", "User");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid channel ID.");
}

[Fact]
public async Task StopDmTyping_InvalidChannelId_ThrowsHubException()
{
    var hub = CreateHub();
    var act = () => hub.StopDmTyping("not-a-guid", "User");
    await act.Should().ThrowAsync<HubException>().WithMessage("Invalid DM channel ID.");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.StartTyping|FullyQualifiedName~ChatHubTests.StopTyping|FullyQualifiedName~ChatHubTests.StartDmTyping|FullyQualifiedName~ChatHubTests.StopDmTyping" --no-restore`

Expected: FAIL.

- [ ] **Step 3: Implement validation and truncation**

Replace the four typing methods in `ChatHub.cs` (lines 145-190):

```csharp
public async Task StartTyping(string channelId, string displayName)
{
    if (!Guid.TryParse(channelId, out _))
        throw new HubException("Invalid channel ID.");
    var safeName = Truncate(displayName, 100);
    await Clients.OthersInGroup(channelId).SendAsync("UserTyping", channelId, safeName);
}

public async Task StopTyping(string channelId, string displayName)
{
    if (!Guid.TryParse(channelId, out _))
        throw new HubException("Invalid channel ID.");
    var safeName = Truncate(displayName, 100);
    await Clients.OthersInGroup(channelId).SendAsync("UserStoppedTyping", channelId, safeName);
}

public async Task StartDmTyping(string dmChannelId, string displayName)
{
    if (!Guid.TryParse(dmChannelId, out _))
        throw new HubException("Invalid DM channel ID.");
    var safeName = Truncate(displayName, 100);
    await Clients.OthersInGroup($"dm-{dmChannelId}")
        .SendAsync("DmTyping", dmChannelId, safeName);
}

public async Task StopDmTyping(string dmChannelId, string displayName)
{
    if (!Guid.TryParse(dmChannelId, out _))
        throw new HubException("Invalid DM channel ID.");
    var safeName = Truncate(displayName, 100);
    await Clients.OthersInGroup($"dm-{dmChannelId}")
        .SendAsync("DmStoppedTyping", dmChannelId, safeName);
}

private static string Truncate(string? value, int maxLength)
    => string.IsNullOrEmpty(value) ? "" : value.Length <= maxLength ? value : value[..maxLength];
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~ChatHubTests.StartTyping|FullyQualifiedName~ChatHubTests.StopTyping|FullyQualifiedName~ChatHubTests.StartDmTyping|FullyQualifiedName~ChatHubTests.StopDmTyping" --no-restore`

Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Hubs/ChatHub.cs apps/api/Codec.Api.Tests/Hubs/ChatHubTests.cs
git commit -m "fix(security): validate channel IDs and truncate displayName in typing methods"
```

---

### Task 4: Add SignalR hub rate limiting filter

**Files:**
- Create: `apps/api/Codec.Api/Filters/HubRateLimitFilter.cs`
- Modify: `apps/api/Codec.Api/Program.cs`
- Test: `apps/api/Codec.Api.Tests/Filters/HubRateLimitFilterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `apps/api/Codec.Api.Tests/Filters/HubRateLimitFilterTests.cs`:

```csharp
using Codec.Api.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Codec.Api.Tests.Filters;

public class HubRateLimitFilterTests
{
    private readonly HubRateLimitFilter _filter = new();

    private static HubInvocationContext CreateContext(string connectionId, string methodName)
    {
        var hubContext = new Mock<HubCallerContext>();
        hubContext.Setup(c => c.ConnectionId).Returns(connectionId);
        var serviceProvider = new Mock<IServiceProvider>();
        return new HubInvocationContext(
            hubContext.Object, serviceProvider.Object, new Mock<Hub>().Object,
            typeof(Hub).GetMethod("ToString")!, []);
    }

    [Fact]
    public async Task InvokeMethodAsync_UnderLimit_Succeeds()
    {
        var context = CreateContext("conn-1", "StartTyping");
        var called = false;
        ValueTask<object?> Next(HubInvocationContext ctx)
        {
            called = true;
            return ValueTask.FromResult<object?>(null);
        }

        await _filter.InvokeMethodAsync(context, Next);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeMethodAsync_OverDefaultLimit_ThrowsHubException()
    {
        var context = CreateContext("conn-2", "SomeMethod");
        ValueTask<object?> Next(HubInvocationContext ctx) => ValueTask.FromResult<object?>(null);

        // Exhaust the 60/minute default limit
        for (var i = 0; i < 60; i++)
            await _filter.InvokeMethodAsync(context, Next);

        var act = () => _filter.InvokeMethodAsync(context, Next).AsTask();
        await act.Should().ThrowAsync<HubException>().WithMessage("Rate limit exceeded.");
    }

    [Fact]
    public async Task InvokeMethodAsync_DifferentConnections_IndependentLimits()
    {
        ValueTask<object?> Next(HubInvocationContext ctx) => ValueTask.FromResult<object?>(null);

        var ctx1 = CreateContext("conn-3", "SomeMethod");
        var ctx2 = CreateContext("conn-4", "SomeMethod");

        for (var i = 0; i < 60; i++)
            await _filter.InvokeMethodAsync(ctx1, Next);

        // conn-3 is exhausted, but conn-4 should still work
        var called = false;
        ValueTask<object?> Next2(HubInvocationContext ctx) { called = true; return ValueTask.FromResult<object?>(null); }
        await _filter.InvokeMethodAsync(ctx2, Next2);
        called.Should().BeTrue();
    }

    [Fact]
    public void OnDisconnected_CleansUpState()
    {
        // Should not throw even for unknown connection
        var ex = new Exception("test");
        var act = () => _filter.OnDisconnectedAsync(ex, "conn-999");
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~HubRateLimitFilterTests" --no-restore`

Expected: FAIL — `HubRateLimitFilter` class doesn't exist yet.

- [ ] **Step 3: Implement the hub rate limit filter**

Create `apps/api/Codec.Api/Filters/HubRateLimitFilter.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Codec.Api.Filters;

/// <summary>
/// SignalR hub filter that enforces per-connection rate limits using a sliding window.
/// </summary>
public sealed class HubRateLimitFilter : IHubFilter
{
    private const int DefaultLimitPerMinute = 60;

    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connectionId = invocationContext.Context.ConnectionId;
        var window = _windows.GetOrAdd(connectionId, _ => new SlidingWindow());

        if (!window.TryAcquire(DefaultLimitPerMinute))
            throw new HubException("Rate limit exceeded.");

        return await next(invocationContext);
    }

    public void OnDisconnectedAsync(Exception? exception, string connectionId)
    {
        _windows.TryRemove(connectionId, out _);
    }

    private sealed class SlidingWindow
    {
        private readonly object _lock = new();
        private readonly Queue<DateTimeOffset> _timestamps = new();

        public bool TryAcquire(int maxPerMinute)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMinutes(-1);

            lock (_lock)
            {
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxPerMinute)
                    return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}
```

- [ ] **Step 4: Register the filter in Program.cs**

In `apps/api/Codec.Api/Program.cs`, line 83 has `var signalRBuilder = builder.Services.AddSignalR()`. Register the filter as a singleton and add it via `AddHubOptions`. Insert before the `AddSignalR()` call:

```csharp
builder.Services.AddSingleton<Codec.Api.Filters.HubRateLimitFilter>();
```

Then change line 83 from:

```csharp
var signalRBuilder = builder.Services.AddSignalR()
```

To:

```csharp
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.AddFilter<Codec.Api.Filters.HubRateLimitFilter>();
})

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~HubRateLimitFilterTests" --no-restore`

Expected: All 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/api/Codec.Api/Filters/HubRateLimitFilter.cs apps/api/Codec.Api/Program.cs apps/api/Codec.Api.Tests/Filters/HubRateLimitFilterTests.cs
git commit -m "feat(security): add per-connection SignalR hub rate limiting"
```

---

### Task 5: Tighten CSP and add security headers

**Files:**
- Modify: `apps/web/src/hooks.server.ts`
- Test: `apps/web/src/hooks.server.test.ts` (check if exists; if not, manual verification)

- [ ] **Step 1: Check for existing test file**

Run: `ls apps/web/src/hooks.server.test.ts 2>/dev/null || echo "no test file"`

If no test file exists, this task will be verified manually.

- [ ] **Step 2: Remove `https:` from CSP img-src**

In `apps/web/src/hooks.server.ts`, replace line 22 (`'https:',`) by removing it. The explicit CDN domains that follow already cover the needed origins.

Change the `img-src` array from:

```typescript
'img-src': [
    "'self'",
    'data:',
    'blob:',
    'https:',
    'https://lh3.googleusercontent.com',
    // ... rest
```

To:

```typescript
'img-src': [
    "'self'",
    'data:',
    'blob:',
    'https://lh3.googleusercontent.com',
    // ... rest
```

- [ ] **Step 3: Add HSTS header**

In `apps/web/src/hooks.server.ts`, after the existing `response.headers.set(...)` calls (after line 53), add:

```typescript
response.headers.set('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
```

- [ ] **Step 4: Expand Permissions-Policy**

In `apps/web/src/hooks.server.ts`, replace line 53:

```typescript
response.headers.set('Permissions-Policy', 'microphone=(self), camera=(self)');
```

With:

```typescript
response.headers.set('Permissions-Policy', 'microphone=(self), camera=(self), geolocation=(), payment=(), usb=(), bluetooth=()');
```

- [ ] **Step 5: Verify the dev server starts**

Run: `cd apps/web && npx svelte-kit sync && cd ../..`

Expected: No errors.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/hooks.server.ts
git commit -m "fix(security): tighten CSP img-src, add HSTS, expand Permissions-Policy"
```

---

### Task 6: Add `[Required]` to `CreateMessageRequest.Body`

**Files:**
- Modify: `apps/api/Codec.Api/Models/CreateMessageRequest.cs:6`
- Test: Verify with existing integration tests

- [ ] **Step 1: Update the model validation attribute**

In `apps/api/Codec.Api/Models/CreateMessageRequest.cs`, change line 6 from:

```csharp
[param: StringLength(8000)] string Body,
```

To:

```csharp
[param: Required, StringLength(8000, MinimumLength = 1)] string Body,
```

- [ ] **Step 2: Run existing tests to check for regressions**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --no-restore`

Expected: All existing tests PASS. No test should rely on sending empty/null message bodies.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Models/CreateMessageRequest.cs
git commit -m "fix(security): require non-empty message body in CreateMessageRequest"
```

---

### Task 7: Add audit logging for channel permission override changes

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ChannelsController.cs:1241-1358`
- Modify: `apps/api/Codec.Api/Models/AuditLogEntry.cs` (add new enum values)

- [ ] **Step 1: Add new AuditAction enum values**

In `apps/api/Codec.Api/Models/AuditLogEntry.cs`, add after the existing enum values (after `MemberUnbanned` or at the end):

```csharp
ChannelOverrideUpdated,
ChannelOverrideDeleted,
```

- [ ] **Step 2: Add audit logging to `SetChannelOverride`**

In `apps/api/Codec.Api/Controllers/ChannelsController.cs`, the `SetChannelOverride` method (line 1241) needs `[FromServices] AuditService audit` added to its parameters and an `audit.Log` call.

Add `[FromServices] AuditService audit` parameter to the method signature.

After `await db.SaveChangesAsync();` (line 1307), before the `SendAsync` call, add:

```csharp
audit.Log(channel.ServerId, appUser.Id, AuditAction.ChannelOverrideUpdated,
    targetType: "Channel", targetId: channelId.ToString(),
    details: $"Role: {roleId}, Allow: {request.Allow}, Deny: {request.Deny}");
```

- [ ] **Step 3: Add audit logging to `DeleteChannelOverride`**

Add `[FromServices] AuditService audit` parameter to the `DeleteChannelOverride` method (line 1318).

After `db.ChannelPermissionOverrides.Remove(existing);` and before `await db.SaveChangesAsync();` (line 1353), add:

```csharp
audit.Log(channel.ServerId, appUser.Id, AuditAction.ChannelOverrideDeleted,
    targetType: "Channel", targetId: channelId.ToString(),
    details: $"Role: {roleId}");
```

- [ ] **Step 4: Run existing tests to check for regressions**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --no-restore`

Expected: All existing tests PASS. The `AuditService` is injected via `[FromServices]`, so existing tests that don't supply it will get the registered service from the container.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ChannelsController.cs apps/api/Codec.Api/Models/AuditLogEntry.cs
git commit -m "feat(security): audit log channel permission override changes"
```

---

### Task 8: Run full test suite and verify

**Files:** None (verification only)

- [ ] **Step 1: Run all API tests**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --no-restore -v normal`

Expected: All tests PASS, including the new ones from Tasks 1-4.

- [ ] **Step 2: Run web checks**

Run: `cd apps/web && npm run check && cd ../..`

Expected: No errors.

- [ ] **Step 3: Verify API builds cleanly**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj --no-restore`

Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 4: Update design doc to note what was descoped**

Update `docs/superpowers/specs/2026-04-02-security-hardening-design.md`, Section 5a to note that role CRUD, ban/unban, and server settings already have audit logging. Only channel permission overrides were missing.

- [ ] **Step 5: Final commit**

```bash
git add docs/superpowers/specs/2026-04-02-security-hardening-design.md
git commit -m "docs: update security hardening spec with audit logging findings"
```
