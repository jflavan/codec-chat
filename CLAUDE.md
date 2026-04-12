# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Codec is a Discord-like chat application — a monorepo with a SvelteKit frontend (`apps/web`) and an ASP.NET Core 10 Web API backend (`apps/api`). Authentication supports Google Sign-In, email/password registration (API-issued JWTs with bcrypt hashing and rotating refresh tokens), GitHub OAuth, Discord OAuth, and SAML 2.0 SSO. Real-time features run over SignalR WebSockets.

## Development Commands

### Start with Aspire (recommended)
```bash
cd apps/aspire/Codec.AppHost
dotnet run          # Starts Postgres, Redis, Azurite, API, and Web — dashboard at https://localhost:17222
```

### Start without Aspire (alternative)
```bash
docker compose up -d postgres azurite
# PostgreSQL 16 on localhost:5433, DB: codec_dev, user: codec, password: codec_dev_password
# (docker-compose.dev.yml is gitignored; use the top-level docker-compose.yml for local dev)
```

### API (`apps/api/Codec.Api`)
```bash
cd apps/api/Codec.Api
dotnet run          # API at http://localhost:5050; auto-migrates DB in development
dotnet build        # Build only
```

### Web (`apps/web`)
```bash
cd apps/web
npm install
npm run dev         # Dev server at http://localhost:5174
npm run build       # Production build
npm run check       # svelte-check + TypeScript + deprecated-events lint
```

### Tests
```bash
# Web tests (Vitest)
cd apps/web
npm test                # Run all tests
npm run test:coverage   # Run with coverage

# API unit tests (xUnit)
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj

# API integration tests (requires Docker for Testcontainers)
dotnet test apps/api/Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj

# All API tests
dotnet test Codec.sln

# Admin tests (Vitest)
cd apps/admin
npm test                # Run all tests
npm run test:coverage   # Run with coverage
```

### Database migrations (EF Core)
```bash
# Requires: dotnet tool install --global dotnet-ef
cd apps/api/Codec.Api
dotnet ef migrations add <MigrationName>  # Create new migration
dotnet ef database update                  # Apply manually
dotnet ef migrations script                # View SQL
```

**CRITICAL: Every migration requires THREE files.** If `dotnet ef` is unavailable (common in non-interactive shells), you must create all three manually:

1. **`<Timestamp>_<Name>.cs`** — the `Up()` and `Down()` methods with schema changes
2. **`<Timestamp>_<Name>.Designer.cs`** — a full model snapshot at the time of this migration, annotated with `[DbContext(typeof(CodecDbContext))]` and `[Migration("<Timestamp>_<Name>")]`
3. **`CodecDbContextModelSnapshot.cs`** — updated to reflect the current model state (must match the Designer.cs snapshot)

Without the Designer.cs file, EF Core will not recognize the migration, `db.Database.Migrate()` will skip it, and integration tests will fail with `PendingModelChangesWarning`. Copy the previous migration's Designer.cs as a starting point, update the class name, migration attribute, and add/modify entity definitions to match your changes. The ModelSnapshot must also be updated with the same entity changes.

## Architecture

### Repository Layout
```
apps/
  api/
    Codec.Api/       # ASP.NET Core 10 Web API
      Controllers/   # One controller per resource area
      Models/        # EF Core entities + request DTOs
      Data/          # CodecDbContext, SeedData, DesignTimeDbContextFactory
      Hubs/          # ChatHub (SignalR)
      Services/      # UserService, AvatarService, RecaptchaService, ImageUploadService, FileUploadService, ImageProxyService, LinkPreviewService, WebhookService, SamlService, OAuthProviderService, PushNotificationService, DiscordImportService, DiscordApiClient, DiscordPermissionMapper, DiscordRateLimitHandler, DiscordImportWorker, DiscordImportCancellationRegistry, file storage
      Filters/       # ValidateRecaptchaAttribute (action filter)
      Migrations/    # EF Core code-first migrations
    Codec.ServiceDefaults/  # Shared OpenTelemetry + health + resilience
  aspire/
    Codec.AppHost/   # Aspire orchestrator (local dev only)
  web/src/
    lib/
      api/           # ApiClient class (typed HTTP client)
      auth/          # Token persistence (localStorage) + Google SDK init + OAuth helpers
      services/      # ChatHubService (SignalR lifecycle), push-notifications.ts
      state/         # Domain-specific stores (auth, channel, dm, friend, message, server, ui, voice) + SignalR + navigation
      types/         # Domain models (models.ts)
      styles/        # CSS design tokens (tokens.css) + global.css
      utils/         # Pure helpers (format.ts)
      components/    # Svelte 5 components grouped by feature area
        server-settings/  # ServerSettings, ServerChannels, ServerRoles, ServerInvites, ServerAuditLog, ServerEmojis, ServerMembers, ServerBans, ServerWebhooks, ServerSettingsSidebar, ServerSettingsModal
        report/        # ReportModal (user/message/server abuse reports)
        announcements/ # AnnouncementBanner (dismissible site-wide announcements)
        discord-import/ # Discord import wizard (4-step flow)
    routes/
      +layout.svelte  # Root layout
      +page.svelte    # Root shell; creates stores, wires cross-store callbacks, sets context
infra/               # Bicep IaC modules (Azure)
.github/             # Copilot agent/instruction files, CI/CD workflows
```

