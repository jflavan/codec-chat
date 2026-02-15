# Agents

This repo is designed for Copilot agent-based work. Each agent should be stateless and rely on docs in this repo.

## Available agents
- Planner: Updates PLAN.md and aligns milestones.
- Backend: ASP.NET Core Web API endpoints, auth, configuration.
- Frontend: SvelteKit UI, client auth, API integration.
- Data: EF Core models, migrations, and seed data.
- Auth: Google Sign-In flow and token validation concerns.
- Docs: Documentation updates for every change.
- QA: Tests, linting, and verification steps.

## Vendored agents (awesome-copilot)
- CSharpExpert.agent.md
- expert-dotnet-software-engineer.agent.md
- api-architect.agent.md
- github-actions-expert.agent.md
- playwright-tester.agent.md

## Agent rules
- Always read PLAN.md first.
- Confirm the stack: SvelteKit + TypeScript, ASP.NET Core (.NET 10), EF Core, PostgreSQL, Google ID tokens.
- Update docs when behavior or configuration changes.
- Keep edits scoped to the current task.
