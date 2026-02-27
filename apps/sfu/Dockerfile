FROM node:22-slim AS build
WORKDIR /app

# mediasoup compiles a native C++ worker binary during npm install.
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    build-essential \
    ninja-build \
  && rm -rf /var/lib/apt/lists/*

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# Runtime — copy compiled dist + node_modules (contains mediasoup-worker binary).
# Must stay on a glibc-based image (not alpine) for the native binary to run.
FROM node:22-slim AS runtime
WORKDIR /app
EXPOSE 3001
ENV NODE_ENV=production

RUN adduser --disabled-password --gecos '' appuser

COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json .

USER appuser
CMD ["node", "dist/index.js"]
