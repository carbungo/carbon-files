# CarbonFiles API Specification

A file-sharing platform with bucket-based organization and API key authentication. This document defines the API only — no frontend, no rendering, no previews. The API stores and serves bytes + metadata.

## Architecture

- **ASP.NET Minimal API** (.NET 10, **Native AOT compiled**)
- **EF Core** with **SQLite** for metadata (buckets, files, keys, upload tokens)
- **File system** for file storage
- **Streaming** for uploads and downloads — never buffer entire files in memory
- **Scalar** for OpenAPI documentation (not Swagger/Swashbuckle)
- **Sub-millisecond response times** — this must be fast. AOT, no JIT warmup, minimal allocations.

## Project Standards

### Project Structure (Clean Architecture)

```
CarbonFiles/
├── src/
│   ├── CarbonFiles.Api/            # Minimal API endpoints, middleware, Program.cs
│   ├── CarbonFiles.Core/           # Domain models, interfaces, no dependencies
│   └── CarbonFiles.Infrastructure/ # EF Core, SQLite, file system, JWT, services
├── tests/
│   ├── CarbonFiles.Api.Tests/      # Integration tests (WebApplicationFactory)
│   ├── CarbonFiles.Core.Tests/     # Unit tests for domain logic
│   └── CarbonFiles.Infrastructure.Tests/ # Unit tests for services, repos
├── .github/
│   └── workflows/
│       └── ci.yml                  # Build, test, publish to GHCR
├── Dockerfile
├── docker-compose.yml
├── README.md
└── CarbonFiles.sln
```

### Native AOT

The API project must compile with Native AOT:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

This means:
- No reflection-based serialization — use `System.Text.Json` source generators
- No dynamic loading
- EF Core AOT-compatible configuration (use compiled models)
- All JSON serialization via `JsonSerializerContext` with `[JsonSerializable]` attributes
- **SignalR hub payloads must be included in the source generator context** — `FileInfo`, `Bucket`, `BucketChanges`, and any other types sent/received via hub methods. SignalR uses `System.Text.Json` internally and will fail at runtime under AOT if these types aren't registered.
- **Verify SignalR transport negotiation works under AOT in .NET 10.** Historically it used reflection for connection management. Test all transports (WebSocket, SSE, long-polling) in an AOT-published build. If negotiation breaks, pin to WebSocket transport only.

### EF Core & Migrations

Use EF Core with SQLite. Migrations must work with AOT:

```bash
# 1. Create migration
dotnet ef migrations add <Name> --project src/CarbonFiles.Infrastructure --startup-project src/CarbonFiles.Api

# 2. Apply migrations
dotnet ef database update --project src/CarbonFiles.Infrastructure --startup-project src/CarbonFiles.Api

# 3. Regenerate compiled models (REQUIRED after every migration for AOT)
dotnet ef dbcontext optimize --project src/CarbonFiles.Infrastructure --startup-project src/CarbonFiles.Api
```

**Important:** Step 3 must happen after every migration. Compiled models are required for Native AOT — skipping this step causes runtime errors. Document this workflow prominently in README.md.

The app should auto-apply pending migrations on startup in development, but NOT in production (require explicit migration).

### Testing

Heavy unit and integration test coverage. Every endpoint, every service, every edge case.

- **Unit tests** — domain logic, auth resolution, MIME detection, ID generation, expiry calculations, upload token validation
- **Integration tests** — full HTTP tests using `WebApplicationFactory<Program>`, test every endpoint with valid/invalid auth, edge cases, streaming uploads/downloads, WebSocket events, Range requests
- **Use xUnit** with `FluentAssertions`
- Test names: `MethodName_Scenario_ExpectedResult`
- Aim for >90% coverage on Core and Infrastructure

### CI/CD (GitHub Actions)

`.github/workflows/ci.yml`:
- Trigger on push to `main` and PRs
- Steps: restore → build → test → publish AOT → Docker build → push to GHCR
- Tag images with `latest` and git SHA
- Run tests before building the container — fail fast

### Docker

```dockerfile
# Build stage — AOT compile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# ...publish with AOT...

# Runtime — no .NET runtime needed, just the native binary
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
COPY --from=build /app/publish /app
EXPOSE 8080
ENTRYPOINT ["/app/CarbonFiles.Api"]
```

