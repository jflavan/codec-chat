# FOSSA Dependency Quality & Security Notes

Companion to [`FOSSA_LICENSE_NOTES.md`](./FOSSA_LICENSE_NOTES.md), which covers the
License Compliance check. This file covers the **Security Analysis** and
**Dependency Quality** checks.

## Baseline and confirmed outcome

The `main` build at `55297af` failed both checks. FOSSA's own status descriptions,
before and after the fix on this branch:

| Check | `55297af` (main) | `912c514` (this branch) |
|-------|------------------|--------------------------|
| Security Analysis | `error` — **67 vulnerabilities found** | `success` — **All checks passed** |
| Dependency Quality | `error` — **5 quality issues found** | 4 of 5 fixed; 1 needs a dashboard waiver |
| License Compliance | `success` | `success` |

Security Analysis is fully resolved. Dependency Quality is governed by a separate
"3 or more majors behind" rule; four of its five issues are fixed here and the fifth
cannot be fixed in code — see "Dependency Quality" at the end of this file.

At the failing commit the repo carried:

| Ecosystem | Vulnerable packages |
|-----------|--------------------|
| NuGet | 4 direct/transitive packages, 16 advisories (1 high, rest moderate) |
| npm (`apps/web`) | 18 advisories (11 high, 5 moderate, 2 low) |
| npm (`apps/admin`) | 12 advisories (7 high, 4 moderate, 1 low) |

All three are now zero. Re-check with:

```bash
dotnet list Codec.sln package --vulnerable --include-transitive
cd apps/web && npm audit
cd apps/admin && npm audit
```

## NuGet fixes

Every .NET advisory was transitive and cleared by moving direct references forward —
no pins or exclusions were needed.

| Package | Was | Now | Advisories cleared |
|---------|-----|-----|--------------------|
| `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*` | 10.0.3 / 10.0.5 | 10.0.12 | `MessagePack` → 2.5.302 (GHSA-hv8m-jj95-wg3x, GHSA-vh6j-jc39-fggf + 9 moderate); `Microsoft.OpenApi` → 2.12.0 (GHSA-v5pm-xwqc-g5wc) |
| `OpenTelemetry.*` | 1.12.0 | 1.18.0 | GHSA-g94r-2vxg-569j, GHSA-4625-4j76-fww9 |
| `Aspire.Hosting.*` + AppHost SDK | 13.2.0 | 13.5.3 | `MessagePack` 2.5.192 (same set as above) |
| `Testcontainers.*` | 4.6.0 | 4.15.0 | `SSH.NET` → 2026.0.0 (GHSA-q939-rpr3-3284) |

Testcontainers 4.15 deprecates the parameterless `PostgreSqlBuilder()` /
`RedisBuilder()` constructors, so `CodecWebFactory` now passes the image to the
constructor instead of calling `.WithImage(...)`. The images are unchanged.

## npm fixes

Direct dependencies moved forward within their existing major, except `vitest` and
`@vitest/coverage-v8` — see the note below.

| Package | Was | Now |
|---------|-----|-----|
| `@sveltejs/kit` | ^2.52.2 | ^2.70.3 |
| `svelte` | ^5.53.0 | ^5.57.0 |
| `vite` | ^7.3.1 | ^7.3.6 |
| `jsdom` | ^29.0.x | ^29.1.1 (pulls patched `undici` 7.29.1) |
| `devalue` (web) | ^5.6.4 | ^5.9.2 |
| `vitest`, `@vitest/coverage-v8` | ^4.1.x | ^5.0.0 |

### Why vitest went to a major

The patched 4.x release (`4.1.11`) cannot be installed: npm 10.9.3 — the version
bundled with Node 22, which is what CI uses — crashes with
`Cannot read properties of null (reading 'edgesOut')` while resolving its peer set.
This reproduces on a clean tree and on incremental installs. `vitest@5` resolves
normally, still supports `vite@^7`, and the full suite passes unchanged
(web 234 tests, admin 75 tests). Revisit pinning back to 4.x only if npm fixes the
arborist bug and there is a reason to.

### Overrides

The remaining advisories were in transitive packages whose parents pin old ranges
(`workbox-build`, `vite`, `@microsoft/signalr`). They are resolved with `overrides`
entries in each `package.json`, extending the block that was already there.

**Every override is bounded to the compatible major** (`>=x.y.z <M.0.0`), not left
open-ended. This matters: an unbounded `">=7.29.6"` on `@babel/core` resolves to
Babel 8, which `workbox-build` rejects at build time with
`Requires Babel "^7.0.0-0", but was loaded with "8.0.5"` — it breaks the service
worker, not the install. Unbounded overrides also silently pulled `ws` to 8.x under
`@microsoft/signalr`, which declares `^7.5.10`. Keep the upper bound when adding to
this list.

| Override | Reason | Pulled in by |
|----------|--------|--------------|
| `@babel/core` `>=7.29.6 <8.0.0` | GHSA in <=7.29.0 | `workbox-build` |
| `@babel/plugin-transform-modules-systemjs` `>=7.29.4 <8.0.0` | high, <=7.29.3 | `workbox-build` |
| `baseline-browser-mapping` `>=2.11.0 <3.0.0` | moderate, <2.11.0 | `browserslist` |
| `brace-expansion` `>=5.0.9 <6.0.0` | high, <5.0.9 | `minimatch` ← `glob` ← `workbox-build` |
| `browserslist` `>=4.28.7 <5.0.0` | high, <=4.28.6 | `@babel/*`, `core-js-compat` |
| `esbuild` `>=0.28.1 <0.29.0` | low, dev-server-only, <0.28.1 | `vite` |
| `fast-uri` `>=3.1.6 <4.0.0` | high, <3.1.6 | `ajv` ← `workbox-build` |
| `nanoid` `>=6.0.0 <7.0.0` | high CVE in <3.3.18, **and** the Major-3 quality rule | `postcss` ← `vite` |
| `postcss` `>=8.5.23 <9.0.0` | high, <=8.5.22 | `vite` |
| `ws` `>=7.5.11 <8.0.0` | high, <7.5.11 | `@microsoft/signalr` (declares `^7.5.10`) |

