# Email/Password Authentication Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add email/password registration, login, email verification, and password reset alongside existing Google Sign-In auth.

**Architecture:** Hybrid Identity approach — use ASP.NET Core Identity's `PasswordHasher<T>` for password hashing and `System.Security.Cryptography` for token generation, but keep the existing `User` entity and `UserService` pattern. The API issues its own HMAC-SHA256 JWTs for local users while continuing to validate Google-issued JWTs. A policy scheme routes tokens to the correct validator based on the `iss` claim.

**Tech Stack:** .NET 10, EF Core 10, `Microsoft.Extensions.Identity.Core` (PasswordHasher only), `System.IdentityModel.Tokens.Jwt`, `Azure.Communication.Email`, SvelteKit/Svelte 5

**Design Doc:** `docs/plans/2026-03-06-email-password-auth-design.md`

---

### Task 1: Add NuGet Dependencies

**Files:**
- Modify: `apps/api/Codec.Api/Codec.Api.csproj`

**Step 1: Add required packages**

Add these PackageReferences inside the existing `<ItemGroup>` at line 12 of `Codec.Api.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Identity.Core" Version="10.0.3" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.7.0" />
<PackageReference Include="Azure.Communication.Email" Version="1.0.1" />
```

Note: `Microsoft.Extensions.Identity.Core` provides `PasswordHasher<T>` without the full Identity framework (the legacy `Microsoft.AspNetCore.Identity` 2.x package is incompatible with .NET 10). `System.IdentityModel.Tokens.Jwt` provides JWT creation (we already have `Microsoft.AspNetCore.Authentication.JwtBearer` for validation). `Azure.Communication.Email` provides the ACS email SDK.

**Step 2: Restore packages**

Run: `cd apps/api/Codec.Api && dotnet restore`
Expected: Restore succeeds with no errors.

**Step 3: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build --no-restore`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

**Step 4: Commit**

```bash
git add apps/api/Codec.Api/Codec.Api.csproj
git commit -m "chore(api): add Identity, JWT, and ACS Email NuGet packages"
```

---

### Task 2: Extend the User Entity

**Files:**
- Modify: `apps/api/Codec.Api/Models/User.cs`

**Step 1: Add auth fields to User model**

In `apps/api/Codec.Api/Models/User.cs`, replace the `GoogleSubject` property (line 6) to make it nullable, and add the new auth fields after the existing properties (before `EffectiveDisplayName` on line 36):

Change line 6 from:
```csharp
public string GoogleSubject { get; set; } = string.Empty;
```
to:
```csharp
public string? GoogleSubject { get; set; }
```

Add these properties before the `EffectiveDisplayName` computed property (before line 36):

```csharp
/// <summary>
/// Identifies how the account was created: "google" or "local".
/// </summary>
public string AuthProvider { get; set; } = "google";

/// <summary>
/// Hashed password for local auth users. Null for Google-only users.
/// </summary>
public string? PasswordHash { get; set; }

/// <summary>
/// Whether the user's email address has been verified.
/// Always true for Google users; false until verified for local users.
/// </summary>
public bool EmailConfirmed { get; set; }

public string? EmailConfirmationToken { get; set; }
public DateTimeOffset? EmailConfirmationTokenExpiry { get; set; }

public string? PasswordResetToken { get; set; }
public DateTimeOffset? PasswordResetTokenExpiry { get; set; }

/// <summary>
/// Number of consecutive failed login attempts. Resets on successful login.
/// </summary>
public int FailedLoginAttempts { get; set; }

/// <summary>
/// Account is locked until this time. Null means not locked.
/// </summary>
public DateTimeOffset? LockoutEnd { get; set; }
```

Also update the doc comment on `EffectiveDisplayName` (currently says "Google display name"):
```csharp
/// <summary>
/// Returns the effective display name: nickname if set, otherwise the provider-given display name.
/// </summary>
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds (some existing code referencing `GoogleSubject` may need nullability adjustments — fix any warnings).

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Models/User.cs
git commit -m "feat(api): add email/password auth fields to User entity"
```

---

### Task 3: Update CodecDbContext Entity Configuration

**Files:**
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`

**Step 1: Update User entity configuration**

In `apps/api/Codec.Api/Data/CodecDbContext.cs`, replace the existing User index configuration (lines 40-42):

```csharp
modelBuilder.Entity<User>()
    .HasIndex(user => user.GoogleSubject)
    .IsUnique();
```

with:

```csharp
// GoogleSubject is now nullable (null for local auth users).
// Filtered unique index ensures uniqueness only among non-null values.
modelBuilder.Entity<User>()
    .HasIndex(user => user.GoogleSubject)
    .IsUnique()
    .HasFilter("\"GoogleSubject\" IS NOT NULL");

// Prevent duplicate emails per auth provider.
modelBuilder.Entity<User>()
    .HasIndex(user => new { user.Email, user.AuthProvider })
    .IsUnique()
    .HasFilter("\"Email\" IS NOT NULL");

modelBuilder.Entity<User>()
    .Property(user => user.AuthProvider)
    .HasMaxLength(10)
    .HasDefaultValue("google");
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Data/CodecDbContext.cs
git commit -m "feat(api): update User entity config for dual auth providers"
```

---

### Task 4: Create and Apply EF Core Migration

**Files:**
- Create: `apps/api/Codec.Api/Migrations/<timestamp>_AddEmailPasswordAuth.cs` (auto-generated)

**Step 1: Ensure database is running**

