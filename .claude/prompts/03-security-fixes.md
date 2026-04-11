Fix confirmed security vulnerabilities in the Codec Chat codebase. Work through each fix carefully.

## Fix 1: JWT Secret Fallback (CRITICAL)
**File:** `apps/api/Codec.Api/Program.cs`
**Issue:** Hardcoded dev JWT secret could be used in production if config missing.
**Fix:** In non-development environments, throw if `Jwt:Secret` is not configured:
```csharp
if (builder.Environment.IsDevelopment())
    jwtSecret = "dev-only-jwt-secret-that-is-at-least-32-chars-long!!";
else
    throw new InvalidOperationException("Jwt:Secret must be configured in production");
```

## Fix 2: XSS in Search Results (HIGH)
**File:** `apps/web/src/lib/components/search/SearchResultItem.svelte`
**Issue:** `{@html highlightMatches(...)}` renders user content with HTML.
**Fix:** Verify the `highlightMatches` function properly escapes ALL user content before wrapping matches in `<mark>` tags. Also verify `result.authorName` and `result.channelName` are rendered as plain text `{result.authorName}` not through `{@html}`.

## Fix 3: Missing Rate Limits (MEDIUM)
**File:** `apps/api/Codec.Api/Controllers/DmController.cs`
**Issue:** DM channel creation has no rate limiting.
**Fix:** Add `[EnableRateLimiting("sliding")]` to `CreateOrResumeChannel` endpoint.

**File:** `apps/api/Codec.Api/Controllers/UsersController.cs`
**Issue:** `SearchUsers` has no rate limiting.
**Fix:** Add `[EnableRateLimiting("sliding")]` to the search endpoint.

## Fix 4: Input Validation (MEDIUM)
**File:** `apps/api/Codec.Api/Controllers/RolesController.cs`
**Issue:** Role names allow empty strings after trim.
**Fix:** Add validation: reject empty names, names with only whitespace, and names containing null bytes or RTL override chars.

**File:** `apps/api/Codec.Api/Controllers/UsersController.cs`
**Issue:** Search query has no max length.
**Fix:** Add `if (q.Length > 100) return BadRequest("Query too long")` to SearchUsers.

## Fix 5: OAuth Error Info Leak (MEDIUM)
**File:** `apps/api/Codec.Api/Controllers/AuthController.cs`
**Issue:** Error reveals which OAuth provider is linked to an email.
**Fix:** Return generic message: "An account with this email already exists. Please sign in with your existing account."

## Quality Gate
After all fixes:
```bash
cd apps/api/Codec.Api && dotnet build
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
cd apps/web && npm run check && npm test
```
All must pass. Commit each fix separately with a descriptive message.
