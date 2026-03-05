# CarbonFiles.Client

Handcrafted C# client for the [CarbonFiles](https://github.com/carbungo/carbon-files) file-sharing API. Wraps `HttpClient` with a fluent, resource-scoped API featuring upload progress callbacks, cancellation support, and real-time SignalR events.

## Installation

```bash
dotnet add package CarbonFiles.Client
```

Targets `net10.0` and `netstandard2.0`.

## Quick Start

```csharp
using CarbonFiles.Client;
using CarbonFiles.Client.Models;

var client = new CarbonFilesClient("https://files.example.com", "cf4_your_api_key");

// Create a bucket
var bucket = await client.Buckets.CreateAsync(new CreateBucketRequest
{
    Name = "my-bucket",
    Description = "Project assets",
    ExpiresIn = "30d"
});

Console.WriteLine($"Created bucket: {bucket.Id}");
```

### Bring your own HttpClient

```csharp
var client = new CarbonFilesClient(new CarbonFilesClientOptions
{
    BaseAddress = new Uri("https://files.example.com"),
    ApiKey = "cf4_your_api_key",
    HttpClient = myHttpClient,       // optional
    JsonOptions = myJsonOptions      // optional
});
```

## Fluent API

All operations are organized as a resource tree accessed via properties and indexers:

```csharp
// Buckets
var buckets = await client.Buckets.ListAsync();
var detail = await client.Buckets["bucket-id"].GetAsync();
await client.Buckets["bucket-id"].DeleteAsync();

// Files within a bucket
var files = await client.Buckets["bucket-id"].Files.ListAsync();
var metadata = await client.Buckets["bucket-id"].Files["path/to/file.txt"].GetMetadataAsync();
var stream = await client.Buckets["bucket-id"].Files["path/to/file.txt"].DownloadAsync();

// Admin operations
var key = await client.Keys.CreateAsync(new CreateApiKeyRequest { Name = "ci-agent" });
var stats = await client.Stats.GetAsync();
var usage = await client.Keys["cf4_prefix"].GetUsageAsync();
```

## Uploads

### From a file path

```csharp
var result = await client.Buckets["bucket-id"].Files.UploadFileAsync(
    "/path/to/photo.jpg",
    ct: cancellationToken);
```

The filename is derived from the path automatically. Override it if needed:

```csharp
await client.Buckets["bucket-id"].Files.UploadFileAsync(
    "/path/to/photo.jpg",
    filename: "renamed.jpg");
```

### From any stream

```csharp
using var stream = File.OpenRead("photo.jpg");

var result = await client.Buckets["bucket-id"].Files.UploadAsync(
    stream, "photo.jpg", ct: cancellationToken);
```

### From a byte array

```csharp
var data = Encoding.UTF8.GetBytes("hello world");
await client.Buckets["bucket-id"].Files.UploadAsync(data, "hello.txt");
```

### With progress tracking

All upload methods support progress callbacks:

```csharp
await client.Buckets["bucket-id"].Files.UploadFileAsync(
    "/path/to/large-file.zip",
    progress: new Progress<UploadProgress>(p =>
        Console.WriteLine($"{p.BytesSent}/{p.TotalBytes} bytes ({p.Percentage}%)")),
    ct: cancellationToken);
```

### With an upload token

```csharp
await client.Buckets["bucket-id"].Files.UploadAsync(
    stream, "file.txt",
    uploadToken: "cfu_your_upload_token");
```

## Pagination

All list endpoints return a `PaginatedResponse<T>` and accept a `PaginationOptions` input:

```csharp
// Paginate through buckets
var page = await client.Buckets.ListAsync(new PaginationOptions
{
    Limit = 20,
    Offset = 0,
    Sort = "created_at",
    Order = "desc"
});

Console.WriteLine($"Showing {page.Items.Count} of {page.Total} buckets");

foreach (var bucket in page.Items)
    Console.WriteLine($"  {bucket.Id}: {bucket.Name}");

// Next page
var nextPage = await client.Buckets.ListAsync(new PaginationOptions
{
    Limit = 20,
    Offset = page.Offset + page.Limit
});
```

The same pattern applies to all paginated endpoints:

| Method | Returns |
|--------|---------|
| `client.Buckets.ListAsync(pagination?)` | `PaginatedResponse<Bucket>` |
| `client.Buckets["id"].Files.ListAsync(pagination?)` | `PaginatedResponse<BucketFile>` |
| `client.Keys.ListAsync(pagination?)` | `PaginatedResponse<ApiKeyListItem>` |

## Real-Time Events (SignalR)

```csharp
// Register event handlers
client.Events.OnFileCreated((bucketId, file) =>
{
    Console.WriteLine($"New file in {bucketId}: {file.Name}");
    return Task.CompletedTask;
});

client.Events.OnBucketDeleted(bucketId =>
{
    Console.WriteLine($"Bucket deleted: {bucketId}");
    return Task.CompletedTask;
});

// Connect and subscribe
await client.Events.ConnectAsync();
await client.Events.SubscribeToBucketAsync("bucket-id");

// Later...
await client.Events.DisconnectAsync();
```

Available events: `OnFileCreated`, `OnFileUpdated`, `OnFileDeleted`, `OnBucketCreated`, `OnBucketUpdated`, `OnBucketDeleted`.

## File Operations

