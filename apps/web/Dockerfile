FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
ARG PUBLIC_API_BASE_URL
ARG PUBLIC_GOOGLE_CLIENT_ID
ENV PUBLIC_API_BASE_URL=${PUBLIC_API_BASE_URL}
ENV PUBLIC_GOOGLE_CLIENT_ID=${PUBLIC_GOOGLE_CLIENT_ID}
RUN npm run build

FROM node:20-alpine AS runtime
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
