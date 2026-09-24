using System.Security.Cryptography;

namespace CarbonFiles.Core.Utilities;

public static class IdGenerator
{
    private const string AlphaNumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string GenerateBucketId() => GenerateRandomString(10, "abcdefghijklmnopqrstuvwxyz0123456789");
    public static string GenerateShortCode() => GenerateRandomString(6, AlphaNumeric);

    public static string GenerateUploadToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return $"cfu_{Convert.ToHexStringLower(bytes)}";
    }

    public static (string FullKey, string Prefix) GenerateApiKey()
    {
        var prefixBytes = RandomNumberGenerator.GetBytes(4);
        var secretBytes = RandomNumberGenerator.GetBytes(16);
        var prefix = $"cf4_{Convert.ToHexStringLower(prefixBytes)}";
        var secret = Convert.ToHexStringLower(secretBytes);
        return ($"{prefix}_{secret}", prefix);
    }

    private static string GenerateRandomString(int length, string chars)
    {
        return string.Create(length, chars, static (span, c) =>
        {
            var bytes = RandomNumberGenerator.GetBytes(span.Length);
            for (int i = 0; i < span.Length; i++)
                span[i] = c[bytes[i] % c.Length];
        });
    }
}
