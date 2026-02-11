# Developer setup

## Prerequisites
- Node.js 20+ and npm
- .NET SDK 9.x

## Web
1. Copy apps/web/.env.example to apps/web/.env
2. Update PUBLIC_GOOGLE_CLIENT_ID and PUBLIC_API_BASE_URL
3. Run:
   - cd apps/web
   - npm install
   - npm run dev

## API
1. Update apps/api/Codec.Api/appsettings.Development.json
   - Google:ClientId is required for startup
   - ConnectionStrings:Default controls the SQLite database path
2. Run:
   - cd apps/api/Codec.Api
   - dotnet run

## Database migrations
From apps/api/Codec.Api:
- dotnet tool install --global dotnet-ef
- export PATH="$PATH:$HOME/.dotnet/tools"
- dotnet ef migrations add InitialCreate
- dotnet ef database update

## Ports
- Web: http://localhost:5173
- API: http://localhost:5000 (default)
