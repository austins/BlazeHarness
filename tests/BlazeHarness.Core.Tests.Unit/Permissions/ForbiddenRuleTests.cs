using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class ForbiddenRuleTests
{
    [Fact]
    public void New_NothingSet_HasNoMatchersAndTheDefaultStyle()
    {
        // Act
        var sut = new ForbiddenRule();

        // Assert
        sut.Programs.Should().BeEmpty();
        sut.Arguments.Should().BeEmpty();
        sut.Flags.Should().BeEmpty();
        sut.Contains.Should().BeEmpty();
        sut.Platforms.Should().BeEmpty();
        sut.FlagStyle.Should().Be(FlagStyle.Posix);
        sut.Risk.Should().BeEmpty();
    }

    [Fact]
    public void Find_RuleWithArgumentsAndNoFlags_MatchesTheLeadingArgumentsOnly()
    {
        // Arrange
        var sut = ForbiddenCommands.FromJson("""{ "rules": [ { "programs": ["dotnet"], "arguments": ["user-secrets", "list"], "risk": "Prints secrets." } ] }""", SandboxPlatform.Linux);

        // Act
        var match = sut.Find("dotnet user-secrets list");
        var other = sut.Find("dotnet user-secrets set a b");
        var shorter = sut.Find("dotnet user-secrets");

        // Assert
        match.Should().Be(new ForbiddenMatch("Prints secrets."));
        other.Should().BeNull();
        shorter.Should().BeNull();
    }
}
