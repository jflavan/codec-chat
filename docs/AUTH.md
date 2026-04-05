# Authentication

This document explains how authentication works in Codec, which supports Google Sign-In, email/password registration, GitHub and Discord OAuth, and SAML 2.0 SSO.

## Overview

Codec supports **five authentication methods** with equivalent security guarantees:

1. **Google Sign-In** — Google-issued ID tokens are exchanged for backend-issued JWTs via `POST /auth/google`, giving Google users the same rotating refresh token lifecycle as all other auth methods.
2. **Email/Password** — Registration and login with bcrypt password hashing; the API issues its own signed JWTs (access tokens + refresh tokens).
3. **GitHub OAuth** — Authorization code flow; API exchanges code for GitHub access token, fetches user profile and email.
4. **Discord OAuth** — Authorization code flow; API exchanges code for Discord access token, fetches user profile and avatar.
5. **SAML 2.0 SSO** — SP-initiated login with HTTP-Redirect binding; IdP-signed SAML assertions validated by the API.

All methods use the same JWT-based authorization middleware in the API and produce access tokens with identical claims shapes. All authenticated API requests use `Authorization: Bearer <token>`, regardless of how the token was obtained.

The web client persists the token in `localStorage` so that users stay logged in across page reloads for up to **one week** (refresh-token lifetime).

## Authentication Flow

Codec supports two parallel auth flows. Both result in a backend-issued JWT access token stored in `localStorage` and sent with every API request. All auth methods produce identical `codec-api`-issued tokens with rotating refresh tokens.

---

### Email/Password Flow

#### Registration (`POST /auth/register`)

1. Client submits `{ email, password, nickname }`.
2. API validates email uniqueness (409 Conflict if taken).
3. Password is hashed with bcrypt (cost factor 12).
4. A new `User` record is created with `PasswordHash` set and `GoogleSubject` = null.
5. A 1-hour JWT access token and a 7-day refresh token are issued.
6. The refresh token is stored hashed (SHA-256) in the `RefreshTokens` table.
7. API returns 201 with `{ accessToken, refreshToken, user }`.

#### Sign-In (`POST /auth/login`)

1. Client submits `{ email, password }`.
2. API looks up user by email; returns 401 if not found or if `PasswordHash` is null (Google-only account).
3. If account is locked (`LockoutEnd` > now), returns 401 with "Account temporarily locked."
4. bcrypt verify — on mismatch, increments `FailedLoginAttempts`. After 5 consecutive failures, sets `LockoutEnd` to 15 minutes from now.
5. On success, resets `FailedLoginAttempts` to 0 and clears `LockoutEnd`.
6. Issues new access + refresh token pair.
7. Returns 200 with `{ accessToken, refreshToken, user }`.

#### Token Refresh (`POST /auth/refresh`)

1. Client sends `{ refreshToken }` when the access token is expired.
2. API verifies the token against the stored SHA-256 hash; returns 401 if expired or revoked.
3. Revokes the old refresh token atomically using PostgreSQL's `xmin` optimistic concurrency token — concurrent refresh requests on the same token will fail (second request gets 401).
4. Issues new access + refresh token pair.
5. Refresh token lifetime: **7 days**.

#### Logout (`POST /auth/logout`)

1. Client sends `{ refreshToken }`.
2. API revokes the token (sets `RevokedAt`). Returns 204 regardless of whether the token was found (no information leakage).
3. Frontend calls this before clearing `localStorage` on sign-out.
4. Unauthenticated endpoint — the refresh token itself is the credential.

---

### Google Sign-In Flow

### 1. Client-Side Sign-In

The web client uses the Google Identity Services JavaScript SDK. The initialization logic is encapsulated in `$lib/auth/google.ts`:

```typescript
// $lib/auth/google.ts — wraps Google Identity Services initialization
import { initGoogleIdentity, renderGoogleButton } from '$lib/auth/google';

initGoogleIdentity(clientId, (credential) => {
  // credential is a Google ID token — exchanged for backend JWTs below
}, { autoSelect: hasStoredAuthType() && authType === 'google' });
```

### 2. Token Exchange (`POST /auth/google`)

When the user signs in with Google, the frontend sends the Google ID token to the backend for exchange:

