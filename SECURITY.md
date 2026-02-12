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

## Disclosure Policy

We follow coordinated disclosure. Once a fix is released, we will credit the reporter (unless they prefer to remain anonymous) and publish a security advisory.
