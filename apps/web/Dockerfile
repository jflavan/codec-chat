FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
ARG PUBLIC_API_BASE_URL
ARG PUBLIC_GOOGLE_CLIENT_ID
ARG PUBLIC_RECAPTCHA_SITE_KEY
ARG PUBLIC_GIPHY_API_KEY
ARG PUBLIC_LIVEKIT_URL
ENV PUBLIC_API_BASE_URL=${PUBLIC_API_BASE_URL}
ENV PUBLIC_GOOGLE_CLIENT_ID=${PUBLIC_GOOGLE_CLIENT_ID}
ENV PUBLIC_RECAPTCHA_SITE_KEY=${PUBLIC_RECAPTCHA_SITE_KEY}
ENV PUBLIC_GIPHY_API_KEY=${PUBLIC_GIPHY_API_KEY}
ENV PUBLIC_LIVEKIT_URL=${PUBLIC_LIVEKIT_URL}
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
EXPOSE 3000
ENV NODE_ENV=production

COPY --from=build /app/build ./build
COPY --from=build /app/package.json .
COPY --from=build /app/package-lock.json .
RUN npm ci --omit=dev

RUN adduser -D appuser
USER appuser

CMD ["node", "build/index.js"]