Run: `docker compose up -d postgres`
Expected: postgres container is up.

**Step 2: Generate migration**

Run: `cd apps/api/Codec.Api && dotnet ef migrations add AddEmailPasswordAuth`
Expected: Migration file created in `Migrations/` directory.

**Step 3: Review the generated migration**

Read the generated migration file. Verify it:
1. Alters `GoogleSubject` column to be nullable
2. Adds columns: `AuthProvider`, `PasswordHash`, `EmailConfirmed`, `EmailConfirmationToken`, `EmailConfirmationTokenExpiry`, `PasswordResetToken`, `PasswordResetTokenExpiry`, `FailedLoginAttempts`, `LockoutEnd`
3. Drops old unique index on `GoogleSubject`, creates filtered unique index
4. Creates composite unique index on `(Email, AuthProvider)`

**Step 4: Add data migration SQL**

The auto-generated migration won't set `AuthProvider = "google"` and `EmailConfirmed = true` for existing users. Add this SQL to the `Up` method, after the columns are added but before indexes are created:

```csharp
migrationBuilder.Sql("UPDATE \"Users\" SET \"AuthProvider\" = 'google', \"EmailConfirmed\" = true WHERE \"AuthProvider\" IS NULL OR \"AuthProvider\" = ''");
```

**Step 5: Apply migration**

Run: `cd apps/api/Codec.Api && dotnet ef database update`
Expected: Migration applied successfully.

**Step 6: Verify API starts**

Run: `cd apps/api/Codec.Api && dotnet run`
Expected: API starts on port 5050, no migration errors.

**Step 7: Commit**

```bash
git add apps/api/Codec.Api/Migrations/
git commit -m "feat(api): add EF migration for email/password auth fields"
```

---

### Task 5: Fix UserService for Nullable GoogleSubject

**Files:**
- Modify: `apps/api/Codec.Api/Services/UserService.cs`

**Step 1: Update GetOrCreateUserAsync**

The existing `GetOrCreateUserAsync` method (line 17-90 of `UserService.cs`) assumes `GoogleSubject` is always present. It needs to handle both Google and local JWT tokens.

Replace the `GetOrCreateUserAsync` method body with:

```csharp
public async Task<User> GetOrCreateUserAsync(ClaimsPrincipal principal)
{
    var issuer = principal.FindFirst("iss")?.Value;

    // Local JWT: user already exists (created at registration). Look up by ID.
    if (issuer == "codec-api")
    {
        var userIdClaim = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new InvalidOperationException("Missing or invalid user ID claim.");
        }

        var localUser = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (localUser is null)
        {
            throw new InvalidOperationException("Local user not found.");
        }

        return localUser;
    }

    // Google JWT: existing flow — look up or create by GoogleSubject.
    var subject = principal.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(subject))
    {
        throw new InvalidOperationException("Missing Google subject claim.");
    }

    var displayName = principal.FindFirst("name")?.Value ?? principal.Identity?.Name ?? "Unknown";
    var email = principal.FindFirst("email")?.Value;
    var avatarUrl = principal.FindFirst("picture")?.Value;

    var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject);

    if (existing is not null)
    {
        var hasChanges = existing.DisplayName != displayName
                      || existing.Email != email
                      || existing.AvatarUrl != avatarUrl;

        if (hasChanges)
        {
            existing.DisplayName = displayName;
            existing.Email = email;
            existing.AvatarUrl = avatarUrl;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        return existing;
    }

    var appUser = new User
    {
        GoogleSubject = subject,
        DisplayName = displayName,
        Email = email,
        AvatarUrl = avatarUrl,
        AuthProvider = "google",
        EmailConfirmed = true
    };

    db.Users.Add(appUser);

    var defaultServerExists = await db.Servers
        .AsNoTracking()
        .AnyAsync(s => s.Id == Server.DefaultServerId);

    if (defaultServerExists)
    {
        db.ServerMembers.Add(new ServerMember
        {
            ServerId = Server.DefaultServerId,
            User = appUser,
            Role = ServerRole.Member,
            JoinedAt = DateTimeOffset.UtcNow
        });
    }

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        db.Entry(appUser).State = EntityState.Detached;
        return await db.Users.FirstAsync(u => u.GoogleSubject == subject);
    }

    return appUser;
}
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Services/UserService.cs
git commit -m "feat(api): update UserService to handle dual auth providers"
```

---

### Task 6: Create JWT Token Service

**Files:**
- Create: `apps/api/Codec.Api/Services/ITokenService.cs`
- Create: `apps/api/Codec.Api/Services/TokenService.cs`

**Step 1: Create ITokenService interface**

Create `apps/api/Codec.Api/Services/ITokenService.cs`:

```csharp
using Codec.Api.Models;

namespace Codec.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
}
```

**Step 2: Create TokenService implementation**

Create `apps/api/Codec.Api/Services/TokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Codec.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Codec.Api.Services;

public class TokenService(IConfiguration configuration) : ITokenService
{
    public string GenerateAccessToken(User user)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim("name", user.EffectiveDisplayName),
            new Claim("auth_provider", "local")
        };

        var token = new JwtSecurityToken(
            issuer: "codec-api",
            audience: "codec-app",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**Step 3: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add apps/api/Codec.Api/Services/ITokenService.cs apps/api/Codec.Api/Services/TokenService.cs
git commit -m "feat(api): add TokenService for local JWT issuance"
```

---

### Task 7: Create Email Service

