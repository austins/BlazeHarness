using BlazeHarness.Core.Mcp;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpResultTextTests
{
    [Fact]
    public void Convert_TextBlocks_AreJoinedByNewlines()
    {
        // Arrange
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "one" }, new TextContentBlock { Text = "two" }] };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Be("one\ntwo");
        converted.IsError.Should().BeFalse();
    }

    [Fact]
    public void Convert_ServerError_IsAnErrorResult()
    {
        // Arrange
        var result = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "no such file" }] };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.IsError.Should().BeTrue();
        converted.Output.Should().Be("no such file");
    }

    [Fact]
    public void Convert_Image_SaysItWasLeftOut()
    {
        // Arrange
        var result = new CallToolResult { Content = [ImageContentBlock.FromBytes(new byte[] { 1, 2, 3 }, "image/png")] };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Contain("image/png").And.Contain("not shown to the model");
    }

    [Fact]
    public void Convert_NoContentButStructuredContent_UsesTheJson()
    {
        // Arrange
        var result = new CallToolResult { Content = [], StructuredContent = JsonSerializer.SerializeToElement(new JsonObject { ["n"] = 5 }) };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Be("""{"n":5}""");
    }

    [Theory]
    [InlineData(false, "The tool returned no output.")]
    [InlineData(true, "The tool failed without saying why.")]
    public void Convert_Nothing_SaysSo(bool isError, string expected)
    {
        // Arrange
        var result = new CallToolResult { Content = [], IsError = isError };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Be(expected);
        converted.IsError.Should().Be(isError);
    }
}
