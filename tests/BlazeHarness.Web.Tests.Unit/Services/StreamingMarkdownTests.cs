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

        // Act
        foreach (var chunk in Document.Chunk(7))
        {
            text += new string(chunk);
            sut.Render(text);
        }

        // Assert
        (sut.Settled + sut.Tail).Should().Be(_renderer.ToHtml(Document));
    }

    [Fact]
    public void Render_BlankLineInsideCodeFence_DoesNotSplitTheBlock()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);

        // Act
        sut.Render("```\nline one\n\nline two\n");
        var html = sut.Settled + sut.Tail;

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
        sut.Render("fresh");

        // Assert
        (sut.Settled + sut.Tail).Should().Be(_renderer.ToHtml("fresh"));
    }

    [Fact]
    public void Render_BlankLineAfterWhichTextContinues_KeepsSettledHtmlStable()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);
        sut.Render("first\n\nsec");
        var settled = sut.Settled;

        // Act
        sut.Render("first\n\nsecond word");

        // Assert
        sut.Settled.Should().BeSameAs(settled).And.Contain("first");
        sut.Tail.Should().Contain("second word");
    }

    [Fact]
    public void Render_FenceOfOtherKindInsideFence_DoesNotEndTheBlock()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);

        // Act
        sut.Render("```\n~~~\n\nstill code\n");

        // Assert
        sut.Settled.Should().BeEmpty();
        sut.Tail.Should().Contain("still code");
    }

    [Fact]
    public void Render_ShorterFenceInsideLongerFence_DoesNotEndTheBlock()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);

        // Act
        sut.Render("````\n```\n\nstill code\n");

        // Assert
        sut.Settled.Should().BeEmpty();
    }

    [Fact]
    public void Render_ClosedFence_AllowsSettlingAfterIt()
    {
        // Arrange
        var sut = new StreamingMarkdown(_renderer);

        // Act
        sut.Render("~~~\ncode\n~~~\n\nafter");

        // Assert
        sut.Settled.Should().Contain("code");
        sut.Tail.Should().Contain("after");
    }
}
