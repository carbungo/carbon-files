# Native AOT Re-enablement Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Re-enable Native AOT publishing with compiled models (no precompiled queries) and add Docker-based E2E tests that validate the AOT binary works correctly.

**Architecture:** Enable `PublishAot` in the API csproj, regenerate EF Core compiled models with `--nativeaot`, wire up `UseModel()` in DI, update the Dockerfile for AOT builds (clang/zlib deps, `runtime-deps` base image), create a new E2E test project that builds the Docker image and runs HTTP tests against the real AOT binary, and add an E2E CI job.

**Tech Stack:** .NET 10, EF Core 10 compiled models, Docker, xUnit, `docker compose`

---

### Task 1: Enable PublishAot in API csproj

**Files:**
- Modify: `src/CarbonFiles.Api/CarbonFiles.Api.csproj`

**Step 1: Update the csproj**

Replace:
```xml
<PublishTrimmed>true</PublishTrimmed>
<PublishSingleFile>true</PublishSingleFile>
<InvariantGlobalization>true</InvariantGlobalization>
<!-- EF Core uses reflection-based expression trees that produce trim warnings.
     These are unavoidable when using EF Core with PublishTrimmed. The JIT runtime
     handles these correctly — they only matter for full Native AOT (which we don't use). -->
<NoWarn>$(NoWarn);IL2026;IL2104;IL3050</NoWarn>
```

With:
```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
<!-- EF Core compiled models produce unavoidable AOT/trim warnings for
     expression-tree based query compilation (queries are interpreted at runtime). -->
<NoWarn>$(NoWarn);IL2026;IL2104;IL3050</NoWarn>
```

Note: `PublishAot` implies `PublishTrimmed`. `PublishSingleFile` is incompatible with AOT and must be removed.

**Step 2: Verify the project builds**

Run: `dotnet build src/CarbonFiles.Api`
Expected: Build succeeds (AOT settings only affect publish, not build)

**Step 3: Commit**

```bash
git add src/CarbonFiles.Api/CarbonFiles.Api.csproj
git commit -m "feat: re-enable PublishAot in API project"
```

---

### Task 2: Regenerate compiled models for NativeAOT and wire up UseModel

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Data/CompiledModels/` (regenerated)
- Modify: `src/CarbonFiles.Infrastructure/DependencyInjection.cs:22-23`

**Step 1: Regenerate compiled models with --nativeaot**

Run:
```bash
dotnet ef dbcontext optimize \
  --nativeaot \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api \
  --output-dir Data/CompiledModels
```

Expected: `CompiledModels/` directory is regenerated with NativeAOT-compatible code. Verify new files exist.

**Step 2: Wire up UseModel in DependencyInjection.cs**

In `src/CarbonFiles.Infrastructure/DependencyInjection.cs`, change line 22-23 from:

```csharp
services.AddDbContext<CarbonFilesDbContext>(opts =>
    opts.UseSqlite($"Data Source={options.DbPath}"));
```

To:

```csharp
services.AddDbContext<CarbonFilesDbContext>(opts =>
    opts.UseSqlite($"Data Source={options.DbPath}")
        .UseModel(CarbonFilesDbContextModel.Instance));
```

Add the required using at the top of the file:

```csharp
using CarbonFiles.Infrastructure.Data.CompiledModels;
```

**Step 3: Run existing tests to verify nothing is broken**

Run: `dotnet test tests/CarbonFiles.Api.Tests`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/CarbonFiles.Infrastructure/
git commit -m "feat: regenerate compiled models for NativeAOT and wire up UseModel"
```

---

### Task 3: Update Dockerfile for AOT builds

**Files:**
- Modify: `Dockerfile`

**Step 1: Rewrite Dockerfile for AOT**

Replace the entire Dockerfile with:

