# @carbonfiles/client

Handcrafted TypeScript client for the [CarbonFiles](https://github.com/carbungo/carbon-files) file-sharing API. Zero runtime dependencies, works in Node.js 18+ and browsers.

## Installation

```bash
npm install @carbonfiles/client
```

## Quick Start

```typescript
import { CarbonFilesClient } from "@carbonfiles/client";

const cf = new CarbonFilesClient("https://files.example.com", "cf4_your_api_key");

// Create a bucket
const bucket = await cf.buckets.create({ name: "my-bucket", expires_in: "30d" });

// Upload a file
const file = new Uint8Array([/* ... */]);
const upload = await cf.buckets[bucket.id].files.upload(file, "photo.jpg");

// Download a file
const response = await cf.buckets[bucket.id].files["photo.jpg"].download();

// Delete a bucket
await cf.buckets[bucket.id].delete();
```

## Fluent Resource API

Access resources using bracket notation for a natural, chainable API:

```typescript
// Buckets
cf.buckets.create({ name: "docs" });
cf.buckets.list({ limit: 10, sort: "name" });
cf.buckets["bucket-id"].get();
cf.buckets["bucket-id"].update({ description: "Updated" });
cf.buckets["bucket-id"].delete();
cf.buckets["bucket-id"].getSummary();
cf.buckets["bucket-id"].downloadZip();

// Files
cf.buckets["bucket-id"].files.list({ limit: 50 });
cf.buckets["bucket-id"].files.listDirectory("images/");
cf.buckets["bucket-id"].files.listTree({ delimiter: "/" });
cf.buckets["bucket-id"].files["report.pdf"].getMetadata();
cf.buckets["bucket-id"].files["report.pdf"].download();
cf.buckets["bucket-id"].files["report.pdf"].verify();
cf.buckets["bucket-id"].files["report.pdf"].delete();

// API Keys
cf.keys.create({ name: "ci-deploy" });
cf.keys.list();
cf.keys["cf4_abc"].revoke();
cf.keys["cf4_abc"].getUsage();

// Short URLs
cf.shortUrls["abc123"].delete();

// Dashboard
cf.dashboard.createToken({ expires_in: "1h" });
cf.dashboard.getCurrentUser();

// Stats & Health
cf.stats.get();
cf.health.check();
```

## Upload with Progress

Track upload progress with a callback:

```typescript
const data = new Uint8Array(1024 * 1024); // 1MB

const result = await cf.buckets["bucket-id"].files.upload(data, "large-file.bin", {
  onProgress: (progress) => {
    console.log(`${progress.bytesSent}/${progress.totalBytes} (${progress.percentage}%)`);
  },
});
```

Upload with a scoped upload token (no API key required):

```typescript
const result = await cf.buckets["bucket-id"].files.upload(data, "file.txt", {
  uploadToken: "cfu_upload_token",
});
```

## Content-Addressed Storage (CAS)

Uploaded files include SHA-256 hashes and deduplication info:

```typescript
const upload = await cf.buckets["bucket-id"].files.upload(data, "file.txt");
console.log(upload.uploaded[0].sha256);       // content hash
console.log(upload.uploaded[0].deduplicated);  // true if content already existed

// Verify file integrity
const verify = await cf.buckets["bucket-id"].files["file.txt"].verify();
console.log(verify.valid);        // true if stored_hash === computed_hash
console.log(verify.stored_hash);
console.log(verify.computed_hash);
```

Bucket details include unique content metrics:

```typescript
const detail = await cf.buckets["bucket-id"].get();
console.log(detail.unique_content_count);  // distinct SHA-256 hashes
console.log(detail.unique_content_size);   // total size of unique content
```

## File Modification

Patch a byte range or append to a file:

```typescript
// Replace bytes 0-9 in a 100-byte file
const patch = new Uint8Array([/* replacement bytes */]);
await cf.buckets["bucket-id"].files["data.bin"].patch(patch, 0, 9, 100);

// Append to a file
const extra = new Uint8Array([/* more data */]);
await cf.buckets["bucket-id"].files["log.txt"].append(extra);
```

## Directory Browsing

Two modes for listing files hierarchically:

```typescript
// Flat directory listing
const dir = await cf.buckets["bucket-id"].files.listDirectory("images/", {
  limit: 50,
});
console.log(dir.files);    // BucketFile[]
console.log(dir.folders);  // string[]

// S3-style tree listing with cursor pagination
const tree = await cf.buckets["bucket-id"].files.listTree({
  delimiter: "/",
  prefix: "docs/",
  limit: 100,
});
console.log(tree.directories);  // DirectoryEntry[]
console.log(tree.files);        // BucketFile[]
if (tree.cursor) {
  // Fetch next page
  const next = await cf.buckets["bucket-id"].files.listTree({
    delimiter: "/",
    prefix: "docs/",
    cursor: tree.cursor,
  });
}
```

## Authentication

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

```typescript
// API key
const cf = new CarbonFilesClient("https://files.example.com", "cf4_your_key");

// Options constructor with custom fetch
const cf = new CarbonFilesClient({
  baseUrl: "https://files.example.com",
  apiKey: "cf4_your_key",
  fetch: customFetch,
});
```

## Error Handling

All API errors throw `CarbonFilesError` with structured error info:

```typescript
import { CarbonFilesClient, CarbonFilesError } from "@carbonfiles/client";

try {
  await cf.buckets["nonexistent"].get();
} catch (e) {
  if (e instanceof CarbonFilesError) {
    console.log(e.status); // 404
    console.log(e.error);  // "Bucket not found"
    console.log(e.hint);   // "Check the bucket ID"
  }
}
```

## Upload Tokens

Create scoped upload tokens for public/limited uploads:

```typescript
const token = await cf.buckets["bucket-id"].tokens.create({
  expires_in: "1h",
  max_uploads: 10,
});
console.log(token.token); // "cfu_..."
```

## Links

- [CarbonFiles repository](https://github.com/carbungo/carbon-files)
- [npm package](https://www.npmjs.com/package/@carbonfiles/client)
