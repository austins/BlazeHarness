using BlazeHarness.Core.Tools;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class WriteFileToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly WriteFileTool _sut = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_NewFileInNewFolders_CreatesIt()
    {
        // Act
        var result = await RunAsync(new { path = "a/b/new.txt", content = "hello\nworld\n" });

        // Assert
        result.Should().Be(ToolResult.Ok("Created a/b/new.txt (2 lines, 12 bytes)."));
        _workspace.Read("a/b/new.txt").Should().Be("hello\nworld\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReplacesItAndLeavesNoTemporaryFile()
    {
        // Arrange
        _workspace.Write("a.txt", "old content that is longer");

        // Act
        var result = await RunAsync(new { path = "a.txt", content = "new" });

        // Assert
        result.Output.Should().StartWith("Replaced a.txt");
        _workspace.Read("a.txt").Should().Be("new");
        Directory.GetFileSystemEntries(_workspace.Root).Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_ExistingCrlfFileAndLfContent_KeepsCrlf()
    {
        // Arrange
        _workspace.Write("win.txt", "one\r\ntwo\r\n");

        // Act
        await RunAsync(new { path = "win.txt", content = "a\nb\nc\n" });

        // Assert
        _workspace.Read("win.txt").Should().Be("a\r\nb\r\nc\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingLfFileAndCrlfContent_WritesTheContentAsGiven()
    {
        // Arrange
        _workspace.Write("unix.txt", "one\ntwo\n");

        // Act
        await RunAsync(new { path = "unix.txt", content = "a\r\nb\r\n" });

        // Assert
        _workspace.Read("unix.txt").Should().Be("a\r\nb\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_NewFile_UsesTheLineEndingsGiven()
    {
        // Act
        await RunAsync(new { path = "new.txt", content = "a\nb\n" });

        // Assert
        _workspace.Read("new.txt").Should().Be("a\nb\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFileWithBom_KeepsTheBom()
    {
        // Arrange
        await File.WriteAllBytesAsync(Path.Combine(_workspace.Root, "bom.txt"), [0xEF, 0xBB, 0xBF, (byte)'a'], TestContext.Current.CancellationToken);

        // Act
        await RunAsync(new { path = "bom.txt", content = "b" });

        // Assert
        (await File.ReadAllBytesAsync(Path.Combine(_workspace.Root, "bom.txt"), TestContext.Current.CancellationToken))
            .Should().Equal(0xEF, 0xBB, 0xBF, (byte)'b');
    }

    [Fact(Skip = "Unix file modes only.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task ExecuteAsync_ExistingExecutable_KeepsItsPermissions()
    {
        // Arrange
        var path = _workspace.Write("run.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Act
        await RunAsync(new { path = "run.sh", content = "#!/bin/sh\necho hi\n" });

        // Assert
        File.GetUnixFileMode(path).Should().HaveFlag(UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyContent_CreatesAnEmptyFile()
    {
        // Act
        var result = await RunAsync(new { path = "empty.txt", content = string.Empty });

        // Assert
        result.Output.Should().Contain("0 lines, 0 bytes");
        _workspace.Read("empty.txt").Should().BeEmpty();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/etc/blazeharness-test.txt")]
    public async Task ExecuteAsync_OutsideTheFolder_IsRefusedAndWritesNothing(string path)
    {
        // Act
        var result = await RunAsync(new { path, content = "x" });

        // Assert
        result.IsError.Should().BeTrue();
        File.Exists(Path.GetFullPath(Path.Combine(_workspace.Root, path))).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_PathIsAFolder_IsAnError()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_workspace.Root, "dir"));

        // Act
        var result = await RunAsync(new { path = "dir", content = "x" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("is a folder");
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ThrowsAnArgumentError()
    {
        // Act
        var act = () => RunAsync(new { path = "a.txt" });

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);
}
