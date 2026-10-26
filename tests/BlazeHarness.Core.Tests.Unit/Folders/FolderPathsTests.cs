using BlazeHarness.Core.Folders;

namespace BlazeHarness.Core.Tests.Unit.Folders;

public sealed class FolderPathsTests
{
    private static readonly string Home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bh-home"));
    private static readonly string Root = Path.GetPathRoot(Home)!;

    [Fact]
    public void ToFullPath_TrailingSeparatorAndDotParts_AreRemoved()
    {
        // Arrange
        var typed = Path.Combine(Root, "work", "app", "..", "app") + Path.DirectorySeparatorChar;

        // Act
        var result = FolderPaths.ToFullPath("  " + typed + "  ", Home);

        // Assert
        result.Should().Be(Path.Combine(Root, "work", "app"));
    }

    [Theory]
    [InlineData("~")]
    [InlineData("~/code/app")]
    public void ToFullPath_Tilde_MeansTheHomeFolder(string typed)
    {
        // Act
        var result = FolderPaths.ToFullPath(typed, Home);

        // Assert
        result.Should().StartWith(Home);
        result.Should().Be(typed == "~" ? Home : Path.Combine(Home, "code", "app"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("code/app")]
    [InlineData("./app")]
    [InlineData("~user/app")]
    public void ToFullPath_EmptyOrRelative_IsRejected(string? typed)
    {
        // Act
        var result = FolderPaths.ToFullPath(typed, Home);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ToFullPath_TheRoot_KeepsItsSeparator()
    {
        // Act
        var result = FolderPaths.ToFullPath(Root, Home);

        // Assert
        result.Should().Be(Root);
    }

    [Fact]
    public void Normalize_PathThatDoesNotExist_IsStillMadeCanonical()
    {
        // Arrange
        var typed = Path.Combine(Root, "no-such-folder", "x") + Path.DirectorySeparatorChar;

        // Act
        var result = FolderPaths.Normalize(typed, Home);

        // Assert
        result.Should().Be(Path.Combine(Root, "no-such-folder", "x"));
    }

    [Fact]
    public void DisplayName_UsesTheLastPart_OrTheRootItself()
    {
        // Act
        var name = FolderPaths.DisplayName(Path.Combine(Root, "work", "my-app") + Path.DirectorySeparatorChar);
        var root = FolderPaths.DisplayName(Root);

        // Assert
        name.Should().Be("my-app");
        root.Should().Be(Root);
    }

    [Fact]
    public void Comparer_MatchesTheFileSystemCaseRules()
    {
        // Act
        var same = FolderPaths.Comparer.Equals("/Work/App", "/work/app");

        // Assert
        same.Should().Be(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }
}