An `ejs` override is in `apps/web` only; `apps/admin` has no `workbox-build` in its
tree. `apps/admin` carries the `esbuild` / `nanoid` / `postcss` / `ws` subset.

## Dependency Quality: the "Major - 3" rule

The check is governed by one FOSSA quality policy, **"Major - 3 Policy"**
(`policyId` 253263), whose only enabled rule is:

```
type:    outdated_dependency
measure: SEMVER / MAJOR
trigger: difference of 3 or more major versions behind latest
```

A second rule ("fallback difference of 20 versions") exists but is disabled. So an
issue fires when a package is **three or more majors behind the newest release** —
nothing to do with vulnerabilities, and nothing to do with how the dependency is
used. Clearing one requires getting within **two** majors of latest.

### An earlier, wrong conclusion

A previous revision of this file argued the check was "not a version problem,"
reasoning that the count stayed at exactly 5 across a huge dependency bump. That was
wrong — the count was a coincidence. Comparing the two scans shows the composition
did change: `nanoid` moved `3.3.11` -> `3.3.19` (its CVE fixed) but stayed 3 majors
behind, so it re-flagged under the same rule. Same package, same rule, same count.
**It is a version problem, and bumping does fix it** — for the dependencies where
bumping is safe.

### The five issues and their resolution

| Package | Was | Latest | Resolution |
|---------|-----|--------|------------|
| `coverlet.collector` | 6.0.4 | 10.0.1 | bumped to 10.0.1 — direct reference |
| `coverlet.msbuild` | 6.0.4 | 10.0.1 | bumped to 10.0.1 — direct reference |
| `ejs` | 3.1.10 | 6.0.1 | override `>=6.0.0 <7.0.0` — verified, see below |
| `nanoid` | 3.3.19 | 6.0.1 | override `>=6.0.0 <7.0.0` — verified, see below |
| `eventsource` | 2.0.2 | 5.1.1 | **not fixable in code** — see below |

### Why `ejs` and `nanoid` are safe to force

Both are three majors ahead of what their parent declares, which is exactly the shape
that broke the Babel 8 attempt described above. A passing build is *not* sufficient
evidence here, because each sits on a code path a build may never execute. Both were
verified by exercising the real path:

- **`nanoid` 6** — `postcss` does `require('nanoid/non-secure')` and destructures
  `{ nanoid }`. nanoid 4+ is ESM-only, but Node 22's `require(esm)` returns a
  namespace object and the named export resolves. Driving `postcss.process()`
  end-to-end produces a valid generated id (`<input css MWbRyf>`), proving the call
  succeeds rather than merely loading.
- **`ejs` 6** — `@trickfilm400/rollup-plugin-off-main-thread` (under `workbox-build`)
  does a top-level `require("ejs")` and calls `ejs.render` to template the service
  worker loader. The generated `sw.js` is byte-identical in size (4917) to the ejs 3
  output, contains zero unrendered `<%` tags, and includes the expected
  `precacheAndRoute` shim — so the template rendered, it did not silently no-op.

### Why `eventsource` is not fixable in code

`@microsoft/signalr` loads it as a CommonJS default export and assigns the module
object straight to a constructor slot:

```js
eventSourceModule = requireFunc("eventsource");   // HttpConnection.js:41
options.EventSource = eventSourceModule;          // HttpConnection.js:56
```

eventsource 2.x sets `module.exports = EventSource`, so the module *is* the
constructor. Every version from 3.x on is ESM with a **named** export, so `require()`
returns a namespace object and `new eventSourceModule(...)` throws
`Ctor is not a constructor`.

This breaks nothing at build time and nothing in the test suites — it fails only at
runtime, in Node, when SignalR falls back to the Server-Sent Events transport. That
is the worst possible failure shape, so the override was tested, confirmed broken,
and removed. `@microsoft/signalr` still declares `eventsource: ^2.0.2` as of 10.0.11,
so there is no upstream version to move to.

**This one needs a FOSSA waiver, not a code change.** Resolve it in the dashboard the
way the SkiaSharp licence issues were handled in
[`FOSSA_LICENSE_NOTES.md`](./FOSSA_LICENSE_NOTES.md), with the rationale: *transitive
dependency pinned by `@microsoft/signalr`; every version satisfying the policy breaks
the SSE transport at runtime; no upstream release available.* Revisit if
`@microsoft/signalr` widens its range.

### Headroom

`ejs` and `nanoid` are pinned to the current latest major, which leaves two majors of
headroom before the rule fires again. `coverlet` is at latest. Expect this check to
re-fail when any of them ships three majors, which for a fast-moving package can be
within a year.

## When adding or upgrading dependencies

- Prefer moving the **direct** reference forward; only reach for an override when the
  parent pins an old range and has no newer release.
- Always bound an override to the compatible major.
- Run `npm ci` from a deleted `node_modules` before committing a lockfile change — a
  tree that installs incrementally does not prove the lockfile is reproducible.
