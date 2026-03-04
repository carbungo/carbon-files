using System.Data;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Auth;
using CarbonFiles.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarbonFiles.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure<T> with manual lambda — IConfiguration.Bind() uses reflection trimmed under AOT
        var section = configuration.GetSection(CarbonFilesOptions.SectionName);
        services.Configure<CarbonFilesOptions>(opts =>
        {
            opts.AdminKey = section[nameof(CarbonFilesOptions.AdminKey)] ?? string.Empty;
            opts.JwtSecret = section[nameof(CarbonFilesOptions.JwtSecret)];
            opts.DataDir = section[nameof(CarbonFilesOptions.DataDir)] ?? "./data";
            opts.DbPath = section[nameof(CarbonFilesOptions.DbPath)] ?? "./data/carbonfiles.db";
            opts.MaxUploadSize = long.TryParse(section[nameof(CarbonFilesOptions.MaxUploadSize)], out var maxUpload) ? maxUpload : 0;
            opts.CleanupIntervalMinutes = int.TryParse(section[nameof(CarbonFilesOptions.CleanupIntervalMinutes)], out var cleanup) ? cleanup : 60;
            opts.CorsOrigins = section[nameof(CarbonFilesOptions.CorsOrigins)] ?? "*";
            opts.EnableScalar = !bool.TryParse(section[nameof(CarbonFilesOptions.EnableScalar)], out var scalar) || scalar;
        });

        // Also read locally for startup-time values (DbPath, JwtSecret)
        var options = new CarbonFilesOptions
        {
            AdminKey = section[nameof(CarbonFilesOptions.AdminKey)] ?? string.Empty,
            JwtSecret = section[nameof(CarbonFilesOptions.JwtSecret)],
            DbPath = section[nameof(CarbonFilesOptions.DbPath)] ?? "./data/carbonfiles.db",
        };

        if (string.IsNullOrWhiteSpace(options.AdminKey))
            throw new InvalidOperationException("CarbonFiles:AdminKey must be configured. Set the CarbonFiles__AdminKey environment variable or CarbonFiles:AdminKey in configuration.");

        // Dapper + SQLite
        var connectionString = $"Data Source={options.DbPath}";
        services.AddScoped<IDbConnection>(_ =>
        {
            var conn = new SqliteConnection(connectionString);
            conn.Open();
            return conn;
        });

        // Auth
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton(new JwtHelper(options.EffectiveJwtSecret));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IDashboardTokenService, DashboardTokenService>();
        services.AddScoped<IBucketService, BucketService>();

        // File services
        services.AddSingleton<FileStorageService>();
        services.AddSingleton<ContentStorageService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IShortUrlService, ShortUrlService>();
        services.AddScoped<IUploadTokenService, UploadTokenService>();
        services.AddScoped<IStatsService, StatsService>();

        // Background cleanup
        services.AddScoped<CleanupRepository>();
        services.AddHostedService<CleanupService>();

        return services;
    }
}
