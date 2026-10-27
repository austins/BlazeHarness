using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class SearchFilesToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly SearchFilesTool _sut = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_Regex_ReturnsPathLineAndTrimmedText()
    {
        // Arrange
        _workspace.Write("src/a.cs", "class A\n    void Run() { }\n");
        _workspace.Write("b.txt", "nothing\n");

        // Act
        var result = await RunAsync(new { pattern = @"void \w+\(" });

        // Assert
        result.Should().Be(ToolResult.Ok("src/a.cs:2: void Run() { }"));
    }

    [Fact]
    public async Task ExecuteAsync_IgnoreCase_MatchesRegardlessOfCase()
    {
        // Arrange
        _workspace.Write("a.txt", "Hello\n");

        // Act
        var strict = await RunAsync(new { pattern = "hello" });
        var loose = await RunAsync(new { pattern = "hello", ignore_case = true });

        // Assert
        strict.Output.Should().Be("[No matches.]");
        loose.Output.Should().Be("a.txt:1: Hello");
    }

    [Fact]
    public async Task ExecuteAsync_Literal_TreatsRegexCharactersAsText()
    {
        // Arrange
        _workspace.Write("a.txt", "a.b\naxb\n");

        // Act
        var result = await RunAsync(new { pattern = "a.b", literal = true });

        // Assert
        result.Output.Should().Be("a.txt:1: a.b");
    }

    [Fact]
    public async Task ExecuteAsync_GlobAndPath_LimitTheFilesSearched()
    {
        // Arrange
        _workspace.Write("src/a.cs", "needle\n");
        _workspace.Write("src/a.md", "needle\n");
        _workspace.Write("other/b.cs", "needle\n");

        // Act
        var result = await RunAsync(new { pattern = "needle", path = "src", glob = "*.cs" });

        // Assert
        result.Output.Should().Be("src/a.cs:1: needle");
    }

    [Fact]
    public async Task ExecuteAsync_PathIsAFile_SearchesJustThatFile()
    {
        // Arrange
        _workspace.Write("a.txt", "needle\n");
        _workspace.Write("b.txt", "needle\n");

        // Act
        var result = await RunAsync(new { pattern = "needle", path = "b.txt" });

        // Assert
        result.Output.Should().Be("b.txt:1: needle");
    }

    [Fact]
    public async Task ExecuteAsync_IgnoredAndBinaryFiles_AreSkipped()
    {
        // Arrange
        _workspace.Write(".gitignore", "build/\n");
        _workspace.Write("build/out.txt", "needle\n");
        _workspace.Write("data.bin", new byte[] { 0, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e' });
        _workspace.Write("src/real.txt", "needle\n");

        // Act
        var result = await RunAsync(new { pattern = "needle" });

        // Assert
        result.Output.Should().Be("src/real.txt:1: needle");
    }

    [Fact]
    public async Task ExecuteAsync_SymlinkedFile_IsNotFollowed()
    {
        // Arrange
        var outside = Directory.CreateTempSubdirectory("bh-outside-");
        try
        {
            var target = Path.Combine(outside.FullName, "secret.txt");
            await File.WriteAllTextAsync(target, "needle\n", TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(Path.Combine(_workspace.Root, "link.txt"), target);

            // Act
            var result = await RunAsync(new { pattern = "needle" });

            // Assert
            result.Output.Should().Be("[No matches.]");
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MoreMatchesThanTheLimit_StopsAndSaysSo()
    {
        // Arrange
        _workspace.Write("a.txt", string.Join('\n', Enumerable.Repeat("hit", 10)));

        // Act
        var result = await RunAsync(new { pattern = "hit", max_results = 4 });

        // Assert
        result.Output.Split('\n').Should().HaveCount(5);
        result.Output.Should().EndWith("[Showing the first 4 matches. Use a narrower path, glob or pattern to see the rest.]");
    }

    [Fact]
    public async Task ExecuteAsync_LongLine_IsCut()
    {
        // Arrange
        _workspace.Write("a.txt", "needle " + new string('x', 1000));

        // Act
        var result = await RunAsync(new { pattern = "needle" });

        // Assert
        result.Output.Should().EndWith("[line cut]");
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedFolder_IsNotSearched()
    {
        // Arrange
        _workspace.Write(".ssh/id_rsa", "PRIVATE KEY\n");
        _workspace.Write("a.txt", "PRIVATE KEY\n");
        var context = _workspace.Context.WithSensitive(new SensitivePaths([Path.Combine(_workspace.Root, ".ssh")]));

        // Act
        var result = await _sut.ExecuteAsync(TempWorkspace.Args(new { pattern = "PRIVATE KEY", include_ignored = true }), context, TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Be("a.txt:1: PRIVATE KEY");
    }

    [Fact]
    public async Task ExecuteAsync_LargeFile_IsSkippedWithANote()
    {
        // Arrange
        _workspace.Write("big.txt", new string('a', (2 * 1024 * 1024) + 10) + "\nneedle\n");
        _workspace.Write("small.txt", "needle\n");

        // Act
        var result = await RunAsync(new { pattern = "needle" });

        // Assert
        result.Output.Should().Be("small.txt:1: needle\n[Skipped files over 2 MiB: 1.]");
    }

    [Theory]
    [InlineData("(unclosed", "not valid")]
    [InlineData("", "empty")]
    public async Task ExecuteAsync_BadPattern_IsAnError(string pattern, string message)
    {
        // Act
        var result = await RunAsync(new { pattern });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain(message);
    }

    [Fact]
    public async Task ExecuteAsync_SlowPattern_ReportsATimeoutInsteadOfHanging()
    {
        // Arrange
        _workspace.Write("a.txt", new string('a', 60) + "!");

        // Act
        var result = await RunAsync(new { pattern = "^(a+)+$" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("took too long");
    }

    [Fact]
    public async Task ExecuteAsync_PathOutside_IsAnError()
    {
        // Act
        var result = await RunAsync(new { pattern = "x", path = ".." });

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact(Skip = "Named pipes are a Unix feature.", SkipUnless = nameof(TempWorkspace.CanMakeFifo), SkipType = typeof(TempWorkspace))]
    public async Task ExecuteAsync_NamedPipe_IsSkippedWithoutBlocking()
    {
        // Arrange
        _workspace.MakeFifo("pipe");
        _workspace.Write("a.txt", "needle\n");

        // Act
        var result = await RunAsync(new { pattern = "needle" }).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Be("a.txt:1: needle");
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);
}