**Files:**
- Create: `apps/api/Codec.Api/Services/IEmailService.cs`
- Create: `apps/api/Codec.Api/Services/ConsoleEmailService.cs`
- Create: `apps/api/Codec.Api/Services/AcsEmailService.cs`

**Step 1: Create IEmailService interface**

Create `apps/api/Codec.Api/Services/IEmailService.cs`:

```csharp
namespace Codec.Api.Services;

public interface IEmailService
{
    Task SendEmailVerificationAsync(string toEmail, string displayName, string token);
    Task SendPasswordResetAsync(string toEmail, string displayName, string token);
}
```

**Step 2: Create ConsoleEmailService (development)**

Create `apps/api/Codec.Api/Services/ConsoleEmailService.cs`:

```csharp
namespace Codec.Api.Services;

/// <summary>
/// Development email service that logs email content to the console
/// instead of sending real emails.
/// </summary>
public class ConsoleEmailService(IConfiguration configuration, ILogger<ConsoleEmailService> logger) : IEmailService
{
    public Task SendEmailVerificationAsync(string toEmail, string displayName, string token)
    {
        var webBaseUrl = configuration["Email:WebBaseUrl"] ?? "http://localhost:5174";
        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(token);
        var link = $"{webBaseUrl}/verify-email?token={encodedToken}&email={encodedEmail}";

        logger.LogInformation(
            "📧 Email Verification for {Email} ({Name}):\n  Link: {Link}",
            toEmail, displayName, link);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string displayName, string token)
    {
        var webBaseUrl = configuration["Email:WebBaseUrl"] ?? "http://localhost:5174";
        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(token);
        var link = $"{webBaseUrl}/reset-password?token={encodedToken}&email={encodedEmail}";

        logger.LogInformation(
            "📧 Password Reset for {Email} ({Name}):\n  Link: {Link}",
            toEmail, displayName, link);

        return Task.CompletedTask;
    }
}
```

**Step 3: Create AcsEmailService (production)**

Create `apps/api/Codec.Api/Services/AcsEmailService.cs`:

```csharp
using Azure.Communication.Email;

namespace Codec.Api.Services;

/// <summary>
/// Production email service using Azure Communication Services.
/// </summary>
public class AcsEmailService(IConfiguration configuration, ILogger<AcsEmailService> logger) : IEmailService
{
    private EmailClient? _client;

    private EmailClient Client => _client ??= new EmailClient(
        configuration["Email:ConnectionString"]
            ?? throw new InvalidOperationException("Email:ConnectionString is not configured."));

    private string SenderAddress => configuration["Email:SenderAddress"]
        ?? throw new InvalidOperationException("Email:SenderAddress is not configured.");

    private string WebBaseUrl => configuration["Email:WebBaseUrl"]
        ?? throw new InvalidOperationException("Email:WebBaseUrl is not configured.");

    public async Task SendEmailVerificationAsync(string toEmail, string displayName, string token)
    {
        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(token);
        var link = $"{WebBaseUrl}/verify-email?token={encodedToken}&email={encodedEmail}";

        var htmlBody = $"""
            <h2>Verify your Codec account</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(displayName)},</p>
            <p>Click the link below to verify your email address:</p>
            <p><a href="{link}">Verify Email</a></p>
            <p>This link expires in 24 hours.</p>
            <p>If you didn't create a Codec account, you can ignore this email.</p>
            """;

        await SendAsync(toEmail, "Verify your Codec account", htmlBody);
    }

    public async Task SendPasswordResetAsync(string toEmail, string displayName, string token)
    {
        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(token);
        var link = $"{WebBaseUrl}/reset-password?token={encodedToken}&email={encodedEmail}";

        var htmlBody = $"""
            <h2>Reset your Codec password</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(displayName)},</p>
            <p>Click the link below to reset your password:</p>
            <p><a href="{link}">Reset Password</a></p>
            <p>This link expires in 1 hour.</p>
            <p>If you didn't request a password reset, you can ignore this email.</p>
            """;

        await SendAsync(toEmail, "Reset your Codec password", htmlBody);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var operation = await Client.SendAsync(
            Azure.WaitUntil.Started,
            SenderAddress,
            toEmail,
            subject,
            htmlBody);

        logger.LogInformation("Email sent to {Email}, operation ID: {OperationId}", toEmail, operation.Id);
    }
}
```

**Step 4: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add apps/api/Codec.Api/Services/IEmailService.cs apps/api/Codec.Api/Services/ConsoleEmailService.cs apps/api/Codec.Api/Services/AcsEmailService.cs
git commit -m "feat(api): add IEmailService with Console and ACS implementations"
```

---

### Task 8: Create Auth Request/Response DTOs

**Files:**
- Create: `apps/api/Codec.Api/Models/AuthDtos.cs`

**Step 1: Create auth DTOs**

Create `apps/api/Codec.Api/Models/AuthDtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class VerifyEmailRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;
}

public class ResendVerificationRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class AuthResponse
{
    public required string Token { get; set; }
    public required object User { get; set; }
}
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Models/AuthDtos.cs
git commit -m "feat(api): add auth request/response DTOs"
```

---

### Task 9: Create AuthService (Core Auth Logic)

**Files:**
- Create: `apps/api/Codec.Api/Services/IAuthService.cs`
- Create: `apps/api/Codec.Api/Services/AuthService.cs`

**Step 1: Create IAuthService interface**

Create `apps/api/Codec.Api/Services/IAuthService.cs`:

```csharp
using Codec.Api.Models;

namespace Codec.Api.Services;

