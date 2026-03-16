# Email Verification Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add email verification to email/password registration so users must verify their email before accessing the app (hard gate).

**Architecture:** New fields on the `User` entity track verification state. An `IEmailSender` abstraction sends verification emails (console in dev, Azure Communication Services in prod). A `[RequireEmailVerified]` action filter gates data-loading endpoints. The frontend adds a `/verify` route and a `VerificationGate` component that blocks unverified users.

**Tech Stack:** ASP.NET Core 10, EF Core, Azure.Communication.Email, SvelteKit 5, Vitest

**Spec:** `docs/superpowers/specs/2026-03-15-email-verification-design.md`

---

## Chunk 1: Backend Data Model & Email Service

### Task 1: Add EmailVerified fields to User model

**Files:**
- Modify: `apps/api/Codec.Api/Models/User.cs:43` (after LockoutEnd property)

- [ ] **Step 1: Add verification properties to User**

Add these properties after `LockoutEnd` (line 43) in `apps/api/Codec.Api/Models/User.cs`:

```csharp
/// <summary>
/// Whether the user has verified their email address.
/// </summary>
public bool EmailVerified { get; set; }

/// <summary>
/// SHA-256 hash of the email verification token. Null when no pending verification.
/// </summary>
public string? EmailVerificationToken { get; set; }

/// <summary>
/// When the email verification token expires.
/// </summary>
public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }

/// <summary>
/// When the last verification email was sent (for rate limiting resends).
/// </summary>
public DateTimeOffset? EmailVerificationTokenSentAt { get; set; }
```

- [ ] **Step 2: Add index configuration in CodecDbContext**

In `apps/api/Codec.Api/Data/CodecDbContext.cs`, after the existing `User` Email unique index block (around line 50), add:

```csharp
modelBuilder.Entity<User>()
    .HasIndex(user => user.EmailVerificationToken)
    .IsUnique()
    .HasFilter("\"EmailVerificationToken\" IS NOT NULL");
```

- [ ] **Step 3: Set EmailVerified=true for Google users in UserService**

In `apps/api/Codec.Api/Services/UserService.cs`, in the `GetOrCreateUserAsync` method where a new Google user is created (line 66-72), add `EmailVerified = true`:

```csharp
var appUser = new User
{
    GoogleSubject = subject,
    DisplayName = displayName,
    Email = email,
    AvatarUrl = avatarUrl,
    EmailVerified = true
};
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Models/User.cs apps/api/Codec.Api/Data/CodecDbContext.cs apps/api/Codec.Api/Services/UserService.cs
git commit -m "feat: add email verification fields to User model"
```

### Task 2: Create EF Core migration

**Files:**
- Create: `apps/api/Codec.Api/Migrations/20260316000000_AddEmailVerification.cs`

- [ ] **Step 1: Write the migration file**

Create `apps/api/Codec.Api/Migrations/20260316000000_AddEmailVerification.cs`:

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerificationTokenExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerificationTokenSentAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailVerificationToken",
                table: "Users",
                column: "EmailVerificationToken",
                unique: true,
                filter: "\"EmailVerificationToken\" IS NOT NULL");

            // Existing users (Google Sign-In) should be marked as verified
            migrationBuilder.Sql("UPDATE \"Users\" SET \"EmailVerified\" = true WHERE \"GoogleSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailVerificationToken",
                table: "Users");

            migrationBuilder.DropColumn(name: "EmailVerified", table: "Users");
            migrationBuilder.DropColumn(name: "EmailVerificationToken", table: "Users");
            migrationBuilder.DropColumn(name: "EmailVerificationTokenExpiresAt", table: "Users");
            migrationBuilder.DropColumn(name: "EmailVerificationTokenSentAt", table: "Users");
        }
    }
}
```

- [ ] **Step 2: Update the model snapshot**

The model snapshot at `apps/api/Codec.Api/Migrations/CodecDbContextModelSnapshot.cs` needs to be updated to include the new columns. Add the four new column definitions in the `User` entity builder section (alphabetically among the other properties), and add the new index definition. Look at how existing columns like `FailedLoginAttempts` and `LockoutEnd` are defined in the snapshot and follow that pattern.

- [ ] **Step 3: Build to verify**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Migrations/
git commit -m "feat: add email verification migration"
```

### Task 3: Create IEmailSender interface and ConsoleEmailSender

