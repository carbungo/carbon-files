using System.Text.RegularExpressions;
using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Utilities;

public partial class IdGeneratorTests
{
    [GeneratedRegex("^[a-zA-Z0-9]+$")]
    private static partial Regex AlphaNumericRegex();

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex LowercaseAlphaNumericRegex();

    [GeneratedRegex("^cf4_[0-9a-f]{8}_[0-9a-f]{32}$")]
    private static partial Regex ApiKeyFullRegex();

    [GeneratedRegex("^cf4_[0-9a-f]{8}$")]
    private static partial Regex ApiKeyPrefixRegex();

    [GeneratedRegex("^cfu_[0-9a-f]{48}$")]
    private static partial Regex UploadTokenRegex();

    [Fact]
    public void GenerateBucketId_Returns10LowercaseAlphanumericCharacters()
    {
        var id = IdGenerator.GenerateBucketId();

        id.Should().HaveLength(10);
        LowercaseAlphaNumericRegex().IsMatch(id).Should().BeTrue();
    }

    [Fact]
    public void GenerateShortCode_Returns6AlphanumericCharacters()
    {
        var code = IdGenerator.GenerateShortCode();

        code.Should().HaveLength(6);
        AlphaNumericRegex().IsMatch(code).Should().BeTrue();
    }

    [Fact]
    public void GenerateApiKey_HasCorrectFormat()
    {
        var (fullKey, prefix) = IdGenerator.GenerateApiKey();

        ApiKeyFullRegex().IsMatch(fullKey).Should().BeTrue(
            $"full key '{fullKey}' should match cf4_{{8hex}}_{{32hex}}");
        ApiKeyPrefixRegex().IsMatch(prefix).Should().BeTrue(
            $"prefix '{prefix}' should match cf4_{{8hex}}");
        fullKey.Should().StartWith(prefix);
    }

    [Fact]
    public void GenerateUploadToken_HasCorrectFormat()
    {
        var token = IdGenerator.GenerateUploadToken();

        token.Should().StartWith("cfu_");
        UploadTokenRegex().IsMatch(token).Should().BeTrue(
            $"token '{token}' should match cfu_{{48hex}}");
    }

    [Fact]
    public void GenerateBucketId_ProducesUniqueValues()
    {
        var ids = Enumerable.Range(0, 1000)
            .Select(_ => IdGenerator.GenerateBucketId())
            .ToHashSet();

        ids.Should().HaveCount(1000, "all 1000 generated bucket IDs should be unique");
    }

    [Fact]
    public void GenerateShortCode_ProducesUniqueValues()
    {
        var codes = Enumerable.Range(0, 1000)
            .Select(_ => IdGenerator.GenerateShortCode())
            .ToHashSet();

        codes.Should().HaveCount(1000, "all 1000 generated short codes should be unique");
    }

    [Fact]
    public void GenerateApiKey_ProducesUniqueValues()
    {
        var keys = Enumerable.Range(0, 1000)
            .Select(_ => IdGenerator.GenerateApiKey().FullKey)
            .ToHashSet();

        keys.Should().HaveCount(1000, "all 1000 generated API keys should be unique");
    }

    [Fact]
    public void GenerateUploadToken_ProducesUniqueValues()
    {
        var tokens = Enumerable.Range(0, 1000)
            .Select(_ => IdGenerator.GenerateUploadToken())
            .ToHashSet();

        tokens.Should().HaveCount(1000, "all 1000 generated upload tokens should be unique");
    }
}
