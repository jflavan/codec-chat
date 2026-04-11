Find and remove dead code across the codebase.

## Search Strategy

### 1. Unused TypeScript/Svelte Exports
```bash
# For each exported function/class/const in apps/web/src/lib/
# Check if it's imported anywhere else
```
- Check `apps/web/src/lib/utils/` for unused helper functions
- Check `apps/web/src/lib/types/` for unused type definitions
- Check `apps/web/src/lib/api/client.ts` for methods never called from frontend code

### 2. Unused API Endpoints
- For each controller action, grep the frontend for the corresponding API path
- Check if any endpoints are only used by removed features

### 3. Unused CSS
- Check `apps/web/src/lib/styles/tokens.css` for CSS custom properties not referenced
- Check component `<style>` blocks for unused selectors

### 4. Stale Imports
- Find imports that are no longer used in their files
- Remove them

### 5. Commented-Out Code
- Find blocks of commented-out code (>3 lines) and remove them
- Git history preserves the code; commented blocks are noise

## Rules
- ONLY remove code you're CERTAIN is unused
- Verify with grep before removing — check all apps (web, admin, api)
- Run full build + tests after each removal batch
- Commit in logical groups (e.g., "remove unused API client methods")

## Quality Gate
```bash
cd apps/web && npm run build && npm run check && npm test
cd apps/admin && npm run build && npm run check
cd apps/api/Codec.Api && dotnet build
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
```
