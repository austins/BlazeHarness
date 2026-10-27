using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpToolTests
{
    private readonly IMcpConnection _connection = Substitute.For<IMcpConnection>();

    [Fact]
    public void Constructor_FillsInTheToolFromTheServersDescription()
    {
        // Arrange
        var info = new McpToolInfo("read", "Reads a thing.", """{"type":"object","properties":{"id":{"type":"string"}}}""", ReadOnlyHint: true, DestructiveHint: false);

        // Act
        var sut = new McpTool("files__read", "files", info, _connection);

        // Assert
        sut.Name.Should().Be("files__read");
        sut.ServerName.Should().Be("files");
        sut.RemoteName.Should().Be("read");
        sut.Description.Should().Be("Reads a thing.");
        sut.ReadOnlyHint.Should().BeTrue();
        sut.DestructiveHint.Should().BeFalse();
        sut.IsReadOnly.Should().BeFalse("a hint from the server is not proof");
        ((ITool)sut).RunsCommands.Should().BeFalse();
        sut.InputSchema.Should().Contain("\"id\"");
    }

    [Fact]
    public void TrimDescription_LongText_IsCollapsedAndCapped()
    {
        // Arrange
        var text = "One\n\n  two   " + new string('x', 600);

        // Act
        var trimmed = McpTool.TrimDescription(text);

        // Assert
        trimmed.Should().StartWith("One two xxx");
        trimmed.Length.Should().Be(McpTool.MaxDescriptionCharacters);
        trimmed.Should().EndWith("…");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void TrimDescription_Blank_IsEmpty(string? text)
    {
        // Act and assert
        McpTool.TrimDescription(text).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("5")]
    public void NormalizeSchema_MissingOrBroken_IsAnEmptyObjectSchema(string? schema)
    {
        // Act
        var normalized = McpTool.NormalizeSchema(schema);

        // Assert
        normalized.Should().Be("""{"type":"object","properties":{}}""");
    }

    [Fact]
    public void NormalizeSchema_ObjectWithoutProperties_GetsThem()
    {
        // Act
        var normalized = McpTool.NormalizeSchema("""{"type":"object"}""");

        // Assert
        JsonDocument.Parse(normalized).RootElement.TryGetProperty("properties", out _).Should().BeTrue();
    }

    [Fact]
    public void Summarize_Arguments_ListsThemShortly()
    {
        // Arrange
        var sut = new McpTool("s__t", "s", new McpToolInfo("t", string.Empty, "{}"), _connection);
        using var arguments = JsonDocument.Parse("""{"path":"a.txt","count":3,"text":"line one\nline two"}""");

        // Act
        var summary = sut.Summarize(arguments.RootElement);

        // Assert
        summary.Should().Be("path=a.txt, count=3, text=line one line two");
    }

    [Fact]
    public void Summarize_LongArguments_AreCut()
    {
        // Arrange
        var sut = new McpTool("s__t", "s", new McpToolInfo("t", string.Empty, "{}"), _connection);
        using var arguments = JsonDocument.Parse("{\"a\":\"" + new string('x', 500) + "\",\"b\":\"" + new string('y', 500) + "\",\"c\":\"" + new string('z', 500) + "\",\"d\":\"" + new string('w', 500) + "\"}");

        // Act
        var summary = sut.Summarize(arguments.RootElement);

        // Assert
        summary.Length.Should().BeLessThanOrEqualTo(121);
        summary.Should().Contain("…");
    }

    [Fact]
    public void Summarize_NotAnObject_IsEmpty()
    {
        // Arrange
        var sut = new McpTool("s__t", "s", new McpToolInfo("t", string.Empty, "{}"), _connection);
        using var arguments = JsonDocument.Parse("[1]");

        // Act and assert
        sut.Summarize(arguments.RootElement).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CallsTheServerWithItsOwnNameAndTheArguments()
    {
        // Arrange
        _connection.CallToolAsync("read", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ToolResult.Ok("contents"));
        var sut = new McpTool("files__read", "files", new McpToolInfo("read", string.Empty, "{}"), _connection);
        var context = new ToolContext(Path.GetTempPath());

        // Act
        var result = await sut.ExecuteAsync(ToolArguments.Parse("""{"id":"7"}"""), context, TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Be("contents");
        await _connection.Received(1).CallToolAsync("read", """{"id":"7"}""", Arg.Any<CancellationToken>());
    }
}
