# Build context is the repository root, not web/: the build copies sample payloads out of templates/.
#   docker build -f web/Dockerfile .

FROM node:24-alpine AS build
WORKDIR /src/web

# Dependencies first, so editing source does not invalidate the npm ci layer.
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY templates/ /src/templates/
COPY web/ ./
RUN npm run build


# Unprivileged variant: listens on 8080 and runs as a non-root user.
FROM nginxinc/nginx-unprivileged:1.29-alpine

# Rendered by the image's entrypoint with envsubst into conf.d/default.conf at start-up.
COPY web/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/web/dist/pdf-builder-web/browser /usr/share/nginx/html

# Where the API answers from inside the container network. Override per environment.
ENV API_UPSTREAM=http://api:8080

EXPOSE 8080