The final image should be tiny — just the native binary + runtime deps. No SDK, no .NET runtime.

`docker-compose.yml` for local dev with volume mounts for data and SQLite.

### Documentation

**README.md** must include:
- What it is, what it does
- Quick start (Docker)
- API overview with curl examples
- Configuration (env vars)
- Migration workflow
- Development setup
- Architecture diagram (text-based)
- License

### Configuration

Use the standard .NET configuration system (`appsettings.json`, environment variables, or user secrets). All settings should be bindable to a strongly-typed options class via `IOptions<T>`.

`appsettings.json`:
```json
{
  "CarbonFiles": {
    "AdminKey": "your-admin-key",
    "JwtSecret": "optional-override",
    "DataDir": "./data",
    "DbPath": "./data/carbonfiles.db",
    "MaxUploadSize": 0,
    "CleanupIntervalMinutes": 60,
    "SiteDomain": null
  }
}
```

All settings are also overridable via environment variables (e.g. `CarbonFiles__AdminKey`).

| Setting | Description | Default |
|---------|-------------|---------|
| `AdminKey` | Admin API key | Required |
| `JwtSecret` | Secret for signing dashboard JWTs | Derived from AdminKey if not set |
| `DataDir` | Directory for file storage | `./data` |
| `DbPath` | SQLite database path | `./data/carbonfiles.db` |
| `MaxUploadSize` | Max upload size in bytes | `0` (unlimited) |
| `CleanupIntervalMinutes` | Minutes between expired bucket cleanup | `60` |
| `CorsOrigins` | Comma-separated allowed origins for CORS | `*` |
| `SiteDomain` | Wildcard domain for bucket static sites (for example, `files.example.com`) | Disabled |

CORS must be configured — any browser-based frontend (including dashboard JWT flow) calls this API directly. If `CorsOrigins` is `*`, allow all origins. Otherwise, restrict to the listed origins. Allow headers: `Authorization`, `Content-Type`. Allow methods: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`. Expose headers: `Content-Range`, `Accept-Ranges`, `Content-Length`, `ETag`, `Last-Modified`.

### Performance

- Response times must be sub-millisecond for metadata operations
- File streaming should saturate the network, not the CPU
- Use `async`/`await` throughout — no blocking calls
- Connection pooling for SQLite (WAL mode enabled)
- Minimize allocations in hot paths (use `Span<T>`, `Memory<T>` where appropriate)
- Cache API key lookups in memory with short TTL

## Authentication

All auth uses the `Authorization: Bearer <token>` header. Three token types:

1. **Admin key** — configured via env var or config file. Full access to everything.
2. **API keys** — created by admin, each scoped to its own buckets. Format: `cf4_<prefix>_<secret>`.
3. **Dashboard tokens** — short-lived JWTs created by the admin key holder. Grant full admin access without exposing the real admin key. Stateless — no database storage, just signature verification + expiry check.

How auth resolves:
- **Admin key or dashboard JWT** → admin access (all keys, all buckets, stats, everything)
- **API key** → owner access (only buckets created by that key)
- **No auth** → public access (bucket details, file metadata, file downloads, short URLs, ZIP, summaries)
- **Upload token** (query param `?token=`) → upload-only access to a specific bucket

## Error Format

All errors return JSON:

```json
{
  "error": "Short error message",
  "hint": "Actionable suggestion for fixing the issue."
}
```

Status codes:
- `200` — OK
- `201` — Created (new bucket, key, upload, token)
- `204` — No content (successful delete)
- `206` — Partial content (Range request fulfilled)
- `304` — Not modified (ETag/Last-Modified matched, no body returned)
- `400` — Bad request (invalid input, missing required fields)
- `401` — Missing or invalid authentication
- `403` — Forbidden (wrong scope, not owner)
- `404` — Not found or expired
- `409` — Conflict (short URL code collision — retry with new code)
- `413` — Payload too large (`MaxUploadSize` exceeded)
- `416` — Range not satisfiable (invalid Range header)
- `429` — Rate limited (if rate limiting is enabled)

## Pagination

All endpoints that return lists support pagination via query params:

| Param | Type | Description | Default |
|-------|------|-------------|---------|
| `limit` | int | Max items to return | 50 |
| `offset` | int | Number of items to skip | 0 |
| `sort` | string | Field to sort by (varies per endpoint) | `created_at` |
| `order` | string | `asc` or `desc` | `desc` |

All paginated responses include:

```json
{
  "items": [ ],
  "total": 125,
  "limit": 50,
  "offset": 0
}
```

`total` is the total count before pagination, so clients can calculate pages.

## Expiry Format

All `expires_in` fields throughout the API accept three formats:

- **Duration string**: `15m`, `1h`, `6h`, `12h`, `1d`, `3d`, `1w`, `2w`, `1m`, `never`
- **Unix epoch (number)**: absolute expiry as seconds since epoch UTC (e.g. `1772240400`)
- **ISO 8601 (string with `T`)**: absolute expiry (e.g. `2026-03-06T00:00:00Z`)

Detection: if the value is a number → Unix epoch. If it's a string containing `T` → ISO 8601. Otherwise → duration preset.

This applies to: bucket creation/update, dashboard tokens, and upload tokens.

---

## Endpoints

### Health

#### `GET /healthz` — Health check
**Auth:** Public

Returns 200 with basic status. For use by load balancers, Docker healthchecks, orchestrators.

```json
// Response 200
{
  "status": "healthy",
  "uptime_seconds": 86400,
  "db": "ok"
}
```

Returns 503 if the database is unreachable.

---

### API Keys

#### `POST /api/keys` — Create API key
**Auth:** Admin

```json
// Request
{ "name": "claude-agent" }