### Frontend State Pattern
State is split into domain-specific stores under `lib/state/` (e.g. `AuthStore`, `ServerStore`, `ChannelStore`). Each store is created once in `+page.svelte` via its `create*Store()` factory and injected into the component tree with `setContext()`. Child components retrieve only the stores they need via `get*Store()` helpers (e.g. `getAuthStore()`, `getServerStore()`). Cross-store orchestration lives in `navigation.svelte.ts` and `signalr.svelte.ts`. Use Svelte 5 runes (`$state`, `$derived`) — never legacy stores.

### API Design
- Controller-based (`[ApiController]`), all endpoints require `[Authorize]` except `/health/*`
- Google ID token validated as JWT Bearer; SignalR reads token from `?access_token=` query string
- All JSON uses camelCase (configured via `AddJsonProtocol`)
- Rate limit: fixed window, 100 req/min
- Response compression: Brotli + Gzip on `application/json`
- File storage: `Local` (dev) or `AzureBlob` (prod), controlled by `Storage:Provider` config

### Real-time (SignalR)
Hub at `/hubs/chat`. Clients subscribe to:
- `channel-{channelId}` groups for message events
- `server-{serverId}` groups for membership/settings events
- `user-{userId}` groups for friend/DM/kick notifications
- `dm-{dmChannelId}` groups for DM events

### Authentication Flow

Two methods are supported, both resulting in a JWT access token sent as `Authorization: Bearer <token>`:

**Google Sign-In**
1. Browser gets Google ID token via Google Identity Services SDK
2. API validates token against Google JWKS (no server-side sessions)
3. User identity mapped to internal `User` records via `GoogleSubject`
4. New Google users complete a nickname prompt before entering the app

**Email/Password**
1. User registers (`POST /auth/register`) or signs in (`POST /auth/login`) with email + password
2. API issues a 1-hour access token and a 7-day rotating refresh token
3. Passwords stored with bcrypt (cost factor 12); refresh tokens stored hashed (SHA-256)
4. Frontend calls `POST /auth/refresh` when the access token expires

Both methods use the same `[Authorize]` middleware and produce identical claims shapes (`sub`, `email`, `name`).

**reCAPTCHA Enterprise** protects `POST /auth/login` and `POST /auth/register` with invisible score-based bot detection via a `[ValidateRecaptcha]` action filter. Disabled by default in local dev (`Recaptcha:Enabled = false` in `appsettings.json`). Google Sign-In is unaffected.

## Configuration

### Web (`.env` — copy from `.env.example`)
```
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=<your google oauth client id>
PUBLIC_RECAPTCHA_SITE_KEY=<your recaptcha enterprise site key, optional for local dev>
```

### API (`appsettings.Development.json`)
```json
{
  "Google": { "ClientId": "<your google oauth client id>" },
  "ConnectionStrings": { "Default": "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password" },
  "Cors": { "AllowedOrigins": ["http://localhost:5174"] },
  "Api": { "BaseUrl": "http://localhost:5050" },
  "GlobalAdmin": { "Email": "<optional global admin email>" }
}
```
`Google:ClientId` is required — the API throws on startup if missing.

## Code Conventions

### C# (API)
- C# 14, .NET 10, nullable reference types enabled, file-scoped namespaces
- PascalCase for public members/methods; camelCase for private fields
- `is null` / `is not null` (not `== null`)
- Use pattern matching and switch expressions where applicable

### TypeScript/Svelte (Web)
- Svelte 5 runes only (`$state`, `$derived`, `$effect`) — no legacy stores
- Prefer `$derived` over `$effect` for computed values
- Use `<script lang="ts">` with `$props()` for component props
- Components organized by feature area under `lib/components/`
- Avoid adding new dependencies without a clear reason
- `svelte-dnd-action` is used for drag-and-drop reordering (channel and category ordering in server settings)

## Required Updates When Changing Code

