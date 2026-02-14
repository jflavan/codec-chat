# Developer Setup

This guide walks through setting up the Codec development environment from scratch.

## Prerequisites
- **Node.js** 20+ and npm
- **.NET SDK** 9.x
- **Docker** (for local PostgreSQL via Docker Compose)
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

## API Setup

1. **Start PostgreSQL** using Docker Compose (from the repository root):
   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```
   This starts a PostgreSQL 16 instance on `localhost:5433` with database `codec_dev`, user `codec`, password `codec_dev_password`.

2. Navigate to the API project:
   ```bash
   cd apps/api/Codec.Api
   ```

3. Update `appsettings.Development.json`:
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
   
   **Note:** `Google:ClientId` is required — the API will fail fast on startup if missing. `Api:BaseUrl` is used to generate public URLs for uploaded avatars and images.

4. Run the API:
   ```bash
   dotnet run
   ```
   
   The API will:
   - Run at `http://localhost:5050` by default
   - Automatically apply database migrations (PostgreSQL)
   - Create the default "Codec HQ" server (with `general` and `announcements` channels) if it doesn't exist
   - Seed sample users and messages if the database is empty

## Web Setup

1. Navigate to the web project:
   ```bash
   cd apps/web
   ```

2. Create environment configuration:
   ```bash
   cp .env.example .env
   ```

3. Update `.env` with your values:
   ```env
   PUBLIC_API_BASE_URL=http://localhost:5050
   PUBLIC_GOOGLE_CLIENT_ID=YOUR_GOOGLE_CLIENT_ID_HERE
   ```

4. Install dependencies and run:
   ```bash
   npm install
   npm run dev
   ```
   
   The web app runs at `http://localhost:5174` by default.

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
- **Web:** `http://localhost:5174`
- **API:** `http://localhost:5050`

## Troubleshooting

### API fails to start
- **Error:** "Google:ClientId is required"
  - **Solution:** Set `Google:ClientId` in `appsettings.Development.json`

### Google Sign-In fails
- **Error:** "Invalid client ID"
  - **Solution:** Verify `PUBLIC_GOOGLE_CLIENT_ID` in `.env` matches your Google Cloud Console client ID
  
- **Error:** "Unauthorized JavaScript origin"
  - **Solution:** Add `http://localhost:5174` to authorized origins in Google Cloud Console

### CORS errors
- **Error:** "Access-Control-Allow-Origin"
  - **Solution:** Ensure `http://localhost:5174` is in `Cors:AllowedOrigins` in API settings

### Database issues
- Reset the database and restart the API to reseed:
  ```bash
  docker compose -f docker-compose.dev.yml down -v
  docker compose -f docker-compose.dev.yml up -d
  cd apps/api/Codec.Api && dotnet run
  ```
