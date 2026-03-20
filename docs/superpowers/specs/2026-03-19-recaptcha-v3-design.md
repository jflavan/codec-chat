# reCAPTCHA v3 Integration — Design Spec

## Overview

Add invisible reCAPTCHA v3 verification to the login and register endpoints to protect against credential stuffing and spam account creation. Uses Google's score-based approach (0.0–1.0) with a configurable threshold. No visible UI change — v3 runs silently in the background.

## Scope

**In scope:**
- reCAPTCHA v3 on login and register (email/password auth)
- Backend token verification via action filter
- Infrastructure (Key Vault, CI/CD) for secret/key management
- Google Cloud CLI setup for reCAPTCHA key pair
- `Enabled` flag to disable in local dev / tests

**Out of scope:**
- Google Sign-In (already validated via Google JWKS)
- Token refresh, logout, email verification endpoints (lower risk, require existing tokens)
- `link-google` endpoint — requires a valid Google ID token in addition to credentials, so bot abuse risk is negligible
- reCAPTCHA Enterprise (paid tier)

## 1. Google Cloud Setup

Create a reCAPTCHA v3 site key pair using `gcloud recaptcha keys create`:
- One key for the web domain (site key — public, used in browser)
- The secret key is retrieved from the Google Cloud Console or via `gcloud` and stored securely

## 2. Infrastructure & CI/CD

Follows the existing secret management pattern (identical to `Google:ClientId` and `Jwt:Secret`).

### API Secret Key Flow
1. `RECAPTCHA_SECRET_KEY` added as a GitHub repository secret
2. `infra.yml` passes it to `main.bicep` as a Bicep parameter
3. `main.bicep` stores it in Key Vault as `Recaptcha--SecretKey`
4. `container-app-api.bicep` maps Key Vault secret to env var `Recaptcha__SecretKey`
5. ASP.NET Core configuration binds `Recaptcha__SecretKey` → `Recaptcha:SecretKey`

### Frontend Site Key Flow
1. `PUBLIC_RECAPTCHA_SITE_KEY` added as a GitHub repository secret
2. `cd.yml` passes it as a Docker `--build-arg` (same pattern as `PUBLIC_GOOGLE_CLIENT_ID`)
3. Web `Dockerfile` declares the build arg and sets it as an env var
4. `container-app-web.bicep` sets it as a runtime env var
5. SvelteKit reads it as a public env var

### Local Development
- `appsettings.Development.json`: `Recaptcha:SecretKey`
- `apps/web/.env.example`: `PUBLIC_RECAPTCHA_SITE_KEY`
- `Recaptcha:Enabled` defaults to `true`; set to `false` if no keys configured

### Security
- Secret key never reaches the frontend — Key Vault → API container only
- Site key is intentionally public (designed for browser embedding, same as Google Client ID)
- Key Vault references mean secrets are not in container image layers

## 3. Frontend Integration

### LoginScreen.svelte
- Load reCAPTCHA v3 script on mount: `google.com/recaptcha/api.js?render=<siteKey>`
- **At submit time** (not on page load — tokens expire after 2 minutes), execute `grecaptcha.execute(siteKey, { action: 'login' | 'register' })` to obtain a token
- Pass the token as `recaptchaToken` in the request body

### ApiClient
- `login(email, password, recaptchaToken)` — add `recaptchaToken` to request body
- `register(email, password, nickname, recaptchaToken)` — add `recaptchaToken` to request body

### AppState
- `login()` and `register()` methods obtain the reCAPTCHA token before calling ApiClient
- If `PUBLIC_RECAPTCHA_SITE_KEY` is not set, skip token generation (local dev without keys)

### UI
- No visible changes — v3 is invisible
- Hide the default reCAPTCHA badge (CSS: `.grecaptcha-badge { visibility: hidden; }`) and add the required branding text below the form: "This site is protected by reCAPTCHA and the Google Privacy Policy and Terms of Service apply." with links to Google's policies. This is the cleaner approach per Google's FAQ.

### Error Handling
- If reCAPTCHA verification fails (403), show: "Verification failed. Please try again."
- If the reCAPTCHA script fails to load or `grecaptcha.execute()` errors, still attempt the request without a token — the backend's `Enabled` flag or fail-open behavior handles it

## 4. Backend Integration

### RecaptchaSettings (Configuration Model)
```csharp
public class RecaptchaSettings
{
    public string SecretKey { get; set; } = "";
    public double ScoreThreshold { get; set; } = 0.5;
    public bool Enabled { get; set; } = true;
}
```
Note: `SiteKey` is not needed on the API — it's only used by the frontend.
Bound from `Recaptcha` config section in `Program.cs`.

