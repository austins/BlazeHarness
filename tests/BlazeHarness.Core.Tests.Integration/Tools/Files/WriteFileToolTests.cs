using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Tools.Files;

public sealed class WriteFileToolTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly WriteFileTool _sut = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ExecuteAsync_NewFileInNewFolders_CreatesIt()
    {
        // Act
        var result = await RunAsync(new { path = "a/b/new.txt", content = "hello\nworld\n" });

        // Assert
        result.Should().Be(ToolResult.Ok("Created a/b/new.txt (2 lines, 12 bytes)."));
        _root.Read("a/b/new.txt").Should().Be("hello\nworld\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReplacesItAndLeavesNoTemporaryFile()
    {
        // Arrange
        _root.Write("a.txt", "old content that is longer");

        // Act
        var result = await RunAsync(new { path = "a.txt", content = "new" });

        // Assert
        result.Output.Should().StartWith("Replaced a.txt");
        _root.Read("a.txt").Should().Be("new");
        Directory.GetFileSystemEntries(_root.Root).Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_ExistingCrlfFileAndLfContent_KeepsCrlf()
    {
        // Arrange
        _root.Write("win.txt", "one\r\ntwo\r\n");

        // Act
        await RunAsync(new { path = "win.txt", content = "a\nb\nc\n" });

        // Assert
        _root.Read("win.txt").Should().Be("a\r\nb\r\nc\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingLfFileAndCrlfContent_WritesTheContentAsGiven()
    {
        // Arrange
        _root.Write("unix.txt", "one\ntwo\n");

        // Act
        await RunAsync(new { path = "unix.txt", content = "a\r\nb\r\n" });

        // Assert
        _root.Read("unix.txt").Should().Be("a\r\nb\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_NewFile_UsesTheLineEndingsGiven()
    {
        // Act
        await RunAsync(new { path = "new.txt", content = "a\nb\n" });

        // Assert
        _root.Read("new.txt").Should().Be("a\nb\n");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFileWithBom_KeepsTheBom()
    {
        // Arrange
        await File.WriteAllBytesAsync(Path.Combine(_root.Root, "bom.txt"), [0xEF, 0xBB, 0xBF, (byte)'a'], TestContext.Current.CancellationToken);

        // Act
        await RunAsync(new { path = "bom.txt", content = "b" });

        // Assert
        (await File.ReadAllBytesAsync(Path.Combine(_root.Root, "bom.txt"), TestContext.Current.CancellationToken))
            .Should().Equal(0xEF, 0xBB, 0xBF, (byte)'b');
    }

    [Fact(Skip = "Unix file modes only.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task ExecuteAsync_ExistingExecutable_KeepsItsPermissions()
    {
        // Arrange
        var path = _root.Write("run.sh", "#!/bin/sh\n");
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
        _root.Read("empty.txt").Should().BeEmpty();
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
        File.Exists(Path.GetFullPath(Path.Combine(_root.Root, path))).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_PathIsAFolder_IsAnError()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_root.Root, "dir"));

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

    [Fact]
    public void PreviewChange_ExistingFile_ComparesItWithTheNewContentWithoutWriting()
    {
        // Arrange
        _root.Write("notes.txt", "first\r\nsecond\r\nthird");

        // Act
        var lines = Preview(new { path = "notes.txt", content = "first\nchanged\nthird" });

        // Assert
        lines.Should().Equal(
            new DiffLine(DiffKind.Same, "first"),
            new DiffLine(DiffKind.Removed, "second"),
            new DiffLine(DiffKind.Added, "changed"),
            new DiffLine(DiffKind.Same, "third"));
        _root.Read("notes.txt").Should().Be("first\r\nsecond\r\nthird");
    }

    [Fact]
    public void PreviewChange_NewFile_ShowsEveryLineAsAdded()
    {
        // Act
        var lines = Preview(new { path = "sub/new.txt", content = "one\ntwo" });

        // Assert
        lines.Should().Equal(new DiffLine(DiffKind.Added, "one"), new DiffLine(DiffKind.Added, "two"));
        File.Exists(Path.Combine(_root.Root, "sub", "new.txt")).Should().BeFalse();
    }

    [Fact]
    public void PreviewChange_BinaryFile_HasNoPreview()
    {
        // Arrange
        _root.Write("data.bin", [1, 0, 2, 0]);

        // Act
        var lines = Preview(new { path = "data.bin", content = "text" });

        // Assert
        lines.Should().BeNull();
    }

    [Fact]
    public void PreviewChange_ProtectedFile_HasNoPreview()
    {
        // Arrange
        var secret = _root.Write("secret.txt", "private");
        var context = _root.Context.WithSensitive(new SensitivePaths([secret]));

        // Act
        var lines = ((IPreviewsChange)_sut).PreviewChange(TempRoot.Args(new { path = "secret.txt", content = "x" }), context);

        // Assert
        lines.Should().BeNull();
    }

    [Fact]
    public void PreviewChange_ContentOverTheLimit_HasNoPreview()
    {
        // Act
        var lines = Preview(new { path = "big.txt", content = new string('x', WriteFileTool.MaxPreviewCharacters + 1) });

        // Assert
        lines.Should().BeNull();
    }

    private IReadOnlyList<DiffLine>? Preview(object args) => ((IPreviewsChange)_sut).PreviewChange(TempRoot.Args(args), _root.Context);

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempRoot.Args(args), _root.Context, TestContext.Current.CancellationToken);
}
