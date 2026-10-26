using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ShowImageToolTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly TempWorkspace _workspace = new();
    private readonly ShowImageTool _sut = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_PngInTheFolder_ConfirmsItIsShown()
    {
        // Arrange
        _workspace.Write("out/chart.png", PngBytes);

        // Act
        var result = await RunAsync("out/chart.png");

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("out/chart.png").And.Contain("image/png");
    }

    [Fact]
    public async Task ExecuteAsync_Svg_IsShown()
    {
        // Arrange
        _workspace.Write("logo.svg", "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        // Act
        var result = await RunAsync("logo.svg");

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("image/svg+xml");
    }

    [Fact]
    public async Task ExecuteAsync_Avif_IsShown()
    {
        // Arrange
        _workspace.Write("photo.avif", [0, 0, 0, 0x1C, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0]);

        // Act
        var result = await RunAsync("photo.avif");

        // Assert
        result.Output.Should().Contain("image/avif");
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("fake.svg")]
    [InlineData("missing.png")]
    public async Task ExecuteAsync_NotAnImageOrMissing_IsAnError(string path)
    {
        // Arrange
        _workspace.Write("notes.txt", "just text");
        _workspace.Write("fake.svg", "just text, no element");

        // Act
        var result = await RunAsync(path);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PathOutsideTheFolder_IsAnError()
    {
        // Act
        var result = await RunAsync("../outside.png");

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ImageOverTheSizeCap_IsAnError()
    {
        // Arrange
        var big = new byte[(10 * 1024 * 1024) + 1];
        PngBytes.CopyTo(big, 0);
        _workspace.Write("big.png", big);

        // Act
        var result = await RunAsync("big.png");

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("larger than");
    }

    [Fact]
    public void Properties_Always_AreReadOnly()
    {
        // Assert
        _sut.IsReadOnly.Should().BeTrue();
        _sut.Name.Should().Be("show_image");
    }

    private Task<ToolResult> RunAsync(string path)
    {
        return _sut.ExecuteAsync(TempWorkspace.Args(new { path }), _workspace.Context, TestContext.Current.CancellationToken);
    }
}
