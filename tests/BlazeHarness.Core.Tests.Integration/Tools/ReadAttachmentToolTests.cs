using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tests.Integration.Attachments;
using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ReadAttachmentToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly SessionAttachments _attachments;
    private readonly ReadAttachmentTool _sut;

    public ReadAttachmentToolTests()
    {
        _attachments = new SessionAttachments(Path.Combine(_workspace.Root, "attachments"));
        _sut = new ReadAttachmentTool(_attachments);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_ById_ReadsTheFile()
    {
        // Arrange
        var info = await _attachments.SaveAsync("a.txt", "one\ntwo\nthree");

        // Act
        var result = await RunAsync(new { id = info.Id });

        // Assert
        result.Should().Be(ToolResult.Ok("one\ntwo\nthree"));
    }

    [Fact]
    public async Task ExecuteAsync_WithOffsetAndLimit_ReadsAPiece()
    {
        // Arrange
        var info = await _attachments.SaveAsync("a.txt", string.Join('\n', Enumerable.Range(1, 10).Select(i => "line" + i)));

        // Act
        var result = await RunAsync(new { id = info.Id, offset = 3, limit = 2 });

        // Assert
        result.Output.Should().StartWith("line3\nline4").And.Contain("[Showing lines 3-4 of 10. Call again with offset=5 to continue.]");
    }

    [Fact]
    public async Task ExecuteAsync_Image_IsAnError()
    {
        // Arrange
        var info = await _attachments.SaveAsync("pic.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]);

        // Act
        var result = await RunAsync(new { id = info.Id });

        // Assert
        result.Should().Be(ToolResult.Error("'pic.png' is an image, which can't be read as text."));
    }

    [Theory]
    [InlineData("0123abcd")]
    [InlineData("../../etc/passwd")]
    public async Task ExecuteAsync_UnknownOrMalformedId_IsAnError(string id)
    {
        // Act
        var result = await RunAsync(new { id });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("no attachment");
    }

    [Fact]
    public async Task Summarize_KnownId_ShowsTheFileName()
    {
        // Arrange
        var info = await _attachments.SaveAsync("report.txt", "x");
        using var document = JsonDocument.Parse($$"""{"id":"{{info.Id}}"}""");

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().Be("report.txt");
    }

    [Fact]
    public void Properties_Always_AreReadOnlyAndNeedNoApproval()
    {
        // Assert
        _sut.IsReadOnly.Should().BeTrue();
        _sut.Name.Should().Be("read_attachment");
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);
}
