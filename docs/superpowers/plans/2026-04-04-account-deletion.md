# Account Deletion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow users to permanently delete their accounts with password re-authentication and typed confirmation, anonymizing their messages.

**Architecture:** New `DELETE /me` endpoint on `UsersController` delegates to a `DeleteAccountAsync` method on `UserService`. The method runs in a single transaction: checks server ownership, cleans up Restrict-FK entities, nulls `AuthorUserId` on messages, then deletes the user row (cascading the rest). Frontend adds a danger section to `AccountSettings.svelte` with a confirmation modal.

**Tech Stack:** ASP.NET Core 10 / EF Core / PostgreSQL (API), SvelteKit / Svelte 5 (frontend), xUnit / Moq / FluentAssertions (tests), Vitest (frontend tests)

---

### Task 1: Request DTO

**Files:**
- Create: `apps/api/Codec.Api/Models/DeleteAccountRequest.cs`

- [ ] **Step 1: Create the request DTO**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class DeleteAccountRequest
{
    [MaxLength(128)]
    public string? Password { get; set; }

    public string? GoogleCredential { get; set; }

    [Required]
    public string ConfirmationText { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Commit**

```bash
git add apps/api/Codec.Api/Models/DeleteAccountRequest.cs
git commit -m "feat: add DeleteAccountRequest DTO"
```

---

### Task 2: UserService.DeleteAccountAsync

**Files:**
- Modify: `apps/api/Codec.Api/Services/IUserService.cs`
- Modify: `apps/api/Codec.Api/Services/UserService.cs`

- [ ] **Step 1: Add interface method to IUserService**

Add to `apps/api/Codec.Api/Services/IUserService.cs` before the closing brace:

```csharp
/// <summary>
/// Returns the list of servers the user owns (position-0 role).
/// Used to block account deletion until ownership is transferred.
/// </summary>
Task<List<(Guid ServerId, string ServerName)>> GetOwnedServersAsync(Guid userId);

/// <summary>
/// Permanently deletes a user account within a single transaction.
/// Anonymizes messages, cleans up Restrict-FK entities, then removes the user row.
/// Caller must verify authentication and server-ownership preconditions first.
/// </summary>
Task DeleteAccountAsync(Guid userId);
```

- [ ] **Step 2: Implement GetOwnedServersAsync in UserService**

Add to `apps/api/Codec.Api/Services/UserService.cs` before the closing brace:

```csharp
/// <inheritdoc />
public async Task<List<(Guid ServerId, string ServerName)>> GetOwnedServersAsync(Guid userId)
{
    // Owner = position-0 system role
    var ownedServers = await db.ServerMemberRoles
        .AsNoTracking()
        .Where(mr => mr.UserId == userId)
        .Join(db.ServerRoles.Where(r => r.IsSystemRole && r.Position == 0),
            mr => mr.RoleId, r => r.Id, (mr, r) => r.ServerId)
        .Join(db.Servers, sid => sid, s => s.Id, (sid, s) => new { s.Id, s.Name })
        .ToListAsync();

    return ownedServers.Select(s => (s.Id, s.Name)).ToList();
}
```

- [ ] **Step 3: Implement DeleteAccountAsync in UserService**

Add to `apps/api/Codec.Api/Services/UserService.cs` before the closing brace:

```csharp
/// <inheritdoc />
public async Task DeleteAccountAsync(Guid userId)
{
    await using var transaction = await db.Database.BeginTransactionAsync();

    // 1. Revoke all refresh tokens
    await db.RefreshTokens.Where(rt => rt.UserId == userId).ExecuteDeleteAsync();

    // 2. Clean up Restrict-FK entities that would block user deletion
    await db.Friendships
        .Where(f => f.RequesterId == userId || f.RecipientId == userId)
        .ExecuteDeleteAsync();

    await db.VoiceCalls
        .Where(vc => vc.CallerUserId == userId || vc.RecipientUserId == userId)
        .ExecuteDeleteAsync();

    await db.CustomEmojis
        .Where(e => e.UploadedByUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(e => e.UploadedByUserId, (Guid?)null));

    await db.Webhooks
        .Where(w => w.CreatedByUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreatedByUserId, (Guid?)null));

    await db.ServerInvites
        .Where(i => i.CreatedByUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedByUserId, (Guid?)null));

    await db.AdminActions
        .Where(a => a.ActorUserId == userId)
        .ExecuteDeleteAsync();

    await db.SystemAnnouncements
        .Where(a => a.CreatedByUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedByUserId, (Guid?)null));

    await db.Reports
        .Where(r => r.ReporterId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReporterId, (Guid?)null));

    await db.BannedMembers
        .Where(b => b.BannedByUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(b => b.BannedByUserId, (Guid?)null));

    // 3. Anonymize messages (set AuthorUserId to null)
    await db.Messages
        .Where(m => m.AuthorUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.AuthorUserId, (Guid?)null));

    await db.DirectMessages
        .Where(dm => dm.AuthorUserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(dm => dm.AuthorUserId, (Guid?)null));

    // 4. Delete reactions by this user
    await db.Reactions
        .Where(r => r.UserId == userId)
        .ExecuteDeleteAsync();

    // 5. Delete the user row (cascades: ServerMember, ServerMemberRole,
    //    DmChannelMember, PresenceState, VoiceState, PushSubscription,
    //    ChannelNotificationOverride, BannedMember)
    await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();

    await transaction.CommitAsync();
}
```

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Services/IUserService.cs apps/api/Codec.Api/Services/UserService.cs
git commit -m "feat: add DeleteAccountAsync and GetOwnedServersAsync to UserService"
```

---

### Task 3: DELETE /me endpoint

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/UsersController.cs`

- [ ] **Step 1: Add required usings and update constructor**

Add `using System.IdentityModel.Tokens.Jwt;`, `using Microsoft.IdentityModel.Protocols;`, `using Microsoft.IdentityModel.Protocols.OpenIdConnect;`, and `using Microsoft.IdentityModel.Tokens;` to the top of `UsersController.cs`.

Update the constructor to inject `IConfiguration`:

```csharp
public class UsersController(IUserService userService, IAvatarService avatarService, CodecDbContext db, IHubContext<ChatHub> hub, IConfiguration configuration) : ControllerBase
```

- [ ] **Step 2: Add the DELETE /me endpoint**

Add this method to `UsersController` after the `Me()` method:

```csharp
/// <summary>
/// Permanently deletes the authenticated user's account.
/// Requires password re-authentication (or Google credential for Google-only accounts)
/// and typing "DELETE" to confirm.
/// </summary>
[HttpDelete("me")]
public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
{
    if (request.ConfirmationText != "DELETE")
    {
        return BadRequest(new { error = "You must type DELETE to confirm account deletion." });
    }

    var (appUser, _) = await userService.GetOrCreateUserAsync(User);

    // Check server ownership
    var ownedServers = await userService.GetOwnedServersAsync(appUser.Id);
    if (ownedServers.Count > 0)
    {
        return BadRequest(new
        {
            error = "You must transfer ownership of all servers before deleting your account.",
            ownedServers = ownedServers.Select(s => new { id = s.ServerId, name = s.ServerName })
        });
    }

    // Verify identity: password for users with a password, Google credential for Google-only
    if (appUser.PasswordHash is not null)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Password is required to delete your account." });
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, appUser.PasswordHash))
        {
            return Unauthorized(new { error = "Incorrect password." });
        }
    }
    else if (appUser.GoogleSubject is not null)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleCredential))
        {
            return BadRequest(new { error = "Google re-authentication is required to delete your account." });
        }

        var googleClientId = configuration["Google:ClientId"];
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        try
        {
            var openIdConfig = await configManager.GetConfigurationAsync();
            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
                ValidateAudience = true,
                ValidAudience = googleClientId,
                ValidateLifetime = true,
                IssuerSigningKeys = openIdConfig.SigningKeys
            };

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(request.GoogleCredential, validationParams, out _);
            var googleSubject = principal.FindFirst("sub")?.Value;

            if (googleSubject != appUser.GoogleSubject)
            {
                return Unauthorized(new { error = "Google account does not match." });
            }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return Unauthorized(new { error = "Invalid or expired Google credential." });
        }
    }
    else
    {
        return BadRequest(new { error = "Unable to verify identity for account deletion." });
    }

    // Force-disconnect SignalR connections
    await hub.Clients.Group($"user-{appUser.Id}").SendAsync("AccountDeleted");

    await userService.DeleteAccountAsync(appUser.Id);

    return Ok(new { message = "Account deleted successfully." });
}
```

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/UsersController.cs
git commit -m "feat: add DELETE /me endpoint for account deletion"
```

---

### Task 4: API unit tests

**Files:**
- Create: `apps/api/Codec.Api.Tests/Controllers/DeleteAccountTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class DeleteAccountTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly UsersController _controller;
    private readonly User _testUser;

    public DeleteAccountTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test User",
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", workFactor: 4)
        };

        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _controller = new UsersController(_userService.Object, _avatarService.Object, _db, _hub.Object, _config.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("iss", "codec-api"),
                    new Claim("sub", _testUser.Id.ToString())
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));
        _userService.Setup(u => u.GetOwnedServersAsync(_testUser.Id))
            .ReturnsAsync(new List<(Guid, string)>());

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _hub.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task DeleteAccount_WrongConfirmationText_Returns400()
    {
        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "WRONG"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_OwnsServer_Returns400WithServerList()
    {
        var serverId = Guid.NewGuid();
        _userService.Setup(u => u.GetOwnedServersAsync(_testUser.Id))
            .ReturnsAsync(new List<(Guid, string)> { (serverId, "My Server") });

        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_Returns401()
    {
        var request = new DeleteAccountRequest
        {
            Password = "wrongpassword",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_MissingPassword_Returns400()
    {
        var request = new DeleteAccountRequest
        {
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_CorrectPassword_ReturnsOkAndCallsDelete()
    {
        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<OkObjectResult>();
        _userService.Verify(u => u.DeleteAccountAsync(_testUser.Id), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
cd apps/api/Codec.Api.Tests && dotnet test --filter "DeleteAccountTests" --verbosity normal
```

Expected: All 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api.Tests/Controllers/DeleteAccountTests.cs
git commit -m "test: add unit tests for DELETE /me endpoint"
```

---

### Task 5: EF migration for nullable FKs

**Files:**
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`
- Create: `apps/api/Codec.Api/Migrations/<timestamp>_AllowNullableDeletedUserFks.cs`

The following FK columns need to allow null values to support user deletion (some are already nullable, but we need to verify and fix):
- `Report.ReporterId` — currently `Restrict`, needs to become nullable with `SetNull`
- `CustomEmoji.UploadedByUserId` — currently `Restrict`, needs to become nullable with `SetNull`
- `Webhook.CreatedByUserId` — currently `Restrict`, needs to become nullable with `SetNull`
- `AdminAction.ActorUserId` — currently `Restrict`, needs to become nullable with `SetNull`
- `SystemAnnouncement.CreatedByUserId` — currently `Restrict`, needs to become nullable with `SetNull`
- `BannedMember.BannedByUserId` — currently `Restrict`, needs to become nullable with `SetNull`

- [ ] **Step 1: Check which FK properties are currently non-nullable**

Read the model files to check which `UserId` properties are `Guid` vs `Guid?`:

```bash
grep -n "ActorUserId\|UploadedByUserId\|CreatedByUserId\|ReporterId\|BannedByUserId" apps/api/Codec.Api/Models/*.cs
```

- [ ] **Step 2: Make FK properties nullable where needed**

For each entity where the FK is `Guid` (non-nullable), change it to `Guid?` and update the navigation property to be nullable. Also update `CodecDbContext.OnModelCreating` to change the delete behavior from `Restrict` to `SetNull`.

Specific changes vary based on Step 1 findings. The pattern for each is:

In the model file (e.g., `CustomEmoji.cs`):
```csharp
// Change from:
public Guid UploadedByUserId { get; set; }
public User UploadedByUser { get; set; } = null!;
// To:
public Guid? UploadedByUserId { get; set; }
public User? UploadedByUser { get; set; }
```

In `CodecDbContext.cs`, change:
```csharp
// From:
.OnDelete(DeleteBehavior.Restrict)
// To:
.OnDelete(DeleteBehavior.SetNull)
```

- [ ] **Step 3: Generate EF migration**

```bash
cd apps/api/Codec.Api && dotnet ef migrations add AllowNullableDeletedUserFks
```

If `dotnet ef` is unavailable, write the migration file manually following the pattern in existing migrations.

- [ ] **Step 4: Verify build**

```bash
cd apps/api/Codec.Api && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Models/ apps/api/Codec.Api/Data/CodecDbContext.cs apps/api/Codec.Api/Migrations/
git commit -m "feat: make user FK columns nullable for account deletion cascade"
```

---

### Task 6: Frontend API client method

**Files:**
- Modify: `apps/web/src/lib/api/client.ts`

- [ ] **Step 1: Add deleteAccount method to ApiClient**

Add this method to the `ApiClient` class in `apps/web/src/lib/api/client.ts`, in the authenticated methods section (after the existing `me`-related methods):

```typescript
async deleteAccount(
    token: string,
    confirmationText: string,
    password?: string,
    googleCredential?: string
): Promise<{ message: string }> {
    return this.request(`${this.baseUrl}/me`, {
        method: 'DELETE',
        headers: this.headers(token, true),
        body: JSON.stringify({ confirmationText, password, googleCredential })
    });
}

async getOwnedServers(token: string): Promise<{ id: string; name: string }[]> {
    return this.request(`${this.baseUrl}/me/owned-servers`, {
        method: 'GET',
        headers: this.headers(token)
    });
}
```

Wait — we don't actually need a separate `getOwnedServers` endpoint. The delete endpoint returns owned servers in the error response. Remove the `getOwnedServers` method. Just add `deleteAccount`:

```typescript
async deleteAccount(
    token: string,
    confirmationText: string,
    password?: string,
    googleCredential?: string
): Promise<{ message: string }> {
    return this.request(`${this.baseUrl}/me`, {
        method: 'DELETE',
        headers: this.headers(token, true),
        body: JSON.stringify({ confirmationText, password, googleCredential })
    });
}
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat: add deleteAccount method to ApiClient"
```

---

### Task 7: Auth store deleteAccount method

**Files:**
- Modify: `apps/web/src/lib/state/auth-store.svelte.ts`

- [ ] **Step 1: Make googleClientId accessible**

In `apps/web/src/lib/state/auth-store.svelte.ts`, change the constructor parameter visibility:

```typescript
// Change from:
private readonly googleClientId: string
// To:
readonly googleClientId: string
```

- [ ] **Step 2: Add deleteAccount method to AuthStore**

Add this method to the `AuthStore` class, after the `signOut` method:

```typescript
async deleteAccount(password?: string, googleCredential?: string): Promise<void> {
    if (!this.idToken) return;
    await this.api.deleteAccount(this.idToken, 'DELETE', password, googleCredential);

    // Clean up same as sign-out
    if (this.onSignedOut) {
        await this.onSignedOut();
    }
    clearStoredSession();
    this.ui.isInitialLoading = false;
    this.ui.isHubConnected = false;
    this.idToken = null;
    this.me = null;
    this.status = 'Signed out';

    await tick();
    renderGoogleButton('google-button');
    renderGoogleButton('login-google-button');
}
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/state/auth-store.svelte.ts
git commit -m "feat: add deleteAccount method to AuthStore"
```

---

### Task 8: Delete Account confirmation modal

**Files:**
- Create: `apps/web/src/lib/components/settings/DeleteAccountModal.svelte`

- [ ] **Step 1: Create the modal component**

```svelte
<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { ApiError } from '$lib/api/client.js';
	import { renderGoogleButton, initGoogleIdentity } from '$lib/auth/google.js';

	let {
		onclose
	}: {
		onclose: () => void;
	} = $props();

	const auth = getAuthStore();
	const ui = getUIStore();

	let password = $state('');
	let confirmationText = $state('');
	let error = $state('');
	let isDeleting = $state(false);
	let ownedServers = $state<{ id: string; name: string }[]>([]);
	let googleCredential = $state<string | null>(null);

	const isGoogleOnly = $derived(
		auth.authType === 'google' && !!auth.me?.user.googleSubject
	);

	const canSubmit = $derived(
		confirmationText === 'DELETE' &&
		(isGoogleOnly ? googleCredential !== null : password.length > 0) &&
		!isDeleting
	);

	function handleGoogleCredential(credential: string) {
		googleCredential = credential;
	}

	// Note: requires making googleClientId public on AuthStore (change
	// "private readonly googleClientId" to "readonly googleClientId" in auth-store.svelte.ts)
	$effect(() => {
		if (isGoogleOnly) {
			// Render a Google sign-in button for re-authentication
			initGoogleIdentity(
				auth.googleClientId,
				(token) => handleGoogleCredential(token),
				{ renderButtonIds: ['delete-google-button'], autoSelect: false }
			);
		}
	});

	async function handleDelete() {
		error = '';
		isDeleting = true;
		ownedServers = [];

		try {
			if (isGoogleOnly) {
				await auth.deleteAccount(undefined, googleCredential ?? undefined);
			} else {
				await auth.deleteAccount(password);
			}
			onclose();
			ui.closeSettings();
		} catch (e) {
			if (e instanceof ApiError) {
				error = e.message ?? 'Failed to delete account.';
			} else {
				error = 'An unexpected error occurred.';
			}
		} finally {
			isDeleting = false;
		}
	}
</script>

<div class="modal-backdrop" role="presentation" onclick={onclose}>
	<div class="modal" role="dialog" aria-modal="true" aria-labelledby="delete-title" onclick|stopPropagation>
		<h2 id="delete-title" class="modal-title">Delete Account</h2>

		<div class="warning">
			<p><strong>This action is permanent and cannot be undone.</strong></p>
			<ul>
				<li>Your messages will remain but show as "Deleted User"</li>
				<li>All server memberships will be removed</li>
				<li>All friendships will be removed</li>
				<li>Your account data will be permanently erased</li>
			</ul>
		</div>

		{#if ownedServers.length > 0}
			<div class="owned-servers-warning">
				<p>You must transfer ownership of these servers before deleting your account:</p>
				<ul>
					{#each ownedServers as server}
						<li>{server.name}</li>
					{/each}
				</ul>
			</div>
		{:else}
			{#if isGoogleOnly}
				<div class="field">
					<span class="field-label">Re-authenticate with Google</span>
					{#if googleCredential}
						<p class="google-verified">Google identity verified</p>
					{:else}
						<div id="delete-google-button"></div>
					{/if}
				</div>
			{:else}
				<label class="field">
					<span class="field-label">Password</span>
					<input
						type="password"
						bind:value={password}
						placeholder="Enter your password"
						autocomplete="current-password"
					/>
				</label>
			{/if}

			<label class="field">
				<span class="field-label">Type <strong>DELETE</strong> to confirm</span>
				<input
					type="text"
					bind:value={confirmationText}
					placeholder="DELETE"
					autocomplete="off"
				/>
			</label>

			{#if error}
				<p class="error">{error}</p>
			{/if}

			<div class="actions">
				<button class="cancel-btn" onclick={onclose} disabled={isDeleting}>Cancel</button>
				<button
					class="delete-btn"
					onclick={handleDelete}
					disabled={!canSubmit}
				>
					{isDeleting ? 'Deleting...' : 'Delete My Account'}
				</button>
			</div>
		{/if}
	</div>
</div>

<style>
	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.7);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
	}

	.modal {
		background: var(--bg-primary);
		border-radius: 8px;
		padding: 24px;
		max-width: 440px;
		width: 90%;
		max-height: 80vh;
		overflow-y: auto;
	}

	.modal-title {
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		margin: 0 0 16px;
	}

	.warning {
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.1);
		border: 1px solid var(--danger);
		border-radius: 4px;
		padding: 12px 16px;
		margin-bottom: 16px;
		font-size: 14px;
		color: var(--text-normal);
	}

	.warning ul {
		margin: 8px 0 0;
		padding-left: 20px;
	}

	.warning li {
		margin: 4px 0;
	}

	.owned-servers-warning {
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.1);
		border: 1px solid var(--danger);
		border-radius: 4px;
		padding: 12px 16px;
		font-size: 14px;
		color: var(--text-normal);
	}

	.owned-servers-warning ul {
		margin: 8px 0 0;
		padding-left: 20px;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 4px;
		margin-bottom: 12px;
	}

	.field-label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.field input {
		padding: 10px 12px;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		outline: none;
	}

	.field input:focus {
		border-color: var(--accent);
	}

	.google-verified {
		color: var(--text-positive, #43b581);
		font-size: 14px;
		margin: 4px 0 0;
	}

	.error {
		color: var(--danger);
		font-size: 13px;
		margin: 0 0 12px;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 8px;
		margin-top: 16px;
	}

	.cancel-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
	}

	.cancel-btn:hover {
		text-decoration: underline;
	}

	.delete-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.delete-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.delete-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/settings/DeleteAccountModal.svelte
git commit -m "feat: add DeleteAccountModal component"
```

---

### Task 9: Add delete section to AccountSettings

**Files:**
- Modify: `apps/web/src/lib/components/settings/AccountSettings.svelte`

- [ ] **Step 1: Update AccountSettings.svelte**

Replace the entire content of `apps/web/src/lib/components/settings/AccountSettings.svelte` with:

```svelte
<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import DeleteAccountModal from './DeleteAccountModal.svelte';

	const auth = getAuthStore();
	const ui = getUIStore();

	let showDeleteModal = $state(false);
</script>

<div class="account-settings" role="tabpanel" aria-labelledby="tab-account">
	<h2 class="section-title">My Account</h2>

	{#if auth.me}
		<div class="info-grid">
			<div class="info-row">
				<span class="info-label">Email</span>
				<span class="info-value">{auth.me.user.email ?? '—'}</span>
			</div>
			<div class="info-row">
				<span class="info-label">Google Display Name</span>
				<span class="info-value">{auth.me.user.displayName}</span>
			</div>
		</div>

		<div class="sign-out-section">
			<button class="sign-out-btn" onclick={() => { ui.closeSettings(); auth.signOut(); }}>
				Sign Out
			</button>
		</div>

		<div class="danger-zone">
			<h3 class="danger-title">Delete Account</h3>
			<p class="danger-description">
				Permanently delete your account. Your messages will remain but show as "Deleted User."
			</p>
			<button class="delete-account-btn" onclick={() => showDeleteModal = true}>
				Delete Account
			</button>
		</div>
	{/if}
</div>

{#if showDeleteModal}
	<DeleteAccountModal onclose={() => showDeleteModal = false} />
{/if}

<style>
	.account-settings {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.section-title {
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		margin: 0;
	}

	.info-grid {
		display: flex;
		flex-direction: column;
		gap: 16px;
		padding: 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
		border: 1px solid var(--border);
	}

	.info-row {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.info-label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.info-value {
		font-size: 15px;
		color: var(--text-normal);
	}

	.sign-out-section {
		padding-top: 8px;
	}

	.sign-out-btn {
		padding: 8px 16px;
		min-height: 44px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.sign-out-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.sign-out-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.danger-zone {
		margin-top: 16px;
		padding: 16px;
		border: 1px solid var(--danger);
		border-radius: 8px;
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.05);
	}

	.danger-title {
		font-size: 16px;
		font-weight: 700;
		color: var(--danger);
		margin: 0 0 4px;
	}

	.danger-description {
		font-size: 14px;
		color: var(--text-muted);
		margin: 0 0 12px;
	}

	.delete-account-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: transparent;
		color: var(--danger);
		border: 1px solid var(--danger);
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.delete-account-btn:hover {
		background: var(--danger);
		color: #fff;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/settings/AccountSettings.svelte
git commit -m "feat: add delete account section to AccountSettings"
```

---

### Task 10: Handle "Deleted User" display in message rendering

**Files:**
- Modify: `apps/web/src/lib/components/chat/MessageFeed.svelte` (check how null authorUserId is displayed)
- Modify: `apps/web/src/lib/components/dm/DmChatArea.svelte` (same for DMs)

The API already returns `authorName` as a denormalized field on messages. When `AuthorUserId` is null (deleted user), the API projections already return the stored `AuthorName`. However, we want to show "Deleted User" instead.

- [ ] **Step 1: Check how messages with null authorUserId display**

Read the message rendering in `MessageFeed.svelte` and `DmChatArea.svelte` to see how `authorName` and `authorUserId` are used. If the frontend already uses `authorName` from the API response, the display will work but show the original name. To show "Deleted User," we need to check `authorUserId === null` in the rendering.

Look for patterns like:
```svelte
{message.authorName}
```

And change to:
```svelte
{message.authorUserId ? message.authorName : 'Deleted User'}
```

Apply this pattern wherever author names are displayed for messages.

- [ ] **Step 2: Apply the pattern to MessageFeed.svelte**

Find all places where `message.authorName` is displayed and wrap with the null check. Also apply a faded style class for deleted user names.

- [ ] **Step 3: Apply the pattern to DmChatArea.svelte**

Same treatment for DM messages.

- [ ] **Step 4: Run frontend type check**

```bash
cd apps/web && npm run check
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/lib/components/chat/MessageFeed.svelte apps/web/src/lib/components/dm/DmChatArea.svelte
git commit -m "feat: show 'Deleted User' for messages with null authorUserId"
```

---

### Task 11: Handle SignalR AccountDeleted event

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts` (add AccountDeleted callback type)
- Modify: `apps/web/src/lib/state/signalr.svelte.ts` (handle the event)

- [ ] **Step 1: Add AccountDeleted to SignalR callback types**

In `apps/web/src/lib/services/chat-hub.ts`, add `AccountDeleted` to the callback registration. Find the section where callbacks are registered and add:

```typescript
connection.on('AccountDeleted', () => {
    callbacks.onAccountDeleted?.();
});
```

And add to the callbacks interface:

```typescript
onAccountDeleted?: () => void;
```

- [ ] **Step 2: Wire up the callback in signalr.svelte.ts**

In the SignalR state setup, handle `onAccountDeleted` by calling `auth.signOut()`:

```typescript
onAccountDeleted: () => {
    auth.signOut();
}
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/services/chat-hub.ts apps/web/src/lib/state/signalr.svelte.ts
git commit -m "feat: handle AccountDeleted SignalR event with forced sign-out"
```

---

### Task 12: Frontend tests

**Files:**
- Create: `apps/web/src/lib/components/settings/DeleteAccountModal.test.ts`

- [ ] **Step 1: Write tests for the DeleteAccountModal**

```typescript
import { describe, it, expect } from 'vitest';

describe('DeleteAccountModal', () => {
    it('should require DELETE confirmation text', () => {
        // Verify the canSubmit logic
        const confirmationText = 'DELETE';
        const password = 'mypassword';
        const isDeleting = false;
        const isGoogleOnly = false;

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly || password.length > 0) &&
            !isDeleting;

        expect(canSubmit).toBe(true);
    });

    it('should not allow submit with wrong confirmation text', () => {
        const confirmationText = 'delete';
        const password = 'mypassword';
        const isDeleting = false;
        const isGoogleOnly = false;

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly || password.length > 0) &&
            !isDeleting;

        expect(canSubmit).toBe(false);
    });

    it('should not allow submit without password for non-Google users', () => {
        const confirmationText = 'DELETE';
        const password = '';
        const isDeleting = false;
        const isGoogleOnly = false;

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly || password.length > 0) &&
            !isDeleting;

        expect(canSubmit).toBe(false);
    });

    it('should allow submit with Google credential for Google-only users', () => {
        const confirmationText = 'DELETE';
        const isDeleting = false;
        const isGoogleOnly = true;
        const googleCredential: string | null = 'some-credential';

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly ? googleCredential !== null : false) &&
            !isDeleting;

        expect(canSubmit).toBe(true);
    });

    it('should not allow submit without Google credential for Google-only users', () => {
        const confirmationText = 'DELETE';
        const isDeleting = false;
        const isGoogleOnly = true;
        const googleCredential: string | null = null;

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly ? googleCredential !== null : false) &&
            !isDeleting;

        expect(canSubmit).toBe(false);
    });

    it('should not allow submit while deleting', () => {
        const confirmationText = 'DELETE';
        const password = 'mypassword';
        const isDeleting = true;
        const isGoogleOnly = false;

        const canSubmit =
            confirmationText === 'DELETE' &&
            (isGoogleOnly || password.length > 0) &&
            !isDeleting;

        expect(canSubmit).toBe(false);
    });
});
```

- [ ] **Step 2: Run frontend tests**

```bash
cd apps/web && npm test -- --run src/lib/components/settings/DeleteAccountModal.test.ts
```

Expected: All 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/components/settings/DeleteAccountModal.test.ts
git commit -m "test: add DeleteAccountModal unit tests"
```

---

### Task 13: Final verification

- [ ] **Step 1: Run all API tests**

```bash
cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --verbosity normal
```

Expected: All tests pass.

- [ ] **Step 2: Run all frontend checks**

```bash
cd apps/web && npm run check && npm test -- --run
```

Expected: No type errors, all tests pass.

- [ ] **Step 3: Build API**

```bash
cd apps/api/Codec.Api && dotnet build
```

Expected: Build succeeds.

- [ ] **Step 4: Build frontend**

```bash
cd apps/web && npm run build
```

Expected: Build succeeds.

- [ ] **Step 5: Final commit if any cleanup needed**

If any issues were found and fixed during verification, commit them here.
