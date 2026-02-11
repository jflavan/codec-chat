# Developer Setup

This guide walks through setting up the Codec development environment from scratch.

## Prerequisites
- **Node.js** 20+ and npm
- **.NET SDK** 9.x
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

1. Navigate to the API project:
   ```bash
   cd apps/api/Codec.Api
   ```

2. Update `appsettings.Development.json`:
   ```json
   {
     "Google": {
       "ClientId": "YOUR_GOOGLE_CLIENT_ID_HERE"
     },
     "ConnectionStrings": {
       "Default": "Data Source=codec-dev.db"
     },
     "Cors": {
       "AllowedOrigins": ["http://localhost:5174"]
     }
   }
   ```
   
   **Note:** `Google:ClientId` is required - the API will fail fast on startup if missing.

3. Run the API:
   ```bash
   dotnet run
   ```
   
   The API will:
   - Run at `http://localhost:5050` by default
   - Automatically apply database migrations
   - Seed initial data if the database is empty

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
   - Your user profile
   - A list of available servers (seeded data: "Codec HQ")
   - Ability to join servers and view channels

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
- Delete the database file and restart the API to reseed:
  ```bash
  rm apps/api/Codec.Api/codec-dev.db
  cd apps/api/Codec.Api && dotnet run
  ```