1. Client calls `POST /auth/google` with `{ credential: "<google-id-token>" }`.
2. API validates the Google ID token against Google's JWKS (issuer, audience, lifetime, signature).
3. API checks for account-linking scenarios: if an existing email/password user has the same email but no linked Google account, returns `{ needsLinking: true, email }` instead of creating a duplicate.
4. API finds or creates a user by `GoogleSubject`, syncing profile fields (name, email, avatar) if changed.
5. API issues a 1-hour access token and a 7-day rotating refresh token (identical to email/password flow).
6. Returns `{ accessToken, refreshToken, isNewUser, user }`.

The frontend stores the backend-issued tokens and uses the standard `POST /auth/refresh` flow for token renewal — no Google One Tap silent re-authentication is needed.

### 3. FedCM Console Error Suppression

Google One Tap `auto_select` is only enabled when the user has a previously stored auth type of `'google'` (`hasStoredAuthType() && authType === 'google'`). This prevents FedCM console errors for first-time visitors who have no prior Google session.

### 4. API Request with Token

All authenticated API requests include the backend-issued JWT in the Authorization header:

```http
GET /servers HTTP/1.1
Host: localhost:5050
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 5. Token Validation (API)

The API uses a **dual JWT Bearer scheme** with a policy-based selector. On each request, the selector peeks at the JWT `iss` claim (without validating) to route the token to the correct handler:

- **`iss` = `codec-api`** → `"Local"` scheme (validates with the API's HMAC-SHA256 signing key)
- **Any other issuer** → `"Google"` scheme (validates against Google's JWKS)

Since Google Sign-In now exchanges tokens for backend-issued JWTs (`iss` = `codec-api`), Google users' subsequent API requests are validated via the `"Local"` scheme — the same as email/password users. The `"Google"` scheme is retained for backward compatibility.

```csharp
builder.Services.AddAuthentication("Selector")
    .AddPolicyScheme("Selector", "Google or Local", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            // Peek at the issuer claim without validating
            var token = /* extract from Authorization header or access_token query */;
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            if (jwt.Issuer == "codec-api") return "Local";
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
        // SignalR WebSocket: read token from ?access_token query string
    })
    .AddJwtBearer("Local", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "codec-api",
            ValidateAudience = true,
            ValidAudience = "codec-api",
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
        // SignalR WebSocket: read token from ?access_token query string
    });