// Response 201
{
  "key": "cf4_b259367e_15e5492e9d8682b751114705735bf3f7",
  "prefix": "cf4_b259367e",
  "name": "claude-agent",
  "created_at": "2026-02-27T00:00:00Z"
}
```

The full `key` is only returned on creation. After this, only the `prefix` is visible.

#### `GET /api/keys` — List all API keys
**Auth:** Admin

Paginated. Sortable by `name`, `created_at`, `last_used_at`, `total_size`. Returns keys with usage stats (`bucket_count`, `file_count`, `total_size`, `last_used_at`). Never includes the full key secret.

#### `DELETE /api/keys/{prefix}` — Revoke API key
**Auth:** Admin

Revokes the key. Buckets created by the key are preserved.

#### `GET /api/keys/{prefix}/usage` — Detailed key usage
**Auth:** Admin

```json
// Response 200
{
  "prefix": "cf4_b259367e",
  "name": "claude-agent",
  "created_at": "2026-02-27T00:00:00Z",
  "last_used_at": "2026-02-27T12:00:00Z",
  "bucket_count": 5,
  "file_count": 42,
  "total_size": 104857600,
  "total_downloads": 230,
  "buckets": [ ]
}
```

---

### Dashboard Tokens

Dashboard tokens are short-lived JWTs for admin UI access. The admin key holder (typically an LLM) creates one and gives the token to the user. The user's frontend uses it for all API calls without ever seeing the real admin key.

Stateless — the API signs them on creation and verifies the signature on each request. No database storage, no revocation. Keep expiry short.

#### `POST /api/tokens/dashboard` — Create dashboard token
**Auth:** Admin

```json
// Request (all optional)
{
  "expires_in": "1h"
}
```

`expires_in` accepts the same formats as bucket expiry: duration string (`15m`, `1h`, `6h`, `12h`, `1d`), Unix epoch (number), or ISO 8601 string. Default: `1h`.

```json
// Response 201
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expires_at": "2026-02-27T01:00:00Z"
}
```

JWT payload:

```json
{
  "scope": "admin",
  "exp": 1772240400
}
```

Signed with a server-side secret (env var `JWT_SECRET` or derived from admin key).

#### `GET /api/tokens/dashboard/me` — Validate current token
**Auth:** Dashboard token

Frontend calls this on load to check if the session is valid.

```json
// Response 200
{
  "scope": "admin",
  "expires_at": "2026-02-27T01:00:00Z"
}
```

Returns 401 if expired or invalid.

---

### Stats

#### `GET /api/stats` — System stats
**Auth:** Admin

```json
// Response 200
{
  "total_buckets": 25,
  "total_files": 340,
  "total_size": 5368709120,
  "total_keys": 3,
  "total_downloads": 1200,
  "storage_by_owner": [
    {
      "owner": "claude-agent",
      "bucket_count": 10,
      "file_count": 150,
      "total_size": 2147483648
    }
  ]
}
```

---

### Buckets

#### `POST /api/buckets` — Create bucket
**Auth:** API key or admin

```json
// Request
{
  "name": "my-project",
  "description": "Project source files",
  "expires_in": "1w"
}
```

Only `name` is required.

See [Expiry Format](#expiry-format) for accepted values. Default: `1w`.

```json
// Response 201
{
  "id": "abc123defg",
  "name": "my-project",
  "owner": "claude-agent",
  "description": "Project source files",
  "created_at": "2026-02-27T00:00:00Z",
  "expires_at": "2026-03-06T00:00:00Z",
  "file_count": 0,
  "total_size": 0
}
```

`owner` is auto-populated from the API key's name. Admin-created buckets use `"admin"` as owner.

#### `GET /api/buckets` — List buckets
**Auth:** Required. Admin sees all buckets. API key sees only its own.

Paginated. Sortable by `name`, `created_at`, `expires_at`, `last_used_at`, `total_size`.

```json
// Response 200
{
  "items": [ ],
  "total": 25,
  "limit": 50,
  "offset": 0
}
```

Expired buckets are excluded.

#### `GET /api/buckets/{id}` — Get bucket with file listing
**Auth:** Public

```json
// Response 200
{
  "id": "abc123defg",
  "name": "my-project",
  "owner": "claude-agent",
  "description": "Project source files",
  "created_at": "2026-02-27T00:00:00Z",
  "expires_at": "2026-03-06T00:00:00Z",
  "last_used_at": "2026-02-27T12:00:00Z",
  "file_count": 10432,
  "total_size": 49444,
  "files": [ /* first 100 files */ ],
  "has_more_files": true
}
```

The `files` array is capped at 100 items. If `file_count > 100`, `has_more_files` is `true` — use `GET /api/buckets/{id}/files` with pagination to get the full list.

Returns 404 if bucket doesn't exist or has expired.

#### `PATCH /api/buckets/{id}` — Update bucket
**Auth:** Owner or admin

All fields optional, at least one required:

```json
{ "name": "renamed", "description": "new desc", "expires_in": "1m" }
```

#### `DELETE /api/buckets/{id}` — Delete bucket and all files
**Auth:** Owner or admin

Returns 204. Deletes all files from disk.

#### `GET /api/buckets/{id}/zip` — Download bucket as ZIP
**Auth:** Public

Streams a ZIP archive of all files. Sets `Content-Disposition: attachment; filename="bucket-name.zip"`.

#### `GET /api/buckets/{id}/summary` — Plaintext summary
**Auth:** Public

Returns a human/LLM-readable plaintext summary of the bucket and its files. `Content-Type: text/plain`.

---

### Files

#### `GET /api/buckets/{id}/files` — List files in bucket
**Auth:** Public

Paginated. Sortable by `name`, `path`, `size`, `created_at`, `updated_at`, `mime_type`. Returns file metadata only — no file contents.

#### `GET /api/buckets/{id}/files/{path}` — Get file metadata
**Auth:** Public

`{path}` can include slashes (e.g. `src/main.rs`).

```json
// Response 200
{
  "path": "src/main.rs",
  "name": "main.rs",
  "size": 1234,
  "mime_type": "text/x-rust",
  "created_at": "2026-02-27T00:00:00Z",
  "updated_at": "2026-02-27T00:00:00Z"
}
```

#### `GET /api/buckets/{id}/files/{path}/content` — Download file
**Auth:** Public

Returns raw file bytes streamed from disk.

Headers set:
- `Content-Type` — detected from file extension
- `Content-Length` — file size
- `Accept-Ranges: bytes`
- `ETag` — hash of file content or `"{size}-{lastModified}"` (cheap to compute)
- `Last-Modified` — file's `updated_at` timestamp
- `Cache-Control: public, no-cache` — always revalidate via ETag/Last-Modified. Files can be overwritten (re-upload same path), so `max-age` isn't safe. `no-cache` means the browser caches the file but checks with the server before using it — a 304 response is just headers, no body, so it's still fast.

Conditional request support:
- **`If-None-Match`** — compare against `ETag`, return `304 Not Modified` if match
- **`If-Modified-Since`** — compare against `Last-Modified`, return `304 Not Modified` if unchanged

Features:
- **Range requests** — `Range: bytes=0-1023` returns 206 with `Content-Range` header (required for video seeking)
- **`If-Range`** — combine with Range for safe partial re-fetches
- **`?download=true`** — sets `Content-Disposition: attachment` to force download

**Implementation:** Stream from `FileStream` — never read the entire file into memory. For ETag, use `"{size}-{lastModifiedTicks}"` — no need to hash file contents.

#### `PATCH /api/buckets/{id}/files/{path}/content` — Partial file update
**Auth:** Owner, admin, or upload token (`?token=`)

Write bytes to a specific offset within an existing file. Uses `Content-Range` header to specify where.

```bash
# Overwrite bytes 100-199
curl -X PATCH "/api/buckets/abc123/files/data.bin/content" \
  -H "Authorization: Bearer $KEY" \
  -H "Content-Range: bytes 100-199/*" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @patch.bin

