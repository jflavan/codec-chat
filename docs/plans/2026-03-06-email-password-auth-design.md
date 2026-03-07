# Email/Password Authentication Design

**Date:** 2026-03-06
**Status:** Approved
**Author:** Claude (brainstorming session)

## Overview

Add email/password registration and login alongside existing Google Sign-In. The two auth methods create separate, independent accounts (no account linking). Users who register with email must verify their address before accessing the app. Password reset is supported via time-limited email links.

## Requirements

- Users can register with email, password, and display name
- Email verification required before app access
- Login with email + password returns a self-issued JWT
- Password reset via email link (1-hour expiry)
- Google Sign-In continues to work unchanged
- Separate identities: Google and email/password accounts are independent even if they share the same email address
- Password policy: minimum 8 characters, at least one uppercase, one lowercase, one digit
- Emails sent via Azure Communication Services (console logging in dev)

## Approach: Hybrid Identity

Use ASP.NET Core Identity's cryptographic primitives (`PasswordHasher<T>` for password hashing, secure token generation) without adopting its full data model. The existing `User` entity and `UserService` pattern stay intact with new fields added.

**Why this approach over alternatives:**
- **vs. Full ASP.NET Core Identity:** Avoids restructuring the data model around `IdentityUser` and its opinionated table schema
- **vs. Custom from scratch:** Avoids writing security-critical code (password hashing, token generation) that's already battle-tested in Identity

---

## 1. Data Model Changes

### New Fields on `User` Entity

```csharp
// Auth provider identification
public string AuthProvider { get; set; }            // "google" or "local"

// Password (null for Google-only users)
public string? PasswordHash { get; set; }

// Email verification
public bool EmailConfirmed { get; set; }            // true for Google users, false for new email registrations
public string? EmailConfirmationToken { get; set; }
public DateTimeOffset? EmailConfirmationTokenExpiry { get; set; }

// Password reset
public string? PasswordResetToken { get; set; }
public DateTimeOffset? PasswordResetTokenExpiry { get; set; }

// Brute-force protection
public int FailedLoginAttempts { get; set; }
public DateTimeOffset? LockoutEnd { get; set; }
```

### Schema Changes