```dockerfile
# Build stage — needs clang and zlib for Native AOT
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Copy project files first for layer caching
COPY CarbonFiles.slnx .
COPY src/CarbonFiles.Core/CarbonFiles.Core.csproj src/CarbonFiles.Core/
COPY src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj src/CarbonFiles.Infrastructure/
COPY src/CarbonFiles.Api/CarbonFiles.Api.csproj src/CarbonFiles.Api/
COPY src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj src/CarbonFiles.Migrator/
RUN dotnet restore src/CarbonFiles.Api/CarbonFiles.Api.csproj -r linux-x64 && \
    dotnet restore src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj

# Copy everything and publish both
COPY . .
RUN dotnet publish src/CarbonFiles.Api -c Release -r linux-x64 -o /app/api && \
    dotnet publish src/CarbonFiles.Migrator -c Release -o /app/migrator

# Runtime — AOT binary needs no .NET runtime, but Migrator still does
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app

COPY --from=build /app/api ./
COPY --from=build /app/migrator ./migrator/

RUN mkdir -p /app/data && chmod 777 /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run migrator then start API
ENTRYPOINT ["sh", "-c", "dotnet ./migrator/CarbonFiles.Migrator.dll && exec ./CarbonFiles.Api"]
```

Key changes:
- Added `clang zlib1g-dev` install in build stage
- Added `-r linux-x64` to restore and publish for AOT
- Runtime base stays `aspnet` because Migrator needs `dotnet` CLI

**Step 2: Build Docker image locally to verify**

Run: `docker build -t carbonfiles-aot-test .`
Expected: Build completes successfully. The AOT publish step may take a few minutes.

**Step 3: Quick smoke test the container**

Run:
```bash
docker run -d --name cf-aot-test -p 18080:8080 \
  -e CarbonFiles__AdminKey=test-key \
  carbonfiles-aot-test

# Wait for startup
sleep 3

# Health check
curl -s http://localhost:18080/healthz | grep -q status && echo "PASS" || echo "FAIL"

# Cleanup
docker rm -f cf-aot-test
```

Expected: Health check returns status JSON, prints PASS.

**Step 4: Commit**

```bash
git add Dockerfile
git commit -m "feat: update Dockerfile for Native AOT builds"
```

---

### Task 4: Fix AOT runtime bugs

This task is a checkpoint. After Task 3's smoke test, if there are failures, debug and fix them here. Common issues:

- **Missing types in JsonSerializerContext**: Add `[JsonSerializable(typeof(MissingType))]` to `src/CarbonFiles.Api/Serialization/CarbonFilesJsonContext.cs`
- **Trimmed reflection calls**: Replace with source-generated alternatives
- **EF Core query failures**: Ensure compiled model is wired up correctly
- **SignalR startup failures**: Verify JSON protocol registration

Iterate: fix, rebuild Docker image, re-test until health check and basic CRUD work.

**Step 1: If bugs found, fix them and run unit tests**

Run: `dotnet test`
Expected: All tests pass

**Step 2: Rebuild and re-test Docker image**

Run: Repeat Task 3 Steps 2-3.
Expected: Container starts and health check passes.

**Step 3: Commit fixes**

```bash
git add -u
git commit -m "fix: resolve AOT runtime issues"
```

---

### Task 5: Create docker-compose.e2e.yml

**Files:**
- Create: `docker-compose.e2e.yml`

**Step 1: Create the E2E compose file**

```yaml
services:
  api:
    build: .
    ports:
      - "0:8080"
    environment:
      - CarbonFiles__AdminKey=e2e-test-admin-key
      - CarbonFiles__DataDir=/app/data
      - CarbonFiles__DbPath=/app/data/carbonfiles.db
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz"]
      interval: 2s
      timeout: 5s
      retries: 15
      start_period: 5s
    tmpfs:
      - /app/data
```

Key points:
- Port `0:8080` assigns a random host port (tests discover it)
- `tmpfs` mount for data so each run is clean
- Health check for container readiness

**Step 2: Commit**

```bash
git add docker-compose.e2e.yml
git commit -m "feat: add docker-compose.e2e.yml for E2E testing"
```

---

### Task 6: Create E2E test project

**Files:**
- Create: `tests/CarbonFiles.E2E.Tests/CarbonFiles.E2E.Tests.csproj`
- Create: `tests/CarbonFiles.E2E.Tests/E2EFixture.cs`
- Create: `tests/CarbonFiles.E2E.Tests/AotDeploymentTests.cs`
- Modify: `CarbonFiles.slnx` (add project reference)

**Step 1: Create the test project csproj**

File: `tests/CarbonFiles.E2E.Tests/CarbonFiles.E2E.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="8.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit.v3" Version="3.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

</Project>
```

No project reference to the API — these tests hit the Docker container over HTTP.

**Step 2: Create the E2E test fixture**

File: `tests/CarbonFiles.E2E.Tests/E2EFixture.cs`