# Append to end of file
curl -X PATCH "/api/buckets/abc123/files/log.txt/content" \
  -H "Authorization: Bearer $KEY" \
  -H "Content-Range: bytes */*" \
  -H "X-Append: true" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @new-lines.txt
```

```json
// Response 200
{
  "path": "data.bin",
  "name": "data.bin",
  "size": 2048,
  "mime_type": "application/octet-stream",
  "updated_at": "2026-02-27T12:00:00Z"
}
```

Rules:
- `Content-Range: bytes {start}-{end}/*` — overwrite the specified byte range. File must already exist. Range must not exceed current file size unless extending.
- `X-Append: true` — append request body to end of file, ignoring `Content-Range`.
- Returns 404 if the file doesn't exist (use the upload endpoints to create files).
- Returns 416 if the range is invalid or exceeds file size (unless extending).
- Triggers `file:updated` SignalR event.

**Implementation:** Open `FileStream` with `FileMode.Open`, seek to offset, write. Use file locking (`FileShare.None`) for the duration of the write to prevent concurrent partial updates from corrupting the file.

---

#### `DELETE /api/buckets/{id}/files/{path}` — Delete file
**Auth:** Owner or admin

Returns 204.

---

### Upload

#### `POST /api/buckets/{id}/upload` — Multipart upload
**Auth:** Owner, admin, or upload token (`?token=`)

Upload one or more files via `multipart/form-data`.

Field naming:
- Generic names (`file`, `files`, `upload`, `uploads`, `blob`) → keeps original filename
- Custom field name (e.g. `src/main.rs`) → uses the field name as file path in the bucket

Re-uploading the same path overwrites the existing file.

```bash
# Multiple files
curl -X POST /api/buckets/abc123/upload \
  -H "Authorization: Bearer $KEY" \
  -F "files=@screenshot.png" \
  -F "files=@README.md"

# Custom paths
curl -X POST /api/buckets/abc123/upload \
  -H "Authorization: Bearer $KEY" \
  -F "src/main.rs=@main.rs"
```

```json
// Response 201
{
  "uploaded": [
    {
      "path": "screenshot.png",
      "name": "screenshot.png",
      "size": 48210,
      "mime_type": "image/png",
      "created_at": "2026-02-27T00:00:00Z",
      "updated_at": "2026-02-27T00:00:00Z"
    }
  ]
}
```

**Implementation:** Stream multipart parsing — do not buffer uploads in memory.

#### `PUT /api/buckets/{id}/upload/stream` — Stream upload (single file)
**Auth:** Owner, admin, or upload token (`?token=`)

Upload a single file by streaming the raw request body to disk. No multipart. The entire body IS the file. Best for large files.

Query params:
- `filename` (required) — desired path in bucket
- `token` (optional) — upload token

```bash
curl -X PUT "/api/buckets/abc123/upload/stream?filename=big-video.mp4" \
  -H "Authorization: Bearer $KEY" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @big-video.mp4
```

```json
// Response 201
{
  "path": "big-video.mp4",
  "name": "big-video.mp4",
  "size": 1073741824,
  "mime_type": "video/mp4",
  "created_at": "2026-02-27T00:00:00Z",
  "updated_at": "2026-02-27T00:00:00Z"
}
```

**Implementation:** `await request.Body.CopyToAsync(fileStream)` — one line, zero buffering.

---

### Upload Tokens

#### `POST /api/buckets/{id}/tokens` — Create upload token
**Auth:** Owner or admin

Generates a time-limited token allowing uploads to this bucket without an API key.

```json
// Request (all optional)
{
  "expires_in": "1d",
  "max_uploads": 5
}
```

`expires_in` accepts the same formats as bucket expiry: duration string (`1h`, `6h`, `12h`, `1d`, `1w`), Unix epoch (number), or ISO 8601 string. Default: `1d`.
`max_uploads`: max individual files allowed. Each file in a multipart upload counts as one — a single request with 5 files consumes 5. `null` = unlimited.

```json
// Response 201
{
  "token": "cfu_x1y2z3...",
  "bucket_id": "abc123defg",
  "expires_at": "2026-02-28T00:00:00Z",
  "max_uploads": 5,
  "uploads_used": 0
}
```

Usage: append `?token=cfu_x1y2z3...` to any upload endpoint. No Authorization header needed.

---

### Short URLs

Short URLs are automatically generated for every file on upload. Each file gets a unique 6-char code. The short URL is returned in the upload response and in file metadata.

#### File metadata includes short URL

```json
{
  "path": "src/main.rs",
  "name": "main.rs",
  "size": 1234,
  "mime_type": "text/x-rust",
  "short_code": "xK9mQ2",
  "short_url": "/s/xK9mQ2",
  "created_at": "2026-02-27T00:00:00Z",
  "updated_at": "2026-02-27T00:00:00Z"
}
```

#### `GET /s/{code}` — Resolve short URL
**Auth:** Public

302 redirects to the file content URL (`/api/buckets/{id}/files/{path}/content`).

Returns 404 if the code doesn't exist or the bucket has expired.

#### `DELETE /api/short/{code}` — Delete a short URL
**Auth:** Owner or admin

Removes the short URL mapping. The file itself is not deleted. Returns 204.

---

### Real-Time Events (SignalR)

#### Hub endpoint: `/hub/files`

Uses ASP.NET SignalR for real-time notifications. Clients connect, subscribe to resources via hub methods, and receive events when things change.

**Auth:** Optional. Pass bearer token via query string for authenticated features: `/hub/files?access_token=<jwt or api key>`.

#### Client → Server (Hub Methods)

```csharp
// Subscribe to all changes within a bucket (public — no auth needed)
Task SubscribeToBucket(string bucketId);

// Unsubscribe from a bucket
Task UnsubscribeFromBucket(string bucketId);

// Subscribe to a specific file (public)
Task SubscribeToFile(string bucketId, string path);

// Unsubscribe from a file
Task UnsubscribeFromFile(string bucketId, string path);

// Subscribe to all buckets — new creations, deletions (requires admin auth)
Task SubscribeToAll();

// Unsubscribe from all
Task UnsubscribeFromAll();
```

#### Server → Client (Events)

```csharp
// File events — sent to bucket and file subscribers
Task FileCreated(string bucketId, FileInfo file);
Task FileUpdated(string bucketId, FileInfo file);       // re-upload or metadata change
Task FileDeleted(string bucketId, string path);

// Bucket events — sent to bucket subscribers + global subscribers
Task BucketUpdated(string bucketId, BucketChanges changes);  // name, description, expiry changed
Task BucketDeleted(string bucketId);

// Global events — sent to SubscribeToAll subscribers only (admin)
Task BucketCreated(Bucket bucket);
```

#### Server Implementation

Use SignalR Groups for subscriptions:

```csharp
public class FileHub : Hub
{
    public async Task SubscribeToBucket(string bucketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task SubscribeToFile(string bucketId, string path)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task SubscribeToAll()
    {
        // Verify admin auth from Context.User or query string token
        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
    }
}
```

When a file is uploaded/changed/deleted anywhere in the codebase:

```csharp
// Inject IHubContext<FileHub> and notify
await hubContext.Clients.Group($"bucket:{bucketId}").SendAsync("FileCreated", fileInfo);
await hubContext.Clients.Group($"file:{bucketId}:{path}").SendAsync("FileUpdated", fileInfo);
```

#### Client Usage (JavaScript)

```js
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/files", { accessTokenFactory: () => dashboardToken })
    .withAutomaticReconnect()
    .build();

connection.on("FileCreated", (bucketId, file) => { /* update UI */ });
connection.on("FileUpdated", (bucketId, file) => { /* refresh preview */ });
connection.on("FileDeleted", (bucketId, path) => { /* remove from list */ });
connection.on("BucketUpdated", (bucketId, changes) => { /* update metadata */ });
connection.on("BucketCreated", (bucket) => { /* add to dashboard list */ });

