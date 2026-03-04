using System.Diagnostics.CodeAnalysis;

namespace CarbonFiles.Core.Configuration;

// [DynamicallyAccessedMembers] is required to preserve property setters when the binary is
// published with PublishTrimmed=true. Without it, the ILLink trimmer removes setters that
// aren't directly called in production code, causing ConfigurationBinder.Bind() to silently
// fail — all options remain at their default values regardless of environment variables.
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
