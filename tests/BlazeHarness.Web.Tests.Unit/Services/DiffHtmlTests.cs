using BlazeHarness.Core.Diff;
using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class DiffHtmlTests
{
    [Fact]
    public void Mark_PlainText_WrapsTheRange()
    {
        // Act
        var html = DiffHtml.Mark("let total = 1;", [new TextRange(4, 5)], "w");

        // Assert
        html.Should().Be("let <mark class=\"w\">total</mark> = 1;");
    }

    [Fact]
    public void Mark_RangeAcrossATag_ClosesAndReopensTheMark()
    {
        // Arrange
        const string Html = "<span class=\"tok-keyword\">let</span> total";

        // Act
        var html = DiffHtml.Mark(Html, [new TextRange(1, 5)], "w");

        // Assert
        html.Should().Be("<span class=\"tok-keyword\">l<mark class=\"w\">et</mark></span><mark class=\"w\"> to</mark>tal");
    }

    [Fact]
    public void Mark_EntityCountsAsOneCharacter()
    {
        // Act
        var html = DiffHtml.Mark("a &amp; b &lt; c", [new TextRange(4, 1)], "w");

        // Assert
        html.Should().Be("a &amp; <mark class=\"w\">b</mark> &lt; c");
    }

    [Fact]
    public void Mark_EntityInsideTheRange_StaysWhole()
    {
        // Act
        var html = DiffHtml.Mark("a &amp; b", [new TextRange(0, 3)], "w");

        // Assert
        html.Should().Be("<mark class=\"w\">a &amp;</mark> b");
    }

    [Fact]
    public void Mark_CharacterOutsideTheBasicPlane_CountsAsTwoCharacters()
    {
        // Arrange
        const string Text = "😀 ab";

        // Act
        var html = DiffHtml.Mark(System.Net.WebUtility.HtmlEncode(Text), [new TextRange(3, 2)], "w");

        // Assert
        html.Should().EndWith("<mark class=\"w\">ab</mark>");
    }

    [Fact]
    public void Mark_RangeAtTheEnd_ClosesTheMark()
    {
        // Act
        var html = DiffHtml.Mark("abc", [new TextRange(1, 2)], "w");

        // Assert
        html.Should().Be("a<mark class=\"w\">bc</mark>");
    }

    [Fact]
    public void Mark_SeveralRanges_MarksEach()
    {
        // Act
        var html = DiffHtml.Mark("a b c", [new TextRange(0, 1), new TextRange(4, 1)], "w");

        // Assert
        html.Should().Be("<mark class=\"w\">a</mark> b <mark class=\"w\">c</mark>");
    }

    [Fact]
    public void Mark_NoRanges_ReturnsTheHtmlUnchanged()
    {
        // Act
        var html = DiffHtml.Mark("<b>x</b>", [], "w");

        // Assert
        html.Should().Be("<b>x</b>");
    }

    [Fact]
    public void TextOf_ColoredHtml_ReturnsTheDecodedText()
    {
        // Act
        var text = DiffHtml.TextOf("<span class=\"tok-keyword\">if</span> (a &lt; b)");

        // Assert
        text.Should().Be("if (a < b)");
    }
}
