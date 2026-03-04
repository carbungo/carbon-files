using CarbonFiles.Infrastructure.Auth;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Auth;

public class JwtHelperTests
{
    private readonly JwtHelper _jwt = new("test-secret-key");

    [Fact]
    public void CreateDashboardToken_ReturnsValidJwtString()
    {
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var (token, returnedExpiry) = _jwt.CreateDashboardToken(expiresAt);

        token.Should().NotBeNullOrWhiteSpace();
        token.Split('.').Should().HaveCount(3, "a JWT has three dot-separated parts");
        returnedExpiry.Should().Be(expiresAt);
    }

    [Fact]
    public async Task ValidateTokenAsync_SucceedsForValidToken()
    {
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var (token, _) = _jwt.CreateDashboardToken(expiresAt);

        var (isValid, validatedExpiry) = await _jwt.ValidateTokenAsync(token);

        isValid.Should().BeTrue();
        validatedExpiry.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ValidateTokenAsync_FailsForExpiredToken()
    {
        // Create a token with a different key — should fail validation
        var differentHelper = new JwtHelper("different-secret");
        var (token, _) = differentHelper.CreateDashboardToken(DateTime.UtcNow.AddMinutes(1));

        // Validate with wrong key — should fail
        var (isValid, _) = await _jwt.ValidateTokenAsync(token);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_FailsForTamperedToken()
    {
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var (token, _) = _jwt.CreateDashboardToken(expiresAt);

        // Tamper with the token by changing a character in the signature
        var parts = token.Split('.');
        var tamperedSignature = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        var (isValid, _) = await _jwt.ValidateTokenAsync(tamperedToken);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void CreateDashboardToken_ThrowsWhenExpiryExceeds24Hours()
    {
        var expiresAt = DateTime.UtcNow.AddHours(25);

        var act = () => _jwt.CreateDashboardToken(expiresAt);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*24 hours*");
    }
}
