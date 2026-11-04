using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Tools.Files;

public sealed class ReadFileToolTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ReadFileTool _sut = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ExecuteAsync_SmallFile_ReturnsContentUnchanged()
    {
        // Arrange
        _root.Write("a.txt", "one\ntwo\nthree");

        // Act
        var result = await RunAsync(new { path = "a.txt" });

        // Assert
        result.Should().Be(ToolResult.Ok("one\ntwo\nthree"));
    }

    [Fact]
    public async Task ExecuteAsync_OffsetAndLimit_ReturnsThatRangeWithAContinueHint()
    {
        // Arrange
        _root.Write("a.txt", string.Join('\n', Enumerable.Range(1, 10).Select(i => "line" + i)));

        // Act
        var result = await RunAsync(new { path = "a.txt", offset = 3, limit = 2 });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Be("line3\nline4\n[Showing lines 3-4. Call again with offset=5 to continue.]");
    }

    [Fact]
    public async Task ExecuteAsync_RangeReachingTheEnd_ReportsTheRangeWithoutContinueHint()
    {
        // Arrange
        _root.Write("a.txt", "a\nb\nc\nd");

        // Act
        var result = await RunAsync(new { path = "a.txt", offset = 3 });

        // Assert
        result.Output.Should().Be("c\nd\n[Showing lines 3-4 of 4.]");
    }

    [Fact]
    public async Task ExecuteAsync_FileOverTheDefaultLimit_StopsAndSaysWhere()
    {
        // Arrange
        _root.Write("big.txt", string.Join('\n', Enumerable.Range(1, 2500).Select(i => "x" + i)));

        // Act
        var result = await RunAsync(new { path = "big.txt" });

        // Assert
        result.Output.Should().EndWith("[Showing lines 1-2000. Call again with offset=2001 to continue.]");
    }

    [Fact]
    public async Task ExecuteAsync_ManyLongLines_StaysWithinTheOutputBudget()
    {
        // Arrange
        _root.Write("wide.txt", string.Join('\n', Enumerable.Range(1, 100).Select(_ => new string('w', 1500))));

        // Act
        var result = await RunAsync(new { path = "wide.txt" });

        // Assert
        result.Output.Length.Should().BeLessThan(ToolSet.MaxOutputCharacters);
        result.Output.Should().Contain("Call again with offset=");
    }

    [Fact]
    public async Task ExecuteAsync_VeryLongLine_IsCut()
    {
        // Arrange
        _root.Write("a.txt", new string('z', 5000));

        // Act
        var result = await RunAsync(new { path = "a.txt" });

        // Assert
        result.Output.Should().EndWith("[line cut]");
        result.Output.Length.Should().BeLessThan(2100);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFile_SaysSo()
    {
        // Arrange
        _root.Write("empty.txt", string.Empty);

        // Act
        var result = await RunAsync(new { path = "empty.txt" });

        // Assert
        result.Should().Be(ToolResult.Ok("[empty.txt is empty.]"));
    }

    [Fact]
    public async Task ExecuteAsync_OffsetPastTheEnd_IsAnError()
    {
        // Arrange
        _root.Write("a.txt", "a\nb");

        // Act
        var result = await RunAsync(new { path = "a.txt", offset = 9 });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("past the end").And.Contain("2 lines");
    }

    [Fact]
    public async Task ExecuteAsync_BinaryFile_IsRefused()
    {
        // Arrange
        _root.Write("data.bin", new byte[] { 1, 2, 0, 3 });

        // Act
        var result = await RunAsync(new { path = "data.bin" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("binary");
    }

    [Fact]
    public async Task ExecuteAsync_PngFile_ReturnsThePicture()
    {
        // Arrange
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
        _root.Write("shot.png", png);

        // Act
        var result = await RunAsync(new { path = "shot.png" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("shot.png").And.Contain("image/png");
        result.Image.Should().NotBeNull();
        result.Image.MediaType.Should().Be("image/png");
        result.Image.Data.ToArray().Should().Equal(png);
    }

    [Fact]
    public async Task ExecuteAsync_ImageTooLarge_IsRefused()
    {
        // Arrange
        var big = new byte[ImageFiles.MaxViewableBytes + 1];
        "GIF89a"u8.CopyTo(big);
        _root.Write("big.gif", big);

        // Act
        var result = await RunAsync(new { path = "big.gif" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Image.Should().BeNull();
        result.Output.Should().Contain("big.gif");
    }

    [Fact]
    public async Task ExecuteAsync_Utf16FileWithBom_IsDecoded()
    {
        // Arrange
        var path = Path.Combine(_root.Root, "u16.txt");
        await File.WriteAllTextAsync(path, "héllo\nwörld", System.Text.Encoding.Unicode, TestContext.Current.CancellationToken);

        // Act
        var result = await RunAsync(new { path = "u16.txt" });

        // Assert
        result.Output.Should().Be("héllo\nwörld");
    }

    [Fact]
    public async Task ExecuteAsync_WindowsLineEndings_AreReturnedAsLines()
    {
        // Arrange
        _root.Write("crlf.txt", "a\r\nb\r\n");

        // Act
        var result = await RunAsync(new { path = "crlf.txt" });

        // Assert
        result.Output.Should().Be("a\nb");
    }

    [Fact]
    public async Task ExecuteAsync_HugeSingleLine_IsCutInsteadOfBufferedWhole()
    {
        // Arrange
        _root.Write("one-line.txt", new string('x', 3 * 1024 * 1024));

        // Act
        var result = await RunAsync(new { path = "one-line.txt" });

        // Assert
        result.Output.Should().Be(new string('x', 2000) + " [line cut]");
    }

    [Fact(Skip = "Named pipes are a Unix feature.", SkipUnless = nameof(TempRoot.CanMakeFifo), SkipType = typeof(TempRoot))]
    public async Task ExecuteAsync_NamedPipe_IsNotOpened()
    {
        // Arrange
        _root.MakeFifo("pipe");

        // Act
        var result = await RunAsync(new { path = "pipe" }).WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Ok("[pipe is empty.]"));
    }

    [Theory]
    [InlineData("missing.txt", "does not exist")]
    [InlineData("sub", "is a folder")]
    [InlineData("../x.txt", "outside the working folder")]
    public async Task ExecuteAsync_BadPath_IsAnError(string path, string message)
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_root.Root, "sub"));

        // Act
        var result = await RunAsync(new { path });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain(message);
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempRoot.Args(args), _root.Context, TestContext.Current.CancellationToken);
}
