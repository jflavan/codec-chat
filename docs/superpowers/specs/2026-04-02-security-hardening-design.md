# Security Hardening Pass — Design Spec

**Date:** 2026-04-02
**Approach:** Layered priority sweep — fix critical issues first, then systematically harden

## Context

The codebase has strong security fundamentals after recent work (commits #140, #142, #143 and the SAML/OAuth hardening pass). This spec addresses remaining gaps found during an in-depth security review.

---

## Section 1: Critical — SignalR Authorization Gaps

Three ChatHub methods lack authorization checks, allowing authenticated users to eavesdrop on events they shouldn't receive.

### 1a. `JoinServer(serverId)` — No membership check

**File:** `apps/api/Codec.Api/Hubs/ChatHub.cs`, line 93

**Problem:** Any authenticated user can join any server's SignalR group by calling `JoinServer` with a known server GUID. They then receive all real-time events for that server (membership changes, settings updates, role changes).

**Fix:** Validate GUID format, look up the server, verify the caller is a member (or global admin). Follow the same pattern as `JoinChannel()` (line 110).

### 1b. `JoinDmChannel(dmChannelId)` — No membership check

**File:** `apps/api/Codec.Api/Hubs/ChatHub.cs`, line 161

**Problem:** Any authenticated user can join any DM channel's SignalR group. They then receive all DM messages in real-time.

**Fix:** Validate GUID format, verify the caller is a member of the DM channel via `DmChannelMembers` table lookup.

### 1c. `StartTyping`/`StopTyping` — No channel validation

**File:** `apps/api/Codec.Api/Hubs/ChatHub.cs`, lines 145-156

**Problem:** No validation that the caller is actually in the channel. A user can broadcast typing indicators to arbitrary channels.

**Fix:** Validate that the caller's connection is a member of the specified channel group. Since SignalR doesn't expose group membership queries, track joined channels per connection (e.g., `ConcurrentDictionary<string, HashSet<string>>` keyed by connection ID) and check against it.

---

## Section 2: High — SignalR Rate Limiting

### Problem

HTTP endpoints have rate limiting (100 req/min fixed, 10 req/min auth) but SignalR hub methods have none. An authenticated user could flood `StartTyping`, spam `StartCall` for ringing notifications, or churn `JoinChannel`/`LeaveChannel`.

### Fix

Implement a custom `IHubFilter` that tracks per-connection invocation counts using a sliding window. When the limit is exceeded, throw `HubException("Rate limit exceeded")`.

**Suggested limits:**
- Typing methods (`StartTyping`, `StopTyping`, `StartDmTyping`, `StopDmTyping`): 10/second
- Channel join/leave: 20/minute
- Voice calls (`StartCall`, `AcceptCall`): 5/minute
- General hub methods: 60/minute default

---

## Section 3: Medium — CSP and Security Headers

### 3a. Remove `https:` scheme-source from CSP `img-src`

**File:** `apps/web/src/hooks.server.ts`, line 22

**Problem:** `https:` allows any HTTPS domain to serve images — enables tracking pixels and information leakage (user IP, message read timing).

**Fix:** Remove `https:` from `img-src`. Keep the explicit CDN whitelist (Google, GitHub, Azure Blob, Discord, imgur, etc.). User-linked images from Open Graph previews already go through the image proxy service on the API, so only the API origin is needed.

### 3b. Add HSTS header

**File:** `apps/web/src/hooks.server.ts`

**Problem:** No `Strict-Transport-Security` header. Browsers aren't told to enforce HTTPS.

**Fix:** Add `Strict-Transport-Security: max-age=31536000; includeSubDomains` to response headers.

---

## Section 4: Medium — Input Validation Gaps

### 4a. `CreateMessageRequest.Body` missing `[Required]`

**File:** `apps/api/Codec.Api/Models/CreateMessageRequest.cs`, line 6

**Problem:** `Body` has `StringLength(8000)` but no `[Required]` attribute and no `MinimumLength`. A null or empty body could pass validation.

**Fix:** Add `[Required]` and set `MinimumLength = 1` on the `StringLength` attribute.

### 4b. Typing method `displayName` unbounded

**File:** `apps/api/Codec.Api/Hubs/ChatHub.cs`, lines 145-156

**Problem:** `displayName` is a user-supplied string broadcast directly to all other clients with no length constraint.

**Fix:** Truncate `displayName` to a reasonable maximum (e.g., 100 characters) before broadcasting.

### 4c. `JoinServer`/`LeaveServer` missing GUID validation

**File:** `apps/api/Codec.Api/Hubs/ChatHub.cs`, lines 93-105

**Problem:** No `Guid.TryParse` on `serverId`. Malformed strings pass to `Groups.AddToGroupAsync`.

**Fix:** Add `Guid.TryParse` validation with `HubException` on failure (will be part of the authorization check added in Section 1a).

---

## Section 5: Low — Additional Hardening

### 5a. Audit logging for admin-sensitive operations

**Problem:** Audit logging exists for message deletion, channel purging, and pinning. Missing for: role create/update/delete, permission override changes, member ban/unban, server settings updates.

**Fix:** Add `audit.Log()` calls to role CRUD in `RolesController`, permission override changes in `ChannelsController`, ban/unban in `ServersController`, and server settings updates.

### 5b. Expand `Permissions-Policy` header

**File:** `apps/web/src/hooks.server.ts`, line 53

**Problem:** Only restricts `microphone` and `camera`. Other sensitive browser APIs unrestricted.

**Fix:** Expand to: `microphone=(self), camera=(self), geolocation=(), payment=(), usb=(), bluetooth=()`.

---

## Out of Scope

These are acknowledged tradeoffs, not bugs:

- **localStorage refresh tokens** — XSS risk acknowledged in AUTH.md; HttpOnly cookies deferred
- **Password reset** — Deferred feature (no email/password recovery path)
- **Access token revocation** — Short 1-hour TTL mitigates; per-token revocation not needed yet
- **JWT secret hardcoded in dev** — Already guarded: throws in non-dev environments (Program.cs line 118-119)