```

**Validation Steps (Local — used by email/password, Google, GitHub, Discord, and SAML users):**
1. Verify HMAC-SHA256 signature using `Jwt:Secret`
2. Check issuer and audience are `codec-api`
3. Verify token has not expired
4. Extract user claims (sub = user GUID, email, name, picture)

**Validation Steps (Google — retained for backward compatibility):**
1. Verify JWT signature using Google's public keys (JWKS)
2. Check issuer is Google (`accounts.google.com`)
3. Validate audience matches your Google Client ID
4. Verify token has not expired
5. Extract user claims (subject, email, name, picture)

### 6. Nickname on First Sign-Up

Both flows gate entry to the app on a nickname being set:

- **Email/password users** — Nickname is collected during registration (`RegisterRequest.Nickname`) and written directly to `User.Nickname`.
- **Google users** — After the first Google sign-in, `POST /auth/google` returns `isNewUser: true`. The frontend shows a `NicknameModal` (pre-filled with the Google display name) before allowing navigation. On submit, the frontend calls `PATCH /me/nickname`.

---

### 7. Account Linking

An email/password user can link their Google account by visiting Settings → My Account → Link Google Account.

1. User signs in with Google on the link screen.
2. Frontend sends `{ googleIdToken, password }` to `POST /auth/link-google`.
3. API confirms the password (required — user must prove ownership of the email/password account).
4. API stores the Google subject on the existing `User` record.
5. API issues backend JWTs; the frontend switches to `authType = 'google'`.
6. From that point on, the user can sign in with either method.

Account linking is also detected automatically during `POST /auth/google`: if a Google sign-in matches an existing email/password account by email, the API returns `{ needsLinking: true }` and the frontend prompts the user to confirm their password before linking.

> **Deferred:** Reverse linking (Google-first users adding a password) and password reset via email are not yet implemented.

---

### 8. User Identity Mapping

After validation, `UserService.GetOrCreateUserAsync` checks the JWT `iss` claim to determine the auth method and maps claims to an internal `User` record:

- **Local tokens** (`iss` = `codec-api`): The `sub` claim is the user's GUID. The user is looked up directly by `Id`. This covers email/password, Google (post-exchange), GitHub, Discord, and SAML users.
- **Google tokens** (`iss` = `accounts.google.com`): Retained for backward compatibility. The `sub` claim is the Google subject string. The user is looked up by `GoogleSubject`.

```csharp
public async Task<(User, bool isNewUser)> GetOrCreateUserAsync(ClaimsPrincipal principal)
{
    var issuer = principal.FindFirst("iss")?.Value;

    if (issuer == "codec-api")
    {
        // Local JWT — sub is the user's GUID
        var userId = Guid.Parse(principal.FindFirst("sub")!.Value);
        var user = await db.Users.FindAsync(userId);
        return (user!, false);
    }

    // Google JWT (backward compat) — sub is the Google subject string
    var googleSubject = principal.FindFirst("sub")?.Value;
    var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSubject);

    if (existing is not null)
    {
        // Sync profile from Google token
        existing.DisplayName = principal.FindFirst("name")?.Value ?? existing.DisplayName;
        existing.Email = principal.FindFirst("email")?.Value;
        existing.AvatarUrl = principal.FindFirst("picture")?.Value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return (existing, false);
    }

    // New Google user
    var newUser = new User { GoogleSubject = googleSubject, /* ... */ };
    db.Users.Add(newUser);
    await db.SaveChangesAsync();
    return (newUser, true);
}
```

> **Note:** `MapInboundClaims = false` is set on both JWT Bearer schemes so that raw JWT claim names (`sub`, `name`, `email`, `picture`) pass through without being remapped to .NET `ClaimTypes` URIs.

## Token Claims

Both token types produce an identical claims shape for application code:

| Claim | Google Token | Local Token |
|-------|-------------|-------------|
| `sub` | Google user ID (`110169484474386276334`) | User GUID (`a1b2c3d4-...`) |
| `email` | User's email | User's email |
| `name` | Google display name | Effective display name |
| `picture` | Google avatar URL | Custom avatar path (if set) |
| `iss` | `accounts.google.com` | `codec-api` |
| `aud` | Google Client ID | `codec-api` |
| `exp` | 1 hour from issuance | 1 hour from issuance |

> With `MapInboundClaims = false` on both schemes, these claim names are used as-is in the `ClaimsPrincipal`. The `iss` claim is used by `UserService` to determine which lookup strategy to use (`Id` for local, `GoogleSubject` for Google).

## Configuration

### Web Client (`apps/web/.env`)

```env
# Your Google OAuth 2.0 Client ID
PUBLIC_GOOGLE_CLIENT_ID=123456789-abc.apps.googleusercontent.com

# API base URL for requests
PUBLIC_API_BASE_URL=http://localhost:5050
```

### API Server (`apps/api/Codec.Api/appsettings.Development.json`)

```json
{
  "Google": {
    "ClientId": "123456789-abc.apps.googleusercontent.com"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5174"]
  }
}
```

**Important:** Both the web client and API must use the **same Google Client ID**.

## Google Cloud Console Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Navigate to **APIs & Services** > **Credentials**
3. Click **Create Credentials** > **OAuth 2.0 Client ID**
4. Configure application type as **Web application**
5. Add **Authorized JavaScript origins**:
   - Development: `http://localhost:5174`
   - Production: `https://yourdomain.com`
6. **Do not** add redirect URIs (not needed for ID token flow)
7. Copy the generated Client ID

## Security Considerations

### What's Protected

✅ **JWT Signature Verification**
- Tokens are cryptographically signed by Google
- API validates signature using Google's public keys (JWKS)
- Prevents token tampering

✅ **Audience Validation**
- Ensures token was issued for your application
- Prevents token reuse from other apps

✅ **Issuer Validation**
- Confirms token came from Google
- Prevents tokens from untrusted sources

✅ **Expiration Checking**
- Tokens expire after 1 hour
- Prevents replay of old tokens

