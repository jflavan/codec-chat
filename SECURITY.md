# Security Policy

## Supported Versions

Codec is currently in **alpha**. Security fixes are applied to the latest version on the `main` branch only.

| Version | Supported          |
| ------- | ------------------ |
| main    | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability in Codec, please report it responsibly using **GitHub's private vulnerability reporting** feature:

1. Go to the [Security Advisories](https://github.com/jflavan/codec-chat/security/advisories) page for this repository.
2. Click **"Report a vulnerability"**.
3. Provide a clear description of the issue, steps to reproduce, and any potential impact.

**Please do not open a public issue for security vulnerabilities.**

We will acknowledge receipt within 48 hours and aim to provide a fix or mitigation plan within 7 days for critical issues.

## Scope

The following are in scope for security reports:

- Authentication and authorization bypasses
- Injection vulnerabilities (SQL, XSS, command injection)
- Server-side request forgery (SSRF)
- Sensitive data exposure
- Insecure direct object references

## Out of Scope

- Vulnerabilities in third-party dependencies (please report those to the respective maintainers)
- Issues that require physical access to the server
- Social engineering attacks

## Dependency Management

### Accepted Outdated Transitive Dependencies

The following transitive npm dependencies are flagged as outdated by FOSSA but are **dev/build-time only** — none ship to production or affect the runtime application. They are locked by upstream packages that are already at their latest versions, so they cannot be updated without upstream changes.

| Package | Version | Latest | Parent Chain | Category |
|---------|---------|--------|-------------|----------|
| `crypto-random-string` | 2.0.0 | 5.0.0 | `@vite-pwa/sveltekit` → `vite-plugin-pwa` → `workbox-build` → `tempy` → `unique-string` | PWA tooling |
| `tempy` | 0.6.0 | 3.2.0 | `@vite-pwa/sveltekit` → `vite-plugin-pwa` → `workbox-build` | PWA tooling |
| `lru-cache` | 5.1.1 | 11.2.7 | `@vite-pwa/sveltekit` → `vite-plugin-pwa` → `workbox-build` → `@babel/core` → `@babel/helper-compilation-targets` | PWA tooling |
| `supports-color` | 7.2.0 | 10.2.2 | `@vitest/coverage-v8` → `istanbul-lib-report` | Test coverage |
| `tinybench` | 2.9.0 | 6.0.0 | `vitest` | Test framework |

**Not present in our dependency tree** (FOSSA may be scanning stale data):
`data-uri-to-buffer`, `postgres-interval`, `string-width`, `@types/tedious`, `wrap-ansi`

Last reviewed: 2026-03-20. Revisit when `workbox-build`, `vitest`, or `@vitest/coverage-v8` release new major versions.

## Disclosure Policy

We follow coordinated disclosure. Once a fix is released, we will credit the reporter (unless they prefer to remain anonymous) and publish a security advisory.
