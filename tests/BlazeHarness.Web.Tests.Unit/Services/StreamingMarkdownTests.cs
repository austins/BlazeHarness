using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class StreamingMarkdownTests
{
    private const string Document = "# Title\n\nFirst paragraph with **bold**.\n\n```csharp\nvar a = 1;\n\nvar b = 2;\n```\n\n- one\n- two\n\nLast paragraph.";

    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void Render_TextArrivingInChunks_MatchesParsingTheWholeText()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);
        var text = string.Empty;
        string? html = null;

        // Act
        foreach (var chunk in Document.Chunk(7))
        {
            text += new string(chunk);
            html = sut.Render(text);
        }

        // Assert
        html.Should().Be(_renderer.ToHtml(Document));
    }

    [Fact]
    public void Render_BlankLineInsideCodeFence_DoesNotSplitTheBlock()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);

        // Act
        var html = sut.Render("```\nline one\n\nline two\n");

        // Assert
        html.Should().Contain("line one").And.Contain("line two");
        html.Split("<pre>").Length.Should().Be(2);
    }

    [Fact]
    public void Render_TextThatShrinks_StartsOver()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);
        sut.Render("first\n\nsecond\n\nthird");

        // Act
        var html = sut.Render("fresh");

        // Assert
        html.Should().Be(_renderer.ToHtml("fresh"));
    }
}
