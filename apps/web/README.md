# Codec Web

SvelteKit client for Codec (Discord-like chat). Handles Google Sign-In, server/channel browsing, and message posting against the ASP.NET Core API.

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

`npm run check` runs `svelte-check` and verifies that deprecated `on:*` event directives are not used.
