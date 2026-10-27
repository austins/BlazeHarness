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
    [InlineData("brainfuck")]
    [InlineData("rust")]
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
}
