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
        // Build and start container (no --wait; we poll /healthz below)
        await RunCompose("up", "-d", "--build");

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
        // Capture container logs for debugging before tearing down
        try
        {
            var logs = await RunComposeOutput("logs", "--no-color");
            Console.WriteLine($"=== Container logs ===\n{logs}");
        }
        catch { }

        Client?.Dispose();
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
        // Read both streams concurrently to avoid deadlock when buffers fill
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stderr = await stderrTask;
        await stdoutTask;
        if (proc.ExitCode != 0)
            throw new Exception($"docker compose {string.Join(' ', args)} failed (exit {proc.ExitCode}): {stderr}");
    }

    private async Task<string> RunComposeOutput(params string[] args)
    {
        var psi = new ProcessStartInfo("docker", $"compose -f {_composeFile} -p {_projectName} {string.Join(' ', args)}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi)!;
        var outputTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        await stderrTask;
        return await outputTask;
    }
}