```csharp
using System.Diagnostics;
using System.Net;
using Xunit;

namespace CarbonFiles.E2E.Tests;

public class E2EFixture : IAsyncLifetime
{
    private readonly string _composeFile;
    private readonly string _projectName;
    public HttpClient Client { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;

    private const string AdminKey = "e2e-test-admin-key";

    public E2EFixture()
    {
        _composeFile = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker-compose.e2e.yml"));
        _projectName = $"cfe2e-{Guid.NewGuid():N}"[..20];
    }

    public async ValueTask InitializeAsync()
    {
        // Build and start container
        await RunCompose("up", "-d", "--build", "--wait");

        // Discover the mapped port
        var port = (await RunComposeOutput("port", "api", "8080")).Trim();
        // Output: "0.0.0.0:XXXXX" — extract port
        var hostPort = port.Split(':').Last();
        BaseUrl = $"http://localhost:{hostPort}";

        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        // Wait for health (compose --wait should handle this, but belt-and-suspenders)
        for (var i = 0; i < 30; i++)
        {
            try
            {
                var resp = await Client.GetAsync("/healthz");
                if (resp.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch { }
            await Task.Delay(1000);
        }

        throw new Exception("Container did not become healthy within 30 seconds");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await RunCompose("down", "-v", "--remove-orphans");
    }

    public HttpClient CreateAdminClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task RunCompose(params string[] args)
    {
        var psi = new ProcessStartInfo("docker", $"compose -f {_composeFile} -p {_projectName} {string.Join(' ', args)}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            throw new Exception($"docker compose {string.Join(' ', args)} failed (exit {proc.ExitCode}): {stderr}");
        }
    }

    private async Task<string> RunComposeOutput(params string[] args)
    {
        var psi = new ProcessStartInfo("docker", $"compose -f {_composeFile} -p {_projectName} {string.Join(' ', args)}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output;
    }
}
```

**Step 3: Create the E2E test class**

File: `tests/CarbonFiles.E2E.Tests/AotDeploymentTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.E2E.Tests;

public class AotDeploymentTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AotDeploymentTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _fixture.Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("\"status\"").And.Contain("\"uptime_seconds\"");
    }

    [Fact]
    public async Task OpenApi_Spec_IsAvailable()
    {
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        spec.Should().Contain("\"paths\"").And.Contain("/api/buckets");
    }

    [Fact]
    public async Task FullWorkflow_CrudOperations_WorkUnderAot()
    {
        using var admin = _fixture.CreateAdminClient();

        // === Create API key ===
        var keyResp = await admin.PostAsJsonAsync("/api/keys",
            new { name = "e2e-test" }, JsonOptions, TestContext.Current.CancellationToken);
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyJson = await keyResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var keyDoc = JsonDocument.Parse(keyJson);
        var apiKey = keyDoc.RootElement.GetProperty("key").GetString()!;
        var keyPrefix = keyDoc.RootElement.GetProperty("prefix").GetString()!;

        // Verify snake_case
        keyJson.Should().Contain("\"created_at\"");

        // === Create bucket ===
        using var keyClient = _fixture.CreateAuthenticatedClient(apiKey);
        var bucketResp = await keyClient.PostAsJsonAsync("/api/buckets",
            new { name = "e2e-bucket", expires_in = "1d" }, JsonOptions, TestContext.Current.CancellationToken);
        bucketResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketJson = await bucketResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var bucketDoc = JsonDocument.Parse(bucketJson);
        var bucketId = bucketDoc.RootElement.GetProperty("id").GetString()!;

        bucketJson.Should().Contain("\"file_count\"").And.Contain("\"total_size\"");

        // === Upload file ===
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent("hello AOT"u8.ToArray()), "files", "test.txt");
        var uploadResp = await keyClient.PostAsync($"/api/buckets/{bucketId}/upload",
            multipart, TestContext.Current.CancellationToken);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadJson = await uploadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        uploadJson.Should().Contain("\"short_code\"").And.Contain("\"mime_type\"");

        // === Download file ===
        var downloadResp = await _fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/test.txt/content", TestContext.Current.CancellationToken);
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await downloadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be("hello AOT");

        // === List files ===
        var listResp = await _fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files", TestContext.Current.CancellationToken);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        listJson.Should().Contain("\"items\"").And.Contain("\"total\"");

        // === Stats (admin) ===
        var statsResp = await admin.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsJson = await statsResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        statsJson.Should().Contain("\"total_buckets\"").And.Contain("\"total_files\"");

        // === Error responses serialize correctly ===
        var notFoundResp = await _fixture.Client.GetAsync("/api/buckets/nonexistent", TestContext.Current.CancellationToken);
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var forbiddenResp = await _fixture.Client.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var errorJson = await forbiddenResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        errorJson.Should().Contain("\"error\"");

        // === Cleanup ===
        (await keyClient.DeleteAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/keys/{keyPrefix}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

**Step 4: Add project to solution**

In `CarbonFiles.slnx`, add to the `/tests/` folder:

```xml
<Project Path="tests/CarbonFiles.E2E.Tests/CarbonFiles.E2E.Tests.csproj" />
```

**Step 5: Verify E2E project builds**

Run: `dotnet build tests/CarbonFiles.E2E.Tests`
Expected: Build succeeds

**Step 6: Run E2E tests**

Run: `dotnet test tests/CarbonFiles.E2E.Tests --verbosity normal`
Expected: All 3 tests pass (requires Docker running). This will build the Docker image as part of the test fixture.

**Step 7: Commit**

```bash
git add tests/CarbonFiles.E2E.Tests/ CarbonFiles.slnx
git commit -m "feat: add Docker-based E2E tests for AOT deployment"
```

---

### Task 7: Update CI workflow with E2E job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Step 1: Add e2e job**

Add this job after the existing `build-and-test` job but before `publish`:

```yaml
  e2e:
    needs: build-and-test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build Docker image
        run: docker compose -f docker-compose.e2e.yml build

      - name: Run E2E tests
        run: dotnet test tests/CarbonFiles.E2E.Tests --verbosity normal

  publish:
    needs: [build-and-test, e2e]
