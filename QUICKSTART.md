# Codec Chat - Quick Start Guide

Get Codec running on your local machine in minutes! This guide walks you through setting up the development environment from scratch.

## 📋 Prerequisites

### Required Software
- **Node.js** 20.19+, 22.12+, or 24+ — [Download here](https://nodejs.org/)
- **.NET SDK** 10.0.100 — [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)  
- **Docker** — [Download here](https://www.docker.com/products/docker-desktop)
- **Google Cloud Console** account for OAuth setup

### Check Your Versions
```bash
node --version    # Should be 20.19+, 22.12+, or 24+
dotnet --version  # Should be 10.0.100
docker --version  # Any recent version
```

## 🔧 Setup Instructions

### 1. Get a Google OAuth Client ID

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Navigate to **"APIs & Services" > "Credentials"**
4. Click **"+ CREATE CREDENTIALS"** → **"OAuth 2.0 Client IDs"**
5. Configure OAuth consent screen if prompted (choose "External")
6. Set application type to **"Web application"**
7. Add authorized JavaScript origins:
   - `http://localhost:5174` (for development)
   - `http://localhost:3000` (for Docker)
8. Copy your Client ID (format: `123456789-abc123.apps.googleusercontent.com`)

### 2. Clone and Configure Environment

```bash
# Navigate to your project directory
cd codec-chat

# Create environment file with your Google Client ID
cp .env.example .env
```

Edit `.env` and replace with your actual Google Client ID:
```env
GOOGLE_CLIENT_ID=your-actual-client-id.apps.googleusercontent.com
```

### 3. Set Up Frontend Environment

```bash
# Create frontend environment file
cd apps/web
cp .env.example .env
```

Edit `apps/web/.env` with your Google Client ID:
```env
PUBLIC_API_BASE_URL=http://localhost:5050
PUBLIC_GOOGLE_CLIENT_ID=your-actual-client-id.apps.googleusercontent.com
```

### 4. Update Node.js (if needed)

If your Node.js version is too old:

**Using Node Version Manager (nvm):**
```bash
nvm install 22.12.0
nvm use 22.12.0
```

**Or download directly from [nodejs.org](https://nodejs.org/)**

### 5. Install .NET 10 SDK (if needed)

**Using the official installer script:**
```powershell
# Download and install .NET 10.0.100
Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "dotnet-install.ps1"
.\dotnet-install.ps1 -Version 10.0.100
```

**Or download directly from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0)**

## 🚀 Running the Application

### Step 1: Start PostgreSQL Database
```bash
docker compose -f docker-compose.dev.yml up -d
```

### Step 2: Install Frontend Dependencies
```bash
cd apps/web
npm install
```

### Step 3: Set Up Backend Database
```bash
cd ../api/Codec.Api

# Install EF tools (first time only)
dotnet tool install --global dotnet-ef

# Restore packages
dotnet restore

# Run database migrations
dotnet ef database update
```

### Step 4: Start the API Backend
```bash
# From apps/api/Codec.Api directory
dotnet run
```

The API will start on `http://localhost:5050`

### Step 5: Start the Frontend
```bash
# Open a new terminal, navigate to apps/web
cd apps/web
npm run dev
```

The frontend will start on `http://localhost:5174`

## 🎉 Access the Application

1. **Open your browser** and navigate to: **http://localhost:5174**
2. **Sign in with Google** using the account you configured
3. **Start chatting!** You'll automatically join the "Codec HQ" server

## 🔧 Troubleshooting

### Common Issues

**"Google:ClientId is required for authentication"**
- Make sure you've set `GOOGLE_CLIENT_ID` in both `.env` files

**"PUBLIC_GOOGLE_CLIENT_ID is missing"**  
- Ensure `apps/web/.env` exists with `PUBLIC_GOOGLE_CLIENT_ID` set

**"Unsupported engine" error with npm**
- Update Node.js to version 20.19+, 22.12+, or 24+

**".NET SDK was not found"**
- Install .NET 10.0.100 SDK using the instructions above

**Database connection errors**
- Verify Docker is running: `docker ps`
- Restart the database: `docker compose -f docker-compose.dev.yml restart`

### Stopping the Application

1. **Stop the frontend**: Press `Ctrl+C` in the web terminal
2. **Stop the API**: Press `Ctrl+C` in the API terminal  
3. **Stop PostgreSQL**: `docker compose -f docker-compose.dev.yml down`

## 📚 What's Next?

- **Explore features**: Check out [FEATURES.md](docs/FEATURES.md) for a full feature list
- **Read the architecture**: See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for technical details
- **Development setup**: View [DEV_SETUP.md](docs/DEV_SETUP.md) for additional development info
- **Report bugs**: Use our [bug report template](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml)

## 🆘 Need Help?

- **File a bug report**: [GitHub Issues](https://github.com/jflavan/codec-chat/issues)
- **Check documentation**: Browse the `docs/` folder
- **Review logs**: Check terminal outputs for detailed error messages

---

**🎯 You're all set!** Enjoy using Codec Chat on your local machine!