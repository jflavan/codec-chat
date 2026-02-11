# Repo instructions

## Layout
- apps/web: SvelteKit app
- apps/api: ASP.NET Core Web API
- docs: Project documentation
- .github: Copilot instructions and workflows

## Tech stack
- Web: SvelteKit, TypeScript, Vite
- API: ASP.NET Core (.NET 9)
- Data: EF Core, SQLite
- Auth: Google Identity Services (ID tokens validated by the API)

## Vendored instructions (awesome-copilot)
- svelte.instructions.md
- typescript-5-es2022.instructions.md
- aspnet-rest-apis.instructions.md
- csharp.instructions.md
- dotnet-architecture-good-practices.instructions.md
- security-and-owasp.instructions.md
- github-actions-ci-cd-best-practices.instructions.md

## Conventions
- Keep public env vars in apps/web/.env.example
- Keep API config in apps/api/Codec.Api/appsettings*.json
- Prefer minimal, composable components
- Avoid adding new deps without a clear reason
- If touching auth, update docs/AUTH.md
- If touching data models, update migrations and SeedData

## Required updates
- If you add or change behavior, update PLAN.md and any relevant docs
- Update docs/ARCHITECTURE.md and docs/FEATURES.md for user-visible changes

## Verification
- API: cd apps/api/Codec.Api && dotnet run
- Web: cd apps/web && npm run dev
- Optional: cd apps/web && npm run build