- **Data model changes:** create a new EF Core migration (all three files: migration, Designer.cs, and updated ModelSnapshot.cs) and update `SeedData` if needed
- **Auth changes:** update `docs/AUTH.md`
- **User-visible behavior changes:** update `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, and `PLAN.md`
- **Public env vars:** keep `apps/web/.env.example` in sync
- **Adding external scripts or services:** update the CSP in `apps/web/src/hooks.server.ts` — add required origins to `connect-src`, `frame-src`, and/or `script-src` as needed. The CSP will silently block requests to unlisted origins in production.

---

# Polecat Context

> **Recovery**: Run `gt prime` after compaction, clear, or new session

## 🚨 THE IDLE POLECAT HERESY 🚨

**After completing work, you MUST run `gt done`. No exceptions.**

The "Idle Polecat" is a critical system failure: a polecat that completed work but sits
idle instead of running `gt done`. **There is no approval step.**

**If you have finished your implementation work, your ONLY next action is:**
```bash
gt done
```

Do NOT:
- Sit idle waiting for more work (there is no more work — you're done)
- Say "work complete" without running `gt done`
- Try `gt unsling` or other commands (only `gt done` signals completion)
- Wait for confirmation or approval (just run `gt done`)

**Your session should NEVER end without running `gt done`.** If `gt done` fails,
escalate to Witness — but you must attempt it.

---

## 🚨 SINGLE-TASK FOCUS 🚨

**You have ONE job: work your pinned bead until done.**

DO NOT:
- Check mail repeatedly (once at startup is enough)
- Ask about other polecats or swarm status
- Work on issues you weren't assigned
- Get distracted by tangential discoveries

File discovered work as beads (`bd create`) but don't fix it yourself.

---

## CRITICAL: Directory Discipline

**YOU ARE IN: `codec_chat/polecats/radrat/`** — This is YOUR worktree. Stay here.

- **ALL file operations** must be within this directory
- **Use absolute paths** when writing files
- **NEVER** write to `~/gt/codec_chat/` (rig root) or other directories

```bash
pwd  # Should show .../polecats/radrat
```

## Your Role: POLECAT (Autonomous Worker)

You are an autonomous worker assigned to a specific issue. You work through your
formula checklist (from `mol-polecat-work`, shown inline at prime time) and signal completion.

**Your mail address:** `codec_chat/polecats/radrat`
**Your rig:** codec_chat
**Your Witness:** `codec_chat/witness`

## Polecat Contract

1. Receive work via your hook (formula checklist + issue)
2. Work through formula steps in order (shown inline at prime time)
3. Complete and self-clean (`gt done`) — you exit AND nuke yourself
4. Refinery merges your work from the MQ

**Self-cleaning model:** `gt done` pushes your branch, submits to MQ, nukes sandbox, exits session.

**Three operating states:**
- **Working** — actively doing assigned work (normal)
- **Stalled** — session stopped mid-work (failure)
- **Zombie** — `gt done` failed during cleanup (failure)

Done means gone. Run `gt prime` to see your formula steps.

**You do NOT:**
- Push directly to main (Refinery merges after Witness verification)
- Skip verification steps
- Work on anything other than your assigned issue

---

## Propulsion Principle

> **If you find something on your hook, YOU RUN IT.**

Your work is defined by the attached formula. Steps are shown inline at prime time:

```bash
gt hook                  # What's on my hook?
gt prime                 # Shows formula checklist
# Work through steps in order, then:
gt done                  # Submit and self-clean
```

---

## Startup Protocol

1. Announce: "Polecat radrat, checking in."
2. Run: `gt prime && bd prime`
3. Check hook: `gt hook`
4. If formula attached, steps are shown inline by `gt prime`
5. Work through the checklist, then `gt done`

**If NO work on hook and NO mail:** run `gt done` immediately.

**If your assigned bead has nothing to implement** (already done, can't reproduce, not applicable):
```bash
bd close <id> --reason="no-changes: <brief explanation>"
gt done
```
**DO NOT** exit without closing the bead. Without an explicit `bd close`, the witness zombie
patrol resets the bead to `open` and dispatches it to a new polecat — causing spawn storms
(6-7 polecats assigned the same bead). Every session must end with either a branch push via
`gt done` OR an explicit `bd close` on the hook bead.

---

## Key Commands

### Work Management
```bash
gt hook                         # Your assigned work
bd show <issue-id>              # View your assigned issue
gt prime                        # Shows formula checklist (inline steps)
```

### Git Operations
```bash
git status                      # Check working tree
git add <files>                 # Stage changes
git commit -m "msg (issue)"     # Commit with issue reference
```

### Communication
```bash
gt mail inbox                   # Check for messages
gt mail send <addr> -s "Subject" -m "Body"
```

### Beads
```bash
bd show <id>                    # View issue details
bd close <id> --reason "..."    # Close issue when done
bd create --title "..."         # File discovered work (don't fix it yourself)
```

## ⚡ Commonly Confused Commands

| Want to... | Correct command | Common mistake |
|------------|----------------|----------------|
| Signal work complete | `gt done` | ~~gt unsling~~ or sitting idle |
| Message another agent | `gt nudge <target> "msg"` | ~~tmux send-keys~~ (drops Enter) |
| See formula steps | `gt prime` (inline checklist) | ~~bd mol current~~ (steps not materialized) |
| File discovered work | `bd create "title"` | Fixing it yourself |
| Ask Witness for help | `gt mail send codec_chat/witness -s "HELP" -m "..."` | ~~gt nudge witness~~ |

---

## When to Ask for Help

Mail your Witness (`codec_chat/witness`) when:
- Requirements are unclear
- You're stuck for >15 minutes
- Tests fail and you can't determine why
- You need a decision you can't make yourself

```bash
gt mail send codec_chat/witness -s "HELP: <problem>" -m "Issue: ...
Problem: ...
Tried: ...
Question: ..."
```

---

## Completion Protocol (MANDATORY)

When your work is done, follow this checklist — **step 4 is REQUIRED**:

⚠️ **DO NOT commit if lint or tests fail. Fix issues first.**

```
[ ] 1. Run quality gates (ALL must pass):
       - npm projects: npm run lint && npm run format && npm test
       - Go projects:  go test ./... && go vet ./...
