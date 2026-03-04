namespace CarbonFiles.Core.Utilities;

public static class ExpiryParser
{
    /// <summary>
    /// Returns null for "never", DateTime for everything else.
    /// Default (null input) returns 1 week from now.
    /// </summary>
    public static DateTime? Parse(string? value, DateTime? defaultExpiry = null)
    {
        if (value == null)
            return defaultExpiry ?? DateTime.UtcNow.AddDays(7);

        if (value == "never")
            return null;

        // Unix epoch: all digits
        if (long.TryParse(value, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

        // ISO 8601: contains 'T'
        if (value.Contains('T') && DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var iso))
            return iso;

        // Duration presets
        return value switch
        {
            "15m" => DateTime.UtcNow.AddMinutes(15),
            "1h" => DateTime.UtcNow.AddHours(1),
            "6h" => DateTime.UtcNow.AddHours(6),
            "12h" => DateTime.UtcNow.AddHours(12),
            "1d" => DateTime.UtcNow.AddDays(1),
            "3d" => DateTime.UtcNow.AddDays(3),
            "1w" => DateTime.UtcNow.AddDays(7),
            "2w" => DateTime.UtcNow.AddDays(14),
            "30d" or "1m" => DateTime.UtcNow.AddDays(30),
            _ => throw new ArgumentException($"Invalid expiry format: {value}")
        };
    }
}
