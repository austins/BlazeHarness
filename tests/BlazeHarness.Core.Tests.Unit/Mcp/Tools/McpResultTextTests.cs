using BlazeHarness.Core.Mcp.Tools;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Tests.Unit.Mcp.Tools;

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
    public void Convert_Images_ArePassedOnWithTheSniffedType()
    {
        // Arrange
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        var result = new CallToolResult
        {
            Content = [ImageContentBlock.FromBytes(png, "image/jpeg"), new TextContentBlock { Text = "done" }, ImageContentBlock.FromBytes(png, "image/png")],
        };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Images.Should().HaveCount(2).And.OnlyContain(i => i.Content.MediaType == "image/png" && i.Attachment == null);
        converted.Images[0].Content.Data.ToArray().Should().Equal(png);
        converted.Output.Should().Be("[Picture 1 of 2 (image/png, 12 bytes) follows the tool results.]\ndone\n[Picture 2 of 2 (image/png, 12 bytes) follows the tool results.]");
    }

    [Fact]
    public void Convert_UnsupportedImage_IsNamedInstead()
    {
        // Arrange
        var result = new CallToolResult { Content = [ImageContentBlock.FromBytes(new byte[] { 1, 2, 3 }, "image/bmp")] };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Images.Should().BeEmpty();
        converted.Output.Should().Contain("image/bmp").And.Contain("can't be shown");
    }

    [Fact]
    public void Convert_UndecodableImage_SaysSo()
    {
        // Arrange
        var result = new CallToolResult { Content = [new ImageContentBlock { Data = "not base64!"u8.ToArray(), MimeType = "image/png" }] };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Images.Should().BeEmpty();
        converted.Output.Should().Be("[A picture was returned that could not be decoded.]");
    }

    [Fact]
    public void Convert_TextAndDifferentStructuredContent_AppendsTheJson()
    {
        // Arrange
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "five" }], StructuredContent = JsonSerializer.SerializeToElement(new JsonObject { ["n"] = 5 }) };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Be("five\nStructured result:\n{\"n\":5}");
    }

    [Fact]
    public void Convert_TextHoldsTheStructuredContent_LeavesItOut()
    {
        // Arrange
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "{ \"n\": 5 }" }], StructuredContent = JsonSerializer.SerializeToElement(new JsonObject { ["n"] = 5 }) };

        // Act
        var converted = McpResultText.Convert(result);

        // Assert
        converted.Output.Should().Be("{ \"n\": 5 }");
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
