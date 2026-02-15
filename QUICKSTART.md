# Codec Chat - Docker Quick Start Guide

Get Codec running on your machine in minutes using Docker containers! No need to install .NET, Node.js, or PostgreSQL on your host machine.

## 📋 Prerequisites

### Required Software
- **Docker Desktop** — [Download here](https://www.docker.com/products/docker-desktop)
- **Google Cloud Console** account for OAuth setup

### Check Docker Installation
```bash
docker --version          # Any recent version
docker compose --version  # Should be available
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
   - `http://localhost:3000` (for Docker web container)
8. Copy your Client ID (format: `123456789-abc123.apps.googleusercontent.com`)

### 2. Configure Environment

Create your environment file:
```bash
# Copy the example file
cp .env.example .env
```

Edit `.env` with your Google Client ID:
```env
# Required: Your Google OAuth 2.0 Client ID
GOOGLE_CLIENT_ID=your-actual-client-id.apps.googleusercontent.com
```

### 3. Start Docker Desktop

1. **Launch Docker Desktop** from your applications
2. **Wait for startup** - look for the whale icon in your system tray
3. **Verify it's running**: `docker ps` (should not show connection errors)

## 🚀 Running the Application

### Single Command Launch
```bash
docker compose up --build
```

This command will:
- ✅ Build the .NET 10 API container with all dependencies
- ✅ Build the Node.js 20 web container with SvelteKit
- ✅ Start PostgreSQL 16 database
- ✅ Start Azurite (Azure Storage emulator)
- ✅ Run database migrations automatically
- ✅ Configure proper container networking

**First run takes 2-3 minutes** to download images and build containers.  
**Subsequent runs take ~30 seconds** using cached layers.

### Container Status
You should see output similar to:
```
postgres-1  | database system is ready to accept connections
azurite-1   | Azurite Blob service successfully listens on http://0.0.0.0:10000
api-1       | Now listening on: http://localhost:5050
web-1       | Listening on http://0.0.0.0:3000
```

## 🎉 Access the Application

**Open your browser and navigate to: http://localhost:3000**

1. **Sign in with Google** using your configured OAuth credentials
2. **Start chatting!** You'll automatically join the "Codec HQ" server
3. **Explore features**: Create channels, send messages, upload images

## 🔧 Container Management

### Useful Commands

**Run in background (detached):**
```bash
docker compose up -d --build
```

**View logs:**
```bash
docker compose logs -f          # All services
docker compose logs -f api      # API only
docker compose logs -f web      # Web only
```

**Stop all containers:**
```bash
docker compose down
```

**Stop and remove all data:**
```bash
docker compose down -v
```

**Rebuild specific service:**
```bash
docker compose up --build api   # API only
docker compose up --build web   # Web only
```

### Service URLs

| Service | Internal Port | External URL |
|---------|---------------|--------------|
| **Web App** | 3000 | http://localhost:3000 |
| **API** | 8080 → 5050 | http://localhost:5050 |
| **PostgreSQL** | 5432 → 5433 | localhost:5433 |
| **Azurite** | 10000 | http://localhost:10000 |

## 🔧 Troubleshooting

### Common Issues

**"Cannot connect to the Docker daemon"**
- Ensure Docker Desktop is running
- Check system tray for Docker whale icon
- Restart Docker Desktop

**"Port already in use" errors**
- Stop conflicting services: `docker compose down`
- Check for other applications using ports 3000, 5050, 5433, 10000

**"Build failed" or dependency errors**
- Clean rebuild: `docker compose down && docker compose up --build --no-cache`
- Check your internet connection for package downloads

**Google Sign-In issues**
- Verify your `GOOGLE_CLIENT_ID` in `.env` is correct
- Ensure `http://localhost:3000` is in your OAuth allowed origins
- Check browser console for authentication errors

**Database connection errors**
- Wait for PostgreSQL container to fully start (watch logs)
- Ensure containers can communicate (don't stop mid-startup)

### Development Tips

**Reset everything (fresh start):**
```bash
docker compose down -v
docker system prune -f
docker compose up --build
```

**Monitor resource usage:**
```bash
docker stats
```

**Access container shell for debugging:**
```bash
docker compose exec api bash      # API container
docker compose exec postgres bash # Database container
```

## 📚 What's Next?

- **Explore features**: Check out [FEATURES.md](docs/FEATURES.md) for complete functionality list
- **Learn the architecture**: See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for technical details
- **Report bugs**: Use our [bug report template](https://github.com/jflavan/codec-chat/issues/new?template=bug-report.yml)
- **Deploy to production**: See [DEPLOYMENT.md](docs/DEPLOYMENT.md) for Azure deployment guide

## 🆘 Need Help?

- **File a bug report**: [GitHub Issues](https://github.com/jflavan/codec-chat/issues)
- **Check container logs**: `docker compose logs -f`
- **Review documentation**: Browse the `docs/` folder
- **Clean restart**: `docker compose down -v && docker compose up --build`

---

**🎯 Congratulations!** You're running a production-ready Discord-like chat application entirely in Docker containers! 🐳