✅ **Stateless Design**
- No server-side sessions to hijack
- Tokens contain all necessary information

### Session Persistence

✅ **localStorage Token Storage**
- ID token persisted to `localStorage` across page reloads
- Client-side JWT `exp` claim check (with 60-second buffer) prevents sending stale tokens
- Maximum session duration capped at **7 days** via a stored login timestamp
- Session is cleared automatically when the 1-week limit is reached

✅ **Automatic Token Refresh**
- All auth methods (Google, email/password, GitHub, Discord, SAML) use the same `POST /auth/refresh` rotating refresh token flow
- When a stored access token expires (but session is still within 1 week), the frontend automatically calls `POST /auth/refresh` to obtain a new access token
- Google One Tap `auto_select` is only enabled for returning Google users (prevents FedCM console errors for new visitors)

### Security Comparison: Google vs. Email/Password

| Property | Google Sign-In | Email/Password |
|---|---|---|
| Access token lifetime | 1 hour | 1 hour |
| Session duration | 7 days (refresh token) | 7 days (refresh token) |
| Password storage | N/A (Google manages) | bcrypt cost factor 12 |
| Token signing | API HMAC-SHA256 secret (post-exchange) | API HMAC-SHA256 secret |
| Rate limiting on auth endpoints | Standard (100 req/min) | Strict (10 req/min per IP) |
| Account lockout | N/A (Google manages) | 5 failed attempts → 15-min lockout |
| Refresh mechanism | Rotating opaque refresh tokens (stored hashed) | Rotating opaque refresh tokens (stored hashed) |
| Logout | Server-side revocation via `POST /auth/logout` | Server-side revocation via `POST /auth/logout` |

### Account Disabling (Global Admin)

Global admins can disable user accounts via the admin panel (`POST /admin/users/{id}/disable`). Disabled accounts are blocked across all auth flows:

- **Email/password login** (`POST /auth/login`) — returns 401 with `{ error: "Account is disabled.", reason: "..." }`
- **Token refresh** (`POST /auth/refresh`) — returns 401 with `{ error: "Account is disabled." }`
- **Google Sign-In** (`POST /auth/google`) — returns 401 after token validation
- **OAuth callbacks** (GitHub, Discord) — returns 401 after code exchange

When an account is disabled:
1. `User.IsDisabled` is set to `true` with a reason and timestamp
2. All refresh tokens for the user are immediately revoked
3. The `GlobalAdminHandler` authorization policy rejects disabled admins, even if their JWT is still valid
4. The existing access token remains valid until it naturally expires (up to 1 hour), but only for non-admin endpoints — admin endpoints check `IsDisabled` on every request via the authorization handler

Re-enabling an account (`POST /admin/users/{id}/enable`) clears the disabled flag. The user must sign in again since their refresh tokens were revoked.

### Account Deletion (Self-Service)

Users can permanently delete their own accounts via `DELETE /me`. This is a hard delete — the user row is removed from the database and cannot be recovered.

#### Prerequisites

- **Server ownership transfer required** — users who own servers must transfer ownership before deletion. The endpoint returns 400 with a list of owned servers if any exist.
- **Identity re-authentication** — users must prove their identity before deletion:
  - Email/password users: provide their current password
  - Google-only users (no password): re-authenticate with a Google credential
  - GitHub/Discord OAuth users: provide their password (OAuth linking requires a password)
- **Typed confirmation** — the request must include `confirmationText: "DELETE"` (case-sensitive)

#### Deletion Flow (`DELETE /me`)

1. Client sends `{ confirmationText, password?, googleCredential? }`.
2. API validates `confirmationText == "DELETE"` (400 if not).
3. API checks server ownership via `GetOwnedServersAsync` (400 if user owns servers).
4. API verifies identity via password (bcrypt) or Google credential (OIDC JWT validation).
5. API broadcasts `AccountDeleted` SignalR event to `user-{userId}` group (forces all sessions to sign out).
6. API calls `DeleteAccountAsync` which runs in a single transaction:
   - Revokes all refresh tokens
   - Deletes friendships and voice calls
   - Nulls user references on custom emojis, webhooks, server invites, system announcements, reports, and banned member records
   - Sets `AuthorUserId = null` on all server messages and direct messages (anonymization)
   - Deletes reactions by the user
   - Deletes the user row (EF cascade handles server memberships, DM channel memberships, presence state, voice state, push subscriptions, notification overrides)
