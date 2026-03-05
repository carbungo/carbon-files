# Unified CarbonFiles Platform Documentation — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create unified, LLM-consumable documentation for the CarbonFiles platform (API + Dashboard) that enables any LLM to use CarbonFiles as a competent operator.

**Architecture:** Five deliverables: (1) `llms.txt` — dense API reference with every endpoint, SDK/CLI examples, and workflow recipes; (2) `llms-full.txt` — extended version with internals; (3) `docs/DEPLOYMENT.md` — production deployment guide; (4) dashboard `llms.txt` update in files-ui repo; (5) README updates in both repos.

**Tech Stack:** Markdown documentation, git, gh CLI for cloning files-ui

---

## Preconditions

- Working directory: `/home/carbon/carbon-files`
- files-ui repo will be cloned to `/home/carbon/files-ui` (Task 4)
- All endpoint shapes derived from actual code exploration (completed during design phase)

---

### Task 1: Create `llms.txt` — Primary LLM Reference

**Files:**
- Create: `llms.txt` (repo root)

**Context:** This is the primary doc an LLM reads. Must be under 15KB. Dense, scannable, every endpoint documented with shapes. SDK examples pulled from actual SDK READMEs.

**Step 1: Write `llms.txt`**

Create the file with these sections in order:

1. **Header** — What CarbonFiles is, one paragraph
2. **Quick Start** — Base URL, auth key, first API call (create bucket + upload)
3. **Authentication** — All 4 methods with exact header/param formats
4. **URL Conventions** — When to use dashboard URLs vs API URLs vs short URLs
5. **Every Endpoint** grouped by category:
   - Health: `GET /healthz`
   - Buckets: POST/GET/GET/{id}/PATCH/DELETE + summary + zip
   - Files: list (flat + tree + ls), metadata, content, verify, delete, patch/append
   - Uploads: multipart POST + stream PUT
   - Upload Tokens: POST create
   - API Keys: POST/GET/DELETE + usage
   - Dashboard Tokens: POST create + GET validate
   - Stats: GET
   - Short URLs: GET redirect + DELETE
   - SignalR Hub: connection, subscribe methods, event types

   Each endpoint: `METHOD /path` — Auth level — Request body shape — Response shape (JSON with types)

6. **Common Workflows** — Step-by-step recipes:
   - Upload a file and get a share link
   - Create bucket + generate upload token + share
   - Download bucket as ZIP
   - Set up real-time notifications
   - Verify file integrity
   - Browse directory tree
   - Generate dashboard login URL

