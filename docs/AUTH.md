# Authentication

This document explains how authentication works in Codec, which supports both Google Sign-In and email/password registration.

## Overview

Codec supports **two authentication methods** with equivalent security guarantees:

1. **Google Sign-In** — Stateless authentication using Google-issued ID tokens validated on every API request.
2. **Email/Password** — Registration and login with bcrypt password hashing; the API issues its own signed JWTs (access tokens + refresh tokens).

Both methods use the same JWT-based authorization middleware in the API and produce access tokens with identical claims shapes. All authenticated API requests use `Authorization: Bearer <token>`, regardless of how the token was obtained.

The web client persists the token in `localStorage` so that users stay logged in across page reloads for up to **one week** (Google session limit or refresh-token lifetime).

## Authentication Flow

Codec supports two parallel auth flows. Both result in a JWT access token stored in `localStorage` and sent with every API request.

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
  // credential is the JWT ID token
  persistToken(credential); // $lib/auth/session.ts
}, { autoSelect: true });
```

Token persistence and session management are handled by `$lib/auth/session.ts`:

```typescript
// $lib/auth/session.ts — token lifecycle management
import { persistToken, loadStoredToken, clearSession, isTokenExpired, isSessionExpired } from '$lib/auth/session';

// On sign-in: persist token + login timestamp
persistToken(idToken);

// On page load: restore token if still valid
const storedToken = loadStoredToken(); // returns null if expired or session > 1 week

// On sign-out or session expiry
clearSession();
```

### 2. API Request with Token

All authenticated API requests include the ID token in the Authorization header:

```http
GET /servers HTTP/1.1
Host: localhost:5050
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 3. Token Validation (API)

The API uses a **dual JWT Bearer scheme** with a policy-based selector. On each request, the selector peeks at the JWT `iss` claim (without validating) to route the token to the correct handler:

- **`iss` = `codec-api`** → `"Local"` scheme (validates with the API's HMAC-SHA256 signing key)
- **Any other issuer** → `"Google"` scheme (validates against Google's JWKS)

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

**Validation Steps (Google):**
1. Verify JWT signature using Google's public keys (JWKS)
2. Check issuer is Google (`accounts.google.com`)
3. Validate audience matches your Google Client ID
4. Verify token has not expired
5. Extract user claims (subject, email, name, picture)

**Validation Steps (Local):**
1. Verify HMAC-SHA256 signature using `Jwt:Secret`
2. Check issuer and audience are `codec-api`
3. Verify token has not expired
4. Extract user claims (sub = user GUID, email, name, picture)

### 4. Nickname on First Sign-Up

Both flows gate entry to the app on a nickname being set:

- **Email/password users** — Nickname is collected during registration (`RegisterRequest.Nickname`) and written directly to `User.Nickname`.
- **Google users** — After the first Google sign-in, `/me` returns `isNewUser: true`. The frontend shows a `NicknameModal` (pre-filled with the Google display name) before allowing navigation. On submit, the frontend calls `PATCH /me/nickname`.

---

### 5. Account Linking

An email/password user can link their Google account by visiting Settings → My Account → Link Google Account.

1. User signs in with Google on the link screen.
2. Frontend sends `{ googleIdToken, password }` to `POST /auth/link-google`.
3. API confirms the password (required — user must prove ownership of the email/password account).
4. API stores the Google subject on the existing `User` record.
5. From that point on, the user can sign in with either method.

> **Deferred:** Reverse linking (Google-first users adding a password) and password reset via email are not yet implemented.

---

### 6. User Identity Mapping

After validation, `UserService.GetOrCreateUserAsync` checks the JWT `iss` claim to determine the auth method and maps claims to an internal `User` record:

- **Local tokens** (`iss` = `codec-api`): The `sub` claim is the user's GUID. The user is looked up directly by `Id`.
- **Google tokens** (`iss` = `accounts.google.com`): The `sub` claim is the Google subject string. The user is looked up by `GoogleSubject`, and profile fields (display name, email, avatar) are synced from the Google token on each request.

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

    // Google JWT — sub is the Google subject string
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
- Google Identity Services initialized with `auto_select: true`
- `google.accounts.id.prompt()` triggers One Tap silent re-authentication
- When a stored token expires (but session is still within 1 week), Google silently issues a fresh token

### Security Comparison: Google vs. Email/Password

| Property | Google Sign-In | Email/Password |
|---|---|---|
| Access token lifetime | 1 hour | 1 hour |
| Session duration | 7 days (One Tap refresh) | 7 days (refresh token) |
| Password storage | N/A (Google manages) | bcrypt cost factor 12 |
| Token signing | Google RSA (JWKS) | API HMAC-SHA256 secret |
| Rate limiting on auth endpoints | Standard (100 req/min) | Strict (10 req/min per IP) |
| Account lockout | N/A (Google manages) | 5 failed attempts → 15-min lockout |
| Refresh mechanism | Google One Tap silent re-auth | Rotating opaque refresh tokens (stored hashed) |
| Logout | Client-side only | Server-side revocation via `POST /auth/logout` |

### Current Limitations

⚠️ **Token Revocation**
- Google tokens: no real-time invalidation; compromised tokens valid until expiration
- Local tokens: access tokens cannot be individually revoked (short 1-hour TTL mitigates); refresh tokens are invalidated on use (rotation) and on sign-out via the logout endpoint

⚠️ **Password Reset**
- Password reset via email is not yet implemented

⚠️ **Reverse Account Linking**
- Google-first users (no password set) cannot yet add an email/password credential; this is deferred

⚠️ **Sign-Out**
- Sign-out button is available in the user panel (bottom of channel sidebar)
- For email/password users, sign-out calls `POST /auth/logout` to revoke the refresh token server-side before clearing local state
- For Google users, calls `google.accounts.id.disableAutoSelect()` and clears `localStorage`
- Resets application state and returns user to the sign-in screen

### Accepted Tradeoffs

⚠️ **Registration Email Enumeration**
- The `POST /auth/register` endpoint returns 409 when an email is already taken, revealing whether an email is registered. Without email verification, we cannot return a generic "check your email" response. Accepted risk; mitigated by rate limiting (10 req/min).

⚠️ **Refresh Token in localStorage**
- Refresh tokens are stored in `localStorage`, which is accessible to any JavaScript on the page (XSS risk). HttpOnly cookies would be more secure but require API-side cookie management, CORS/SameSite changes, and CSRF protection rework. Documented as a future improvement.

⚠️ **No Email Verification**
- Users can register with any email address without verifying ownership. This enables email squatting (registering someone else's email before they do). Email verification is planned as future work.

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
**Cause:** ID token older than 1 hour
**Solution:** The client automatically attempts silent re-authentication via Google One Tap. If One Tap is blocked or the 1-week session has expired, the user must sign in again manually

## References

- [Google Identity Services](https://developers.google.com/identity/gsi/web)
- [JWT.io](https://jwt.io/) - Decode and inspect tokens
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
