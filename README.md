# ⚡ CarbonFiles

A fast, lightweight file-sharing API with bucket-based organization, API key authentication, and real-time WebSocket events. Part of the [Carbungo](https://github.com/carbungo) platform.

Built with ASP.NET Minimal API on .NET 10. Designed to be self-hosted, single-binary, and fast by default.

## Quick Start

```bash
docker compose up -d
```

The API is available at `http://localhost:8080`. Set your admin key via the `CarbonFiles__AdminKey` environment variable.

## Features

- **Bucket-based storage** — organize files into buckets with optional expiration
- **Multi-auth** — admin keys, scoped API keys (`cf4_`), upload tokens (`cfu_`), dashboard JWTs
- **Real-time events** — WebSocket notifications for file and bucket changes
- **Short URLs** — shareable links to any file
- **ZIP downloads** — download entire buckets as archives
- **Stream uploads** — PUT large files without multipart overhead
- **LLM-friendly** — plaintext bucket summaries, content negotiation, clean JSON
- **Client SDKs** — TypeScript, C#, Python, and PowerShell

## Usage

```bash
# Create an API key (admin)
curl -X POST http://localhost:8080/api/keys \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-agent"}'

# Create a bucket
curl -X POST http://localhost:8080/api/buckets \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-project", "expires_in": "1w"}'

# Upload files
curl -X POST http://localhost:8080/api/buckets/$BUCKET_ID/upload \
  -H "Authorization: Bearer $API_KEY" \
  -F "files=@screenshot.png" \
  -F "files=@README.md"

# Download a file
curl http://localhost:8080/api/buckets/$BUCKET_ID/files/screenshot.png/content

# Download via short URL
curl -L http://localhost:8080/s/xK9mQ2

# Get bucket summary (plaintext)
curl http://localhost:8080/api/buckets/$BUCKET_ID/summary
```

## Real-Time Events (WebSocket)

Connect to `/ws/files` for live bucket and file notifications:

```javascript
const ws = new WebSocket(`ws://localhost:8080/ws/files?token=${apiKey}`);

ws.onmessage = (event) => {
  const msg = JSON.parse(event.data);
  // msg.type: FileCreated | FileUpdated | FileDeleted | BucketCreated | BucketUpdated | BucketDeleted
  console.log(msg.type, msg.data);
};

// Subscribe to a specific bucket
ws.send(JSON.stringify({ action: "subscribe", bucket_id: "abc123" }));
```

## Configuration

| Setting | Env Var | Default | Description |
|---------|---------|---------|-------------|
| Admin Key | `CarbonFiles__AdminKey` | *Required* | Admin API key |
| JWT Secret | `CarbonFiles__JwtSecret` | Derived from AdminKey | JWT signing secret |
| Data Dir | `CarbonFiles__DataDir` | `./data` | File storage directory |
| DB Path | `CarbonFiles__DbPath` | `./data/carbonfiles.db` | SQLite database path |
| Max Upload | `CarbonFiles__MaxUploadSize` | `0` (unlimited) | Max upload size in bytes |
| Cleanup Interval | `CarbonFiles__CleanupIntervalMinutes` | `60` | Expired bucket cleanup interval |
| CORS Origins | `CarbonFiles__CorsOrigins` | `*` | Allowed CORS origins |

## Architecture

```
Clients (curl, frontend, SDK, LLM agents)
              │
              │ HTTP / WebSocket
              ▼
┌──────────────────────────────┐
│       CarbonFiles.Api        │
│  Endpoints │ Auth │ WS Hub   │
├──────────────────────────────┤
│   CarbonFiles.Infrastructure │
│  Services │ Dapper │ Storage  │
├──────────┬───────────────────┤
│  SQLite  │    Filesystem     │
└──────────┴───────────────────┘
```

## Client SDKs

**TypeScript:**
```bash
npm install @carbonfiles/client
```

**C# (.NET):**
```bash
dotnet add package CarbonFiles.Client
```

**Python:**
```bash
pip install carbonfiles-client
```

**PowerShell:**
```powershell
Install-Module CarbonFiles
```

📖 See each SDK's README for usage examples.

## Development

```bash
# Prerequisites: .NET 10 SDK
dotnet build
dotnet test
dotnet run --project src/CarbonFiles.Api
```

## OpenAPI

Spec available at `/openapi/v1.json`. Interactive docs at `/scalar` in development mode.

## License

MIT

---

Part of [Carbungo](https://github.com/carbungo) — your cloud, your hardware, your rules.
