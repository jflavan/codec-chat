# Authentication

## Google ID token flow
- The client uses Google Identity Services to obtain an ID token.
- The token is sent to the API in the Authorization header:
  - Authorization: Bearer <token>
- The API validates the token using Google as the issuer and the configured client ID as the audience.

## Configuration
- Web: apps/web/.env
  - PUBLIC_GOOGLE_CLIENT_ID
  - PUBLIC_API_BASE_URL
- API: apps/api/Codec.Api/appsettings.Development.json
  - Google:ClientId
  - Cors:AllowedOrigins

## Notes
- This flow is stateless. The API does not issue its own tokens.
- For production, set strict allowed origins and add proper logging/monitoring.
