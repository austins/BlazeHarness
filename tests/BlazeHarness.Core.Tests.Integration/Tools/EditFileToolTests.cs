using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class EditFileToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly EditFileTool _sut = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_UniqueMatch_ReplacesIt()
    {
        // Arrange
        _workspace.Write("a.txt", "alpha\nbeta\ngamma\n");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = "beta", new_string = "BETA" });

        // Assert
        result.Should().Be(ToolResult.Ok("Edited a.txt: replaced 1 occurrence."));
        _workspace.Read("a.txt").Should().Be("alpha\nBETA\ngamma\n");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMatchesWithoutReplaceAll_ChangesNothing()
    {
        // Arrange
        _workspace.Write("a.txt", "x x x");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = "x", new_string = "y" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("3 times").And.Contain("replace_all");
        _workspace.Read("a.txt").Should().Be("x x x");
    }

    [Fact]
    public async Task ExecuteAsync_ReplaceAll_ReplacesEveryOccurrence()
    {
        // Arrange
        _workspace.Write("a.txt", "x x x");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = "x", new_string = "yy", replace_all = true });

        // Assert
        result.Output.Should().Be("Edited a.txt: replaced 3 occurrences.");
        _workspace.Read("a.txt").Should().Be("yy yy yy");
    }

    [Fact]
    public async Task ExecuteAsync_NoMatch_IsAnErrorAndChangesNothing()
    {
        // Arrange
        _workspace.Write("a.txt", "abc");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = "zzz", new_string = "y" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("not found");
        _workspace.Read("a.txt").Should().Be("abc");
    }

    [Fact]
    public async Task ExecuteAsync_CrlfFileAndLfOldString_MatchesAndKeepsCrlf()
    {
        // Arrange
        var path = _workspace.Write("crlf.txt", "one\r\ntwo\r\nthree\r\n");

        // Act
        var result = await RunAsync(new { path = "crlf.txt", old_string = "one\ntwo", new_string = "1\n2\n2b" });

        // Assert
        result.IsError.Should().BeFalse();
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("1\r\n2\r\n2b\r\nthree\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_LfFileAndCrlfOldString_StillMatchesAndStaysLf()
    {
        // Arrange
        _workspace.Write("lf.txt", "one\ntwo\n");

        // Act
        var result = await RunAsync(new { path = "lf.txt", old_string = "one\r\ntwo", new_string = "x" });

        // Assert
        result.IsError.Should().BeFalse();
        _workspace.Read("lf.txt").Should().Be("x\n");
    }

    [Fact]
    public async Task ExecuteAsync_FileWithBom_KeepsTheBom()
    {
        // Arrange
        var path = Path.Combine(_workspace.Root, "bom.txt");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. "hello"u8.ToArray()], TestContext.Current.CancellationToken);

        // Act
        await RunAsync(new { path = "bom.txt", old_string = "hello", new_string = "bye" });

        // Assert
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal([0xEF, 0xBB, 0xBF, .. "bye"u8.ToArray()]);
    }

    [Theory]
    [InlineData("", "x", "empty")]
    [InlineData("same", "same", "the same")]
    public async Task ExecuteAsync_UselessEdit_IsAnError(string oldText, string newText, string message)
    {
        // Arrange
        _workspace.Write("a.txt", "same");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = oldText, new_string = newText });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain(message);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidUtf8_IsRefusedAndLeftAlone()
    {
        // Arrange
        var path = _workspace.Write("bad.txt", new byte[] { (byte)'a', 0xC3, 0x28, (byte)'b' });

        // Act
        var result = await RunAsync(new { path = "bad.txt", old_string = "a", new_string = "c" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("not valid UTF-8");
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal((byte)'a', 0xC3, 0x28, (byte)'b');
    }

    [Theory]
    [InlineData("missing.txt", "does not exist")]
    [InlineData("../x.txt", "outside the working folder")]
    public async Task ExecuteAsync_BadPath_IsAnError(string path, string message)
    {
        // Act
        var result = await RunAsync(new { path, old_string = "a", new_string = "b" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain(message);
    }

    [Fact]
    public async Task ExecuteAsync_SameTextExceptLineEndings_IsAnError()
    {
        // Arrange
        _workspace.Write("a.txt", "a\nb\n");

        // Act
        var result = await RunAsync(new { path = "a.txt", old_string = "a\r\nb", new_string = "a\nb" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("the same");
    }

    [Fact]
    public async Task ExecuteAsync_MixedLineEndings_KeepsTheOtherLinesAsTheyWere()
    {
        // Arrange
        var path = _workspace.Write("mixed.txt", "one\r\ntwo\nthree\r\n");

        // Act
        var result = await RunAsync(new { path = "mixed.txt", old_string = "two", new_string = "2" });

        // Assert
        result.IsError.Should().BeFalse();
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("one\r\n2\nthree\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_FileOverTheSizeLimit_IsRefusedAndLeftAlone()
    {
        // Arrange
        var path = _workspace.Write("big.txt", new string('a', (10 * 1024 * 1024) + 1));

        // Act
        var result = await RunAsync(new { path = "big.txt", old_string = "a", new_string = "b" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("larger than 10 MiB");
        new FileInfo(path).Length.Should().Be((10 * 1024 * 1024) + 1);
    }

    [Fact(Skip = "Named pipes are a Unix feature.", SkipUnless = nameof(TempWorkspace.CanMakeFifo), SkipType = typeof(TempWorkspace))]
    public async Task ExecuteAsync_NamedPipe_IsRefusedWithoutBlocking()
    {
        // Arrange
        _workspace.MakeFifo("pipe");

        // Act
        var result = await RunAsync(new { path = "pipe", old_string = "a", new_string = "b" }).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("is empty");
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);
}
