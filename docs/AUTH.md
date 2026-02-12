# Authentication

This document explains how authentication works in Codec using Google ID tokens.

## Overview

Codec uses a **stateless authentication** model powered by Google Identity Services. The web client obtains a JWT ID token from Google, and the API validates it on each request without maintaining server-side sessions.

To provide a seamless user experience, the web client persists the token in `localStorage` so that users stay logged in across page reloads for up to **one week**.

## Authentication Flow

### 1. Client-Side Sign-In

The web client uses Google Identity Services JavaScript SDK:

```javascript
// Initialize Google Identity Services
google.accounts.id.initialize({
  client_id: PUBLIC_GOOGLE_CLIENT_ID,
  auto_select: true, // Enable silent re-authentication
  callback: handleCredentialResponse
});

// User clicks "Sign in with Google", or One Tap fires automatically
google.accounts.id.prompt();

// Receive ID token
function handleCredentialResponse(response) {
  const idToken = response.credential; // JWT token
  // Persist token to localStorage for session continuity
  localStorage.setItem('codec_id_token', idToken);
  localStorage.setItem('codec_login_ts', String(Date.now())); // track session age
}
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
    });
```

**Validation Steps:**
1. Verify JWT signature using Google's public keys (JWKS)
2. Check issuer is Google (`accounts.google.com`)
3. Validate audience matches your Google Client ID
4. Verify token has not expired
5. Extract user claims (subject, email, name, picture)

### 4. User Identity Mapping

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

### Current Limitations

⚠️ **Token Revocation**
- No real-time token invalidation
- Compromised tokens valid until expiration
- Future: Implement token blacklist or short TTL

⚠️ **Single Identity Provider**
- Only Google authentication supported
- Future: Add Microsoft, GitHub, email/password

⚠️ **Sign-Out**
- No explicit sign-out button yet
- Users can clear session by clearing browser storage
- Future: Add sign-out UI that calls `clearSession()` and `google.accounts.id.disableAutoSelect()`

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