7. Returns 200 with `{ message: "Account deleted successfully." }`.

#### Data Handling

- **Messages preserved** — server and DM messages remain in place but with `AuthorUserId` set to null. The frontend displays these as "Deleted User" with dimmed styling.
- **Friendships removed** — all friend requests (sent and received) are deleted.
- **Server memberships removed** — the user is removed from all servers via cascade delete.
- **Audit trail** — audit log entries retain `ActorUserId = null` (SetNull behavior) so logs are preserved but de-identified.
- **Admin actions deleted** — `AdminAction` rows where the user was the actor are deleted.

#### Frontend UX

- Account Settings page includes a "Delete Account" danger zone section
- Clicking "Delete Account" opens a confirmation modal with:
  - Warning about irreversible consequences
  - Password input (or Google re-auth button for Google-only users)
  - "Type DELETE to confirm" text input
  - Disabled submit button until both fields are filled
- On successful deletion, the client clears auth state and redirects to the sign-in screen
- Active SignalR connections receive an `AccountDeleted` event that triggers sign-out

### Current Limitations

⚠️ **Token Revocation**
- Access tokens (all auth methods): cannot be individually revoked (short 1-hour TTL mitigates)
- Refresh tokens: invalidated on use (rotation) and on sign-out via the logout endpoint

⚠️ **Password Reset**
- Admin password reset (`POST /admin/users/{id}/reset-password`) removes the user's password credential (sets `PasswordHash` to null). It does not send a reset email. Users must use another auth provider (Google, GitHub, Discord, or SAML) to regain access. Full password reset via email is not yet implemented

⚠️ **Reverse Account Linking**
- Google-first users (no password set) cannot yet add an email/password credential; this is deferred

⚠️ **Sign-Out**
- Sign-out button is available in the user panel (bottom of channel sidebar)
- All auth methods call `POST /auth/logout` to revoke the refresh token server-side before clearing local state
- For Google users, also calls `google.accounts.id.disableAutoSelect()`
- Resets application state and returns user to the sign-in screen

### Accepted Tradeoffs

⚠️ **Registration Email Enumeration**
- The `POST /auth/register` endpoint returns 409 when an email is already taken, revealing whether an email is registered. Without email verification, we cannot return a generic "check your email" response. Accepted risk; mitigated by rate limiting (10 req/min).

⚠️ **Refresh Token in localStorage**
- Refresh tokens are stored in `localStorage`, which is accessible to any JavaScript on the page (XSS risk). HttpOnly cookies would be more secure but require API-side cookie management, CORS/SameSite changes, and CSRF protection rework. Documented as a future improvement.

### Email Verification

Email verification is required for all email/password registrations (hard gate):

- On registration, a verification email is sent with a 24-hour token link. If email sending fails, registration still succeeds (the response includes `emailSent: false` so the frontend can prompt the user to resend)
- Users cannot access the app until they click the verification link
- The `POST /auth/verify-email` endpoint validates the token (anonymous)
- The `POST /auth/resend-verification` endpoint allows resending (authenticated, 2-minute cooldown). Returns 502 if email sending fails
- Google Sign-In users are auto-verified (Google already verified the email)
- Verification tokens are SHA-256 hashed in the database (same pattern as refresh tokens)
- A `[RequireEmailVerified]` action filter gates data-loading endpoints, returning 403 with `{ code: "email_not_verified" }` for unverified users
- Exempt endpoints: `/auth/*`, `/me`, `/auth/refresh`, `/auth/logout`
- In development, verification emails are logged to the console via `ConsoleEmailSender`
- In production, emails are sent via Azure Communication Services (`AzureEmailSender`)

## GitHub OAuth

### Flow (`POST /auth/oauth/github`)