public interface IAuthService
{
    Task<User> RegisterAsync(string email, string password, string displayName);
    Task<(User User, string Token)> LoginAsync(string email, string password);
    Task VerifyEmailAsync(string email, string token);
    Task ResendVerificationAsync(string email);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(string email, string token, string newPassword);
}
```

**Step 2: Create AuthService implementation**

Create `apps/api/Codec.Api/Services/AuthService.cs`:

```csharp
using System.Security.Cryptography;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Codec.Api.Services;

public class AuthService(
    CodecDbContext db,
    ITokenService tokenService,
    IEmailService emailService,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EmailConfirmationExpiry = TimeSpan.FromHours(24);
    private static readonly TimeSpan PasswordResetExpiry = TimeSpan.FromHours(1);

    public async Task<User> RegisterAsync(string email, string password, string displayName)
    {
        ValidatePasswordPolicy(password);

        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Check if account already exists — but don't reveal this to the caller.
        // Return normally either way to prevent user enumeration.
        var existingUser = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        if (existingUser is not null)
        {
            // Account exists. Don't reveal this — just return the existing user
            // without sending a verification email. The caller always gets a
            // "check your email" response regardless.
            return existingUser;
        }

        var user = new User
        {
            DisplayName = displayName.Trim(),
            Email = normalizedEmail,
            AuthProvider = "local",
            EmailConfirmed = false
        };

        user.PasswordHash = PasswordHasher.HashPassword(user, password);

        var token = GenerateSecureToken();
        user.EmailConfirmationToken = token;
        user.EmailConfirmationTokenExpiry = DateTimeOffset.UtcNow.Add(EmailConfirmationExpiry);

        db.Users.Add(user);

        // Auto-join the default server.
        var defaultServerExists = await db.Servers
            .AsNoTracking()
            .AnyAsync(s => s.Id == Server.DefaultServerId);

        if (defaultServerExists)
        {
            db.ServerMembers.Add(new ServerMember
            {
                ServerId = Server.DefaultServerId,
                User = user,
                Role = ServerRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Race condition: account was created between our check and save.
            // Swallow silently to prevent user enumeration.
            return await db.Users.FirstAsync(u =>
                u.Email == normalizedEmail && u.AuthProvider == "local");
        }

        await emailService.SendEmailVerificationAsync(normalizedEmail, user.DisplayName, token);

        return user;
    }

    public async Task<(User User, string Token)> LoginAsync(string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        if (user is null)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Check lockout.
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            throw new TooManyRequestsException(
                $"Account locked. Try again after {user.LockoutEnd.Value:HH:mm} UTC.");
        }

        // Verify password.
        var result = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, password);

        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.Add(LockoutDuration);
                logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts",
                    normalizedEmail, user.FailedLoginAttempts);
            }

            await db.SaveChangesAsync();
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Check email verified.
        if (!user.EmailConfirmed)
        {
            throw new ForbiddenException("Please verify your email before signing in.");
        }

        // Successful login — reset counters.
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Rehash if Identity indicates the hash needs upgrading.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = PasswordHasher.HashPassword(user, password);
        }

        await db.SaveChangesAsync();

        var accessToken = tokenService.GenerateAccessToken(user);
        return (user, accessToken);
    }

    public async Task VerifyEmailAsync(string email, string token)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        if (user is null)
        {
            throw new NotFoundException("Invalid verification link.");
        }

        if (user.EmailConfirmed)
        {
            return; // Already verified — idempotent.
        }

        if (user.EmailConfirmationToken != token ||
            user.EmailConfirmationTokenExpiry < DateTimeOffset.UtcNow)
        {
            throw new BadRequestException("Invalid or expired verification link.");
        }

        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiry = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task ResendVerificationAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        // Silent return to prevent user enumeration.
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var token = GenerateSecureToken();
        user.EmailConfirmationToken = token;
        user.EmailConfirmationTokenExpiry = DateTimeOffset.UtcNow.Add(EmailConfirmationExpiry);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        await emailService.SendEmailVerificationAsync(normalizedEmail, user.DisplayName, token);
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        // Silent return to prevent user enumeration.
        if (user is null)
        {
            return;
        }

        var token = GenerateSecureToken();
        user.PasswordResetToken = HashToken(token);
        user.PasswordResetTokenExpiry = DateTimeOffset.UtcNow.Add(PasswordResetExpiry);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        await emailService.SendPasswordResetAsync(normalizedEmail, user.DisplayName, token);
    }

    public async Task ResetPasswordAsync(string email, string token, string newPassword)
    {
        ValidatePasswordPolicy(newPassword);

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == normalizedEmail && u.AuthProvider == "local");

        if (user is null)
        {
            throw new BadRequestException("Invalid or expired reset link.");
        }

        var hashedToken = HashToken(token);

        if (user.PasswordResetToken != hashedToken ||
            user.PasswordResetTokenExpiry < DateTimeOffset.UtcNow)
        {
            throw new BadRequestException("Invalid or expired reset link.");
        }

        user.PasswordHash = PasswordHasher.HashPassword(user, newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
    }

    private static void ValidatePasswordPolicy(string password)
    {
        if (password.Length < 8)
            throw new BadRequestException("Password must be at least 8 characters.");
        if (!password.Any(char.IsUpper))
            throw new BadRequestException("Password must contain at least one uppercase letter.");
        if (!password.Any(char.IsLower))
            throw new BadRequestException("Password must contain at least one lowercase letter.");
        if (!password.Any(char.IsDigit))
            throw new BadRequestException("Password must contain at least one digit.");
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
```

**Step 3: Add missing exception classes**

Create `apps/api/Codec.Api/Services/Exceptions/UnauthorizedException.cs`:
```csharp
namespace Codec.Api.Services.Exceptions;

public class UnauthorizedException(string message = "Unauthorized.") : CodecException(401, message);
```

Create `apps/api/Codec.Api/Services/Exceptions/BadRequestException.cs`:
```csharp
namespace Codec.Api.Services.Exceptions;

public class BadRequestException(string message = "Bad request.") : CodecException(400, message);
```

Create `apps/api/Codec.Api/Services/Exceptions/ConflictException.cs`:
```csharp
namespace Codec.Api.Services.Exceptions;

public class ConflictException(string message = "Conflict.") : CodecException(409, message);
```

Create `apps/api/Codec.Api/Services/Exceptions/TooManyRequestsException.cs`:
```csharp
namespace Codec.Api.Services.Exceptions;

public class TooManyRequestsException(string message = "Too many requests.") : CodecException(429, message);
```

**Step 4: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add apps/api/Codec.Api/Services/IAuthService.cs apps/api/Codec.Api/Services/AuthService.cs apps/api/Codec.Api/Services/Exceptions/
git commit -m "feat(api): add AuthService with registration, login, verification, and password reset"
```

---

### Task 10: Create AuthController

**Files:**
- Create: `apps/api/Codec.Api/Controllers/AuthController.cs`

**Step 1: Create the controller**

Create `apps/api/Codec.Api/Controllers/AuthController.cs`:

```csharp
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        await authService.RegisterAsync(request.Email, request.Password, request.DisplayName);
        return StatusCode(201, new { message = "Check your email to verify your account." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (user, token) = await authService.LoginAsync(request.Email, request.Password);
        return Ok(new AuthResponse
        {
            Token = token,
            User = new
            {
                id = user.Id,
                displayName = user.DisplayName,
                nickname = user.Nickname,
                effectiveDisplayName = user.EffectiveDisplayName,
                email = user.Email,
                avatarUrl = user.AvatarUrl,
                isGlobalAdmin = user.IsGlobalAdmin
            }
        });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        await authService.VerifyEmailAsync(request.Email, request.Token);
        return Ok(new { message = "Email verified. You can now sign in." });
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        await authService.ResendVerificationAsync(request.Email);
        return Ok(new { message = "If your email is registered and unverified, we've sent a new verification link." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.ForgotPasswordAsync(request.Email);
        return Ok(new { message = "If an account with that email exists, we've sent a reset link." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await authService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
        return Ok(new { message = "Password reset successfully. You can now sign in." });
    }
}
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/AuthController.cs
git commit -m "feat(api): add AuthController with registration, login, and password reset endpoints"
```

---

### Task 11: Configure Dual JWT Auth in Program.cs

**Files:**
- Modify: `apps/api/Codec.Api/Program.cs`

**Step 1: Add using statements**

Add at the top of `Program.cs` (after line 11):

```csharp
using System.Text;
```

**Step 2: Replace the authentication configuration**

Replace lines 46-82 of `Program.cs` (the entire Google auth block + `AddAuthorization()`) with:

```csharp
var googleClientId = builder.Configuration["Google:ClientId"];
if (string.IsNullOrWhiteSpace(googleClientId))
{
    throw new InvalidOperationException("Google:ClientId is required for authentication.");
}

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("Jwt:Secret is required for local authentication.");
}

builder.Services.AddAuthentication("Smart")
    .AddPolicyScheme("Smart", "Google or Local JWT", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ") == true
                ? authHeader["Bearer ".Length..]
                : context.Request.Query["access_token"].FirstOrDefault();

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    // Read the "iss" claim without validating signature.
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    var issuer = jwt.Issuer;

                    if (issuer == "codec-api")
                        return "Local";
                }
                catch
                {
                    // Malformed token — fall through to Google scheme.
                }
            }

            return "Google";
        };
    })
    .AddJwtBearer("Google", options =>
    {
        options.Authority = "https://accounts.google.com";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = true,
            ValidAudience = googleClientId,
            ValidateLifetime = true
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer("Local", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "codec-api",
            ValidateAudience = true,
            ValidAudience = "codec-app",
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
```

**Step 3: Register new services in DI**

After the existing `builder.Services.AddScoped<IUserService, UserService>();` (line 89), add:

```csharp
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IEmailService, ConsoleEmailService>();
}
else
{
    builder.Services.AddScoped<IEmailService, AcsEmailService>();
}
```

**Step 4: Add Jwt:Secret to appsettings.Development.json**

Read and modify `apps/api/Codec.Api/appsettings.Development.json` to add:

```json
"Jwt": {
  "Secret": "dev-only-secret-key-that-is-at-least-32-characters-long-for-hmac-sha256"
},
"Email": {
  "WebBaseUrl": "http://localhost:5174"
}
```

**Step 5: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 6: Test API starts**

Run: `cd apps/api/Codec.Api && dotnet run`
Expected: API starts on port 5050. Check logs for any auth configuration errors.

**Step 7: Commit**

```bash
git add apps/api/Codec.Api/Program.cs apps/api/Codec.Api/appsettings.Development.json
git commit -m "feat(api): configure dual JWT auth (Google + local) with DI registration"
```

---

### Task 12: Update SeedData for Development

**Files:**
- Modify: `apps/api/Codec.Api/Data/SeedData.cs`

**Step 1: Add email/password test users**

In `SeedData.cs`, add seed users with `AuthProvider = "google"` for existing users, and add a local auth test user. Update the existing seed users in `InitializeAsync` (line 61-63) to include `AuthProvider`:

```csharp
var avery = new User { GoogleSubject = "seed-avery", DisplayName = "Avery", AuthProvider = "google", EmailConfirmed = true };
var morgan = new User { GoogleSubject = "seed-morgan", DisplayName = "Morgan", AuthProvider = "google", EmailConfirmed = true };
var rae = new User { GoogleSubject = "seed-rae", DisplayName = "Rae", AuthProvider = "google", EmailConfirmed = true };
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Data/SeedData.cs
git commit -m "feat(api): update seed data with AuthProvider for existing users"
```

---

### Task 13: Add Auth Methods to ApiClient (Frontend)

**Files:**
- Modify: `apps/web/src/lib/api/client.ts`

**Step 1: Add public (unauthenticated) request methods**

Add these methods to the `ApiClient` class in `apps/web/src/lib/api/client.ts`. These are public endpoints that don't require a Bearer token. Add a new helper method and the auth methods after the existing `requestVoid` method (after line 97):

```typescript
/** Make a request without auth token (for public endpoints like /auth/*). */
private async requestPublic<T>(url: string, init: RequestInit): Promise<T> {
    const response = await fetch(url, init);
    if (!response.ok) {
        const body = await response.json().catch(() => null);
        const message = body?.error
            ?? body?.detail
            ?? body?.message
            ?? (body?.errors ? Object.values(body.errors).flat().join('; ') : null);
        throw new ApiError(response.status, message ?? undefined);
    }
    return response.json() as Promise<T>;
}

/* ───── Auth (public endpoints) ───── */

register(email: string, password: string, displayName: string): Promise<{ message: string }> {
    return this.requestPublic(`${this.baseUrl}/auth/register`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password, displayName })
    });
}

login(email: string, password: string): Promise<{ token: string; user: UserProfile }> {
    return this.requestPublic(`${this.baseUrl}/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password })
    });
}

