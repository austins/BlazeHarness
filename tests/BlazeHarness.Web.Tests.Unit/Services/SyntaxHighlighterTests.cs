using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class SyntaxHighlighterTests
{
    [Fact]
    public void TryHighlight_CSharpCode_WrapsKeywordsStringsAndComments()
    {
        // Act
        var html = SyntaxHighlighter.TryHighlight("public class A { string s = \"x\"; } // note", "csharp");

        // Assert
        html.Should().Contain("<span class=\"tok-keyword\">public</span>")
            .And.Contain("tok-string")
            .And.Contain("<span class=\"tok-comment\">// note</span>");
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("C#")]
    [InlineData("CSharp")]
    [InlineData("csharp title=\"Program.cs\"")]
    [InlineData("csharp{1,3}")]
    public void TryHighlight_LanguageAliases_AreRecognized(string info)
    {
        // Act
        var html = SyntaxHighlighter.TryHighlight("class A { }", info);

        // Assert
        html.Should().Contain("tok-keyword");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-language")]
    [InlineData("cobol")]
    public void TryHighlight_UnknownOrMissingLanguage_ReturnsNull(string? info)
    {
        // Act
        var html = SyntaxHighlighter.TryHighlight("fn main() {}", info);

        // Assert
        html.Should().BeNull();
    }

    [Fact]
    public void TryHighlight_CodeTooLong_ReturnsNull()
    {
        // Arrange
        var code = new string('a', 20_001);

        // Act
        var html = SyntaxHighlighter.TryHighlight(code, "csharp");

        // Assert
        html.Should().BeNull();
    }

    [Fact]
    public void TryHighlight_MarkupInCode_IsEscaped()
    {
        // Act
        var html = SyntaxHighlighter.TryHighlight("var s = \"<script>alert(1)</script>\";", "csharp");

        // Assert
        html.Should().NotContain("<script").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void TryHighlight_JsonWithHtmlLikeText_KeepsAllTextAndEscapesIt()
    {
        // Arrange
        const string Code = "{\n  \"a\": \"<b>\",\n  \"n\": 12\n}";

        // Act
        var html = SyntaxHighlighter.TryHighlight(Code, "json");

        // Assert
        html.Should().NotBeNull();
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty))
            .Should().Be(Code);
    }

    [Fact]
    public void TryHighlightLines_BlockCommentAcrossLines_ColorsEveryLineAndClosesTheSpans()
    {
        // Arrange
        const string Code = "int a; /* first\nsecond\nthird */ int b;";

        // Act
        var lines = SyntaxHighlighter.TryHighlightLines(Code, "cs");

        // Assert
        lines.Should().NotBeNull();
        lines.Should().HaveCount(3);
        lines[1].Should().StartWith("<span class=\"tok-comment\">").And.EndWith("</span>");
        foreach (var line in lines)
        {
            System.Text.RegularExpressions.Regex.Count(line, "<span").Should().Be(System.Text.RegularExpressions.Regex.Count(line, "</span>"));
        }
    }

    [Fact]
    public void TryHighlightLines_Lines_KeepTheTextOfEachLine()
    {
        // Arrange
        const string Code = "a < b\n\nreturn \"x\";";

        // Act
        var lines = SyntaxHighlighter.TryHighlightLines(Code, "cs");

        // Assert
        lines.Should().NotBeNull();
        lines.Select(DiffHtml.TextOf).Should().Equal("a < b", string.Empty, "return \"x\";");
    }

    [Theory]
    [InlineData("rust", "fn main() { let x = 1; }")]
    [InlineData("py", "def main(): return 1")]
    [InlineData("bash", "if true; then echo hi; fi")]
    public void TryHighlight_OtherLanguages_AreColored(string info, string code)
    {
        // Act
        var html = SyntaxHighlighter.TryHighlight(code, info);

        // Assert
        html.Should().Contain("tok-keyword");
    }

    [Fact]
    public void TryHighlightLines_MarkdownStartingInsideCodeFence_ReturnsEveryLine()
    {
        // Arrange
        // A diff hunk can start on the closing fence of a code block, so the fence looks unclosed and is followed by prose.
        var prose = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 40));
        string[] expected = ["```", prose, string.Empty, "## Next steps", "- Run the   tests  again   later."];

        // Act
        var lines = SyntaxHighlighter.TryHighlightLines(string.Join('\n', expected), "md");

        // Assert
        lines.Should().NotBeNull();
        lines.Select(DiffHtml.TextOf).Should().Equal(expected);
    }

    [Fact]
    public void TryHighlightLines_UnknownLanguage_ReturnsNull()
    {
        // Act
        var lines = SyntaxHighlighter.TryHighlightLines("x", "not-a-language");

        // Assert
        lines.Should().BeNull();
    }
}
