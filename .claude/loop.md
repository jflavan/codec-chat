Run these quality checks across the monorepo. Fix any issues found. If everything passes, look for one small improvement opportunity.

## 0. Ensure Dependencies
If any command below fails with "command not found" or "assets file not found", run the appropriate install first:
- Web/Admin: `cd apps/web && npm install` or `cd apps/admin && npm install`
- API: `cd apps/api/Codec.Api && dotnet restore`

## 1. Web Quality Gates
```bash
cd apps/web && npx svelte-kit sync && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | tail -20
```
If svelte-check or TypeScript errors found, fix them.

```bash
cd apps/web && npm run lint:events 2>&1 | tail -10
```
If deprecated Svelte 5 event handlers found, migrate them to runes syntax.

```bash
cd apps/web && npx vitest run 2>&1 | tail -30
```
If any tests fail, fix the failing tests or the code they test.

## 2. API Quality Gates
```bash
cd apps/api/Codec.Api && dotnet build 2>&1 | tail -20
```
If build errors, fix them.

```bash
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj 2>&1 | tail -20
```
If unit tests fail, fix the failing tests or the code they test.

## 3. Admin Quality Gates
```bash
cd apps/admin && npx svelte-kit sync && npx svelte-check --tsconfig ./tsconfig.json 2>&1 | tail -20
```
Fix any type errors found.

## 4. Improvement Opportunity (only if all gates pass)
Pick ONE of these and do it:
- Find an untested API controller method and write a unit test for it
- Find a Svelte component using `on:click` instead of `onclick` and migrate it
- Find a `console.log` in production code (not `console.error`) and remove it
- Find a missing null check on an API response in the frontend and add it

Commit any fixes with a descriptive message referencing what was fixed.
