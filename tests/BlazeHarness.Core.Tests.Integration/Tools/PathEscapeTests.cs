using BlazeHarness.Core.Tools;
using System.Diagnostics;

namespace BlazeHarness.Core.Tests.Integration.Tools;

/// <summary>
/// Tries to get the file tools to reach a folder outside the working folder through links and relative paths.
/// </summary>
public sealed class PathEscapeTests : IDisposable
{
    private const string SkipReason = "Creating symbolic links needs a privilege on Windows.";
    private const string Secret = "top secret";

    private readonly TempWorkspace _workspace = new();
    private readonly DirectoryInfo _outside = Directory.CreateTempSubdirectory("bh-outside-");
    private readonly string _secretFile;

    public PathEscapeTests()
    {
        _secretFile = Path.Combine(_outside.FullName, "secret.txt");
        File.WriteAllText(_secretFile, Secret);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        _workspace.Dispose();
        _outside.Delete(recursive: true);
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public async Task ReadFile_ThroughASymlinkedFolder_IsRefused()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), _outside.FullName);

        // Act
        var result = await new ReadFileTool().ExecuteAsync(TempWorkspace.Args(new { path = "link/secret.txt" }), _workspace.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain(Secret);
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public async Task WriteFile_NewFileInASymlinkedFolder_IsRefusedAndCreatesNothing()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), _outside.FullName);

        // Act
        var result = await new WriteFileTool().ExecuteAsync(TempWorkspace.Args(new { path = "link/new.txt", content = "x" }), _workspace.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        File.Exists(Path.Combine(_outside.FullName, "new.txt")).Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public async Task WriteFile_OverASymlinkedFile_IsRefusedAndKeepsTheTarget()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_workspace.Root, "link.txt"), _secretFile);

        // Act
        var result = await new WriteFileTool().ExecuteAsync(TempWorkspace.Args(new { path = "link.txt", content = "overwritten" }), _workspace.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        (await File.ReadAllTextAsync(_secretFile, TestContext.Current.CancellationToken)).Should().Be(Secret);
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public async Task EditFile_ASymlinkedFile_IsRefusedAndKeepsTheTarget()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_workspace.Root, "link.txt"), _secretFile);

        // Act
        var result = await new EditFileTool().ExecuteAsync(
            TempWorkspace.Args(new { path = "link.txt", old_string = "top", new_string = "changed" }),
            _workspace.Context,
            TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        (await File.ReadAllTextAsync(_secretFile, TestContext.Current.CancellationToken)).Should().Be(Secret);
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public void TryResolve_ChainOfSymlinks_IsFollowedToTheEndAndRefused()
    {
        // Arrange
        var second = Path.Combine(_workspace.Root, "second");
        Directory.CreateSymbolicLink(second, _outside.FullName);
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "first"), second);

        // Act
        var ok = _workspace.Context.TryResolve("first/secret.txt", out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("outside");
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public void TryResolve_RelativeSymlinkTarget_IsRefused()
    {
        // Arrange
        var relative = Path.GetRelativePath(_workspace.Root, _outside.FullName);
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), relative);

        // Act
        var ok = _workspace.Context.TryResolve("link/secret.txt", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public void TryResolve_SymlinkLoop_IsRefusedWithoutHanging()
    {
        // Arrange
        var a = Path.Combine(_workspace.Root, "a");
        var b = Path.Combine(_workspace.Root, "b");
        Directory.CreateSymbolicLink(a, b);
        Directory.CreateSymbolicLink(b, a);

        // Act
        var ok = _workspace.Context.TryResolve("a/file.txt", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipWhen = nameof(IsWindows))]
    public void TryResolve_DotDotAfterASymlink_IsResolvedFromTheLinkNotItsTarget()
    {
        // Arrange
        var deep = Directory.CreateDirectory(Path.Combine(_outside.FullName, "deep", "deeper"));
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), deep.FullName);
        _workspace.Write("notes.txt", "mine");

        // Act
        var ok = _workspace.Context.TryResolve("link/../notes.txt", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(WorkspacePath.CanonicalRoot(_workspace.Root), "notes.txt"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("a/./b/../../../outside.txt")]
    [InlineData("./../outside.txt")]
    public void TryResolve_DotDotSegmentsThatLeave_AreRefused(string input)
    {
        // Act
        var ok = _workspace.Context.TryResolve(input, out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("~/secret.txt")]
    [InlineData("$HOME/secret.txt")]
    [InlineData("%2e%2e/secret.txt")]
    public void TryResolve_ShellOrUrlSyntax_IsTakenLiterallyInsideTheFolder(string input)
    {
        // Act
        var ok = _workspace.Context.TryResolve(input, out var full, out _);

        // Assert
        ok.Should().BeTrue();
        WorkspacePath.IsInside(_workspace.Root, full).Should().BeTrue();
    }

    [Fact(Skip = "Junctions exist only on Windows.", SkipUnless = nameof(IsWindows))]
    public void TryResolve_JunctionLeadingOut_IsRefused()
    {
        // Arrange
        var junction = Path.Combine(_workspace.Root, "junction");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{_outside.FullName}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();

        // Act
        var folder = _workspace.Context.TryResolve("junction", out _, out _);
        var file = _workspace.Context.TryResolve("junction/secret.txt", out _, out _);

        // Assert
        folder.Should().BeFalse();
        file.Should().BeFalse();
    }
}
