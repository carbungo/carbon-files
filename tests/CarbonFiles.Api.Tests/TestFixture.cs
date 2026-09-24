using System.Data;
using CarbonFiles.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarbonFiles.Api.Tests;

public class TestFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private SqliteConnection _keepAlive = null!;
    private string _connectionString = null!;
    private string _tempDir = null!;
    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Shared named in-memory SQLite database. The _keepAlive connection keeps
        // the DB alive; each DI scope gets its own SqliteConnection to the same DB.
        var dbName = $"TestFixture_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        // Create schema on the keep-alive connection
        DatabaseInitializer.Initialize(_keepAlive);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Remove existing IDbConnection registration
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IDbConnection));
                if (descriptor != null)
                    services.Remove(descriptor);

                // Each scope gets its own connection to the shared in-memory DB
                var connStr = _connectionString;
                services.AddScoped<IDbConnection>(_ =>
                {
                    var conn = new SqliteConnection(connStr);
                    conn.Open();
                    return conn;
                });
            });

            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CarbonFiles:AdminKey"] = "test-admin-key",
                    ["CarbonFiles:DataDir"] = _tempDir,
                    ["CarbonFiles:DbPath"] = Path.Combine(_tempDir, "test.db"),
                    ["CarbonFiles:SiteDomain"] = "files.test",
                });
            });
        });

        Client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAdminClient()
        => CreateAuthenticatedClient("test-admin-key");

    public HttpClient CreateNoRedirectClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return client;
    }

    /// <summary>
    /// Returns the base URL of the test server for SignalR client connections.
    /// </summary>
    public string GetServerUrl()
        => _factory.Server.BaseAddress.ToString().TrimEnd('/');

    /// <summary>
    /// Returns an HttpMessageHandler wired to the test server for SignalR client connections.
    /// </summary>
    public HttpMessageHandler GetHandler()
        => _factory.Server.CreateHandler();
}