```

Update the `publish` job's `needs` to include `e2e`.

**Step 2: Verify existing tests still pass**

Run: `dotnet test tests/CarbonFiles.Api.Tests`
Expected: All tests pass

**Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add E2E AOT deployment test job"
```

---

### Task 8: Exclude E2E tests from default `dotnet test`

The E2E tests require Docker and take minutes. They should NOT run during normal `dotnet test`.

**Files:**
- Modify: `tests/CarbonFiles.E2E.Tests/CarbonFiles.E2E.Tests.csproj`

**Step 1: Add a test category filter**

The simplest approach: don't include E2E project in the default `dotnet test` at the solution level. Add to the csproj:

```xml
<PropertyGroup>
  <!-- Exclude from default 'dotnet test' at solution level.
       Run explicitly: dotnet test tests/CarbonFiles.E2E.Tests -->
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

Actually, the better approach is to exclude it from solution-level test runs by removing it from the solution's test discovery. Since `dotnet test` at root runs all test projects in the solution, we should instead NOT add the E2E project to the solution file — OR — keep it in the solution but use a filter in CI's default `dotnet test` command.

**Revised approach**: Keep the project in the solution but update the CI `Test` step to exclude it:

In `.github/workflows/ci.yml`, change the Test step from:
```yaml
- name: Test
  run: dotnet test --no-build --configuration Release --verbosity normal
```
To:
```yaml
- name: Test
  run: dotnet test --no-build --configuration Release --verbosity normal --filter "FullyQualifiedName!~E2E"
```

Also update the root-level `dotnet test` behavior. If running `dotnet test` locally, developers should know to pass `--filter` or target specific projects.

**Step 2: Commit**

```bash
git add .github/workflows/ci.yml tests/CarbonFiles.E2E.Tests/CarbonFiles.E2E.Tests.csproj
git commit -m "ci: exclude E2E tests from default test run"
```

---

### Task 9: Final validation

**Step 1: Run all unit/integration tests**

Run: `dotnet test --filter "FullyQualifiedName!~E2E"`
Expected: All tests pass

**Step 2: Run E2E tests**

Run: `dotnet test tests/CarbonFiles.E2E.Tests --verbosity normal`
Expected: All E2E tests pass

**Step 3: Verify Docker image size**

Run: `docker images carbonfiles-aot-test --format "{{.Size}}"`
Expected: Image should be reasonable (likely 200-300MB with aspnet base for Migrator)

**Step 4: Commit any remaining fixes**

If any fixes were needed, commit them with descriptive messages.