```csharp
var bucket = client.Buckets["bucket-id"];

// List files (paginated)
var files = await bucket.Files.ListAsync(new PaginationOptions { Limit = 20 });

// Directory listing
var dir = await bucket.Files.ListDirectoryAsync("docs/");

// Tree listing (S3-style delimiter mode)
var tree = await bucket.Files.ListTreeAsync(delimiter: "/", prefix: "docs/");
foreach (var d in tree.Directories)
    Console.WriteLine($"  {d.Path} ({d.FileCount} files, {d.TotalSize} bytes)");

// Verify file integrity (CAS files)
var verify = await bucket.Files["readme.md"].VerifyAsync();
Console.WriteLine($"Valid: {verify.Valid}");

// Download
var stream = await bucket.Files["readme.md"].DownloadAsync();

// Delete
await bucket.Files["old-file.txt"].DeleteAsync();

// Append to file
await bucket.Files["log.txt"].AppendAsync(appendStream);

// Patch with byte range
await bucket.Files["data.bin"].PatchAsync(patchStream, rangeStart: 0, rangeEnd: 99, totalSize: 1000);

// Bucket summary (plaintext)
var summary = await bucket.GetSummaryAsync();

// Download entire bucket as ZIP
var zip = await bucket.DownloadZipAsync();
```

## Authentication

Pass the token when constructing the client:

```csharp
// API key
var client = new CarbonFilesClient("https://files.example.com", "cf4_your_api_key");

// Admin key
var client = new CarbonFilesClient("https://files.example.com", "your-admin-key");
```

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

## Error Handling

API errors throw `CarbonFilesException`:

```csharp
try
{
    await client.Buckets["nonexistent"].GetAsync();
}
catch (CarbonFilesException ex)
{
    Console.WriteLine($"HTTP {(int)ex.StatusCode}: {ex.Error}");
    if (ex.Hint != null)
        Console.WriteLine($"Hint: {ex.Hint}");
}
```

## API Reference

### `client.Buckets`
| Method | Description |
|--------|-------------|
| `.CreateAsync(request)` | Create bucket |
| `.ListAsync(pagination?, includeExpired?)` | List buckets |
| `["id"].GetAsync()` | Get bucket details |
| `["id"].UpdateAsync(request)` | Update bucket |
| `["id"].DeleteAsync()` | Delete bucket |
| `["id"].GetSummaryAsync()` | Plaintext summary |
| `["id"].DownloadZipAsync()` | Download as ZIP |

### `client.Buckets["id"].Files`
| Method | Description |
|--------|-------------|
| `.ListAsync(pagination?)` | List files |
| `.ListTreeAsync(delimiter?, prefix?, limit?, cursor?)` | Tree listing (S3-style) |
| `.ListDirectoryAsync(path?, pagination?)` | Directory listing |
| `.UploadAsync(stream, filename, progress?, uploadToken?)` | Upload from any stream |
| `.UploadAsync(bytes, filename, progress?, uploadToken?)` | Upload from byte array |
| `.UploadFileAsync(filePath, filename?, progress?, uploadToken?)` | Upload from file path |
| `["path"].GetMetadataAsync()` | File metadata |
| `["path"].VerifyAsync()` | Verify file integrity |
| `["path"].DownloadAsync()` | Download file |
| `["path"].DeleteAsync()` | Delete file |
| `["path"].PatchAsync(stream, start, end, total)` | Byte-range patch |
| `["path"].AppendAsync(stream)` | Append to file |

### `client.Buckets["id"].Tokens`
| Method | Description |
|--------|-------------|
| `.CreateAsync(request)` | Create upload token |

### `client.Keys`
| Method | Description |
|--------|-------------|
| `.CreateAsync(request)` | Create API key |
| `.ListAsync(pagination?)` | List API keys |
| `["prefix"].RevokeAsync()` | Revoke API key |
| `["prefix"].GetUsageAsync()` | Usage stats |

### `client.Dashboard`
| Method | Description |
|--------|-------------|
| `.CreateTokenAsync(request?)` | Create dashboard token |
| `.GetCurrentUserAsync()` | Validate current token |

### `client.Stats`
| Method | Description |
|--------|-------------|
| `.GetAsync()` | System statistics |

### `client.ShortUrls`
| Method | Description |
|--------|-------------|
| `["code"].DeleteAsync()` | Delete short URL |

### `client.Health`
| Method | Description |
|--------|-------------|
| `.CheckAsync()` | Health check |

### `client.Events`
| Method | Description |
|--------|-------------|
| `.ConnectAsync()` | Connect to SignalR hub |
| `.DisconnectAsync()` | Disconnect |
| `.SubscribeToBucketAsync(id)` | Subscribe to bucket events |
| `.SubscribeToFileAsync(id, path)` | Subscribe to file events |
| `.SubscribeToAllAsync()` | Subscribe to all (admin) |
| `.OnFileCreated(handler)` | File created event |
| `.OnFileUpdated(handler)` | File updated event |
| `.OnFileDeleted(handler)` | File deleted event |
| `.OnBucketCreated(handler)` | Bucket created event |
| `.OnBucketUpdated(handler)` | Bucket updated event |
| `.OnBucketDeleted(handler)` | Bucket deleted event |

## Links

- [CarbonFiles repository](https://github.com/carbungo/carbon-files)
- [NuGet package](https://www.nuget.org/packages/CarbonFiles.Client)
