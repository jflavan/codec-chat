# Email Verification Design

## Overview

Add email verification to the email/password registration flow. Users must verify their email before accessing the app (hard gate). Google Sign-In users are unaffected — their emails are already verified by Google.

## Data Model

New fields on the `User` entity:

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `EmailVerified` | `bool` | `false` | Whether the user has verified their email |
| `EmailVerificationToken` | `string?` | `null` | SHA-256 hash of the verification token (indexed) |
| `EmailVerificationTokenExpiresAt` | `DateTimeOffset?` | `null` | Token expiry (24 hours from creation) |
| `EmailVerificationTokenSentAt` | `DateTimeOffset?` | `null` | Last send time (for rate limiting resends) |

Google Sign-In users get `EmailVerified = true` automatically — set in `UserService.GetOrCreateUserAsync()` when creating a user via Google.

No new table — verification is 1:1 with the user, so fields on `User` are sufficient.

## Backend API

### Modified Endpoints

**`POST /auth/register`**
After creating the user, generate a 32-byte random verification token, Base64Url-encode it for the email link, SHA-256 hash it for storage, and send the verification email via `IEmailSender`. The user is created with `EmailVerified = false`. Response now includes `emailVerified: false` in the user object. Returns 201 as before.

**`POST /auth/login`**
Response now includes `emailVerified` in the user object so the frontend can show the verification gate for unverified users.

### New Endpoints

**`POST /auth/verify-email`** (anonymous)
- Request: `{ "token": "base64url-encoded-token" }`
- Hashes the incoming token with SHA-256, looks up the user by indexed hash match
- Validates token is not expired
- Sets `EmailVerified = true`, clears all token fields
- Returns 200 on success
- Returns 400 if token is invalid, expired, or already verified

**`POST /auth/resend-verification`** (requires `[Authorize]`)
- Checks user is not already verified (400 if so)
- Rate limited: rejects if `EmailVerificationTokenSentAt` was less than 2 minutes ago (429)
- Generates a new token, hashes and stores it, updates `EmailVerificationTokenSentAt`
- Sends verification email via `IEmailSender`
- Returns 200 on success

### Verification Gate

Implemented as a `[RequireEmailVerified]` action filter attribute, applied broadly to data-loading endpoints. This ensures new endpoints don't accidentally skip the check.

**Exempt endpoints** (must remain accessible to unverified users):
- `POST /auth/resend-verification` — so the user can request a new email
- `POST /auth/refresh` — so the frontend can maintain a valid session
- `POST /auth/logout` — so the user can sign out
- `GET /users/me` — so the frontend can check `emailVerified` status

Gated endpoints return 403 with `{ "code": "email_not_verified" }` for unverified users.

### Token Generation

Same pattern as refresh tokens: 32-byte `RandomNumberGenerator`, Base64Url-encoded for the email link, SHA-256 hashed for storage. Tokens expire after 24 hours. A database index on `EmailVerificationToken` ensures efficient lookup.

## Email Service

### Interface

```csharp
public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}
```

### Implementations

**`ConsoleEmailSender`** (Development)
Logs recipient, subject, the full verification URL, and the raw token (for easy `curl` testing) to the console via `ILogger`. Registered when `IsDevelopment()` is true.

**`AzureEmailSender`** (Production)
Uses the `Azure.Communication.Email` NuGet package. Configured via:
- `Email:ConnectionString` — Azure Communication Services connection string
- `Email:SenderAddress` — verified sender address (e.g., `noreply@codec.app`)

### Registration in Program.cs

```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, AzureEmailSender>();
```

### Email Content

- **Subject:** "Verify your Codec email"
- **Body:** Simple HTML with a link to `{frontendBaseUrl}/verify?token={rawToken}`
- **Frontend base URL:** from a `Frontend:BaseUrl` config setting (falls back to `Cors:AllowedOrigins[0]`)

## Frontend

### New Route: `/verify` (`routes/verify/+page.svelte`)

- Reads `token` from URL query params
- Calls `POST /auth/verify-email` with the token on mount
- **If user is logged in and verification succeeds:** redirects straight into the app
- **If user is not logged in and verification succeeds:** shows success message with a button to navigate to login
- Shows error message if token is invalid/expired, with a resend option if the user is logged in

### New Component: `VerificationGate`

- Inserted in `+page.svelte`, wraps the main app content
- After login/register, if `user.emailVerified` is `false`, renders a "Check your email" screen instead of the app
- Shows "Resend verification email" button with a 2-minute cooldown displayed in the UI
- Provides a "I've verified, continue" button that re-fetches user data to check `emailVerified`

### API Client Additions

```typescript
verifyEmail(token: string): Promise<void>
resendVerification(): Promise<void>
```

### Type Changes

- `AuthResponse.user` gains `emailVerified: boolean`
- `User` type in `models.ts` gains `emailVerified: boolean`
- `UserProfile` inline user object gains `emailVerified: boolean`

### Login Flow Change

- `handleLocalAuth` checks `emailVerified` after successful login/register
- If `false`, shows the `VerificationGate` instead of loading the full app
- Google auth users always have `emailVerified: true` and are unaffected

## Out of Scope

- **Email change for unverified users** — if a user registers with a typo, they must re-register with the correct email. Adding an email-change flow is a separate feature.
- **Password reset via email** — planned as separate future work.

## Testing

### Backend Unit Tests

- Token generation, hashing, and validation
- Registration creates unverified user and triggers email send
- `POST /auth/verify-email`: valid token, expired token, invalid token, already verified user
- `POST /auth/resend-verification`: success, rate limiting (under 2 min), already verified
- `ConsoleEmailSender` logs the expected output
- `[RequireEmailVerified]` filter: allows verified users, blocks unverified, allows exempt endpoints

### Backend Integration Tests

- Full registration-to-verification flow using `ConsoleEmailSender` (extract token from DB in tests)
- Unverified user receives 403 `email_not_verified` when loading app data
- Resend rate limiting returns 429

### Frontend Tests

- `/verify` route: success state (logged in redirects to app, logged out shows login link), error state (invalid token), error state (expired token)
- `VerificationGate`: renders when `emailVerified` is false, hidden when true
- Resend button: disabled during 2-minute cooldown, enabled after
- Cross-device flow: user registers on device A, verifies on device B, clicks "I've verified" on device A

## Configuration

### API (`appsettings.json` additions)

```json
{
  "Email": {
    "ConnectionString": "",
    "SenderAddress": "noreply@codec.app"
  },
  "Frontend": {
    "BaseUrl": "http://localhost:5174"
  }
}
```

`Email:ConnectionString` is only required in production. `Frontend:BaseUrl` defaults to the first entry in `Cors:AllowedOrigins` if not set.

### Migration

New EF Core migration adding the four columns to the `Users` table with an index on `EmailVerificationToken`. Existing users (all Google Sign-In) get `EmailVerified = true` via a data migration in the `Up()` method.
