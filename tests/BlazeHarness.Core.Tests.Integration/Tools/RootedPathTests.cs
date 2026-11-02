using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class RootedPathTests : IDisposable
{
    private readonly TempRoot _root = new();

    public static bool CannotDenyAccess => OperatingSystem.IsWindows() || Environment.UserName == "root";

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void TryResolve_RelativePath_ResolvesInsideTheRoot()
    {
        // Act
        var ok = _root.Context.TryResolve("src/app.cs", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(_root.Root, "src", "app.cs"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    public void TryResolve_BlankOrDot_MeansTheRoot(string? input)
    {
        // Act
        var ok = _root.Context.TryResolve(input, out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(_root.Root);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    public void TryResolve_DotDotEscape_IsRefused(string input)
    {
        // Act
        var ok = _root.Context.TryResolve(input, out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("outside the working folder");
    }

    [Fact]
    public void TryResolve_AbsolutePathOutside_IsRefused()
    {
        // Arrange
        var outside = Path.Combine(Path.GetTempPath(), "somewhere-else.txt");

        // Act
        var ok = _root.Context.TryResolve(outside, out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_SiblingFolderWithSamePrefix_IsRefused()
    {
        // Arrange
        var sibling = _root.Root + "-evil";

        // Act
        var ok = _root.Context.TryResolve(sibling, out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_NulCharacter_IsRefused()
    {
        // Act
        var ok = _root.Context.TryResolve("a\0b", out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("not valid");
    }

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public void TryResolve_SymlinkedFolderLeadingOut_IsRefused()
    {
        // Arrange
        using var outside = new TempFolder("bh-outside-");
        File.WriteAllText(Path.Combine(outside.Root, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), outside.Root);

        // Act
        var folder = _root.Context.TryResolve("link", out _, out _);
        var file = _root.Context.TryResolve("link/secret.txt", out _, out _);
        var missing = _root.Context.TryResolve("link/new.txt", out _, out _);

        // Assert
        folder.Should().BeFalse();
        file.Should().BeFalse();
        missing.Should().BeFalse();
    }

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public void TryResolve_SymlinkedFileLeadingOut_IsRefused()
    {
        // Arrange
        using var outside = new TempFolder("bh-outside-");
        var target = Path.Combine(outside.Root, "secret.txt");
        File.WriteAllText(target, "secret");
        File.CreateSymbolicLink(Path.Combine(_root.Root, "link.txt"), target);

        // Act
        var ok = _root.Context.TryResolve("link.txt", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public void TryResolve_SymlinkStayingInside_IsAllowedAndResolved()
    {
        // Arrange
        _root.Write("real/file.txt", "x");
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "alias"), Path.Combine(_root.Root, "real"));

        // Act
        var ok = _root.Context.TryResolve("alias/file.txt", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(_root.Root, "real", "file.txt"));
    }

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public void TryResolve_DanglingSymlink_IsRefused()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_root.Root, "dangling"), Path.Combine(Path.GetTempPath(), "bh-missing-" + Guid.NewGuid().ToString("N")));

        // Act
        var ok = _root.Context.TryResolve("dangling", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_OutsidePathWhenAllowed_ResolvesIt()
    {
        // Arrange
        var outside = Path.GetTempPath();

        // Act
        var refused = _root.Context.TryResolve(outside, out _, out _);
        var allowed = _root.Context.WithOutsideAllowed().TryResolve(outside, out var full, out _);

        // Assert
        refused.Should().BeFalse();
        allowed.Should().BeTrue();
        full.TrimEnd(Path.DirectorySeparatorChar).Should().Be(RootedPath.CanonicalRoot(outside));
    }

    [Fact]
    public void TryLocate_PathOutsideTheRoot_ReturnsItsRealPath()
    {
        // Act
        var ok = RootedPath.TryLocate(_root.Root, "../elsewhere/a.txt", out var full);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(Path.GetDirectoryName(_root.Root)!, "elsewhere", "a.txt"));
    }

    [Fact(Skip = "Needs a Unix account that is not root.", SkipWhen = nameof(CannotDenyAccess))]
    [UnsupportedOSPlatform("windows")]
    public void TryLocate_FolderThatCannotBeSearched_DoesNotThrow()
    {
        // Arrange
        var locked = Path.Combine(_root.Root, "locked");
        Directory.CreateDirectory(Path.Combine(locked, "inner"));
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            // Act
            var act = () => RootedPath.TryLocate(_root.Root, "locked/inner/a.txt", out _);

            // Assert
            act.Should().NotThrow();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void TryLocate_InvalidPath_ReturnsFalse()
    {
        // Act
        var ok = RootedPath.TryLocate(_root.Root, "a\0b", out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void Relative_Path_UsesForwardSlashes()
    {
        // Act
        var result = _root.Context.Relative(Path.Combine(_root.Root, "a", "b.txt"));

        // Assert
        result.Should().Be("a/b.txt");
    }
}
