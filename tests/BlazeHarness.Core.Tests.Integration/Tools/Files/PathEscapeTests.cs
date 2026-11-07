using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Tests.Shared;
using System.Diagnostics;

namespace BlazeHarness.Core.Tests.Integration.Tools.Files;

/// <summary>
/// Tries to get the file tools to reach a folder outside the working folder through links and relative paths.
/// </summary>
public sealed class PathEscapeTests : IDisposable
{
    private const string Secret = "top secret";

    private readonly TempRoot _root = new();
    private readonly TempFolder _outside = new("bh-outside-");
    private readonly string _secretFile;

    public PathEscapeTests()
    {
        _secretFile = Path.Combine(_outside.Root, "secret.txt");
        File.WriteAllText(_secretFile, Secret);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        _root.Dispose();
        _outside.Dispose();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task ReadFile_ThroughASymlinkedFolder_IsRefused()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), _outside.Root);

        // Act
        var result = await new ReadFileTool().ExecuteAsync(TempRoot.Args(new { path = "link/secret.txt" }), _root.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain(Secret);
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task WriteFile_NewFileInASymlinkedFolder_IsRefusedAndCreatesNothing()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), _outside.Root);

        // Act
        var result = await new WriteFileTool().ExecuteAsync(TempRoot.Args(new { path = "link/new.txt", content = "x" }), _root.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        File.Exists(Path.Combine(_outside.Root, "new.txt")).Should().BeFalse();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task WriteFile_OverASymlinkedFile_IsRefusedAndKeepsTheTarget()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_root.Root, "link.txt"), _secretFile);

        // Act
        var result = await new WriteFileTool().ExecuteAsync(TempRoot.Args(new { path = "link.txt", content = "overwritten" }), _root.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        (await File.ReadAllTextAsync(_secretFile, TestContext.Current.CancellationToken)).Should().Be(Secret);
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task EditFile_ASymlinkedFile_IsRefusedAndKeepsTheTarget()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_root.Root, "link.txt"), _secretFile);

        // Act
        var result = await new EditFileTool().ExecuteAsync(
            TempRoot.Args(new { path = "link.txt", old_string = "top", new_string = "changed" }),
            _root.Context,
            TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        (await File.ReadAllTextAsync(_secretFile, TestContext.Current.CancellationToken)).Should().Be(Secret);
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public void TryResolve_ChainOfSymlinks_IsFollowedToTheEndAndRefused()
    {
        // Arrange
        var second = Path.Combine(_root.Root, "second");
        Directory.CreateSymbolicLink(second, _outside.Root);
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "first"), second);

        // Act
        var ok = _root.Context.TryResolve("first/secret.txt", out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("outside");
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public void TryResolve_RelativeSymlinkTarget_IsRefused()
    {
        // Arrange
        var relative = Path.GetRelativePath(_root.Root, _outside.Root);
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), relative);

        // Act
        var ok = _root.Context.TryResolve("link/secret.txt", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public void TryResolve_SymlinkLoop_IsRefusedWithoutHanging()
    {
        // Arrange
        var a = Path.Combine(_root.Root, "a");
        var b = Path.Combine(_root.Root, "b");
        Directory.CreateSymbolicLink(a, b);
        Directory.CreateSymbolicLink(b, a);

        // Act
        var ok = _root.Context.TryResolve("a/file.txt", out _, out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public void TryResolve_DotDotAfterASymlink_IsResolvedFromTheLinkNotItsTarget()
    {
        // Arrange
        var deep = Directory.CreateDirectory(Path.Combine(_outside.Root, "deep", "deeper"));
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), deep.FullName);
        _root.Write("notes.txt", "mine");

        // Act
        var ok = _root.Context.TryResolve("link/../notes.txt", out var full, out _);

        // Assert
        ok.Should().BeTrue();
        full.Should().Be(Path.Combine(RootedPath.CanonicalRoot(_root.Root), "notes.txt"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("a/./b/../../../outside.txt")]
    [InlineData("./../outside.txt")]
    public void TryResolve_DotDotSegmentsThatLeave_AreRefused(string input)
    {
        // Act
        var ok = _root.Context.TryResolve(input, out _, out _);

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
        var ok = _root.Context.TryResolve(input, out var full, out _);

        // Assert
        ok.Should().BeTrue();
        RootedPath.IsInside(_root.Root, full).Should().BeTrue();
    }

    [Fact(Skip = "Junctions exist only on Windows.", SkipUnless = nameof(IsWindows))]
    public void TryResolve_JunctionLeadingOut_IsRefused()
    {
        // Arrange
        var junction = Path.Combine(_root.Root, "junction");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{_outside.Root}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0);

        try
        {
            // Act
            var folder = _root.Context.TryResolve("junction", out _, out _);
            var file = _root.Context.TryResolve("junction/secret.txt", out _, out _);

            // Assert
            folder.Should().BeFalse();
            file.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(junction);
        }

        File.ReadAllText(_secretFile).Should().Be(Secret);
    }
}