1. Frontend redirects user to GitHub's authorization URL with the configured client ID.
2. GitHub redirects back with an authorization code.
3. Frontend sends the code to `POST /auth/oauth/github`.
4. API exchanges the code for an access token via GitHub's token endpoint.
5. API fetches the user's profile (`GET https://api.github.com/user`) and primary email (`GET https://api.github.com/user/emails`, includes private emails).
6. User is matched by `GitHubSubject` (GitHub user ID). If no match, a new user is created or linked to an existing email/password account.
7. API issues access + refresh token pair (same as email/password flow).

### Configuration

```json
{
  "OAuth": {
    "GitHub": {
      "ClientId": "<github-oauth-app-client-id>",
      "ClientSecret": "<github-oauth-app-client-secret>",
      "Enabled": true
    }
  }
}
```

---

## Discord OAuth

### Flow (`POST /auth/oauth/discord`)

1. Frontend redirects user to Discord's authorization URL with the configured client ID.
2. Discord redirects back with an authorization code.
3. Frontend sends the code to `POST /auth/oauth/discord`.
4. API exchanges the code for an access token via Discord's token endpoint.
5. API fetches the user's profile (`GET https://discord.com/api/users/@me`), including email and avatar.
6. User is matched by `DiscordSubject` (Discord user ID). If no match, a new user is created or linked to an existing email/password account.
7. API issues access + refresh token pair.

### Configuration

```json
{
  "OAuth": {
    "Discord": {
      "ClientId": "<discord-application-client-id>",
      "ClientSecret": "<discord-application-client-secret>",
      "Enabled": true
    }
  }
}
```

---

## OAuth Provider Discovery

`GET /auth/oauth/config` (public, no auth) returns which OAuth providers are enabled:

```json
{
  "github": { "enabled": true, "clientId": "..." },
  "discord": { "enabled": true, "clientId": "..." }
}
```

The frontend uses this to show/hide OAuth buttons on the login page.

---

## SAML 2.0 SSO

### Overview

Codec supports SP-initiated SAML 2.0 single sign-on for enterprise identity providers (Okta, Microsoft Entra ID, etc.). SAML IdPs are configured per-instance (not per-server) by global admins.

### Flow

1. User clicks an IdP button on the login page (IdPs listed via `GET /auth/saml/providers`).
2. Frontend navigates to `GET /auth/saml/login/{idpId}`.
3. API generates an `AuthnRequest` with Deflate compression and redirects to the IdP's SSO URL (HTTP-Redirect binding).
4. A correlation cookie stores the SAML request ID for response validation.
5. User authenticates at the IdP.
6. IdP POSTs a SAML Response to `POST /auth/saml/acs` (Assertion Consumer Service).
7. API validates the response:
   - XML signature verification using the IdP's X.509 certificate (PEM-encoded in `SamlIdentityProvider.CertificatePem`).
   - `InResponseTo` matches the stored request ID from the correlation cookie.
   - Assertion `Conditions` (NotBefore, NotOnOrAfter) are checked.
   - `Audience` matches the SP entity ID.
8. User is matched by `SamlNameId` + `SamlIdentityProviderId`. If no match and `AllowJitProvisioning` is enabled, a new user is created (JIT provisioning).
9. API issues access + refresh token pair and redirects to the frontend with tokens.

### Data Model

```csharp
public class SamlIdentityProvider
{
    public Guid Id { get; set; }
    public string EntityId { get; set; }           // IdP entity ID
    public string DisplayName { get; set; }         // Shown on login page
    public string SingleSignOnUrl { get; set; }     // IdP SSO URL
    public string CertificatePem { get; set; }      // X.509 cert for signature verification
    public bool IsEnabled { get; set; }             // Active and usable
    public bool AllowJitProvisioning { get; set; }  // Auto-create accounts
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

User entity extended with:
- `SamlNameId` (string?) — persistent NameID from the IdP
- `SamlIdentityProviderId` (Guid?) — FK to `SamlIdentityProvider`

### Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/auth/saml/providers` | Public | List enabled IdPs (display name + ID) |
| `GET` | `/auth/saml/login/{idpId}` | Public | SP-initiated SSO redirect |
| `POST` | `/auth/saml/acs` | Public | Assertion Consumer Service (callback) |
| `GET` | `/auth/saml/metadata` | Public | SP metadata XML |
| `POST` | `/auth/saml/idps` | Global Admin | Create IdP configuration |
| `PUT` | `/auth/saml/idps/{id}` | Global Admin | Update IdP configuration |
| `DELETE` | `/auth/saml/idps/{id}` | Global Admin | Delete IdP configuration |
| `POST` | `/auth/saml/idps/import-metadata` | Global Admin | Import IdP from metadata XML |

