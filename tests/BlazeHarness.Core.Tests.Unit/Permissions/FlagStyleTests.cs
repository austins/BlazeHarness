using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class FlagStyleTests
{
    [Theory]
    [InlineData("rm -rf build", true)]
    [InlineData("rm -fr build", true)]
    [InlineData("rm -r build", true)]
    [InlineData("rm --recursive build", true)]
    [InlineData("rm --rec build", true)]
    [InlineData("rm --recursive=yes build", true)]
    [InlineData("rm build", false)]
    [InlineData("rm --verbose build", false)]
    [InlineData("rm -v build", false)]
    public void Find_PosixFlags_MatchShortClustersAndLongOptions(string command, bool flagged)
    {
        // Arrange
        var sut = Load("""{ "programs": ["rm"], "flags": ["-r", "--recursive"] }""");

        // Act
        var match = sut.Find(command);

        // Assert
        (match is not null).Should().Be(flagged);
    }

    [Theory]
    [InlineData("rd /s folder", true)]
    [InlineData("RD /S folder", true)]
    [InlineData("rd /s/q folder", true)]
    [InlineData("rd /q folder", false)]
    [InlineData("rd -s folder", false)]
    [InlineData("rd folder", false)]
    public void Find_WindowsFlags_AreSlashSeparated(string command, bool flagged)
    {
        // Arrange
        var sut = Load("""{ "programs": ["rd"], "flags": ["/s"], "flagStyle": "windows" }""");

        // Act
        var match = sut.Find(command);

        // Assert
        (match is not null).Should().Be(flagged);
    }

    [Theory]
    [InlineData("remove-item x -Recurse", true)]
    [InlineData("remove-item x -recurse", true)]
    [InlineData("remove-item x -Rec", true)]
    [InlineData("remove-item x -Recurse:$true", true)]
    [InlineData("remove-item x --Recurse", false)]
    [InlineData("remove-item x -R", true)]
    [InlineData("remove-item x -Path y", false)]
    [InlineData("remove-item x", false)]
    public void Find_PowerShellFlags_MatchTheNameOrAnyStartOfIt(string command, bool flagged)
    {
        // Arrange
        var sut = Load("""{ "programs": ["remove-item"], "flags": ["-Recurse"], "flagStyle": "powershell" }""");

        // Act
        var match = sut.Find(command);

        // Assert
        (match is not null).Should().Be(flagged);
    }

    [Fact]
    public void Find_NoFlagStyleGiven_UsesPosix()
    {
        // Arrange
        var sut = Load("""{ "programs": ["rd"], "flags": ["-s"] }""");

        // Act
        var posix = sut.Find("rd -s x");
        var windows = sut.Find("rd /s x");

        // Assert
        posix.Should().NotBeNull();
        windows.Should().BeNull();
    }

    private static ForbiddenCommands Load(string rule)
    {
        return ForbiddenCommands.FromJson($$"""{ "rules": [ {{rule[..^1]}}, "risk": "Risky." } ] }""", SandboxPlatform.Linux);
    }
}
