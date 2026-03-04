using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Utilities;

public class PathNormalizerTests
{
    [Theory]
    [InlineData("readme.md", "readme.md")]
    [InlineData("/readme.md", "readme.md")]
    [InlineData("src\\main.cs", "src/main.cs")]
    [InlineData("src//utils//file.cs", "src/utils/file.cs")]
    [InlineData("src/main.cs/", "src/main.cs")]
    [InlineData("/src/main.cs", "src/main.cs")]
    public void Normalize_ValidPaths_ReturnsNormalized(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrNull_ThrowsArgumentException(string? input)
    {
        var act = () => PathNormalizer.Normalize(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("src/../secret")]
    [InlineData("..")]
    public void Normalize_PathTraversal_ThrowsArgumentException(string input)
    {
        var act = () => PathNormalizer.Normalize(input);
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Theory]
    [InlineData("src/./file")]
    public void Normalize_EmptyComponents_ThrowsArgumentException(string input)
    {
        var act = () => PathNormalizer.Normalize(input);
        act.Should().Throw<ArgumentException>();
    }
}