**Files:**
- Create: `apps/api/Codec.Api/Services/IEmailSender.cs`
- Create: `apps/api/Codec.Api/Services/ConsoleEmailSender.cs`

- [ ] **Step 1: Create IEmailSender interface**

Create `apps/api/Codec.Api/Services/IEmailSender.cs`:

```csharp
namespace Codec.Api.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}
```

- [ ] **Step 2: Create ConsoleEmailSender**

Create `apps/api/Codec.Api/Services/ConsoleEmailSender.cs`:

```csharp
namespace Codec.Api.Services;

public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        logger.LogInformation(
            "═══ EMAIL ═══\nTo: {To}\nSubject: {Subject}\nBody:\n{Body}\n═════════════",
            to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Create AzureEmailSender**

Create `apps/api/Codec.Api/Services/AzureEmailSender.cs`:

```csharp
using Azure.Communication.Email;

namespace Codec.Api.Services;

public class AzureEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var connectionString = configuration["Email:ConnectionString"]
            ?? throw new InvalidOperationException("Email:ConnectionString is required.");
        var senderAddress = configuration["Email:SenderAddress"]
            ?? throw new InvalidOperationException("Email:SenderAddress is required.");

        var client = new EmailClient(connectionString);
        await client.SendAsync(
            Azure.WaitUntil.Completed,
            senderAddress,
            to,
            subject,
            htmlBody);
    }
}
```

- [ ] **Step 4: Add Azure.Communication.Email NuGet package**

Run: `dotnet add apps/api/Codec.Api/Codec.Api.csproj package Azure.Communication.Email`

- [ ] **Step 5: Register IEmailSender in Program.cs**

In `apps/api/Codec.Api/Program.cs`, after the `TokenService` registration (line 200), add:

```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, AzureEmailSender>();
```

- [ ] **Step 6: Add Email config to appsettings**

In `apps/api/Codec.Api/appsettings.json`, add:

```json
"Email": {
    "ConnectionString": "",
    "SenderAddress": "noreply@codec.app"
},
"Frontend": {
    "BaseUrl": "http://localhost:5174"
}
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add apps/api/Codec.Api/Services/IEmailSender.cs apps/api/Codec.Api/Services/ConsoleEmailSender.cs apps/api/Codec.Api/Services/AzureEmailSender.cs apps/api/Codec.Api/Program.cs apps/api/Codec.Api/Codec.Api.csproj apps/api/Codec.Api/appsettings.json
git commit -m "feat: add IEmailSender with console and Azure implementations"
```

### Task 4: Create EmailVerificationService

**Files:**
- Create: `apps/api/Codec.Api/Services/EmailVerificationService.cs`

- [ ] **Step 1: Create EmailVerificationService**

Create `apps/api/Codec.Api/Services/EmailVerificationService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class EmailVerificationService(
    CodecDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    public async Task<string> GenerateAndSendVerificationAsync(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        user.EmailVerificationToken = tokenHash;
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        user.EmailVerificationTokenSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var frontendBaseUrl = configuration["Frontend:BaseUrl"]
            ?? configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()
            ?? "http://localhost:5174";

        var verifyUrl = $"{frontendBaseUrl.TrimEnd('/')}/verify?token={Uri.EscapeDataString(rawToken)}";

        var htmlBody = $"""
            <div style="font-family: sans-serif; max-width: 480px; margin: 0 auto;">
                <h2>Verify your Codec email</h2>
                <p>Click the button below to verify your email address:</p>
                <a href="{verifyUrl}"
                   style="display: inline-block; padding: 12px 24px; background: #5865F2; color: white;
                          text-decoration: none; border-radius: 4px; font-weight: 600;">
                    Verify Email
                </a>
                <p style="margin-top: 24px; color: #666; font-size: 14px;">
                    Or copy this link: {verifyUrl}
                </p>
                <p style="color: #999; font-size: 12px;">This link expires in 24 hours.</p>
            </div>
            """;

        await emailSender.SendEmailAsync(user.Email!, "Verify your Codec email", htmlBody);

        return rawToken;
    }

    public async Task<User?> VerifyTokenAsync(string rawToken)
    {
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == tokenHash);
        if (user is null) return null;
        if (user.EmailVerificationTokenExpiresAt < DateTimeOffset.UtcNow) return null;
        if (user.EmailVerified) return null;

        user.EmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.EmailVerificationTokenSentAt = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return user;
    }

    public bool CanResend(User user)
    {
        if (user.EmailVerified) return false;
        if (user.EmailVerificationTokenSentAt is not null
            && user.EmailVerificationTokenSentAt.Value.Add(ResendCooldown) > DateTimeOffset.UtcNow)
            return false;
        return true;
    }
}
```

- [ ] **Step 2: Register EmailVerificationService in Program.cs**

In `apps/api/Codec.Api/Program.cs`, after the `IEmailSender` registration, add:

```csharp
builder.Services.AddScoped<EmailVerificationService>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api/Services/EmailVerificationService.cs apps/api/Codec.Api/Program.cs
git commit -m "feat: add EmailVerificationService for token generation and validation"
```

## Chunk 2: Backend Endpoints & Action Filter

### Task 5: Create RequireEmailVerified action filter

**Files:**
- Create: `apps/api/Codec.Api/Filters/RequireEmailVerifiedAttribute.cs`

- [ ] **Step 1: Create the filter**

Create `apps/api/Codec.Api/Filters/RequireEmailVerifiedAttribute.cs`:

```csharp
using Codec.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireEmailVerifiedAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            await next();
            return;
        }

        // Google users are always verified
        var issuer = user.FindFirst("iss")?.Value;
        if (issuer is "https://accounts.google.com" or "accounts.google.com")
        {
            await next();
            return;
        }

        var sub = user.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            await next();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<CodecDbContext>();
        var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (dbUser is not null && !dbUser.EmailVerified)
        {
            context.Result = new ObjectResult(new { code = "email_not_verified" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
```

- [ ] **Step 2: Apply filter to gated controllers**

Add `[RequireEmailVerified]` attribute to controllers that should be gated. Apply it at the controller level for:
- `ServersController` (at class level, line 16 area)
- `ChannelsController` (at class level)
- `DmController` (at class level)
- `FriendsController` (at class level)

Do NOT apply it to:
- `AuthController` — all auth endpoints must remain accessible
- `UsersController` — `/me` must remain accessible for the frontend to check verification status

Add the using directive `using Codec.Api.Filters;` to each file where the attribute is applied.

- [ ] **Step 3: Add emailVerified to /me response**

In `apps/api/Codec.Api/Controllers/UsersController.cs`, in the `Me()` method's response object (around line 52), add `appUser.EmailVerified` to the user object:

```csharp
user = new
{
    appUser.Id,
    appUser.DisplayName,
    appUser.Nickname,
    EffectiveDisplayName = effectiveDisplayName,
    appUser.Email,
    AvatarUrl = effectiveAvatarUrl,
    appUser.GoogleSubject,
    appUser.IsGlobalAdmin,
    appUser.EmailVerified
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Filters/ apps/api/Codec.Api/Controllers/
git commit -m "feat: add RequireEmailVerified filter and apply to data controllers"
```

### Task 6: Add verification endpoints to AuthController

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/AuthController.cs`
- Create: `apps/api/Codec.Api/Models/VerifyEmailRequest.cs`

- [ ] **Step 1: Create request model**

Create `apps/api/Codec.Api/Models/VerifyEmailRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Update AuthController constructor to accept new dependencies**

In `apps/api/Codec.Api/Controllers/AuthController.cs`, update the constructor (line 18-22) to add `EmailVerificationService` and `IEmailSender`:

```csharp
public class AuthController(
    CodecDbContext db,
    TokenService tokenService,
    IAvatarService avatarService,
    IConfiguration configuration,
    EmailVerificationService emailVerificationService) : ControllerBase
```

- [ ] **Step 3: Update Register to send verification email**

In the `Register` method, after `await db.SaveChangesAsync()` (around line 67-72), before generating access/refresh tokens, add:

```csharp
await emailVerificationService.GenerateAndSendVerificationAsync(user);
```

And add `EmailVerified = user.EmailVerified` to the response user object (after `IsGlobalAdmin` on line 91):

```csharp
user = new
{
    user.Id,
    user.DisplayName,
    user.Nickname,
    EffectiveDisplayName = user.EffectiveDisplayName,
    user.Email,
    AvatarUrl = effectiveAvatarUrl,
    user.IsGlobalAdmin,
    user.EmailVerified
}
```

- [ ] **Step 4: Update Login response to include EmailVerified**

In the `Login` method response (line 148-162), add `EmailVerified = user.EmailVerified` to the user object:

```csharp
user = new
{
    user.Id,
    user.DisplayName,
    user.Nickname,
    EffectiveDisplayName = user.EffectiveDisplayName,
    user.Email,
    AvatarUrl = effectiveAvatarUrl,
    user.IsGlobalAdmin,
    user.EmailVerified
}
```

- [ ] **Step 5: Add verify-email endpoint**

Add this method to `AuthController`, after the `Logout` method (after line 194):

```csharp
[HttpPost("verify-email")]
public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
{
    var user = await emailVerificationService.VerifyTokenAsync(request.Token);
    if (user is null)
    {
        return BadRequest(new { error = "Invalid or expired verification token." });
    }

    return Ok(new { message = "Email verified successfully." });
}
```

- [ ] **Step 6: Add resend-verification endpoint**

Add this method after `VerifyEmail`:

```csharp
[HttpPost("resend-verification")]
[Microsoft.AspNetCore.Authorization.Authorize]
public async Task<IActionResult> ResendVerification()
{
    var sub = User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var userId))
        return Unauthorized();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user is null) return Unauthorized();

    if (user.EmailVerified)
        return BadRequest(new { error = "Email is already verified." });

    if (!emailVerificationService.CanResend(user))
        return StatusCode(429, new { error = "Please wait before requesting another verification email." });

    await emailVerificationService.GenerateAndSendVerificationAsync(user);
    return Ok(new { message = "Verification email sent." });
}
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add apps/api/Codec.Api/Controllers/AuthController.cs apps/api/Codec.Api/Models/VerifyEmailRequest.cs
git commit -m "feat: add verify-email and resend-verification endpoints"
```

## Chunk 3: Backend Tests

### Task 7: Unit tests for EmailVerificationService

**Files:**
- Create: `apps/api/Codec.Api.Tests/Services/EmailVerificationServiceTests.cs`

- [ ] **Step 1: Write tests**

Create `apps/api/Codec.Api.Tests/Services/EmailVerificationServiceTests.cs`:

```csharp
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Services;

public class EmailVerificationServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly EmailVerificationService _service;

    public EmailVerificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:5174"
            })
            .Build();

        _service = new EmailVerificationService(_db, _emailSender.Object, config);
    }

    public void Dispose() => _db.Dispose();

    private async Task<User> CreateUnverifiedUser(string email = "test@test.com")
    {
        var user = new User { Email = email, PasswordHash = "hash", DisplayName = "Test", Nickname = "Test" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GenerateAndSend_StoresHashedToken_SendsEmail()
    {
        var user = await CreateUnverifiedUser();

        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        rawToken.Should().NotBeNullOrEmpty();
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
        user.EmailVerificationToken.Should().NotBe(rawToken); // Should be hashed
        user.EmailVerificationTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        user.EmailVerificationTokenSentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        _emailSender.Verify(e => e.SendEmailAsync("test@test.com", "Verify your Codec email", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task VerifyToken_ValidToken_SetsEmailVerified()
    {
        var user = await CreateUnverifiedUser();
        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        var result = await _service.VerifyTokenAsync(rawToken);

        result.Should().NotBeNull();
        result!.EmailVerified.Should().BeTrue();
        result.EmailVerificationToken.Should().BeNull();
        result.EmailVerificationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_InvalidToken_ReturnsNull()
    {
        var result = await _service.VerifyTokenAsync("not-a-real-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_ExpiredToken_ReturnsNull()
    {
        var user = await CreateUnverifiedUser();
        await _service.GenerateAndSendVerificationAsync(user);

        // Manually expire the token
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        var result = await _service.VerifyTokenAsync("any-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_AlreadyVerified_ReturnsNull()
    {
        var user = await CreateUnverifiedUser();
        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        // Verify once
        await _service.VerifyTokenAsync(rawToken);

        // Try to verify again with same token pattern
        var result = await _service.VerifyTokenAsync(rawToken);
        result.Should().BeNull();
    }

    [Fact]
    public void CanResend_AlreadyVerified_ReturnsFalse()
    {
        var user = new User { EmailVerified = true };
        _service.CanResend(user).Should().BeFalse();
    }

    [Fact]
    public void CanResend_WithinCooldown_ReturnsFalse()
    {
        var user = new User
        {
            EmailVerified = false,
            EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddSeconds(-30)
        };
        _service.CanResend(user).Should().BeFalse();
    }

    [Fact]
    public void CanResend_AfterCooldown_ReturnsTrue()
    {
        var user = new User
        {
            EmailVerified = false,
            EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        _service.CanResend(user).Should().BeTrue();
    }

    [Fact]
    public void CanResend_NeverSent_ReturnsTrue()
    {
        var user = new User { EmailVerified = false, EmailVerificationTokenSentAt = null };
        _service.CanResend(user).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~EmailVerificationServiceTests"`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api.Tests/Services/EmailVerificationServiceTests.cs
git commit -m "test: add EmailVerificationService unit tests"
```

### Task 8: Unit tests for AuthController verification endpoints

**Files:**
- Modify: `apps/api/Codec.Api.Tests/Controllers/AuthControllerTests.cs`

- [ ] **Step 1: Update AuthControllerTests setup**

The `AuthControllerTests` constructor needs to create the `EmailVerificationService` and pass it to `AuthController`. Update the constructor:

Add fields:
```csharp
private readonly Mock<IEmailSender> _emailSender = new();
private readonly EmailVerificationService _emailVerificationService;
```

Initialize in constructor:
```csharp
_emailVerificationService = new EmailVerificationService(_db, _emailSender.Object, _config);
```

Update controller creation:
```csharp
_controller = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService);
```

- [ ] **Step 2: Add test for Register sending verification email**

```csharp
[Fact]
public async Task Register_SendsVerificationEmail_UserNotVerified()
{
    var request = new RegisterRequest
    {
        Email = "verify@test.com",
        Password = "StrongPass1!",
        Nickname = "VerifyUser"
    };

    await _controller.Register(request);

    var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "verify@test.com");
    user.Should().NotBeNull();
    user!.EmailVerified.Should().BeFalse();
    user.EmailVerificationToken.Should().NotBeNullOrEmpty();
    _emailSender.Verify(e => e.SendEmailAsync("verify@test.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
}
```

- [ ] **Step 3: Add test for verify-email endpoint**

```csharp
[Fact]
public async Task VerifyEmail_ValidToken_Returns200()
{
    var user = await CreateUserWithPassword("v@test.com");
    var rawToken = await _emailVerificationService.GenerateAndSendVerificationAsync(user);

    var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = rawToken });

    result.Should().BeOfType<OkObjectResult>();
    var updated = await _db.Users.FirstAsync(u => u.Id == user.Id);
    updated.EmailVerified.Should().BeTrue();
}

[Fact]
public async Task VerifyEmail_InvalidToken_Returns400()
{
    var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = "bad-token" });
    result.Should().BeOfType<BadRequestObjectResult>();
}
```

- [ ] **Step 4: Run all AuthController tests**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~AuthControllerTests"`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api.Tests/Controllers/AuthControllerTests.cs
git commit -m "test: add email verification tests to AuthControllerTests"
```

### Task 9: Run full backend test suite

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj`
Expected: All tests pass. Fix any failures caused by the new `EmailVerificationService` constructor parameter requirement on `AuthController`.

- [ ] **Step 2: Commit any fixes**

If fixes were needed:
```bash
git add -A
git commit -m "fix: update existing tests for email verification dependencies"
```

## Chunk 4: Frontend Changes

### Task 10: Update TypeScript types

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`

- [ ] **Step 1: Add emailVerified to AuthResponse and UserProfile types**

In `apps/web/src/lib/types/models.ts`:

Add `emailVerified?: boolean;` to the `AuthResponse.user` object (after `isGlobalAdmin` on line 144):

```typescript
export type AuthResponse = {
	accessToken: string;
	refreshToken: string;
	user: {
		id: string;
		displayName: string;
		nickname?: string | null;
		effectiveDisplayName: string;
		email?: string;
		avatarUrl?: string;
		isGlobalAdmin?: boolean;
		emailVerified?: boolean;
	};
};
```

Add `emailVerified?: boolean;` to the `UserProfile.user` object (after `isGlobalAdmin` on line 126):

```typescript
export type UserProfile = {
	user: {
		id: string;
		displayName: string;
		nickname?: string | null;
		effectiveDisplayName: string;
		email?: string;
		avatarUrl?: string;
		isGlobalAdmin?: boolean;
		emailVerified?: boolean;
	};
	isNewUser?: boolean;
	needsLinking?: boolean;
	email?: string;
};
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/types/models.ts
git commit -m "feat: add emailVerified to frontend types"
```

### Task 11: Add API client methods

**Files:**
- Modify: `apps/web/src/lib/api/client.ts`

- [ ] **Step 1: Add verifyEmail and resendVerification methods**

In `apps/web/src/lib/api/client.ts`, after the `logout` method (line 158), add:

```typescript
async verifyEmail(token: string): Promise<{ message: string }> {
    return this.requestNoRetry(`${this.baseUrl}/auth/verify-email`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token })
    });
}

async resendVerification(accessToken: string): Promise<{ message: string }> {
    return this.request(`${this.baseUrl}/auth/resend-verification`, {
        method: 'POST',
        headers: this.headers(accessToken)
    });
}
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat: add verifyEmail and resendVerification API methods"
```

### Task 12: Update AppState for verification gate

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

- [ ] **Step 1: Add emailVerified state field**

In `apps/web/src/lib/state/app-state.svelte.ts`, after `pendingGoogleCredential` (line 113), add:

```typescript
emailVerified = $state(true); // Default true so Google users aren't gated
```

- [ ] **Step 2: Update handleLocalAuth to check emailVerified**

Replace the `handleLocalAuth` method (lines 486-506) with:

```typescript
async handleLocalAuth(response: AuthResponse): Promise<void> {
    this.idToken = response.accessToken;
    this.status = 'Signed in';
    persistToken(response.accessToken);
    persistRefreshToken(response.refreshToken);
    setAuthType('local');
    this.authType = 'local';
    this.emailVerified = response.user.emailVerified ?? false;

    if (!this.emailVerified) {
        this.isInitialLoading = false;
        return;
    }

    this.isInitialLoading = true;
    await this.loadMe();

    await Promise.all([
        this.loadServers(),
        this.loadFriends(),
        this.loadFriendRequests(),
        this.loadDmConversations(),
        this.startSignalR()
    ]);
    this.isInitialLoading = false;
    this.showAlphaNotification = true;
}
```

- [ ] **Step 3: Add resendVerification and checkEmailVerified methods**

After the `handleLocalAuth` method, add:

```typescript
async resendVerification(): Promise<void> {
    if (!this.idToken) return;
    await this.api.resendVerification(this.idToken);
}

async checkEmailVerified(): Promise<boolean> {
    if (!this.idToken) return false;
    try {
        const profile = await this.api.getMe(this.idToken);
        if (profile.user.emailVerified) {
            this.emailVerified = true;
            this.me = profile;
            // Now load the full app
            this.isInitialLoading = true;
            await Promise.all([
                this.loadServers(),
                this.loadFriends(),
                this.loadFriendRequests(),
                this.loadDmConversations(),
                this.startSignalR()
            ]);
            this.isInitialLoading = false;
            this.showAlphaNotification = true;
            return true;
        }
    } catch {
        // ignore
    }
    return false;
}
```

- [ ] **Step 4: Update init to check emailVerified on session restore**

In the `init` method, find where it restores a local auth session (look for where it checks `getAuthType() === 'local'` and loads the stored token). After restoring the token but before loading app data, the `loadMe` call should check `emailVerified`. The existing flow loads `/me` and then loads servers etc. After `loadMe` completes, add a check:

```typescript
if (this.authType === 'local' && this.me && !this.me.user.emailVerified) {
    this.emailVerified = false;
    this.isInitialLoading = false;
    return;
}
```

- [ ] **Step 5: Build to verify**

Run: `cd apps/web && npm run check`
Expected: No errors

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat: add email verification gate to app state"
```

### Task 13: Create VerificationGate component

**Files:**
- Create: `apps/web/src/lib/components/VerificationGate.svelte`

- [ ] **Step 1: Create the component**

Create `apps/web/src/lib/components/VerificationGate.svelte`:

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let resendCooldown = $state(0);
	let resendError = $state('');
	let resendSuccess = $state(false);
	let checking = $state(false);
	let intervalId: ReturnType<typeof setInterval> | null = null;

	function startCooldown() {
		resendCooldown = 120;
		intervalId = setInterval(() => {
			resendCooldown--;
			if (resendCooldown <= 0 && intervalId) {
				clearInterval(intervalId);
				intervalId = null;
			}
		}, 1000);
	}

	async function handleResend() {
		resendError = '';
		resendSuccess = false;
		try {
			await app.resendVerification();
			resendSuccess = true;
			startCooldown();
		} catch (err: unknown) {
			if (err instanceof Error && err.message.includes('429')) {
				resendError = 'Please wait before requesting another email.';
				startCooldown();
			} else {
				resendError = err instanceof Error ? err.message : 'Failed to resend.';
			}
		}
	}

	async function handleCheck() {
		checking = true;
		const verified = await app.checkEmailVerified();
		if (!verified) {
			checking = false;
		}
	}

	function formatTime(seconds: number): string {
		const m = Math.floor(seconds / 60);
		const s = seconds % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	}
</script>

<div class="verification-overlay">
	<div class="verification-card">
		<div class="icon">&#9993;</div>
		<h2>Check your email</h2>
		<p>We sent a verification link to your email address. Click the link to verify your account and start using Codec.</p>

		<div class="actions">
			<button class="btn-primary" onclick={handleCheck} disabled={checking}>
				{checking ? 'Checking...' : "I've verified my email"}
			</button>

			<button
				class="btn-secondary"
				onclick={handleResend}
				disabled={resendCooldown > 0}
			>
				{resendCooldown > 0
					? `Resend in ${formatTime(resendCooldown)}`
					: 'Resend verification email'}
			</button>
		</div>

		{#if resendSuccess}
			<p class="success">Verification email sent!</p>
		{/if}
		{#if resendError}
			<p class="error">{resendError}</p>
		{/if}

		<button class="btn-link" onclick={() => app.signOut()}>Sign out</button>
	</div>
</div>

<style>
	.verification-overlay {
		position: fixed;
		inset: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-primary, #313338);
		z-index: 100;
	}

	.verification-card {
		text-align: center;
		max-width: 440px;
		padding: 40px;
	}

	.icon {
		font-size: 48px;
		margin-bottom: 16px;
	}

	h2 {
		color: var(--text-primary, #f2f3f5);
		margin-bottom: 8px;
	}

	p {
		color: var(--text-secondary, #b5bac1);
		line-height: 1.5;
		margin-bottom: 24px;
	}

	.actions {
		display: flex;
		flex-direction: column;
		gap: 12px;
		margin-bottom: 16px;
	}

	.btn-primary {
		padding: 12px 24px;
		background: var(--brand-primary, #5865f2);
		color: white;
		border: none;
		border-radius: 4px;
		font-size: 16px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-primary:hover {
		background: var(--brand-hover, #4752c4);
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		padding: 12px 24px;
		background: var(--bg-secondary, #2b2d31);
		color: var(--text-primary, #f2f3f5);
		border: 1px solid var(--border-subtle, #3f4147);
		border-radius: 4px;
		font-size: 14px;
		cursor: pointer;
	}

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-link {
		background: none;
		border: none;
		color: var(--text-muted, #949ba4);
		font-size: 14px;
		cursor: pointer;
		text-decoration: underline;
	}

	.btn-link:hover {
		color: var(--text-secondary, #b5bac1);
	}

	.success {
		color: var(--status-positive, #23a55a);
		font-size: 14px;
	}

	.error {
		color: var(--status-danger, #f23f43);
		font-size: 14px;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/VerificationGate.svelte
git commit -m "feat: add VerificationGate component"
```

### Task 14: Create /verify route

**Files:**
- Create: `apps/web/src/routes/verify/+page.svelte`

- [ ] **Step 1: Create the verify page**

Create `apps/web/src/routes/verify/+page.svelte`:

```svelte
<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import ApiClient from '$lib/api/client.js';

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';

	let status = $state<'loading' | 'success' | 'error'>('loading');
	let errorMessage = $state('');

	onMount(async () => {
		const token = $page.url.searchParams.get('token');
		if (!token) {
			status = 'error';
			errorMessage = 'No verification token provided.';
			return;
		}

		try {
			const api = new ApiClient(apiBaseUrl);
			await api.verifyEmail(token);
			status = 'success';
		} catch (err: unknown) {
			status = 'error';
			errorMessage = err instanceof Error ? err.message : 'Verification failed.';
		}
	});
</script>

<svelte:head>
	<title>Verify Email - Codec</title>
</svelte:head>

<div class="verify-page">
	<div class="verify-card">
		{#if status === 'loading'}
			<div class="icon">&#8987;</div>
			<h2>Verifying your email...</h2>
		{:else if status === 'success'}
			<div class="icon">&#10003;</div>
			<h2>Email verified!</h2>
			<p>Your email has been verified. You can now use Codec.</p>
			<button class="btn-primary" onclick={() => goto('/')}>
				Go to Codec
			</button>
		{:else}
			<div class="icon">&#10007;</div>
			<h2>Verification failed</h2>
			<p>{errorMessage}</p>
			<button class="btn-primary" onclick={() => goto('/')}>
				Go to Codec
			</button>
		{/if}
	</div>
</div>

<style>
	.verify-page {
		display: flex;
		align-items: center;
		justify-content: center;
		min-height: 100vh;
		background: var(--bg-primary, #313338);
	}

	.verify-card {
		text-align: center;
		max-width: 440px;
		padding: 40px;
	}

	.icon {
		font-size: 48px;
		margin-bottom: 16px;
	}

	h2 {
		color: var(--text-primary, #f2f3f5);
		margin-bottom: 8px;
	}

	p {
		color: var(--text-secondary, #b5bac1);
		line-height: 1.5;
		margin-bottom: 24px;
	}

	.btn-primary {
		padding: 12px 24px;
		background: var(--brand-primary, #5865f2);
		color: white;
		border: none;
		border-radius: 4px;
		font-size: 16px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-primary:hover {
		background: var(--brand-hover, #4752c4);
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/routes/verify/
git commit -m "feat: add /verify route for email verification"
```

### Task 15: Wire VerificationGate into +page.svelte

**Files:**
- Modify: `apps/web/src/routes/+page.svelte`

- [ ] **Step 1: Import VerificationGate**

In `apps/web/src/routes/+page.svelte`, add the import (after the other component imports, around line 21):

```typescript
import VerificationGate from '$lib/components/VerificationGate.svelte';
```

- [ ] **Step 2: Add the gate after LoginScreen**

After the `LoginScreen` block (line 146-148), add:

```svelte
{#if app.isSignedIn && !app.emailVerified}
	<VerificationGate />
{/if}
```

- [ ] **Step 3: Build to verify**

Run: `cd apps/web && npm run check`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/routes/+page.svelte
git commit -m "feat: wire VerificationGate into main page"
```

## Chunk 5: Frontend Tests & Documentation

### Task 16: Frontend tests

**Files:**
- Create: `apps/web/src/lib/components/__tests__/VerificationGate.test.ts`
- Create: `apps/web/src/routes/verify/__tests__/page.test.ts`

- [ ] **Step 1: Write VerificationGate tests**

Check existing test patterns in the `apps/web/src` directory first. Follow the same testing patterns (imports, mocking style). Write tests that cover:
- Component renders "Check your email" message
- Resend button starts with no cooldown
- Sign out button calls `app.signOut()`

- [ ] **Step 2: Write /verify route tests**

Write tests that cover:
- Shows loading state initially
- Shows success state after successful verification
- Shows error state when token is invalid

- [ ] **Step 3: Run frontend tests**

Run: `cd apps/web && npm test`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/
git commit -m "test: add frontend email verification tests"
```

### Task 17: Update documentation

**Files:**
- Modify: `docs/AUTH.md`

- [ ] **Step 1: Update AUTH.md**

In `docs/AUTH.md`, update the "No Email Verification" limitation section (around line 366) to document the new email verification flow:

Replace the limitation note with documentation of:
- Email verification is now required for email/password users
- Hard gate: users cannot access the app until verified
- 24-hour token lifetime
- Resend with 2-minute cooldown
- Google users are auto-verified

Also update the "Registration Email Enumeration" section (around line 361) to note that email verification does not change this accepted risk.

- [ ] **Step 2: Update .env.example if needed**

Check if `apps/web/.env.example` needs any changes. The email verification feature doesn't require new frontend env vars, so this should be a no-op.

- [ ] **Step 3: Commit**

```bash
git add docs/AUTH.md
git commit -m "docs: update AUTH.md with email verification documentation"
```

### Task 18: Final verification

- [ ] **Step 1: Run full backend test suite**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj`
Expected: All tests pass

- [ ] **Step 2: Run frontend checks**

Run: `cd apps/web && npm run check`
Expected: No errors

- [ ] **Step 3: Run frontend tests**

Run: `cd apps/web && npm test`
Expected: All tests pass

- [ ] **Step 4: Build both projects**

Run: `dotnet build apps/api/Codec.Api/Codec.Api.csproj && cd apps/web && npm run build`
Expected: Both build successfully
