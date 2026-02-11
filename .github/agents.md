# Agents

This repo is designed for Copilot agent-based work. Each agent should be stateless and rely on docs in this repo.

## Available agents
- Planner: Updates PLAN.md and aligns milestones
- Backend: API endpoints, auth, configuration
- Frontend: SvelteKit UI, client auth, integration
- Docs: Documentation updates for every change

## Agent rules
- Always read PLAN.md first.
- Update docs when behavior or configuration changes.
- Keep edits scoped to the current task.
