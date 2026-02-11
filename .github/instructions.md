# Repo instructions

## Layout
- apps/web: SvelteKit app
- apps/api: ASP.NET Core Web API
- docs: Project documentation
- .github: Copilot instructions and workflows

## Conventions
- Keep public env vars in apps/web/.env.example
- Keep API config in apps/api/Codec.Api/appsettings*.json
- Update docs for any user-facing or configuration changes
- Prefer minimal, composable components
- Avoid adding new deps without a clear reason

## Required updates
- If you add or change behavior, update PLAN.md and any relevant docs
