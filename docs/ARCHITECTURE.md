# Architecture

## Overview
Codec is a monorepo with a SvelteKit front-end and an ASP.NET Core Web API. The client uses Google Identity Services to obtain an ID token and sends it to the API as a Bearer token. The API validates the token against Google and exposes authenticated endpoints.

## Components
- Web: SvelteKit, TypeScript, client-side auth
- API: ASP.NET Core Web API (.NET 9)
- Auth: Google ID tokens validated by the API
- Data: SQLite + EF Core

## Data flow
1. User signs in via Google on the web app.
2. Web app receives an ID token.
3. Web app calls API endpoints with Authorization: Bearer <token>.
4. API validates the token and returns user claims.
