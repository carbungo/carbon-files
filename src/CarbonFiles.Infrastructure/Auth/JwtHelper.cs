using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarbonFiles.Infrastructure.Auth;

public sealed class JwtHelper
{
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtHelper(string secret)
    {
        // Ensure key is at least 256 bits for HMAC-SHA256
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _key = new SymmetricSecurityKey(keyBytes);
    }

    public (string Token, DateTime ExpiresAt) CreateDashboardToken(DateTime expiresAt)
    {
        var maxExpiry = DateTime.UtcNow.AddHours(24);
        if (expiresAt > maxExpiry)
            throw new ArgumentException("Dashboard token expiry cannot exceed 24 hours");

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["scope"] = "admin" },
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature)
        };

        var token = _handler.CreateToken(descriptor);
        return (token, expiresAt);
    }

    public async Task<(bool IsValid, DateTime ExpiresAt)> ValidateTokenAsync(string token)
    {
        try
        {
            var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromSeconds(30)
            });

            if (!result.IsValid) return (false, default);

            var scopeClaim = result.ClaimsIdentity.FindFirst("scope");

            if (scopeClaim?.Value != "admin") return (false, default);

            var expiresAt = result.SecurityToken is JsonWebToken jwt
                ? jwt.ValidTo
                : DateTime.UtcNow;

            return (true, expiresAt);
        }
        catch
        {
            return (false, default);
        }
    }
}