await connection.start();
await connection.invoke("SubscribeToBucket", "abc123defg");
```

**Implementation notes:**
- Public bucket/file subscriptions don't require auth — anyone with the bucket ID can subscribe
- `SubscribeToAll` requires admin auth — verify before adding to the `global` group
- No external message broker needed — in-process SignalR is fine for single-instance deployment
- SignalR handles reconnection, ping/pong, and transport fallback (WebSocket → SSE → long-polling) automatically

---

## Data Models

### Bucket
| Field | Type | Description |
|-------|------|-------------|
| id | string | 10-char alphanumeric, URL-safe, randomly generated |
| name | string | Display name |
| owner | string | API key name that created it |
| description | string? | Optional |
| created_at | datetime | |
| expires_at | datetime? | null = never expires |
| last_used_at | datetime? | Last upload or download |
| file_count | int | Number of files |
| total_size | long | Total bytes |

### FileInfo
| Field | Type | Description |
|-------|------|-------------|
| path | string | Full path in bucket (e.g. `src/main.rs`) |
| name | string | Filename only (e.g. `main.rs`) |
| size | long | Bytes |
| mime_type | string | Detected from extension |
| short_code | string | 6-char code for short URL |
| short_url | string | Full short URL (e.g. `/s/xK9mQ2`) |
| created_at | datetime | |
| updated_at | datetime | |

### ShortUrl
| Field | Type | Description |
|-------|------|-------------|
| code | string | 6-char alphanumeric, unique, primary key |
| bucket_id | string | Owning bucket |
| file_path | string | Path within the bucket |
| created_at | datetime | |

### ApiKey
| Field | Type | Description |
|-------|------|-------------|
| prefix | string | Visible identifier (e.g. `cf4_b259367e`) |
| name | string | Human-readable name |
| created_at | datetime | |
| last_used_at | datetime? | Last API call using this key |
| bucket_count | int | |
| file_count | int | |
| total_size | long | |

---

## Implementation Notes

1. **Never buffer files in memory.** Stream uploads to disk, stream downloads from disk.
2. **Expired buckets** — exclude from listings by default, return 404 on direct access. Background cleanup on configurable interval. Admin can pass `?include_expired=true` on `GET /api/buckets` to see expired buckets pending cleanup.
3. **MIME detection** — use file extension, don't sniff content.
4. **Bucket IDs** — 10-char alphanumeric, URL-safe.
5. **Short URL codes** — 6-char alphanumeric, unique per file. **Cascade delete** — when a file or bucket is deleted, associated short URLs are deleted too.
6. **File storage layout** — `{DataDir}/{bucketId}/{encodedPath}`. URL-safe encoding for special characters in filenames. Lowercase paths on disk to avoid case-sensitivity issues across OS.
7. **Dashboard tokens** — stateless JWTs. Sign with server secret, verify signature + expiry on each request. No DB storage. **Maximum expiry capped at 24 hours** — reject `expires_in` values exceeding 24h.
8. **Upload tokens** — stored in SQLite via EF Core, scoped to a single bucket. Increment `uploads_used` atomically on each upload.
9. **OpenAPI** — auto-generate via Scalar (`Scalar.AspNetCore`).
10. **`last_used_at`** — update on key usage, bucket uploads, and file downloads. Don't update on metadata reads to avoid write amplification.
11. **Native AOT** — all serialization via source generators. No reflection. Test AOT compilation in CI.
12. **SQLite WAL mode** — enable on startup for concurrent read performance.
13. **SignalR** — in-process pub/sub, no external broker needed.
14. **Concurrent uploads to same path** — write to a temp file in the same directory, then atomic rename (`File.Move` with overwrite) on completion. Prevents corrupted files from simultaneous writes. Last writer wins, but the file on disk is always valid.
15. **HEAD requests** — all `GET` endpoints returning file content (`/content`, `/zip`) must support `HEAD` — same headers (`Content-Type`, `Content-Length`, `ETag`) with no body. Verify this works for streaming responses.
16. **ZIP streaming** — `GET /api/buckets/{id}/zip` streams without buffering using `ZipArchive` on the response stream. No hard size limit, but log a warning for buckets >10,000 files or >10GB total.