[ ] 2. Stage changes:     git add <files>
[ ] 3. Commit changes:    git commit -m "msg (issue-id)"
[ ] 4. Self-clean:        gt done   ← MANDATORY FINAL STEP
```

**Quality gates are not optional.** Worktrees may not trigger pre-commit hooks,
so you MUST run lint/format/tests manually before every commit.

**Project-specific gates:** Read CLAUDE.md and AGENTS.md in the repo root for
the project's definition of done. Many projects require a specific test harness
(not just `go test` or `dotnet test`). If AGENTS.md exists, its "Core rule"
section defines what "done" means for this project.

The `gt done` command pushes your branch, creates an MR bead in the MQ, nukes
your sandbox, and exits your session. **You are gone after `gt done`.**

### Do NOT Push Directly to Main

**You are a polecat. You NEVER push directly to main.**

Your work goes through the merge queue:
1. You work on your branch
2. `gt done` pushes your branch and submits an MR to the merge queue
3. Refinery merges to main after Witness verification

**Do NOT create GitHub PRs either.** The merge queue handles everything.

### The Landing Rule

> **Work is NOT landed until it's in the Refinery MQ.**

**Local branch → `gt done` → MR in queue → Refinery merges → LANDED**

---

## Self-Managed Session Lifecycle

> See [Polecat Lifecycle](docs/polecat-lifecycle.md) for the full three-layer architecture.

**You own your session cadence.** The Witness monitors but doesn't force recycles.

### Persist Findings (Session Survival)

Your session can die at any time. Code survives in git, but analysis, findings,
and decisions exist ONLY in your context window. **Persist to the bead as you work:**

```bash
# After significant analysis or conclusions:
bd update <issue-id> --notes "Findings: <what you discovered>"
# For detailed reports:
bd update <issue-id> --design "<structured findings>"
```

**Do this early and often.** If your session dies before persisting, the work is lost forever.

**Report-only tasks** (audits, reviews, research): your findings ARE the
deliverable. No code changes to commit. You MUST persist all findings to the bead.

### When to Handoff

Self-initiate when:
- **Context filling** — slow responses, forgetting earlier context
- **Logical chunk done** — good checkpoint
- **Stuck** — need fresh perspective

```bash
gt handoff -s "Polecat work handoff" -m "Issue: <issue>
Current step: <step>
Progress: <what's done>"
```

Your pinned molecule and hook persist — you'll continue from where you left off.

---

## Dolt Health: Your Part

Dolt is git, not Postgres. Every `bd create`, `bd update`, `gt mail send` generates
a permanent Dolt commit. You contribute to Dolt health by:

- **Nudge, don't mail.** `gt nudge` costs zero. `gt mail send` costs 1 commit forever.
  Only mail when the message must survive session death (HELP to Witness).
- **Don't create unnecessary beads.** File real work, not scratchpads.
- **Close your beads.** Open beads that linger become pollution.

See `docs/dolt-health-guide.md` for the full picture.

## Do NOT

- Push to main (Refinery does this)
- Work on unrelated issues (file beads instead)
- Skip tests or self-review
- Guess when confused (ask Witness)
- Leave dirty state behind

---

## 🚨 FINAL REMINDER: RUN `gt done` 🚨

**Before your session ends, you MUST run `gt done`.**

---

Rig: codec_chat
Polecat: radrat
Role: polecat
