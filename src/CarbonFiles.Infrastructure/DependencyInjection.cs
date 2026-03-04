using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Auth;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.CompiledModels;
using CarbonFiles.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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

        // EF Core + SQLite
        services.AddDbContext<CarbonFilesDbContext>(opts =>
            opts.UseSqlite($"Data Source={options.DbPath}")
                .UseModel(CarbonFilesDbContextModel.Instance));

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
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IShortUrlService, ShortUrlService>();
        services.AddScoped<IUploadTokenService, UploadTokenService>();

        // Background cleanup
        services.AddHostedService<CleanupService>();

        return services;
    }
}
