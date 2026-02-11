# Authentication

This document explains how authentication works in Codec using Google ID tokens.

## Overview

Codec uses a **stateless authentication** model powered by Google Identity Services. The web client obtains a JWT ID token from Google, and the API validates it on each request without maintaining server-side sessions.

## Authentication Flow

### 1. Client-Side Sign-In

The web client uses Google Identity Services JavaScript SDK:

```javascript
// Initialize Google Identity Services
google.accounts.id.initialize({
  client_id: PUBLIC_GOOGLE_CLIENT_ID,
  callback: handleCredentialResponse
});

// User clicks "Sign in with Google"
google.accounts.id.prompt();

// Receive ID token
function handleCredentialResponse(response) {
  const idToken = response.credential; // JWT token
  // Store token and use for API calls
}
```

### 2. API Request with Token

All authenticated API requests include the ID token in the Authorization header:

```http
GET /servers HTTP/1.1
Host: localhost:5000
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 3. Token Validation (API)

The ASP.NET Core API validates the token using Microsoft.AspNetCore.Authentication.JwtBearer:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
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

async Task<User> GetOrCreateUserAsync(ClaimsPrincipal principal, CodecDbContext db)
{
    var googleSub = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSub);
    
    if (user is null)
    {
        user = new User
        {
            GoogleSubject = googleSub,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name),
            Email = principal.FindFirstValue(ClaimTypes.Email),
            AvatarUrl = principal.FindFirstValue("picture")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
    
    return user;
}
```

## Token Claims

Google ID tokens contain the following claims:

| Claim | Type | Description | Example |
|-------|------|-------------|---------|
| `sub` | NameIdentifier | Unique Google user ID | `110169484474386276334` |
| `email` | Email | User's email address | `user@example.com` |
| `name` | Name | Full name | `John Doe` |
| `picture` | Custom | Avatar URL | `https://lh3.googleusercontent.com/...` |
| `iss` | Issuer | Token issuer | `accounts.google.com` |
| `aud` | Audience | Client ID | `123456789-abc.apps.googleusercontent.com` |
| `exp` | Expiration | Token expiry timestamp | `1707672600` |
| `iat` | IssuedAt | Token issued timestamp | `1707669000` |

## Configuration

### Web Client (`apps/web/.env`)

```env
# Your Google OAuth 2.0 Client ID
PUBLIC_GOOGLE_CLIENT_ID=123456789-abc.apps.googleusercontent.com

# API base URL for requests
PUBLIC_API_BASE_URL=http://localhost:5000
```

### API Server (`apps/api/Codec.Api/appsettings.Development.json`)

```json
{
  "Google": {
    "ClientId": "123456789-abc.apps.googleusercontent.com"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
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
   - Development: `http://localhost:5173`
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

### Current Limitations

⚠️ **Token Refresh**
- Tokens expire after 1 hour
- User must re-authenticate manually
- Future: Implement automatic token refresh

⚠️ **Token Revocation**
- No real-time token invalidation
- Compromised tokens valid until expiration
- Future: Implement token blacklist or short TTL

⚠️ **Single Identity Provider**
- Only Google authentication supported
- Future: Add Microsoft, GitHub, email/password

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

💡 **Token Refresh**
- Implement automatic token renewal
- Use refresh tokens for long-lived sessions

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
**Solution:** Add `http://localhost:5173` to authorized origins

### Error: "CORS policy error"
**Cause:** API not allowing requests from web origin
**Solution:** Add web origin to `Cors:AllowedOrigins` in API settings

### Error: "Token expired"
**Cause:** ID token older than 1 hour
**Solution:** User must sign in again or implement token refresh

## References

- [Google Identity Services](https://developers.google.com/identity/gsi/web)
- [JWT.io](https://jwt.io/) - Decode and inspect tokens
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