### Configuration

```json
{
  "Saml": {
    "SpEntityId": "https://codec-chat.com",
    "SpAcsUrl": "https://api.codec-chat.com/auth/saml/acs"
  }
}
```

---

## Production Recommendations

### Required for Production

🔒 **HTTPS Only**
```csharp
// Force HTTPS
app.UseHttpsRedirection();

// Strict Transport Security
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000");
    await next();
});
```

🔒 **Secrets Management**
- Store Client ID in Azure Key Vault, AWS Secrets Manager, etc.
- Never commit secrets to source control
- Use environment variables or secret management services

🔒 **CORS Restrictions**
```json
{
  "Cors": {
    "AllowedOrigins": ["https://yourdomain.com"]
  }
}
```

🔒 **Rate Limiting**
- Prevent brute force attacks
- Limit requests per IP/user
- Use ASP.NET Core rate limiting middleware

🔒 **Monitoring & Logging**
- Log authentication failures
- Monitor for suspicious patterns
- Alert on repeated failures
- Use Application Insights or similar

### Optional Enhancements

💡 **Multi-Factor Authentication**
- Require MFA for sensitive operations
- Integrate with Google's MFA

💡 **OAuth Scopes**
- Request minimal required scopes
- Use `openid email profile` only

## reCAPTCHA v3 Bot Protection

Login and registration endpoints are protected by invisible reCAPTCHA v3 (Enterprise). This runs silently — no user interaction is required.

### How It Works

1. The frontend loads the reCAPTCHA Enterprise script on the login page
2. At form submit time, `grecaptcha.enterprise.execute()` generates a score-based token
3. The token is sent with the login/register request body as `recaptchaToken`
4. The API's `[ValidateRecaptcha]` action filter verifies the token via Google's Enterprise Assessment API
5. Requests with missing tokens get 400; failed verification gets 403

### Configuration

**API (`appsettings.json`):**
```json
"Recaptcha": {
  "SecretKey": "<google-cloud-api-key>",
  "SiteKey": "<recaptcha-site-key>",
  "ProjectId": "<gcp-project-id>",
  "ScoreThreshold": 0.5,
  "Enabled": true
}
```

- `SecretKey`: Google Cloud API key restricted to reCAPTCHA Enterprise (stored in Key Vault in production)
- `SiteKey`: reCAPTCHA site key (public)
- `ProjectId`: Google Cloud project ID
- `ScoreThreshold`: Minimum score (0.0–1.0) to pass verification. Default 0.5.
- `Enabled`: Set to `false` to bypass verification (local dev, tests)

**Frontend (`.env`):**
```
PUBLIC_RECAPTCHA_SITE_KEY=<recaptcha-site-key>
```

### Fail-Closed Behavior

If Google's assessment API is unreachable, verification fails and the request is rejected. Google Sign-In is unaffected and remains available as a fallback.

## Troubleshooting

### Error: "Invalid client ID"
**Cause:** Mismatch between web client and API configuration
**Solution:** Ensure same Client ID in both `.env` and `appsettings.json`

### Error: "Token signature invalid"
**Cause:** Token tampered or not from Google
**Solution:** Verify token is genuine, check for network interception

### Error: "Unauthorized JavaScript origin"
**Cause:** Origin not allowed in Google Cloud Console
**Solution:** Add `http://localhost:5174` to authorized origins

### Error: "CORS policy error"
**Cause:** API not allowing requests from web origin
**Solution:** Add web origin to `Cors:AllowedOrigins` in API settings

### Error: "Token expired"
**Cause:** Access token older than 1 hour
**Solution:** The client automatically attempts a token refresh via `POST /auth/refresh`. If the refresh token is also expired or revoked (beyond the 7-day lifetime), the user must sign in again manually

## References

- [Google Identity Services](https://developers.google.com/identity/gsi/web)
- [JWT.io](https://jwt.io/) - Decode and inspect tokens
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
