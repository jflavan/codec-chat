# Contributing to Codec

Thanks for your interest in contributing to Codec! This project is in early alpha, so contributions of all kinds are welcome — bug reports, feature suggestions, documentation improvements, and code.

## Getting Started

1. Fork the repository and clone your fork.
2. Follow the [Development Setup](docs/DEV_SETUP.md) guide to get the project running locally.
3. Create a new branch for your work: `git checkout -b my-feature`.

## Development

- **API:** ASP.NET Core 10 — see [apps/api/](apps/api/) and [Architecture docs](docs/ARCHITECTURE.md).
- **Web:** SvelteKit + Svelte 5 — see [apps/web/](apps/web/) and [web README](apps/web/README.md).
- Run both the API and web app locally to test end-to-end changes.

## Pull Requests

- Keep PRs focused on a single change.
- Write clear commit messages describing what changed and why.
- Ensure the project builds without errors before submitting:
  ```bash
  # API
  cd apps/api/Codec.Api && dotnet build

  # Web
  cd apps/web && npm run build && npx svelte-check
  ```
- Link any related issues in the PR description.

## Reporting Bugs

Open a [GitHub issue](https://github.com/jflavan/codec-chat/issues) with:
- A clear description of the problem.
- Steps to reproduce.
- Expected vs. actual behavior.
- Browser/OS/runtime versions if relevant.

## Suggesting Features

Open a [GitHub issue](https://github.com/jflavan/codec-chat/issues) and tag it as a feature request. Describe the use case and how you envision it working.

## Code Style

- **C#:** Follow standard .NET conventions. See [.github/instructions/csharp.instructions.md](.github/instructions/csharp.instructions.md).
- **TypeScript/Svelte:** Follow the project's existing patterns. See [.github/instructions/svelte.instructions.md](.github/instructions/svelte.instructions.md) and [.github/instructions/typescript-5-es2022.instructions.md](.github/instructions/typescript-5-es2022.instructions.md).

## Security

If you discover a security vulnerability, **do not open a public issue**. See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
