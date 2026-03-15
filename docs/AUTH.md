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
3. bcrypt verify — 401 on mismatch.
4. Issues new access + refresh token pair.
5. Returns 200 with `{ accessToken, refreshToken, user }`.

#### Token Refresh (`POST /auth/refresh`)

1. Client sends `{ refreshToken }` when the access token is expired.
2. API verifies the token against the stored SHA-256 hash; returns 401 if expired or revoked.
3. Issues new access + refresh token pair, revokes the old refresh token (rotation on use).
4. Refresh token lifetime: **7 days**.

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

The ASP.NET Core API validates the token using Microsoft.AspNetCore.Authentication.JwtBearer:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.MapInboundClaims = false; // Preserve raw JWT claim names (sub, name, email, picture)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = true,
            ValidAudience = googleClientId, // Your Google Client ID
            ValidateLifetime = true
        };

        // Allow SignalR to read the JWT from the query string for WebSocket connections.
        // WebSocket requests cannot set Authorization headers, so the token is passed
        // via the access_token query parameter instead.
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
```

**Validation Steps:**
1. Verify JWT signature using Google's public keys (JWKS)
2. Check issuer is Google (`accounts.google.com`)
3. Validate audience matches your Google Client ID
4. Verify token has not expired
5. Extract user claims (subject, email, name, picture)

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

After validation, the API extracts claims and maps to an internal User record:

```csharp
app.MapGet("/me", async (ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    return Results.Ok(appUser);
}).RequireAuthorization();

// With MapInboundClaims = false, raw JWT claim names are preserved.
static async Task<User> GetOrCreateUserAsync(ClaimsPrincipal user, CodecDbContext db)
{
    var subject = user.FindFirst("sub")?.Value;
    var displayName = user.FindFirst("name")?.Value ?? user.Identity?.Name ?? "Unknown";
    var email = user.FindFirst("email")?.Value;
    var avatarUrl = user.FindFirst("picture")?.Value;

    var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject);

    if (existing is not null)
    {
        existing.DisplayName = displayName;
        existing.Email = email;
        existing.AvatarUrl = avatarUrl;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    var appUser = new User
    {
        GoogleSubject = subject,
        DisplayName = displayName,
        Email = email,
        AvatarUrl = avatarUrl
    };

    db.Users.Add(appUser);
    await db.SaveChangesAsync();
    return appUser;
}
```

> **Note:** `MapInboundClaims = false` is set on the JwtBearer configuration so that raw JWT claim names (`sub`, `name`, `email`, `picture`) pass through without being remapped to .NET `ClaimTypes` URIs. This simplifies claim access in application code.

## Token Claims

Google ID tokens contain the following claims:

| Claim | Description | Example |
|-------|-------------|----------|
| `sub` | Unique Google user ID | `110169484474386276334` |
| `email` | User's email address | `user@example.com` |
| `name` | Full name | `John Doe` |
| `picture` | Avatar URL | `https://lh3.googleusercontent.com/...` |
| `iss` | Token issuer | `accounts.google.com` |
| `aud` | Client ID | `123456789-abc.apps.googleusercontent.com` |
| `exp` | Token expiry timestamp | `1707672600` |
| `iat` | Token issued timestamp | `1707669000` |

> With `MapInboundClaims = false`, these claim names are used as-is in the `ClaimsPrincipal`. Without this setting, the JwtBearer middleware would remap them to `ClaimTypes` URIs (e.g., `sub` → `ClaimTypes.NameIdentifier`).

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
| Refresh mechanism | Google One Tap silent re-auth | Rotating opaque refresh tokens (stored hashed) |

### Current Limitations

⚠️ **Token Revocation**
- Google tokens: no real-time invalidation; compromised tokens valid until expiration
- Local tokens: access tokens cannot be individually revoked (short 1-hour TTL mitigates); refresh tokens are invalidated on use (rotation) and on sign-out

⚠️ **Password Reset**
- Password reset via email is not yet implemented

⚠️ **Reverse Account Linking**
- Google-first users (no password set) cannot yet add an email/password credential; this is deferred

⚠️ **Sign-Out**
- Sign-out button is available in the user panel (bottom of channel sidebar)
- Calls `clearSession()` from `$lib/auth/session.ts` and `google.accounts.id.disableAutoSelect()`
- Resets application state and returns user to the sign-in screen

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
