# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CarbonFiles is a file-sharing API with bucket-based organization, API key authentication, and real-time SignalR events. Built with ASP.NET Minimal API on .NET 10, published as a Native AOT binary with compiled EF Core models (no precompiled queries — blocked by dotnet/efcore#35494).

## Build & Development Commands

```bash
# Build
dotnet build

# Run locally (serves on http://localhost:5000 by default)
dotnet run --project src/CarbonFiles.Api

# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/CarbonFiles.Api.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~BucketEndpointTests.CreateBucket"
```

### EF Core Migration Workflow

After changing entity models, all three steps are required:

```bash
# 1. Create migration
dotnet ef migrations add <Name> \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api

# 2. Apply migration (dev only; production uses the Migrator project)
dotnet ef database update \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api

# 3. Regenerate compiled models (REQUIRED — AOT/trimming breaks without this)
dotnet ef dbcontext optimize \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api
```

### Docker

```bash
docker compose up -d          # Run on port 8080
# Admin key: CarbonFiles__AdminKey env var
```

## Architecture

```
CarbonFiles.Api          → Endpoints, auth middleware, SignalR hub, JSON serialization
CarbonFiles.Core         → Domain models, interfaces, configuration, utilities
CarbonFiles.Infrastructure → Services, EF Core DbContext, auth implementation
CarbonFiles.Migrator     → Standalone migration runner (used in Docker entrypoint)
```

### Key Patterns

- **Minimal API endpoints**: No controllers. Each feature has a static `Map*Endpoints()` extension method in `src/CarbonFiles.Api/Endpoints/` registered in `Program.cs`.
- **Service layer, no repository pattern**: Services in `Infrastructure/Services/` use `CarbonFilesDbContext` directly. All registered via `DependencyInjection.AddInfrastructure()`.
- **Source-generated JSON**: `CarbonFilesJsonContext` in `Api/Serialization/` uses `[JsonSerializable]` attributes for AOT-compatible serialization. New request/response types must be added to this context.
- **Compiled EF Core models**: Located in `Infrastructure/Data/CompiledModels/`. Must be regenerated via `dotnet ef dbcontext optimize` after any entity/model changes.
- **Database initialization**: Uses raw SQL DDL (`CREATE TABLE IF NOT EXISTS`) in `DatabaseInitializer` instead of EF migrations at runtime, because `Migrate()` and `EnsureCreated()` are trimmed away.
- **Filesystem blob storage**: Files stored at `./data/{bucketId}/{url-encoded-path}`. Managed by singleton `FileStorageService`.

### Authentication

`AuthMiddleware` extracts a Bearer token and resolves it via `IAuthService` into an `AuthContext` stored in `HttpContext.Items`. Four token types:

- **Admin key** — env var `CarbonFiles__AdminKey`, full access
- **API keys** — `cf4_` prefix, SHA-256 hashed, scoped to own buckets, 30s cache
- **Dashboard JWT** — HMAC-SHA256, 24h max, admin-level access
- **Upload tokens** — `cfu_` prefix, scoped to a single bucket with optional rate limit

### Real-Time (SignalR)

Hub at `/hub/files`. Group-based: `bucket:{id}`, `file:{id}:{path}`, `global`. JSON protocol only (AOT constraint). `HubNotificationService` implements `INotificationService` to push events (FileCreated, FileUpdated, FileDeleted, BucketCreated, BucketUpdated, BucketDeleted).

## Testing

- **Framework**: xUnit + FluentAssertions
- **Integration tests** in `tests/CarbonFiles.Api.Tests/` use `WebApplicationFactory<Program>` with in-memory SQLite and a temp directory for file storage (see `TestFixture.cs`)
- **Test naming**: `MethodName_Scenario_ExpectedResult`
- **CancellationToken**: Pass `TestContext.Current.CancellationToken` in all async test calls
- **Fixture**: `TestFixture` provides `CreateAdminClient()`, `CreateApiKeyClientAsync()`, `CreateAuthenticatedClient(token)`, and `GetServerUrl()` for SignalR tests

## Conventions

- Snake_case JSON naming (`PropertyNamingPolicy.SnakeCaseLower`), nulls omitted
- API error responses: `{"error": "...", "hint": "..."}`
- ID generation: crypto-random via `IdGenerator` — 10-char bucket IDs, 6-char short codes, `cf4_`/`cfu_` prefixed keys
- Expiry parsing: `ExpiryParser` handles duration strings (1h, 1d, 1w, 30d), Unix timestamps, and ISO 8601
- SQLite with WAL mode for concurrent access
- Pagination params: `limit`, `offset`, `sort`, `order`

## Client SDKs

Four client SDKs under `clients/`:

| Language | Package | Generator | Dir |
|---|---|---|---|
| TypeScript | `@carbonfiles/client` (npm) | Hey API (`@hey-api/openapi-ts`) | `clients/typescript/` |
| C# | `CarbonFiles.Client` (NuGet) | Refitter (MSBuild) | `clients/csharp/` |
| Python | `carbonfiles-client` (PyPI) | openapi-python-client | `clients/python/` |
| PowerShell | `CarbonFiles` (PSGallery) | Hand-crafted | `clients/powershell/` |

### Regenerating Clients Locally

```bash
./scripts/export-openapi.sh openapi.json
# TypeScript
cp openapi.json clients/typescript/ && cd clients/typescript && npm run generate && npm run build
# C#
cp openapi.json clients/csharp/ && dotnet build clients/csharp/ -c Release
# Python
cp openapi.json clients/python/ && clients/python/generate.sh
```

Publishing is automated via `.github/workflows/publish-clients.yml` on GitHub Release.
