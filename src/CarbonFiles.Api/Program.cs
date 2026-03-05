using System.Data;
using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Endpoints;
using CarbonFiles.Api.Hubs;
using CarbonFiles.Api.Middleware;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure;
using CarbonFiles.Infrastructure.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel request body size to match MaxUploadSize (0 = unlimited → null removes the limit)
var maxUploadSize = builder.Configuration.GetValue<long>("CarbonFiles:MaxUploadSize");
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = maxUploadSize > 0 ? maxUploadSize : null;
});

// JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// SignalR (JSON protocol only for AOT)
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
    });

// OpenAPI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "CarbonFiles API",
            Version = "v1",
            Description = "File-sharing API with bucket-based organization and API key authentication."
        };

        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
        {
            ["AdminKey"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Admin API key (set via CARBON_FILES__ADMIN_KEY). Full access to all resources."
            },
            ["ApiKey"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "API key (cf4_prefix_secret). Access limited to own buckets."
            },
            ["DashboardToken"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Short-lived JWT dashboard token. Grants admin access."
            },
            ["UploadToken"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.ParameterLocation.Query,
                Name = "token",
                Description = "Upload token (cfu_...). Grants upload access to a specific bucket."
            }
        };

        return Task.CompletedTask;
    });

    // Assign per-operation security requirements based on description conventions
    options.AddOperationTransformer((operation, context, ct) =>
    {
        var desc = operation.Description ?? "";
        var doc = context.Document;

        // Public endpoints: explicitly empty security (override any global)
        if (desc.StartsWith("Public", StringComparison.OrdinalIgnoreCase))
        {
            operation.Security = [];
            return Task.CompletedTask;
        }

        var security = new List<Microsoft.OpenApi.OpenApiSecurityRequirement>();

        if (desc.Contains("Admin only", StringComparison.OrdinalIgnoreCase))
        {
            // Admin key or dashboard token
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("AdminKey", doc)] = new List<string>() });
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("DashboardToken", doc)] = new List<string>() });
        }
        else if (desc.Contains("upload token", StringComparison.OrdinalIgnoreCase))
        {
            // Owner, admin, or upload token
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("ApiKey", doc)] = new List<string>() });
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("AdminKey", doc)] = new List<string>() });
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("UploadToken", doc)] = new List<string>() });
        }
        else if (desc.Contains("Dashboard token", StringComparison.OrdinalIgnoreCase))
        {
            // Dashboard token only
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("DashboardToken", doc)] = new List<string>() });
        }
        else if (desc.StartsWith("Auth:", StringComparison.OrdinalIgnoreCase))
        {
            // Owner or admin
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("ApiKey", doc)] = new List<string>() });
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("AdminKey", doc)] = new List<string>() });
            security.Add(new() { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("DashboardToken", doc)] = new List<string>() });
        }

        if (security.Count > 0)
            operation.Security = security;

        return Task.CompletedTask;
    });
});

// Infrastructure (EF Core, auth)
builder.Services.AddInfrastructure(builder.Configuration);

// Real-time notifications via SignalR
builder.Services.AddScoped<INotificationService, HubNotificationService>();

// CORS
var corsOrigins = builder.Configuration.GetValue<string>("CarbonFiles:CorsOrigins") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins == "*")
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy.AllowAnyMethod()
              .WithHeaders("Authorization", "Content-Type", "Content-Range", "X-Append")
              .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length", "ETag", "Last-Modified");
    });
});

// Trust forwarded headers from reverse proxy (X-Forwarded-For, X-Forwarded-Proto)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Ensure data directory exists
var dataDir = builder.Configuration.GetValue<string>("CarbonFiles:DataDir") ?? "./data";
var dbPath = builder.Configuration.GetValue<string>("CarbonFiles:DbPath") ?? "./data/carbonfiles.db";
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// Initialize database schema + WAL mode + integrity check
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
    DatabaseInitializer.Initialize(db, logger);
}

// Middleware
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// Endpoints
app.MapHealthEndpoints();
app.MapKeyEndpoints();
app.MapBucketEndpoints();
app.MapUploadEndpoints();
app.MapUploadTokenEndpoints();
app.MapFileEndpoints();
app.MapTokenEndpoints();
app.MapShortUrlEndpoints();
app.MapStatsEndpoints();

// SignalR hub
app.MapHub<FileHub>("/hub/files");

// OpenAPI (always available)
app.MapOpenApi();

// Scalar UI (configurable)
if (builder.Configuration.GetValue<bool?>("CarbonFiles:EnableScalar") ?? true)
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("CarbonFiles API")
            .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
            .AddPreferredSecuritySchemes("AdminKey");
    });

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