- `GoogleSubject` becomes **nullable** (email/password users don't have one)
- Unique index on `GoogleSubject` filtered to non-null values: `HasIndex(u => u.GoogleSubject).IsUnique().HasFilter("\"GoogleSubject\" IS NOT NULL")`
- New composite unique index on `(Email, AuthProvider)` to prevent duplicate emails per provider
- `AuthProvider` defaults to `"google"` for existing users (migration sets this)

### Migration

An EF Core migration will:
1. Add all new columns with appropriate defaults
2. Make `GoogleSubject` nullable
3. Set `AuthProvider = "google"` and `EmailConfirmed = true` for all existing users
4. Update the unique index on `GoogleSubject` to be filtered
5. Add the composite unique index on `(Email, AuthProvider)`

---

## 2. API Endpoints

### New `AuthController`

All endpoints are public (no `[Authorize]`).

| Endpoint | Method | Request Body | Response |
|----------|--------|-------------|----------|
| `/auth/register` | POST | `{ email, password, displayName }` | 201 + message |
| `/auth/login` | POST | `{ email, password }` | 200 + `{ token, user }` |
| `/auth/verify-email` | POST | `{ email, token }` | 200 + message |
| `/auth/resend-verification` | POST | `{ email }` | 200 + message |
| `/auth/forgot-password` | POST | `{ email }` | 200 + message |
| `/auth/reset-password` | POST | `{ email, token, newPassword }` | 200 + message |

### Registration Flow

1. Validate email format, password policy, display name (non-empty, max 32 chars)
2. Check no existing user with same email + `AuthProvider = "local"`
3. Hash password with `PasswordHasher<User>`
4. Generate cryptographically random email confirmation token (64-byte, base64url-encoded)
5. Create `User` with `EmailConfirmed = false`, `AuthProvider = "local"`, `GoogleSubject = null`
6. Send verification email via `IEmailService`
7. Auto-join default server ("Codec HQ")
8. Return 201: `"Check your email to verify your account"`

### Login Flow

1. Find user by email + `AuthProvider = "local"`
2. If not found: return 401 `"Invalid email or password"` (no user enumeration)
3. Check `LockoutEnd` — if locked out, return 429 with lockout remaining time
4. Verify password with `PasswordHasher<User>.VerifyHashedPassword()`
5. If wrong: increment `FailedLoginAttempts`. After 5 failures, set `LockoutEnd = now + 15min`. Return 401 `"Invalid email or password"`
6. If correct but `EmailConfirmed = false`: return 403 `"Please verify your email before signing in"`
7. Reset `FailedLoginAttempts = 0`, clear `LockoutEnd`
8. Issue self-signed JWT (7-day lifetime)
9. Return 200 with `{ token, user }` (same user shape as `/me`)

### Email Verification Flow

1. `/auth/verify-email`: Look up user by email + `AuthProvider = "local"`
2. Compare provided token against stored `EmailConfirmationToken`
3. Check `EmailConfirmationTokenExpiry` not passed (24-hour window)
4. Set `EmailConfirmed = true`, clear token fields
5. Return 200: `"Email verified. You can now sign in."`

### Password Reset Flow

1. `/auth/forgot-password`: Look up user by email + `AuthProvider = "local"`
2. If not found, still return 200 `"If an account with that email exists, we've sent a reset link"` (no enumeration)
3. Generate reset token (64-byte, base64url-encoded), store hashed token + 1-hour expiry
4. Send password reset email
5. `/auth/reset-password`: Validate token against stored hash, check expiry
6. Hash new password, update `PasswordHash`, clear reset token fields, reset `FailedLoginAttempts`
7. Return 200: `"Password reset successfully. You can now sign in."`

### Resend Verification Flow

1. Look up user by email + `AuthProvider = "local"`
2. If not found or already confirmed, return 200 (no enumeration)
3. Generate new token, update expiry
4. Send verification email
5. Return 200: `"If your email is registered and unverified, we've sent a new verification link"`

---

## 3. JWT Issuance & Dual Auth

### Self-Issued JWT

- **Algorithm:** HMAC-SHA256
- **Signing key:** 256-bit random secret from configuration (`Jwt:Secret`), stored in Azure Key Vault for production
- **Issuer:** `"codec-api"`
- **Audience:** `"codec-app"`
- **Lifetime:** 7 days (matching current Google session length)
- **Claims:**
  - `sub`: user ID (GUID)
  - `email`: user's email
  - `name`: user's display name
  - `auth_provider`: `"local"`

### Dual Validation (Program.cs)

The API accepts two JWT formats using ASP.NET Core's `AddPolicyScheme`:

1. **Google scheme** (existing): Validates against Google JWKS, issuer `accounts.google.com`
2. **Local scheme** (new): Validates against server HMAC secret, issuer `codec-api`

A "smart" policy scheme inspects the token's `iss` claim and forwards to the correct validator:

```
Token arrives → Read "iss" claim
  → "accounts.google.com" or "https://accounts.google.com" → Google scheme
  → "codec-api" → Local scheme
  → anything else → reject
```

### UserService Changes

`GetOrCreateUserAsync(ClaimsPrincipal)` is updated to handle both providers:
- If issuer is Google: existing flow (lookup by `GoogleSubject` claim)
- If issuer is `codec-api`: lookup by user ID from `sub` claim (user already exists — was created during registration)

All existing controllers, SignalR hub, and middleware continue to call `GetOrCreateUserAsync` and receive a `User` — no changes needed downstream.

---

## 4. Email Service

### Interface

```csharp
public interface IEmailService
{
    Task SendEmailVerificationAsync(string toEmail, string displayName, string token);
    Task SendPasswordResetAsync(string toEmail, string displayName, string token);
}
```

### Implementations

**`AcsEmailService` (Production):**
- Uses `Azure.Communication.Email` SDK
- Sends HTML emails with verification/reset links
- Links point to: `{WebBaseUrl}/verify-email?token={token}&email={email}` and `{WebBaseUrl}/reset-password?token={token}&email={email}`
- Sender: configured via `Email:SenderAddress`

**`ConsoleEmailService` (Development):**
- Logs email content and clickable links to console
- Registered via DI when `ASPNETCORE_ENVIRONMENT=Development`

### Configuration

```json
{
  "Email": {
    "ConnectionString": "<ACS connection string (Key Vault in prod)>",
    "SenderAddress": "noreply@<domain>.com",
    "WebBaseUrl": "http://localhost:5174"
  }
}
```

---

## 5. Frontend Changes

### LoginScreen Update

The login screen gets two modes, toggled by tabs or buttons:

- **Google tab:** Existing Google Sign-In button (unchanged)
- **Email tab:** Login form with email + password fields, "Sign in" button, links to "Create account" and "Forgot password?"

### New Components

| Component | Purpose |
|-----------|---------|
| `EmailLoginForm.svelte` | Email + password form with sign-in button |
| `RegisterForm.svelte` | Registration form: email, display name, password, confirm password |
| `ForgotPasswordForm.svelte` | Email input to request password reset |

### New Routes

| Route | File | Purpose |
|-------|------|---------|
| `/verify-email` | `routes/verify-email/+page.svelte` | Reads `token` + `email` from URL, calls `/auth/verify-email`, shows result |
| `/reset-password` | `routes/reset-password/+page.svelte` | Reads `token` + `email` from URL, shows new password form, calls `/auth/reset-password` |

Registration and forgot-password are modals/views within the login screen (not separate routes).

### AppState Changes

- `init()`: checks token issuer — if `codec-api`, skips Google One Tap refresh
- New `loginWithEmail(email: string, password: string)`: calls `/auth/login`, receives JWT, then calls existing `handleCredential(token)` to set up the session
- New `registerWithEmail(email: string, password: string, displayName: string)`: calls `/auth/register`, returns success/error
- Token refresh for local users: the 7-day token expires and the user re-authenticates (no silent refresh — matches Google session behavior)
- `signOut()`: works unchanged (clears localStorage, resets state)

### ApiClient Changes

No changes needed — `ApiClient` already uses `Authorization: Bearer <token>` for all requests and handles 401 retry. The JWT format doesn't matter to the client.

---

## 6. Infrastructure (Bicep) Changes

### New Azure Resources

- `Microsoft.Communication/communicationServices` — ACS resource
- `Microsoft.Communication/emailServices` — email service linked to ACS
- `Microsoft.Communication/emailServices/domains` — Azure-managed domain for sending

### Key Vault Secrets

| Secret Name | Value |
|-------------|-------|
| `jwt-signing-key` | 256-bit random HMAC secret |
| `acs-email-connection-string` | ACS connection string |

### App Service Configuration

| Setting | Source |
|---------|--------|
| `Jwt__Secret` | Key Vault reference → `jwt-signing-key` |
| `Email__ConnectionString` | Key Vault reference → `acs-email-connection-string` |
| `Email__SenderAddress` | ACS domain-based sender address |
| `Email__WebBaseUrl` | App Service URL |

---

## 7. Security

### Password Hashing
- ASP.NET Core Identity's `PasswordHasher<T>`: PBKDF2 with 600,000 iterations, per-password random salt, automatic rehashing of weaker hashes on verification

### Brute-Force Protection
- Account lockout after 5 consecutive failed login attempts
- 15-minute lockout window
- `FailedLoginAttempts` counter resets on successful login

### No User Enumeration
- Login: returns generic `"Invalid email or password"` for both wrong email and wrong password
- Forgot password: returns `"If an account exists, we've sent a reset link"` regardless
- Resend verification: returns generic message regardless

### Token Security
- Email verification tokens: 64-byte cryptographically random, base64url-encoded, 24-hour expiry
- Password reset tokens: 64-byte cryptographically random, base64url-encoded, stored as hash in DB (raw token only in email), 1-hour expiry
- JWT signing secret: 256-bit random, stored in Azure Key Vault, never logged or exposed

### Rate Limiting
- Existing 100 req/min fixed window applies to all auth endpoints
- Future enhancement: stricter per-IP limits on `/auth/login` and `/auth/forgot-password`

### Transport Security
- All email links use HTTPS
- Tokens transmitted only over TLS

---

## 8. Testing Strategy

### API Unit Tests
- Registration: valid input, duplicate email, weak password, invalid email format
- Login: correct credentials, wrong password, wrong email, locked account, unverified email
- Email verification: valid token, expired token, invalid token, already verified
- Password reset: valid flow, expired token, invalid token, password policy enforcement
- JWT issuance: correct claims, correct lifetime, valid signature

### Integration Tests
- Full registration → verification → login → API access flow
- Full forgot password → reset → login flow
- Dual auth: Google user and local user accessing same endpoints
- SignalR connection with local JWT

### Frontend Tests
- Login form validation and submission
- Registration form validation and submission
- Error state display (locked account, unverified email, wrong credentials)
- Verify email and reset password pages with valid/invalid tokens