### RecaptchaService
- Registered as a typed HttpClient via `builder.Services.AddHttpClient<RecaptchaService>()` (standard ASP.NET Core pattern, avoids socket exhaustion)
- `VerifyAsync(string token, string expectedAction)` → calls `https://www.google.com/recaptcha/api/siteverify` with secret key and token
- Returns `(bool success, double score, string? errorMessage)`
- Validates: success flag, score ≥ threshold, action matches expected action
- **Fail-closed by default**: if Google's siteverify is unreachable or times out, verification fails (rejects the request). This is the safer default — a temporary Google outage blocks email/password auth but prevents bot bypass. Google Sign-In remains unaffected as a fallback auth method.

### ValidateRecaptchaAttribute (Action Filter)
- Custom `IAsyncActionFilter` registered as a service filter
- Reads `recaptchaToken` from `context.ActionArguments` (runs after model binding, avoids double-reading the request body stream)
- Extracts the `RecaptchaToken` property from the bound request DTO
- Calls `RecaptchaService.VerifyAsync()`
- Returns `400 Bad Request` if token is missing (and `Enabled` is `true`)
- Returns `403 Forbidden` with body `{ "error": "reCAPTCHA verification failed." }` if score is below threshold
- If `RecaptchaSettings.Enabled` is `false`, passes through without verification
- Applied to `Login` and `Register` actions in `AuthController`

### Updated DTOs
- `LoginRequest`: add `string? RecaptchaToken` property (nullable so the DTO is valid when `Enabled=false`)
- `RegisterRequest`: add `string? RecaptchaToken` property (nullable so the DTO is valid when `Enabled=false`)
- Both implement a shared `IRecaptchaRequest` interface with `string? RecaptchaToken` so the filter can extract the token without knowing the concrete type

## 5. Testing

### API Unit Tests
- `RecaptchaServiceTests`: mock HTTP call to Google siteverify, verify score threshold logic, handle timeouts/errors
- `ValidateRecaptchaFilterTests`: reject missing tokens (400), reject low scores (403), pass valid tokens

### Frontend Tests
- Mock `grecaptcha.execute()` in existing login/register tests
- Verify `recaptchaToken` is included in API request bodies

### Integration Tests
- Set `Recaptcha:Enabled = false` in test configuration so reCAPTCHA does not block existing integration test suites

## File Changes Summary

### New Files
- `apps/api/Codec.Api/Services/RecaptchaService.cs`
- `apps/api/Codec.Api/Filters/ValidateRecaptchaAttribute.cs`
- `apps/api/Codec.Api/Models/RecaptchaSettings.cs`
- `apps/api/Codec.Api.Tests/Services/RecaptchaServiceTests.cs`
- `apps/api/Codec.Api.Tests/Filters/ValidateRecaptchaFilterTests.cs`

### Modified Files
- `apps/api/Codec.Api/Program.cs` — bind `RecaptchaSettings`, register `RecaptchaService`
- `apps/api/Codec.Api/Controllers/AuthController.cs` — add `[ValidateRecaptcha]` to Login/Register
- `apps/api/Codec.Api/Models/LoginRequest.cs` — add `RecaptchaToken` property
- `apps/api/Codec.Api/Models/RegisterRequest.cs` — add `RecaptchaToken` property
- `apps/api/Codec.Api/appsettings.json` — add `Recaptcha` config section
- `apps/api/Codec.Api/appsettings.Development.json` — add dev reCAPTCHA config
- `docs/AUTH.md` — document reCAPTCHA verification in auth flow
- `apps/web/src/lib/components/LoginScreen.svelte` — load script, get token before submit
- `apps/web/src/lib/api/client.ts` — add `recaptchaToken` param to login/register
- `apps/web/src/lib/state/app-state.svelte.ts` — get reCAPTCHA token in login/register
- `apps/web/.env.example` — add `PUBLIC_RECAPTCHA_SITE_KEY`
- `apps/web/Dockerfile` — add `PUBLIC_RECAPTCHA_SITE_KEY` build arg
- `infra/main.bicep` — add `recaptchaSecretKey` param, Key Vault secret
- `infra/modules/container-app-api.bicep` — add Key Vault secret ref + env var
- `infra/modules/container-app-web.bicep` — add `PUBLIC_RECAPTCHA_SITE_KEY` env var
- `.github/workflows/infra.yml` — pass `RECAPTCHA_SECRET_KEY` secret to Bicep
- `.github/workflows/cd.yml` — pass `PUBLIC_RECAPTCHA_SITE_KEY` as Docker build arg
- `.github/workflows/ci.yml` — pass placeholder `PUBLIC_RECAPTCHA_SITE_KEY` build arg