verifyEmail(email: string, token: string): Promise<{ message: string }> {
    return this.requestPublic(`${this.baseUrl}/auth/verify-email`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, token })
    });
}

resendVerification(email: string): Promise<{ message: string }> {
    return this.requestPublic(`${this.baseUrl}/auth/resend-verification`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email })
    });
}

forgotPassword(email: string): Promise<{ message: string }> {
    return this.requestPublic(`${this.baseUrl}/auth/forgot-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email })
    });
}

resetPassword(email: string, token: string, newPassword: string): Promise<{ message: string }> {
    return this.requestPublic(`${this.baseUrl}/auth/reset-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, token, newPassword })
    });
}
```

Also add `UserProfile` to the imports at the top of the file (line 1-18).

**Step 2: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 3: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat(web): add auth API methods to ApiClient"
```

---

### Task 14: Update AppState for Dual Auth

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`
- Modify: `apps/web/src/lib/auth/session.ts`

**Step 1: Add auth provider detection to session.ts**

In `apps/web/src/lib/auth/session.ts`, add a helper to detect the token issuer. Add after the existing `decodeJwtPayload` function (after line 14):

```typescript
/** Returns the issuer from a JWT token, or null if unreadable. */
export function getTokenIssuer(token: string): string | null {
    const payload = decodeJwtPayload(token);
    if (!payload || typeof payload.iss !== 'string') return null;
    return payload.iss;
}
```

Also export `clearSession` as `clearStoredSession` (check if this alias already exists in `app-state.svelte.ts`). Looking at the code, `app-state.svelte.ts` line 381 calls `clearStoredSession()` — verify this is an alias or update accordingly.

**Step 2: Update AppState.init() to handle local tokens**

In `apps/web/src/lib/state/app-state.svelte.ts`, update the `init()` method (lines 336-349) to skip Google One Tap for local tokens:

```typescript
init(): void {
    this._loadUserVolumes();
    this._loadVoicePreferences();
    if (!isSessionExpired()) {
        const stored = loadStoredToken();
        if (stored && !isTokenExpired(stored)) {
            this.handleCredential(stored);
            return;
        }
    }
    clearStoredSession();
    this.isInitialLoading = false;
    this.renderSignIn();
}
```

This stays the same — `handleCredential` works regardless of token issuer since both are JWTs.

**Step 3: Update refreshToken() to handle local tokens**

In `apps/web/src/lib/state/app-state.svelte.ts`, update `refreshToken()` (lines 289-307) to handle local tokens differently:

```typescript
private async refreshToken(): Promise<string | null> {
    if (this.refreshPromise) return this.refreshPromise;

    this.refreshPromise = (async () => {
        try {
            // Local tokens can't be silently refreshed — sign out.
            if (this.idToken && getTokenIssuer(this.idToken) === 'codec-api') {
                await this.signOut();
                return null;
            }

            const freshToken = await requestFreshToken();
            this.idToken = freshToken;
            persistToken(freshToken);
            return freshToken;
        } catch {
            await this.signOut();
            return null;
        } finally {
            this.refreshPromise = null;
        }
    })();

    return this.refreshPromise;
}
```

Add the `getTokenIssuer` import at the top of the file where other session imports are.

**Step 4: Add loginWithEmail and registerWithEmail methods**

Add these methods to the AppState class, in the Auth section (after `handleCredential`, before `signOut`):

```typescript
/** Register a new account with email and password. */
async registerWithEmail(email: string, password: string, displayName: string): Promise<{ message: string }> {
    return this.api.register(email, password, displayName);
}