7. **SDK Quick Reference** — One-liner examples for each SDK (TypeScript, C#, Python, PowerShell) and CLI for common ops
8. **Error Handling** — Error shape, status codes
9. **Content-Addressable Storage** — What it means for users (dedup, SHA256, verify)
10. **File Serving Behavior** — Content negotiation, Range requests, ETag/304, Cache-Control

**Key data for endpoint shapes** (from code exploration):

- Bucket: `{ id, name, owner, description, created_at, expires_at, last_used_at, file_count, total_size }`
- BucketDetail adds: `{ unique_content_count, unique_content_size, files[], has_more_files }`
- BucketFile: `{ path, name, size, mime_type, short_code, short_url, sha256, created_at, updated_at }`
- UploadedFile: BucketFile + `{ deduplicated }`
- Paginated: `{ items[], total, limit, offset }`
- FileTree: `{ prefix, delimiter, directories[{path, file_count, total_size}], files[], total_files, total_directories, cursor }`
- DirectoryListing: `{ files[], folders[], total_files, total_folders, limit, offset }`
- VerifyResponse: `{ path, stored_hash, computed_hash, valid }`
- Error: `{ error, hint }`
- Stats: `{ total_buckets, total_files, total_size, total_keys, total_downloads, storage_by_owner[{owner, bucket_count, file_count, total_size}] }`
- ApiKey (create): `{ key, prefix, name, created_at }`
- ApiKey (list item): `{ prefix, name, created_at, last_used_at, bucket_count, file_count, total_size }`
- ApiKeyUsage: list item + `{ total_downloads, buckets[] }`
- UploadToken: `{ token, bucket_id, expires_at, max_uploads, uploads_used }`
- DashboardToken: `{ token, expires_at }`
- DashboardTokenInfo: `{ scope, expires_at }`
- Health: `{ status, uptime_seconds, db }`

**SDK examples** (from actual READMEs):

TypeScript:
```typescript
import { CarbonFilesClient } from "@carbonfiles/client";
const cf = new CarbonFilesClient("https://files.example.com", "cf4_your_api_key");
const bucket = await cf.buckets.create({ name: "my-bucket", expires_in: "30d" });
await cf.buckets[bucket.id].files.upload(file, "photo.jpg");
```

C#:
```csharp
var client = new CarbonFilesClient("https://files.example.com", "cf4_your_api_key");
var bucket = await client.Buckets.CreateAsync(new CreateBucketRequest { Name = "my-bucket" });
await client.Buckets[bucket.Id].Files.UploadFileAsync("/path/to/photo.jpg");
```

Python:
```python
from carbonfiles import CarbonFiles
cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")
bucket = cf.buckets.create("my-bucket", expires="30d")
cf.buckets[bucket.id].files.upload("/path/to/photo.jpg")
```

PowerShell:
```powershell
Connect-CfServer -Uri "https://files.example.com" -Token "cf4_your_api_key"
New-CfBucket -Name "my-bucket" -ExpiresIn "30d"
Send-CfFile -BucketId $bucket.Id -FilePath ./photo.jpg
```

CLI:
```bash
cf config set-url https://files.example.com
cf config set-key cf4_your_api_key
cf bucket create --name my-bucket --expires 30d
cf file upload $BUCKET_ID ./photo.jpg
```

**Step 2: Verify file size is under 15KB**

Run: `wc -c llms.txt`
Expected: Under 15360 bytes. If over, trim verbose examples while keeping all endpoint shapes.

**Step 3: Commit**

```bash
git add llms.txt
git commit -m "docs: add llms.txt — unified LLM reference for CarbonFiles platform"
```

---

### Task 2: Create `llms-full.txt` — Extended Reference

**Files:**
- Create: `llms-full.txt` (repo root)

**Context:** Extended version for LLMs working ON the codebase. Includes everything from llms.txt plus internals. No size limit but keep it structured.

**Step 1: Write `llms-full.txt`**

Start with a note: "This extends llms.txt with codebase internals. Read llms.txt first for the API reference."

Add these sections:

1. **Database Schema** — All 6 tables with columns, types, indexes:
   - Buckets (Id PK, Name, Owner, OwnerKeyPrefix, Description, CreatedAt, ExpiresAt, LastUsedAt, FileCount, TotalSize, DownloadCount)
   - ContentObjects (Hash PK, Size, DiskPath, RefCount, CreatedAt) + orphan partial index
   - Files (BucketId+Path composite PK, Name, Size, MimeType, ShortCode, ContentHash, CreatedAt, UpdatedAt) + indexes
   - ApiKeys (Prefix PK, HashedSecret, Name, CreatedAt, LastUsedAt)
   - ShortUrls (Code PK, BucketId, FilePath, CreatedAt)
   - UploadTokens (Token PK, BucketId, ExpiresAt, MaxUploads, UploadsUsed, CreatedAt)

2. **CAS Internals**:
   - Storage layout: `./data/content/ab/cd/abcdef...` (2-level sharding like Git)
   - Upload pipeline: stream to tmp → compute SHA-256 inline → check dedup → move or increment RefCount
   - Ref counting: increment on new file referencing hash, decrement on file delete/update
   - Orphan cleanup: `RefCount <= 0 AND CreatedAt < NOW - 1h` → delete from disk + DB
   - Legacy files: `ContentHash = NULL` → stored at `./data/{bucketId}/{encoded-path}`

3. **Architecture**:
   - Project structure: Api / Core / Infrastructure / Migrator
   - Middleware pipeline: ForwardedHeaders → RequestLogging → CORS → AuthMiddleware → Endpoints
   - DI registration: manual property binding (no reflection for AOT)
   - Service responsibility map with singleton/scoped lifetimes

4. **Auth Flow Internals**:
   - AuthMiddleware resolution priority: AdminKey → API key (cf4_ prefix) → Dashboard JWT → Public
   - API key: cf4_{8hex}_{32hex}, prefix is DB lookup, secret SHA-256 hashed, 30s cache
   - JWT: key = SHA256(AdminKey), HMAC-SHA256, 24h max, 30s clock skew
   - Upload token: validated inline in endpoints, 2min cache, atomic usage increment

5. **Caching Strategy**:
   - Per-domain TTLs: bucket 10min, file 5min, short URL 10min, upload token 2min, stats 5min, API key 30s
   - Eager invalidation on mutations, TTLs as safety nets
   - Bulk invalidation on bucket delete

6. **Background Services**:
   - CleanupService: expired buckets + orphaned content, runs every CleanupIntervalMinutes
   - DatabaseHealthService: PRAGMA quick_check every 60min, WAL checkpoint on shutdown

7. **Build/CI**:
   - Native AOT: dotnet publish -c Release -r linux-x64
   - Docker: multi-stage, SDK for build, aspnet:10.0-noble for runtime (Migrator needs .NET)
   - Entrypoint: run migrator then API binary
   - SQLite PRAGMAs: journal_mode=WAL, synchronous=NORMAL, wal_autocheckpoint=1000

8. **Dashboard Integration**:
   - Dashboard is a Next.js 16 app (Bun, TypeScript, Tailwind) in separate repo
   - Auth flow: admin creates dashboard token → user visits `/?token=JWT` → dashboard validates via `/api/tokens/dashboard/me` → sets `cf-auth-token` cookie
   - SSR data fetching: dashboard server calls API with token from cookie
   - Internal vs public URLs: dashboard may use internal API URL for SSR, public URL for browser

**Step 2: Commit**

```bash
git add llms-full.txt
git commit -m "docs: add llms-full.txt — extended codebase reference for LLMs"
```

---

### Task 3: Create `docs/DEPLOYMENT.md`

**Files:**
- Create: `docs/DEPLOYMENT.md`

**Step 1: Write deployment guide**

Sections:

1. **Quick Start** — docker compose up with both API and dashboard
2. **Docker Compose** — Full example with:
   - API service (carbonfiles-api): port 8080, volumes, env vars
   - Dashboard service (carbonfiles-dash): port 3000, env vars (NEXT_PUBLIC_API_URL, INTERNAL_API_URL)
   - Shared network
   - Named volumes for data persistence
3. **Environment Variables** — Table for both services:
   - API: AdminKey, JwtSecret, DataDir, DbPath, MaxUploadSize, CleanupIntervalMinutes, CorsOrigins, EnableScalar
   - Dashboard: NEXT_PUBLIC_API_URL (browser-facing), INTERNAL_API_URL (SSR), PORT
4. **Reverse Proxy (Traefik)** — Docker labels example with:
   - API on `files.example.com`
   - Dashboard on `dash.example.com`
   - Automatic TLS via Let's Encrypt
   - WebSocket support for SignalR
5. **TLS Setup** — Traefik ACME configuration
6. **Health Checks** — Docker healthcheck for API (`/healthz`), dashboard (`/api/version`)
7. **Production Checklist**:
   - Change AdminKey from default
   - Set CorsOrigins to specific domains
   - Set MaxUploadSize
   - Configure backup for ./data volume
   - Set JwtSecret explicitly (separate from AdminKey)
   - Enable Traefik dashboard only on internal network

**Step 2: Commit**

```bash
git add docs/DEPLOYMENT.md
git commit -m "docs: add DEPLOYMENT.md — production deployment guide"
```

---

### Task 4: Update Dashboard `llms.txt`

**Files:**
- Clone: `carbungo/files-ui` to `/home/carbon/files-ui`
- Modify: `files-ui/public/llms.txt` (or wherever the current llms.txt lives)

**Step 1: Clone files-ui repo**

```bash
cd /home/carbon && gh repo clone carbungo/files-ui
```

**Step 2: Find and read current dashboard llms.txt**

```bash
find /home/carbon/files-ui -name "llms.txt" -o -name "llms*.txt"
```

Read the current content to understand what exists.

**Step 3: Replace with slim dashboard-focused version**

Content should include:
- Header: "CarbonFiles Dashboard — the official web UI for CarbonFiles"
- Point to main API docs: "For complete API reference, see the carbon-files repo llms.txt"
- Dashboard routes with descriptions:
  - `/` — Landing (bucket ID input or login)
  - `/?token=JWT` — Auto-login
  - `/dashboard` — Admin dashboard
  - `/dashboard/buckets/{id}` — Bucket detail
  - `/dashboard/keys` — API key management
  - `/buckets/{id}` — Public bucket view
  - `/buckets/{id}/files/{path}` — File detail/preview
  - `/buckets/{id}/upload?token=TOKEN` — Upload page
  - `/auth/set-token` — Token validation + cookie setter
- Auth flow: token → `/auth/set-token` → validates via API → sets cookie → redirects to `/dashboard`
- Note about URL conventions: dashboard URLs are for humans, API URLs are for code

**Step 4: Commit in files-ui repo**

```bash
cd /home/carbon/files-ui
git add -A
git commit -m "docs: update llms.txt — point to unified carbon-files docs"
```

---

### Task 5: Update READMEs

**Files:**
- Modify: `README.md` (carbon-files repo root)
- Modify: `/home/carbon/files-ui/README.md`

**Step 1: Update carbon-files README**

Add after the "Features" section or update existing content:
- Mention the dashboard: "Pair with [CarbonFiles Dashboard](https://github.com/carbungo/files-ui) for a web UI"
- Mention CLI: "Use [cf CLI](https://github.com/carbungo/carbon-files-cli) for command-line access"
- Add LLM docs reference: "See [llms.txt](./llms.txt) for LLM-consumable API reference"
- Mention all 4 SDKs with links to their dirs

**Step 2: Update files-ui README**

Add/update:
- "Official dashboard for [CarbonFiles](https://github.com/carbungo/carbon-files)"
- Link to main repo's llms.txt for API docs
- Note that deployment docs are in the main repo

**Step 3: Commit both**

```bash
cd /home/carbon/carbon-files
git add README.md
git commit -m "docs: update README — add dashboard, CLI, and llms.txt references"

cd /home/carbon/files-ui
git add README.md
git commit -m "docs: update README — link to carbon-files as canonical source"
```

---

## Execution Order

Tasks 1-3 are independent (all in carbon-files repo, different files) and can run in parallel.
Task 4 depends on nothing but needs gh CLI access.
Task 5 depends on Tasks 1-3 being done (references llms.txt).

Recommended: Run Tasks 1, 2, 3 in parallel, then Task 4, then Task 5.
