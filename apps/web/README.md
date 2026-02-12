# Codec Web

SvelteKit 2.x client for Codec — a Discord-like chat application. Handles Google Sign-In, server/channel browsing, real-time messaging (SignalR), typing indicators, friends management, and emoji reactions against the ASP.NET Core API.

## Architecture

The frontend uses a **layered, modular architecture** with Svelte 5 runes for reactivity and context-based dependency injection for state management.

```
src/
├── lib/
│   ├── types/           # Shared TypeScript interfaces (Server, Channel, Message, etc.)
│   ├── api/             # Typed HTTP client (ApiClient class)
│   ├── auth/            # Token persistence (localStorage) & Google Identity Services
│   ├── services/        # SignalR hub connection lifecycle (ChatHubService)
│   ├── state/           # Central reactive state (AppState with $state/$derived runes)
│   ├── styles/          # CSS design tokens & global base styles
│   ├── utils/           # Pure utility functions (date formatting, etc.)
│   ├── components/      # Presentational Svelte 5 components grouped by feature
│   │   ├── server-sidebar/   # Server icon rail
│   │   ├── channel-sidebar/  # Channel list, user panel
│   │   ├── chat/             # Message feed, composer, typing indicator, reactions
│   │   ├── friends/          # Friends panel, friends list, pending requests, add friend
│   │   └── members/          # Members sidebar grouped by role
│   └── index.ts         # Public barrel exports
└── routes/
    ├── +layout.svelte   # Root layout (global CSS, font preconnect)
    └── +page.svelte     # Thin composition shell (~75 lines)
```

### Key patterns

- **State management:** A single `AppState` class uses Svelte 5 `$state` and `$derived` runes. It is created in `+page.svelte` via `createAppState()` and injected into the component tree through `setContext()` / `getContext()`.
- **API client:** `ApiClient` provides typed methods for every REST endpoint, with `encodeURIComponent` on path parameters and a custom `ApiError` class.
- **Auth module:** `session.ts` handles token persistence, expiration checks, and 1-week session enforcement. `google.ts` wraps Google Identity Services initialization.
- **SignalR service:** `ChatHubService` manages the WebSocket connection lifecycle, channel join/leave, and typing indicator events.
- **Components:** Small, focused Svelte 5 components using `$props()` or `getAppState()` for data. Feature-grouped directories keep related files together.

For full architectural details, see [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md).

## Developing

1. Copy the env template and set values:

```sh
cp .env.example .env
```

```env
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=YOUR_DEV_CLIENT_ID
```

2. Install dependencies and start the dev server:

```sh
npm install
npm run dev
```

The web app runs at http://localhost:5174 by default.

## API prerequisites

- Ensure the API is running at `PUBLIC_API_BASE_URL`.
- `Google:ClientId` must be set in the API's development settings to match your client ID.
- CORS in the API should allow `http://localhost:5174` for local testing.

## Multi-account testing

Use two different Google accounts (or an incognito window) to sign in as separate users. This lets you verify member lists and message posting between different identities.

## Building

To create a production version of your app:

```sh
npm run build
```

You can preview the production build with `npm run preview`.

## Useful commands

```sh
npm run check
npm run lint:events
```

- `npm run check` — runs `svelte-check` for type and lint errors.
- `npm run lint:events` — verifies that deprecated `on:*` event directives are not used.