/** Log in with email and password. */
async loginWithEmail(email: string, password: string): Promise<void> {
    const result = await this.api.login(email, password);
    await this.handleCredential(result.token);
}
```

**Step 5: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 6: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts apps/web/src/lib/auth/session.ts
git commit -m "feat(web): update AppState and session for dual auth support"
```

---

### Task 15: Update LoginScreen with Auth Tabs

**Files:**
- Modify: `apps/web/src/lib/components/LoginScreen.svelte`

**Step 1: Rewrite LoginScreen with tabs**

Rewrite `apps/web/src/lib/components/LoginScreen.svelte` to support both Google and email login. The component should have:

- Two tabs: "Google" and "Email"
- Google tab: existing Google sign-in button
- Email tab: login form with email/password, links to "Create account" and "Forgot password?"
- Register form view (shown when "Create account" is clicked)
- Forgot password form view (shown when "Forgot password?" is clicked)
- Success/error message display

The component needs access to `AppState` via `getAppState()` context. Use the existing design tokens from `tokens.css` (especially `--input-bg`, `--border`, `--accent`, `--danger`, `--text-normal`, `--text-muted`).

Key implementation details:
- Use Svelte 5 runes (`$state`) for form state
- Use `$props()` — no props needed currently since it reads AppState from context
- Form validation inline (email format, password length/complexity)
- Call `app.loginWithEmail()`, `app.registerWithEmail()`, `app.api.forgotPassword()`, `app.api.resendVerification()`
- Show loading spinner during API calls
- Display error messages from API (caught `ApiError.message`)

