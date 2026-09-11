# FOSSA Dependency Quality & Security Notes

Companion to [`FOSSA_LICENSE_NOTES.md`](./FOSSA_LICENSE_NOTES.md), which covers the
License Compliance check. This file covers the **Security Analysis** and
**Dependency Quality** checks.

## Baseline

The `main` build at `55297af` failed both checks. At that commit the repo carried:

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
| `nanoid` `>=3.3.18 <4.0.0` | high, <3.3.18 | `postcss` ← `vite` |
| `postcss` `>=8.5.23 <9.0.0` | high, <=8.5.22 | `vite` |
| `ws` `>=7.5.11 <8.0.0` | high, <7.5.11 | `@microsoft/signalr` (declares `^7.5.10`) |

`apps/admin` needs only the `esbuild` / `nanoid` / `postcss` / `ws` subset — it has
no `workbox-build` in its tree.

## Known remaining: deprecated transitive packages

`apps/web` still installs three packages npm marks deprecated. All three come from
`workbox-build@7.4.1` (the newest release), reached via
`@vite-pwa/sveltekit@1.1.0` → `vite-plugin-pwa@1.3.0` — both already at latest:

- `glob@11.1.0` — `workbox-build` declares `glob: ^11.0.1`
- `source-map@0.8.0-beta.0` — `workbox-build` declares `source-map: ^0.8.0-beta.0`
- `sourcemap-codec@1.4.8` — via `magic-string@0.25.9`

None carries a security advisory. They are **deliberately not overridden**: forcing
`glob` to 12.x/13.x or `source-map` to stable 0.8.0 is untested against
`workbox-build`'s usage, and a silent break here produces a bad service worker rather
than a build error. The fix belongs upstream in `workbox-build`. Re-evaluate when
`workbox-build` 8.x ships.

## When adding or upgrading dependencies

- Prefer moving the **direct** reference forward; only reach for an override when the
  parent pins an old range and has no newer release.
- Always bound an override to the compatible major.
- Run `npm ci` from a deleted `node_modules` before committing a lockfile change — a
  tree that installs incrementally does not prove the lockfile is reproducible.
