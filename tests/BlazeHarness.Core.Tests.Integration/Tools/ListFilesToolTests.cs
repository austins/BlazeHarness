using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ListFilesToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ListFilesTool _sut = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_NoArguments_ListsTheTopLevelOnlyWithFoldersMarked()
    {
        // Arrange
        _workspace.Write("b.txt", "x");
        _workspace.Write("a.txt", "x");
        _workspace.Write("src/deep.cs", "x");

        // Act
        var result = await RunAsync(new { });

        // Assert
        Lines(result).Should().Equal("a.txt", "b.txt", "src/");
    }

    [Fact]
    public async Task ExecuteAsync_PatternWithoutSlash_MatchesAtAnyDepth()
    {
        // Arrange
        _workspace.Write("Program.cs", "x");
        _workspace.Write("src/app/Service.cs", "x");
        _workspace.Write("src/readme.md", "x");

        // Act
        var result = await RunAsync(new { pattern = "*.cs" });

        // Assert
        Lines(result).Should().Equal("Program.cs", "src/app/Service.cs");
    }

    [Fact]
    public async Task ExecuteAsync_PatternWithSlash_IsRelativeToThePath()
    {
        // Arrange
        _workspace.Write("src/a.ts", "x");
        _workspace.Write("src/inner/b.ts", "x");
        _workspace.Write("other/c.ts", "x");

        // Act
        var result = await RunAsync(new { path = "src", pattern = "inner/*.ts" });

        // Assert
        Lines(result).Should().Equal("src/inner/b.ts");
    }

    [Fact]
    public async Task ExecuteAsync_Gitignore_HidesIgnoredFilesAndFolders()
    {
        // Arrange
        _workspace.Write(".gitignore", "bin/\n*.log\n!keep.log\n");
        _workspace.Write("bin/out.dll", "x");
        _workspace.Write("run.log", "x");
        _workspace.Write("keep.log", "x");
        _workspace.Write("src/code.cs", "x");
        _workspace.Write("src/.gitignore", "generated.cs\n");
        _workspace.Write("src/generated.cs", "x");

        // Act
        var result = await RunAsync(new { recursive = true });

        // Assert
        Lines(result).Should().Equal(".gitignore", "keep.log", "src/", "src/.gitignore", "src/code.cs");
    }

    [Fact]
    public async Task ExecuteAsync_IncludeIgnored_ListsThemAgain()
    {
        // Arrange
        _workspace.Write(".gitignore", "bin/\n");
        _workspace.Write("bin/out.dll", "x");

        // Act
        var result = await RunAsync(new { recursive = true, include_ignored = true });

        // Assert
        Lines(result).Should().Contain("bin/out.dll");
    }

    [Fact]
    public async Task ExecuteAsync_GitFolder_IsAlwaysSkipped()
    {
        // Arrange
        _workspace.Write(".git/config", "x");
        _workspace.Write("a.txt", "x");

        // Act
        var result = await RunAsync(new { recursive = true, include_ignored = true });

        // Assert
        Lines(result).Should().Equal("a.txt");
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedFolder_IsNotListedOrEntered()
    {
        // Arrange
        _workspace.Write("keys/id_rsa", "x");
        _workspace.Write("a.txt", "x");
        var context = _workspace.Context.WithSensitive(new SensitivePaths([Path.Combine(_workspace.Root, "keys")]));

        // Act
        var result = await _sut.ExecuteAsync(TempWorkspace.Args(new { recursive = true, include_ignored = true }), context, TestContext.Current.CancellationToken);

        // Assert
        Lines(result).Should().Equal("a.txt");
    }

    [Fact]
    public async Task ExecuteAsync_MoreThanTheLimit_StopsAndSaysSo()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            _workspace.Write($"f{i}.txt", "x");
        }

        // Act
        var result = await RunAsync(new { max_results = 3 });

        // Assert
        Lines(result).Should().HaveCount(4);
        result.Output.Should().EndWith("[Showing the first 3 entries. Use a narrower path or pattern to see the rest.]");
    }

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_SymlinkedFolder_IsListedButNotEntered()
    {
        // Arrange
        _workspace.Write("real/inner.txt", "x");
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "alias"), Path.Combine(_workspace.Root, "real"));

        // Act
        var result = await RunAsync(new { recursive = true });

        // Assert
        Lines(result).Should().Equal("alias/", "real/", "real/inner.txt");
    }

    [Theory]
    [InlineData("empty", "[The folder is empty.]")]
    [InlineData("missing", "does not exist")]
    [InlineData("../outside", "outside the working folder")]
    public async Task ExecuteAsync_EmptyMissingOrOutside_SaysSo(string path, string message)
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_workspace.Root, "empty"));

        // Act
        var result = await RunAsync(new { path });

        // Assert
        result.Output.Should().Contain(message);
    }

    [Fact]
    public async Task ExecuteAsync_PatternMatchesNothing_SaysSo()
    {
        // Arrange
        _workspace.Write("a.txt", "x");

        // Act
        var result = await RunAsync(new { pattern = "*.zzz" });

        // Assert
        result.Should().Be(ToolResult.Ok("[Nothing matches the pattern.]"));
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);

    private static string[] Lines(ToolResult result) => result.Output.Split('\n');
}
