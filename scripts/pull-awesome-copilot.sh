#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE="https://raw.githubusercontent.com/github/awesome-copilot/main"

mkdir -p \
  "$ROOT/.github/agents" \
  "$ROOT/.github/instructions" \
  "$ROOT/.github/skills/webapp-testing" \
  "$ROOT/.github/skills/web-design-reviewer" \
  "$ROOT/.github/skills/refactor"

download() {
  local url="$1"
  local dest="$2"

  curl -fsSL "$url" -o "$dest"
}

# Agents

download "$BASE/agents/CSharpExpert.agent.md" \
  "$ROOT/.github/agents/CSharpExpert.agent.md"

download "$BASE/agents/expert-dotnet-software-engineer.agent.md" \
  "$ROOT/.github/agents/expert-dotnet-software-engineer.agent.md"

download "$BASE/agents/api-architect.agent.md" \
  "$ROOT/.github/agents/api-architect.agent.md"

download "$BASE/agents/github-actions-expert.agent.md" \
  "$ROOT/.github/agents/github-actions-expert.agent.md"

download "$BASE/agents/playwright-tester.agent.md" \
  "$ROOT/.github/agents/playwright-tester.agent.md"

# Instructions

download "$BASE/instructions/svelte.instructions.md" \
  "$ROOT/.github/instructions/svelte.instructions.md"

download "$BASE/instructions/typescript-5-es2022.instructions.md" \
  "$ROOT/.github/instructions/typescript-5-es2022.instructions.md"

download "$BASE/instructions/aspnet-rest-apis.instructions.md" \
  "$ROOT/.github/instructions/aspnet-rest-apis.instructions.md"

download "$BASE/instructions/csharp.instructions.md" \
  "$ROOT/.github/instructions/csharp.instructions.md"

download "$BASE/instructions/dotnet-architecture-good-practices.instructions.md" \
  "$ROOT/.github/instructions/dotnet-architecture-good-practices.instructions.md"

download "$BASE/instructions/security-and-owasp.instructions.md" \
  "$ROOT/.github/instructions/security-and-owasp.instructions.md"

download "$BASE/instructions/github-actions-ci-cd-best-practices.instructions.md" \
  "$ROOT/.github/instructions/github-actions-ci-cd-best-practices.instructions.md"

# Skills

download "$BASE/skills/webapp-testing/SKILL.md" \
  "$ROOT/.github/skills/webapp-testing/SKILL.md"

download "$BASE/skills/web-design-reviewer/SKILL.md" \
  "$ROOT/.github/skills/web-design-reviewer/SKILL.md"

download "$BASE/skills/refactor/SKILL.md" \
  "$ROOT/.github/skills/refactor/SKILL.md"

echo "Pulled awesome-copilot agents, instructions, and skills."
