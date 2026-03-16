# Email Verification Design

## Overview

Add email verification to the email/password registration flow. Users must verify their email before accessing the app (hard gate). Google Sign-In users are unaffected — their emails are already verified by Google.

## Data Model

New fields on the `User` entity:

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `EmailVerified` | `bool` | `false` | Whether the user has verified their email |
| `EmailVerificationToken` | `string?` | `null` | SHA-256 hash of the verification token |
| `EmailVerificationTokenExpiresAt` | `DateTimeOffset?` | `null` | Token expiry (24 hours from creation) |
| `EmailVerificationTokenSentAt` | `DateTimeOffset?` | `null` | Last send time (for rate limiting resends) |

Google Sign-In users get `EmailVerified = true` automatically.

No new table — verification is 1:1 with the user, so fields on `User` are sufficient.

## Backend API

### Modified Endpoints

**`POST /auth/register`**
After creating the user, generate a 32-byte random verification token, Base64Url-encode it for the email link, SHA-256 hash it for storage, and send the verification email via `IEmailSender`. The user is created with `EmailVerified = false`. Returns 201 as before.

### New Endpoints

**`POST /auth/verify-email`** (anonymous)
- Request: `{ "token": "base64url-encoded-token" }`
- Hashes the incoming token with SHA-256, looks up the user by hash match
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

Endpoints that load app data (initial server/channel load) check `EmailVerified`. If `false`, return 403 with a response body containing a code like `email_not_verified`. The `[Authorize]` middleware itself is unchanged.

### Token Generation

Same pattern as refresh tokens: 32-byte `RandomNumberGenerator`, Base64Url-encoded for the email link, SHA-256 hashed for storage. Tokens expire after 24 hours.

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
Logs recipient, subject, and the full verification URL to the console via `ILogger`. Registered when `IsDevelopment()` is true.

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
- Shows success message with a button to navigate to login
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

### Login Flow Change

- `handleLocalAuth` checks `emailVerified` after successful login/register
- If `false`, shows the `VerificationGate` instead of loading the full app
- Google auth users always have `emailVerified: true` and are unaffected

## Testing

### Backend Unit Tests

- Token generation, hashing, and validation
- Registration creates unverified user and triggers email send
- `POST /auth/verify-email`: valid token, expired token, invalid token, already verified user
- `POST /auth/resend-verification`: success, rate limiting (under 2 min), already verified
- `ConsoleEmailSender` logs the expected output

### Backend Integration Tests

- Full registration-to-verification flow using `ConsoleEmailSender` (extract token from DB in tests)
- Unverified user receives 403 `email_not_verified` when loading app data
- Resend rate limiting returns 429

### Frontend Tests

- `/verify` route: success state, error state (invalid token), error state (expired token)
- `VerificationGate`: renders when `emailVerified` is false, hidden when true
- Resend button: disabled during 2-minute cooldown, enabled after

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

New EF Core migration adding the four columns to the `Users` table. Existing users (all Google Sign-In) get `EmailVerified = true` via a data migration in the `Up()` method.
