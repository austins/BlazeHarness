using BlazeHarness.Core.Tools.Shell;

namespace BlazeHarness.Core.Tests.Unit.Tools.Shell;

public sealed class RefusedFilesSearchTests
{
    private readonly RefusedFilesSearch _sut = new(2, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".env" }, "**/.env");

    [Fact]
    public void With_DeeperRuleAndMoreNames_TakesTheDeeperDepthAndBothNames()
    {
        // Act
        var result = _sut.With(5, new HashSet<string>(StringComparer.Ordinal) { "id_rsa" });

        // Assert
        result.Depth.Should().Be(5);
        result.Names.Should().BeEquivalentTo(".env", "id_rsa");
        result.Pattern.Should().Be("**/.env");
    }

    [Fact]
    public void With_ShallowerRule_KeepsTheDepth()
    {
        // Act
        var result = _sut.With(1, new HashSet<string>(StringComparer.Ordinal) { ".env" });

        // Assert
        result.Depth.Should().Be(2);
        result.Names.Should().ContainSingle();
    }

    [Fact]
    public void With_AnyName_MatchesEveryName()
    {
        // Act
        var result = _sut.With(1, null);

        // Assert
        result.Names.Should().BeNull();
    }

    [Fact]
    public void With_Names_KeepsTheComparerOfTheFirstSearch()
    {
        // Act
        var result = _sut.With(1, new HashSet<string>(StringComparer.Ordinal) { "Key.pem" });

        // Assert
        result.Names!.Contains(".ENV").Should().BeTrue();
        result.Names.Contains("key.pem").Should().BeTrue();
        _sut.Names!.Should().ContainSingle();
    }
}
