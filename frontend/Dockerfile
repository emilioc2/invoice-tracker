# ---------------------------------------------------------------------------
# Frontend Dockerfile — local docker-compose use only.
#
# Production deployment target is Vercel's git integration (see
# source_documents/architecture.md's README outline, "Deployment" item). This
# image exists purely so `docker-compose up` gives a complete local
# full-stack environment; it is not used to deploy the SPA in production.
# ---------------------------------------------------------------------------

# ---- Stage 1: build ---------------------------------------------------------
FROM node:20-alpine AS build
WORKDIR /app

# Install dependencies first so this layer is cached across source changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite bakes VITE_* env vars in at build time. The browser (running on the
# host, not inside the Docker network) is what calls this URL, so it must
# resolve to wherever the API is reachable from the host, i.e. the API's
# host-mapped port (http://localhost:5100) — not an internal Docker service
# hostname like http://api:8080. docker-compose.yml can override this via a
# build arg if needed.
ARG VITE_API_BASE_URL=http://localhost:5100
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

# ---- Stage 2: serve ----------------------------------------------------------
FROM nginx:alpine AS serve

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
