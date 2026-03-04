# Native AOT Re-enablement Design

## Problem

Native AOT was disabled (commit `fbc0951`) because EF Core's `--precompile-queries` can't handle parameterized LINQ lambdas (dotnet/efcore#35494, still in backlog). The codebase is 95% AOT-ready: source-generated JSON, compiled models, raw SQL schema init, separate migrator.

## Approach

Re-enable `PublishAot` using compiled models only (no `--precompile-queries`). EF Core queries are interpreted at runtime via the expression interpreter. Add Docker-based E2E tests that build the real AOT binary and run HTTP requests against it.

## Changes

### 1. Re-enable AOT in API csproj

- Add `<PublishAot>true</PublishAot>`
- Remove `<PublishSingleFile>true</PublishSingleFile>` (incompatible with AOT)
- Keep `<PublishTrimmed>true</PublishTrimmed>`
- Regenerate compiled models with `dotnet ef dbcontext optimize --nativeaot`

### 2. Update Dockerfile for AOT

- Install native AOT build deps (clang, zlib) in build stage
- Publish with `-r linux-x64` (RID required for AOT)
- Switch runtime to `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble` (no .NET runtime needed for AOT binary)
- Keep aspnet runtime for migrator (it's a regular .NET DLL)

### 3. Create E2E test project

New project: `tests/CarbonFiles.E2E.Tests/`

- xUnit project that builds/starts Docker container, runs HTTP requests against port 8080
- Tests: health check, CRUD buckets, upload/download files, API keys, error responses
- Mirrors existing `AotSmokeTests` but against the actual AOT-published binary
- Test fixture manages container lifecycle via docker compose

### 4. Add docker-compose.e2e.yml

- Builds from local Dockerfile
- Exposes dynamic port
- Sets admin key and temp data dir
- Used by E2E test fixture

### 5. Update CI workflow

- Add `e2e` job that builds Docker image and runs E2E tests
- Runs after `build-and-test`

### 6. Fix AOT runtime bugs

Fix any issues that surface: reflection not covered by source generators, trim warnings that become real failures under AOT.

## Explicitly skipped

- No `--precompile-queries` (blocked upstream)
- No service layer or LINQ query changes
- No Dapper/raw SQL rewrites
