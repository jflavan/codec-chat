# Auth Security Hardening

Addresses security issues identified in code review of the email/password auth feature (commit 188cde4).

## 1. RefreshToken Concurrency Token

**Problem:** `RotateRefreshTokenAsync` catches `DbUpdateConcurrencyException` to prevent double-use of a refresh token, but `RefreshToken` has no concurrency token. EF Core never throws the exception — two concurrent refresh requests can both succeed.

**Fix:** Add `[Timestamp]` property to `RefreshToken`. PostgreSQL uses the `xmin` system column for optimistic concurrency — EF Core + Npgsql handle this via `UseXminAsConcurrencyToken()` in fluent config. No new migration column needed.

## 2. Logout Endpoint

**Problem:** No server-side token revocation on sign-out. Stolen refresh tokens remain valid for 7 days.

**Fix:**
- Add `POST /auth/logout` accepting `{ refreshToken }`. Unauthenticated (the token is the credential). Rate-limited under the "auth" policy.
- Revokes the matching token. Returns 204 regardless of whether the token was found (no information leakage).
- Frontend `signOut()` calls this endpoint before clearing localStorage.

## 3. Refresh Token Cleanup Job

**Problem:** Revoked and expired refresh tokens accumulate indefinitely.

**Fix:** Add `RefreshTokenCleanupService : BackgroundService`. Runs every 6 hours. Deletes tokens where `RevokedAt` is set and older than 24 hours, or `ExpiresAt` is past. Uses `ExecuteDeleteAsync` for bulk efficiency.

## 4. Account Lockout

**Problem:** No per-account brute-force protection beyond IP-based rate limiting.

**Fix:**
- Add `FailedLoginAttempts` (int, default 0) and `LockoutEnd` (DateTimeOffset?, nullable) to `User`.
- On failed login: increment `FailedLoginAttempts`. If >= 5, set `LockoutEnd` to 15 minutes from now.
- On login attempt when locked: return 401 with "Account temporarily locked. Try again later." (same HTTP status as wrong password — no enumeration).
- On successful login: reset `FailedLoginAttempts` to 0 and `LockoutEnd` to null.
- Migration adds the two columns.

## 5. Google Link Preserves User Profile

**Problem:** `link-google` endpoint overwrites `DisplayName` and `AvatarUrl` unconditionally, discarding user-chosen values.

**Fix:** Only update `DisplayName` if the user's current name matches the default (empty or "Unknown"). Only update `AvatarUrl` if the user has no custom avatar path set. This preserves intentional user choices.

## 6. Accepted Tradeoffs (Document Only)

**Registration enumeration:** The `409 Conflict` on duplicate email reveals whether an email is registered. Without email verification, we can't return a generic "check your email" response. Documented as accepted risk.

**Refresh token in localStorage:** HttpOnly cookies require API-side cookie management, CORS/SameSite changes, and CSRF protection rework. Documented as future improvement.

**No email verification:** Prevents email squatting and enables "check your email" registration flow. Documented as future work.

## 7. Testing

- **TokenService**: Test that concurrent rotation of the same token fails (second call returns null).
- **AuthController**: Test logout endpoint (204 on valid token, 204 on invalid token). Test account lockout (locked after 5 failures, login rejected while locked, successful login resets count).
- **RefreshTokenCleanupService**: Test that expired/revoked tokens are deleted, active tokens are preserved.
- **Google link profile preservation**: Test that existing DisplayName/AvatarUrl are not overwritten.

## 8. Documentation

Update `docs/AUTH.md` with:
- Logout endpoint
- Account lockout behavior
- Accepted tradeoffs section (enumeration, localStorage, no email verification)