**Step 2: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/LoginScreen.svelte
git commit -m "feat(web): update LoginScreen with Google/email auth tabs"
```

---

### Task 16: Create Verify Email Page

**Files:**
- Create: `apps/web/src/routes/verify-email/+page.svelte`

**Step 1: Create the verify email route**

Create `apps/web/src/routes/verify-email/+page.svelte`:

This page should:
- Read `token` and `email` from URL search params (`$page.url.searchParams`)
- On mount, call `api.verifyEmail(email, token)`
- Show loading state while verifying
- Show success message with link to sign in (redirect to `/`)
- Show error message if verification fails
- Use the same CRT/phosphor-green styling as LoginScreen

Import `ApiClient` directly (this page doesn't need full AppState) or create a minimal client. Use `$env/dynamic/public` for `PUBLIC_API_BASE_URL`.

**Step 2: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 3: Commit**

```bash
git add apps/web/src/routes/verify-email/+page.svelte
git commit -m "feat(web): add email verification page"
```

---

### Task 17: Create Reset Password Page

**Files:**
- Create: `apps/web/src/routes/reset-password/+page.svelte`

**Step 1: Create the reset password route**

Create `apps/web/src/routes/reset-password/+page.svelte`:

This page should:
- Read `token` and `email` from URL search params
- Show a form with: new password, confirm password fields
- Validate password policy (8+ chars, uppercase, lowercase, digit)
- Validate passwords match
- On submit, call `api.resetPassword(email, token, newPassword)`
- Show success message with link to sign in
- Show error message if reset fails (expired/invalid token)
- Use the same CRT/phosphor-green styling

**Step 2: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 3: Commit**

```bash
git add apps/web/src/routes/reset-password/+page.svelte
git commit -m "feat(web): add password reset page"
```

---

### Task 18: Update +page.svelte for Optional Google Client ID

**Files:**
- Modify: `apps/web/src/routes/+page.svelte`

**Step 1: Make Google client ID optional**

In `apps/web/src/routes/+page.svelte`, the `onMount` block (lines 25-46) currently requires `googleClientId` and shows an error if missing. Update it to allow the app to start without Google client ID (email-only mode):

Replace lines 26-29:
```typescript
if (!googleClientId) {
    app.error = 'Missing PUBLIC_GOOGLE_CLIENT_ID.';
    app.isInitialLoading = false;
    return;
}
```

With: remove this block entirely (or make it a warning instead of a hard error). Google sign-in will simply not render its button if the client ID is empty, but email login will still work.

**Step 2: Verify frontend build**

Run: `cd apps/web && npm run check`
Expected: No errors.

**Step 3: Commit**

```bash
git add apps/web/src/routes/+page.svelte
git commit -m "feat(web): make Google client ID optional for email-only auth"
```

---

### Task 19: Infrastructure — Azure Communication Services Bicep

**Files:**
- Create: `infra/modules/communication-services.bicep`
- Modify: `infra/main.bicep`
- Modify: `infra/modules/container-app-api.bicep`

**Step 1: Create ACS Bicep module**

Create `infra/modules/communication-services.bicep`:

```bicep
/// Azure Communication Services for email sending.
param name string
param location string
param keyVaultName string

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: name
  location: 'global'
  properties: {
    dataLocation: 'unitedstates'
  }
}

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${name}-email'
  location: 'global'
  properties: {
    dataLocation: 'unitedstates'
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
  }
}

// Link email domain to communication service
resource domainLink 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: '${name}-linked'
  location: 'global'
  properties: {
    dataLocation: 'unitedstates'
    linkedDomains: [emailDomain.id]
  }
}

// Store ACS connection string in Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource acsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Email--ConnectionString'
  properties: {
    value: acs.listKeys().primaryConnectionString
  }
}

