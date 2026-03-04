using System.Diagnostics.CodeAnalysis;

namespace CarbonFiles.Core.Configuration;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CarbonFilesOptions
{
    public const string SectionName = "CarbonFiles";

    public string AdminKey { get; set; } = string.Empty;
    public string? JwtSecret { get; set; }
    public string DataDir { get; set; } = "./data";
    public string DbPath { get; set; } = "./data/carbonfiles.db";
    public long MaxUploadSize { get; set; } = 0; // 0 = unlimited
    public int CleanupIntervalMinutes { get; set; } = 60;
    public string CorsOrigins { get; set; } = "*";
    public bool EnableScalar { get; set; } = true;

    public string EffectiveJwtSecret => JwtSecret ?? AdminKey;
}
