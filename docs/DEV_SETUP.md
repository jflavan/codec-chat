# Developer Setup

This guide walks through setting up the Codec development environment from scratch.

## Prerequisites
- **Node.js** 20+ and npm
- **.NET SDK** 10.x — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker** (for Aspire-managed containers or manual Docker Compose)
- **Google Cloud Console** account for OAuth credentials

## Google OAuth Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
2. Create a new project or select an existing one
3. Navigate to **APIs & Services** > **Credentials**
4. Click **Create Credentials** > **OAuth 2.0 Client ID**
5. Configure the consent screen if prompted
6. Select **Web application** as application type
7. Add authorized JavaScript origins:
   - `http://localhost:5174` (for local development)
   - `http://localhost:5050` (optional, for API testing)
8. Save and copy the **Client ID**

## Option A: Start with Aspire (Recommended)

.NET Aspire orchestrates all local services (PostgreSQL, Redis, Azurite, API, and Web) with a single command and provides a developer dashboard with distributed tracing, logs, and metrics.

### 1. Configure the API

Set your Google Client ID using .NET user secrets (persists across Aspire restarts):

```bash
cd apps/aspire/Codec.AppHost
dotnet user-secrets set "Google:ClientId" "YOUR_GOOGLE_CLIENT_ID_HERE"
```

### 2. Configure the Web App

```bash
cd apps/web
cp .env.example .env
```

Edit `.env`:
```env
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=YOUR_GOOGLE_CLIENT_ID_HERE
```

### 3. Run

```bash
cd apps/aspire/Codec.AppHost
dotnet run
```

This starts:
- **PostgreSQL** — managed by Aspire with a data volume
- **Redis** — with TLS (Aspire uses self-signed certs locally)
- **Azurite** — Azure Storage emulator for file uploads
- **API** — ASP.NET Core at `http://localhost:5050`
- **Web** — SvelteKit dev server at `http://localhost:5174`
- **Aspire Dashboard** — at `https://localhost:17222` (traces, logs, metrics)

The API auto-migrates the database and seeds sample data on first run.

> **Note:** Aspire manages container lifecycles, ports, and connection strings automatically. You don't need to run Docker Compose separately.

## Option B: Start without Aspire

If you prefer to manage services manually:

### 1. Start PostgreSQL and Azurite

```bash
docker compose up -d postgres azurite
```

This starts PostgreSQL 16 on `localhost:5433` (database `codec_dev`, user `codec`, password `codec_dev_password`) and Azurite on `localhost:10000`.

### 2. Configure and run the API

```bash
cd apps/api/Codec.Api
```

Update `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5433;Database=codec_dev;Username=codec;Password=codec_dev_password"
  },
  "Api": {
    "BaseUrl": "http://localhost:5050"
  },
  "Google": {
    "ClientId": "YOUR_GOOGLE_CLIENT_ID_HERE"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5174"]
  }
}
```

**Note:** `Google:ClientId` is required — the API will fail fast on startup if missing.

```bash
dotnet run
```

The API will:
- Run at `http://localhost:5050`
- Automatically apply database migrations
- Create the default "Codec HQ" server if it doesn't exist
- Seed sample users and messages if the database is empty

### 3. Configure and run the Web App

```bash
cd apps/web
cp .env.example .env
```

Edit `.env`:
```env
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=YOUR_GOOGLE_CLIENT_ID_HERE
```

```bash
npm install
npm run dev
```

The web app runs at `http://localhost:5174`.

## Database Migrations

Migrations run automatically in development mode. To manually manage migrations:

### Install EF Core tools:
```bash
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Create a new migration:
```bash
cd apps/api/Codec.Api
dotnet ef migrations add YourMigrationName
```

### Apply migrations:
```bash
dotnet ef database update
```

### View migration SQL:
```bash
dotnet ef migrations script
```

## Testing the Setup

1. Open `http://localhost:5174` in your browser
2. Click **Sign in with Google**
3. Complete the Google authentication flow
4. You should see:
   - Your user profile (you are automatically joined to the default "Codec HQ" server on first sign-in)
   - The Codec HQ server with `general`, `announcements`, and `build-log` channels
   - Ability to create servers, join via invite codes, and view channels
5. Open a second browser window (or incognito) and sign in with a different account
6. Both users should see:
   - Real-time message delivery (messages appear without page refresh)
   - Typing indicators when the other user types in the same channel
7. Test the Friends feature:
   - Click the Home icon in the server sidebar to open the Friends panel
   - Go to the **Add Friend** tab, search for the other user by name or email
   - Send a friend request — the other user should see a notification badge on their Home icon
   - Accept/decline the request from the **Pending** tab
   - Confirmed friends appear in the **All Friends** tab
8. Test Direct Messages:
   - From the Friends list, click a friend to start a DM conversation
   - Messages should appear in real-time for both users
   - Typing indicators should show when the other user is typing
9. Test additional features:
   - **Emoji reactions:** hover over a message to see the reaction action bar, click an emoji
   - **Image uploads:** use the `+` button, paste from clipboard, or drag-and-drop an image into the composer
   - **@mentions:** type `@` in the composer to see the member autocomplete picker
   - **Message replies:** hover over a message and click the reply button
   - **Text formatting:** use `*bold*` or `_italic_` in a message
   - **Link previews:** paste a URL into a message and send — a preview card should appear
   - **Nicknames:** click the gear icon (⚙) in the user panel, go to My Profile, and set a nickname
   - **User Settings:** the gear icon opens the full settings modal (profile, account info)

## Default Ports

| Service | URL |
|---------|-----|
| Web | `http://localhost:5174` |
| API | `http://localhost:5050` |
| Aspire Dashboard | `https://localhost:17222` (Aspire only) |

## Docker Compose (Full Stack)

If you prefer running everything in Docker instead of running the API and web app natively, use the root `docker-compose.yml`:

```bash
cp .env.example .env
# Edit .env — set GOOGLE_CLIENT_ID
docker compose up -d
```

This starts PostgreSQL, Redis, Azurite (blob storage emulator), the API (port `5050`), and the web app (port `3000`). See the [Self-Hosting Guide](SELF_HOSTING.md) for more details.

## Troubleshooting

### API fails to start
- **Error:** "Google:ClientId is required"
  - **Solution:** Set `Google:ClientId` in `appsettings.Development.json` (Option B) or via `dotnet user-secrets` (Option A)

### Google Sign-In fails
- **Error:** "Invalid client ID"
  - **Solution:** Verify `PUBLIC_GOOGLE_CLIENT_ID` in `.env` matches your Google Cloud Console client ID

- **Error:** "Unauthorized JavaScript origin"
  - **Solution:** Add `http://localhost:5174` to authorized origins in Google Cloud Console

### CORS errors
- **Error:** "Access-Control-Allow-Origin"
  - **Solution:** Ensure `http://localhost:5174` is in `Cors:AllowedOrigins` in API settings

### Redis connection fails (Aspire)
- Aspire's Redis container uses TLS with a self-signed certificate. The API automatically trusts it in development. If you see certificate errors, ensure `ASPNETCORE_ENVIRONMENT` is `Development`.

### Database issues
- **Aspire:** Stop Aspire, delete the Postgres data volume (`docker volume ls` to find it), and restart.
- **Manual:** Reset with:
  ```bash
  docker compose down -v
  docker compose up -d postgres azurite
  cd apps/api/Codec.Api && dotnet run
  ```
