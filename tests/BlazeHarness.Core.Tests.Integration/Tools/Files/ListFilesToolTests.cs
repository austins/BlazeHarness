using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Tests.Integration.Tools.Files;

public sealed class ListFilesToolTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ListFilesTool _sut = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ExecuteAsync_NoArguments_ListsTheTopLevelOnlyWithFoldersMarked()
    {
        // Arrange
        _root.Write("b.txt", "x");
        _root.Write("a.txt", "x");
        _root.Write("src/deep.cs", "x");

        // Act
        var result = await RunAsync(new { });

        // Assert
        Lines(result).Should().Equal("a.txt", "b.txt", "src/");
    }

    [Fact]
    public async Task ExecuteAsync_PatternWithoutSlash_MatchesAtAnyDepth()
    {
        // Arrange
        _root.Write("Program.cs", "x");
        _root.Write("src/app/Service.cs", "x");
        _root.Write("src/readme.md", "x");

        // Act
        var result = await RunAsync(new { pattern = "*.cs" });

        // Assert
        Lines(result).Should().Equal("Program.cs", "src/app/Service.cs");
    }

    [Fact]
    public async Task ExecuteAsync_PatternWithSlash_IsRelativeToThePath()
    {
        // Arrange
        _root.Write("src/a.ts", "x");
        _root.Write("src/inner/b.ts", "x");
        _root.Write("other/c.ts", "x");

        // Act
        var result = await RunAsync(new { path = "src", pattern = "inner/*.ts" });

        // Assert
        Lines(result).Should().Equal("src/inner/b.ts");
    }

    [Fact]
    public async Task ExecuteAsync_Gitignore_HidesIgnoredFilesAndFolders()
    {
        // Arrange
        _root.Write(".gitignore", "bin/\n*.log\n!keep.log\n");
        _root.Write("bin/out.dll", "x");
        _root.Write("run.log", "x");
        _root.Write("keep.log", "x");
        _root.Write("src/code.cs", "x");
        _root.Write("src/.gitignore", "generated.cs\n");
        _root.Write("src/generated.cs", "x");

        // Act
        var result = await RunAsync(new { recursive = true });

        // Assert
        Lines(result).Should().Equal(".gitignore", "keep.log", "src/", "src/.gitignore", "src/code.cs");
    }

    [Fact]
    public async Task ExecuteAsync_IncludeIgnored_ListsThemAgain()
    {
        // Arrange
        _root.Write(".gitignore", "bin/\n");
        _root.Write("bin/out.dll", "x");

        // Act
        var result = await RunAsync(new { recursive = true, include_ignored = true });

        // Assert
        Lines(result).Should().Contain("bin/out.dll");
    }

    [Fact]
    public async Task ExecuteAsync_GitFolder_IsAlwaysSkipped()
    {
        // Arrange
        _root.Write(".git/config", "x");
        _root.Write("a.txt", "x");

        // Act
        var result = await RunAsync(new { recursive = true, include_ignored = true });

        // Assert
        Lines(result).Should().Equal("a.txt");
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedFolder_IsNotListedOrEntered()
    {
        // Arrange
        _root.Write("keys/id_rsa", "x");
        _root.Write("a.txt", "x");
        var context = _root.Context.WithSensitive(new SensitivePaths([Path.Combine(_root.Root, "keys")]));

        // Act
        var result = await _sut.ExecuteAsync(TempRoot.Args(new { recursive = true, include_ignored = true }), context, TestContext.Current.CancellationToken);

        // Assert
        Lines(result).Should().Equal("a.txt");
    }

    [Fact]
    public async Task ExecuteAsync_MoreThanTheLimit_StopsAndSaysSo()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            _root.Write($"f{i}.txt", "x");
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
        _root.Write("real/inner.txt", "x");
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "alias"), Path.Combine(_root.Root, "real"));

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
        Directory.CreateDirectory(Path.Combine(_root.Root, "empty"));

        // Act
        var result = await RunAsync(new { path });

        // Assert
        result.Output.Should().Contain(message);
    }

    [Fact]
    public async Task ExecuteAsync_PatternMatchesNothing_SaysSo()
    {
        // Arrange
        _root.Write("a.txt", "x");

        // Act
        var result = await RunAsync(new { pattern = "*.zzz" });

        // Assert
        result.Should().Be(ToolResult.Ok("[Nothing matches the pattern.]"));
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempRoot.Args(args), _root.Context, TestContext.Current.CancellationToken);

    private static string[] Lines(ToolResult result) => result.Output.Split('\n');
}
