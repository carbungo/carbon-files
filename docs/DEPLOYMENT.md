# Deployment Guide

Production deployment guide for running CarbonFiles (API + Dashboard).

## Quick Start

```bash
# Clone and run
git clone https://github.com/carbungo/carbon-files.git
cd carbon-files
docker compose up -d
# API available at http://localhost:8080
# Set admin key: edit docker-compose.yml or set CarbonFiles__AdminKey env var
```

## Full Docker Compose (API + Dashboard)

```yaml
services:
  api:
    image: ghcr.io/carbungo/carbon-files:latest
    # Or build locally: build: .
    ports:
      - "8080:8080"
    volumes:
      - carbonfiles-data:/app/data
    environment:
      CarbonFiles__AdminKey: "change-me-to-a-secure-random-string"
      CarbonFiles__DataDir: /app/data
      CarbonFiles__DbPath: /app/data/carbonfiles.db
      CarbonFiles__MaxUploadSize: "104857600"  # 100MB
      CarbonFiles__CorsOrigins: "https://dash.example.com"
      CarbonFiles__JwtSecret: "optional-separate-jwt-secret"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz"]
      interval: 30s
      timeout: 5s
      retries: 3
    restart: unless-stopped

  dashboard:
    image: ghcr.io/carbungo/files-ui:latest
    ports:
      - "3000:3000"
    environment:
      NEXT_PUBLIC_API_URL: "https://files.example.com"  # Browser-facing API URL
      INTERNAL_API_URL: "http://api:8080"               # Internal Docker network URL for SSR
      PORT: "3000"
    depends_on:
      api:
        condition: service_healthy
    restart: unless-stopped

volumes:
  carbonfiles-data:
```

## Environment Variables

### API (service: `api`)

| Variable | Required | Default | Description |
|---|---|---|---|
| `CarbonFiles__AdminKey` | Yes | -- | Master admin key. Use a long random string. |
| `CarbonFiles__JwtSecret` | No | Derived from AdminKey | Separate secret for JWT signing. Recommended for production. |
| `CarbonFiles__DataDir` | No | `./data` | Root directory for file storage and CAS |
| `CarbonFiles__DbPath` | No | `./data/carbonfiles.db` | SQLite database file path |
| `CarbonFiles__MaxUploadSize` | No | `0` (unlimited) | Max upload size in bytes. Set to prevent abuse. |
| `CarbonFiles__CleanupIntervalMinutes` | No | `60` | Minutes between expired bucket + orphan cleanup runs |
| `CarbonFiles__CorsOrigins` | No | `*` | Comma-separated allowed origins. Set to dashboard URL in production. |
| `CarbonFiles__EnableScalar` | No | `true` | Enable interactive API docs at `/scalar` |

### Dashboard (service: `dashboard`)

| Variable | Required | Default | Description |
|---|---|---|---|
| `NEXT_PUBLIC_API_URL` | Yes | -- | Public API URL that browsers will call (e.g., `https://files.example.com`) |
| `INTERNAL_API_URL` | No | Same as NEXT_PUBLIC_API_URL | Internal API URL for server-side rendering. Use Docker service name for performance. |
| `PORT` | No | `3000` | Dashboard port |

## Reverse Proxy (Traefik)

Full docker-compose.yml with Traefik for TLS termination and automatic Let's Encrypt certificates:

```yaml
services:
  traefik:
    image: traefik:v3.3
    command:
      - "--api.dashboard=false"
      - "--providers.docker=true"
      - "--providers.docker.exposedbydefault=false"
      - "--entrypoints.web.address=:80"
      - "--entrypoints.websecure.address=:443"
      - "--entrypoints.web.http.redirections.entrypoint.to=websecure"
      - "--certificatesresolvers.letsencrypt.acme.email=you@example.com"
      - "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json"
      - "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web"
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - letsencrypt:/letsencrypt
    restart: unless-stopped

  api:
    image: ghcr.io/carbungo/carbon-files:latest
    labels:
      traefik.enable: "true"
      traefik.http.routers.api.rule: "Host(`files.example.com`)"
      traefik.http.routers.api.entrypoints: "websecure"
      traefik.http.routers.api.tls.certresolver: "letsencrypt"
      traefik.http.services.api.loadbalancer.server.port: "8080"
    volumes:
      - carbonfiles-data:/app/data
    environment:
      CarbonFiles__AdminKey: "${ADMIN_KEY}"
      CarbonFiles__DataDir: /app/data
      CarbonFiles__DbPath: /app/data/carbonfiles.db
      CarbonFiles__MaxUploadSize: "104857600"
      CarbonFiles__CorsOrigins: "https://dash.example.com"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz"]
      interval: 30s
      timeout: 5s
      retries: 3
    restart: unless-stopped

  dashboard:
    image: ghcr.io/carbungo/files-ui:latest
    labels:
      traefik.enable: "true"
      traefik.http.routers.dashboard.rule: "Host(`dash.example.com`)"
      traefik.http.routers.dashboard.entrypoints: "websecure"
      traefik.http.routers.dashboard.tls.certresolver: "letsencrypt"
      traefik.http.services.dashboard.loadbalancer.server.port: "3000"
    environment:
      NEXT_PUBLIC_API_URL: "https://files.example.com"
      INTERNAL_API_URL: "http://api:8080"
    depends_on:
      api:
        condition: service_healthy
    restart: unless-stopped

volumes:
  carbonfiles-data:
  letsencrypt:
```

Traefik handles WebSocket connections automatically for SignalR (`/hub/files`). No additional configuration is needed.

## Health Checks

- **API**: `GET /healthz` returns 200 with `{"status": "healthy"}` or 503 if the database is unreachable.
- **Dashboard**: `GET /api/version` returns build info.
- Docker healthcheck is configured in the compose files above.
- For external monitoring, poll `/healthz` every 30 seconds.

## Data and Backups

- All data lives in a single volume: SQLite database + file storage.
- **Backup strategy**: Stop writes (or accept WAL consistency), then copy the `./data/` directory.
- SQLite WAL mode: the database is at `./data/carbonfiles.db`, with the WAL file at `./data/carbonfiles.db-wal`.
- For consistent backups without downtime, use the SQLite backup command:
  ```bash
  sqlite3 /app/data/carbonfiles.db ".backup /backup/carbonfiles.db"
  ```
- CAS files in `./data/content/` are immutable and safe to copy at any time.

## Production Checklist

- [ ] Change `CarbonFiles__AdminKey` from default to a secure random string (32+ chars)
- [ ] Set `CarbonFiles__CorsOrigins` to your dashboard domain (not `*`)
- [ ] Set `CarbonFiles__MaxUploadSize` to prevent abuse (e.g., `104857600` for 100MB)
- [ ] Set `CarbonFiles__JwtSecret` to a separate value from AdminKey
- [ ] Configure `NEXT_PUBLIC_API_URL` to your public API domain
- [ ] Set up TLS (Traefik + Let's Encrypt or your own certs)
- [ ] Configure volume backups for `carbonfiles-data`
- [ ] Set `CarbonFiles__EnableScalar` to `false` if you don't want public API docs
- [ ] Review Traefik dashboard access (disabled by default in the config above)
- [ ] Set up log aggregation (API logs to stdout)

## Upgrading

```bash
docker compose pull
docker compose up -d
```

The API runs the migrator on startup -- schema changes are applied automatically. No manual migration steps are needed.
