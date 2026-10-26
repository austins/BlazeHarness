using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class WorkspacePathTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void TryResolve_RelativePath_ResolvesInsideTheRoot()
    {
        // Act
        var ok = _workspace.Context.TryResolve("src/app.cs", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(_workspace.Root, "src", "app.cs"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    public void TryResolve_BlankOrDot_MeansTheRoot(string? input)
    {
        // Act
        var ok = _workspace.Context.TryResolve(input, out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(_workspace.Root);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    public void TryResolve_DotDotEscape_IsRefused(string input)
    {
        // Act
        var ok = _workspace.Context.TryResolve(input, out _, out var error);

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
        var ok = _workspace.Context.TryResolve(outside, out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_SiblingFolderWithSamePrefix_IsRefused()
    {
        // Arrange
        var sibling = _workspace.Root + "-evil";

        // Act
        var ok = _workspace.Context.TryResolve(sibling, out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_NulCharacter_IsRefused()
    {
        // Act
        var ok = _workspace.Context.TryResolve("a\0b", out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("not valid");
    }

    [Fact]
    public void TryResolve_SymlinkedFolderLeadingOut_IsRefused()
    {
        // Arrange
        var outside = Directory.CreateTempSubdirectory("bh-outside-");
        try
        {
            File.WriteAllText(Path.Combine(outside.FullName, "secret.txt"), "secret");
            Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), outside.FullName);

            // Act
            var folder = _workspace.Context.TryResolve("link", out _, out _);
            var file = _workspace.Context.TryResolve("link/secret.txt", out _, out _);
            var missing = _workspace.Context.TryResolve("link/new.txt", out _, out _);

            // Assert
            folder.Should().BeFalse();
            file.Should().BeFalse();
            missing.Should().BeFalse();
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryResolve_SymlinkedFileLeadingOut_IsRefused()
    {
        // Arrange
        var outside = Directory.CreateTempSubdirectory("bh-outside-");
        try
        {
            var target = Path.Combine(outside.FullName, "secret.txt");
            File.WriteAllText(target, "secret");
            File.CreateSymbolicLink(Path.Combine(_workspace.Root, "link.txt"), target);

            // Act
            var ok = _workspace.Context.TryResolve("link.txt", out _, out _);

            // Assert
            ok.Should().BeFalse();
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryResolve_SymlinkStayingInside_IsAllowedAndResolved()
    {
        // Arrange
        _workspace.Write("real/file.txt", "x");
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "alias"), Path.Combine(_workspace.Root, "real"));

        // Act
        var ok = _workspace.Context.TryResolve("alias/file.txt", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(_workspace.Root, "real", "file.txt"));
    }

    [Fact]
    public void TryResolve_DanglingSymlink_IsRefused()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_workspace.Root, "dangling"), Path.Combine(Path.GetTempPath(), "bh-missing-" + Guid.NewGuid().ToString("N")));

        // Act
        var ok = _workspace.Context.TryResolve("dangling", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_OutsidePathWhenAllowed_ResolvesIt()
    {
        // Arrange
        var outside = Path.GetTempPath();

        // Act
        var refused = _workspace.Context.TryResolve(outside, out _, out _);
        var allowed = _workspace.Context.WithOutsideAllowed().TryResolve(outside, out var full, out _);

        // Assert
        refused.Should().BeFalse();
        allowed.Should().BeTrue();
        full.TrimEnd(Path.DirectorySeparatorChar).Should().Be(WorkspacePath.CanonicalRoot(outside));
    }

    [Fact]
    public void TryLocate_PathOutsideTheRoot_ReturnsItsRealPath()
    {
        // Act
        var ok = WorkspacePath.TryLocate(_workspace.Root, "../elsewhere/a.txt", out var full);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(Path.GetDirectoryName(_workspace.Root)!, "elsewhere", "a.txt"));
    }

    [Fact]
    public void TryLocate_InvalidPath_ReturnsFalse()
    {
        // Act
        var ok = WorkspacePath.TryLocate(_workspace.Root, "a\0b", out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void Relative_Path_UsesForwardSlashes()
    {
        // Act
        var result = _workspace.Context.Relative(Path.Combine(_workspace.Root, "a", "b.txt"));

        // Assert
        result.Should().Be("a/b.txt");
    }
}
