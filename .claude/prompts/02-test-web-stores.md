Write unit tests for the untested Svelte state stores. These use Svelte 5 runes ($state, $derived).

## Target Files (zero coverage — all in apps/web/src/lib/state/)
1. `auth-store.svelte.ts` — Authentication state, login/logout, token management
2. `channel-store.svelte.ts` — Channel list, active channel, channel CRUD
3. `dm-store.svelte.ts` — DM conversations, unread counts
4. `friend-store.svelte.ts` — Friend list, requests, blocking
5. `message-store.svelte.ts` — Message list, sending, editing, deleting, reactions
6. `server-store.svelte.ts` — Server list, active server, members, roles
7. `ui-store.svelte.ts` — UI state (modals, sidebar, theme)
8. `voice-store.svelte.ts` — Voice channel state, mute, deafen
9. `announcement-store.svelte.ts` — Site-wide announcement banners
10. `navigation.svelte.ts` — Cross-store navigation orchestration
11. `signalr.svelte.ts` — SignalR connection lifecycle

## Process for Each Store
1. Read the store file to understand its state shape, factory function, and methods
2. Read existing test patterns in `apps/web/src/lib/` (e.g., `client.spec.ts`)
3. Create test file adjacent: `apps/web/src/lib/state/<store-name>.spec.ts`
4. Test:
   - Factory function creates store with correct initial state
   - Each public method updates state correctly
   - Error handling (API failures, network errors)
   - Edge cases (empty lists, null values)
5. Run: `cd apps/web && npx vitest run src/lib/state/<store-name>.spec.ts`
6. Fix failures before moving to next store

## Mocking Strategy
- Mock `ApiClient` methods using `vi.fn()` or `vi.spyOn()`
- Mock `ChatHubService` for SignalR-dependent stores
- Mock `localStorage` for auth token persistence
- For Svelte 5 runes: test the class methods directly, verify state changes via public properties

## Important Notes
- Stores are classes with `$state` and `$derived` properties
- They're created via `create*Store()` factories that take dependencies (apiClient, etc.)
- Test files need `vitest` globals enabled (already configured)
- Don't test private/internal implementation — focus on public API

## Quality Gate
```bash
cd apps/web && npm test
```
All tests must pass.