output name string = acs.name
output senderAddress string = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
```

**Step 2: Update main.bicep**

In `infra/main.bicep`, add:

1. New parameter for JWT secret (after line 23):
```bicep
@description('HMAC-SHA256 secret for signing local JWT tokens.')
@secure()
param jwtSecret string
```

2. New module for ACS (after the storageAccount module, around line 119):
```bicep
module communicationServices 'modules/communication-services.bicep' = {
  name: 'communication-services'
  params: {
    name: 'acs-${baseName}'
    location: location
    keyVaultName: keyVault.outputs.name
  }
}
```

3. New Key Vault secret for JWT (after globalAdminEmailSecret, around line 149):
```bicep
module jwtSecretKv 'modules/key-vault-secret.bicep' = {
  name: 'jwt-signing-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Jwt--Secret'
    secretValue: jwtSecret
  }
}
```

4. Update the apiApp module params (around line 237) to pass new values:
```bicep
jwtSecretKvUrl: '${keyVault.outputs.uri}secrets/Jwt--Secret'
emailConnectionStringKvUrl: '${keyVault.outputs.uri}secrets/Email--ConnectionString'
emailSenderAddress: communicationServices.outputs.senderAddress
emailWebBaseUrl: effectiveWebUrl
```

**Step 3: Update container-app-api.bicep**

Add new parameters and env vars to `infra/modules/container-app-api.bicep`:

New params (after voiceSfuInternalKeyKvUrl):
```bicep
@description('Key Vault secret URL for the JWT signing secret.')
param jwtSecretKvUrl string = ''

@description('Key Vault secret URL for the ACS email connection string.')
param emailConnectionStringKvUrl string = ''

@description('Sender email address for ACS.')
param emailSenderAddress string = ''

@description('Web app base URL for email links.')
param emailWebBaseUrl string = ''
```

Add to secrets array (inside the `concat` on line 80):
```bicep
{
  name: 'jwt-secret'
  keyVaultUrl: '${keyVaultUri}secrets/Jwt--Secret'
  identity: 'system'
}
{
  name: 'email-connection-string'
  keyVaultUrl: '${keyVaultUri}secrets/Email--ConnectionString'
  identity: 'system'
}
```

Add to env array (inside the `concat` on line 125):
```bicep
{
  name: 'Jwt__Secret'
  secretRef: 'jwt-secret'
}
{
  name: 'Email__ConnectionString'
  secretRef: 'email-connection-string'
}
{
  name: 'Email__SenderAddress'
  value: emailSenderAddress
}
{
  name: 'Email__WebBaseUrl'
  value: emailWebBaseUrl
}
```

**Step 4: Commit**

```bash
git add infra/
git commit -m "feat(infra): add ACS email and JWT secret to Bicep infrastructure"
```

---

### Task 20: End-to-End Testing

**Files:** None (manual testing)

**Step 1: Start the full stack**

```bash
docker compose up -d postgres
cd apps/api/Codec.Api && dotnet run &
cd apps/web && npm run dev &
```

**Step 2: Test registration**

1. Open `http://localhost:5174`
2. Switch to "Email" tab on login screen
3. Click "Create account"
4. Fill in email, display name, password (8+ chars, upper, lower, digit)
5. Submit — should show success message
6. Check API console output for verification link

**Step 3: Test email verification**

1. Copy the verification link from console
2. Open it in browser — should show "Email verified" message
3. Click "Sign in" link

**Step 4: Test login**

1. On login screen, switch to "Email" tab
2. Enter registered email and password
3. Should log in and see the app with servers, channels, etc.
4. Verify SignalR connects (check console for connection log)

**Step 5: Test password reset**

1. Sign out
2. Click "Forgot password?" on email login tab
3. Enter email, submit
4. Copy reset link from API console
5. Open it — should show new password form
6. Enter new password, confirm, submit
7. Should show success message
8. Log in with new password — should work

**Step 6: Test Google Sign-In still works**

1. Sign out
2. Switch to "Google" tab
3. Sign in with Google
4. Should log in normally — no regression

**Step 7: Test error cases**

1. Register with existing email — should show error
2. Login with wrong password — should show error
3. Login with unverified email — should show "verify email" error
4. Login 5 times with wrong password — should show lockout message
5. Try expired/invalid verification token — should show error

**Step 8: Commit if any fixes were needed**

```bash
git add -A
git commit -m "fix(api,web): fixes from end-to-end auth testing"
```

---

### Task 21: Update Documentation

**Files:**
- Modify: `docs/AUTH.md` (if exists, or create)
- Modify: `docs/FEATURES.md` (if exists)
- Modify: `docs/ARCHITECTURE.md` (if exists)
- Modify: `PLAN.md`
- Modify: `apps/web/.env.example`

**Step 1: Update .env.example**

The existing `.env.example` stays the same — `PUBLIC_GOOGLE_CLIENT_ID` is still used. No new frontend env vars needed.

**Step 2: Update documentation files**

Update relevant docs to mention email/password auth alongside Google Sign-In. Key points:
- Authentication now supports both Google Sign-In and email/password
- Email/password users must verify their email before accessing the app
- Password policy: 8+ chars, uppercase, lowercase, digit
- Password reset via email link (1-hour expiry)
- Google and email accounts are independent (no linking)
- In development, email verification/reset links are logged to console

**Step 3: Commit**

```bash
git add docs/ PLAN.md apps/web/.env.example
git commit -m "docs: update documentation for email/password auth feature"
```
