using BlazeHarness.Core.Web.Content;
using HtmlAgilityPack;

namespace BlazeHarness.Core.Tests.Unit.Web.Content;

public sealed class HtmlToMarkdownTests
{
    [Fact]
    public void Convert_Page_KeepsStructureAndDropsFurniture()
    {
        // Arrange
        const string Html = """
            <html><head><title>My Page</title><script>evil()</script></head><body>
            <nav>Menu</nav>
            <main><h1>Heading</h1><p>Hello <a href="/x">link</a> and <strong>bold</strong>.</p>
            <ul><li>one</li><li>two</li></ul><pre><code class="language-cs">var a = 1;</code></pre></main>
            <footer>Copyright</footer></body></html>
            """;

        // Act
        var result = Convert(Html);

        // Assert
        result.Title.Should().Be("My Page");
        result.Text.Should().Contain("# Heading").And.Contain("[link](https://example.com/x)").And.Contain("**bold**");
        result.Text.Should().Contain("- one").And.Contain("```cs\nvar a = 1;\n```");
        result.Text.Should().NotContain("Menu").And.NotContain("Copyright").And.NotContain("evil");
        result.Headings.Should().Equal("# Heading");
    }

    [Fact]
    public void Convert_Table_BecomesPipeTable()
    {
        // Act
        var result = Convert("<body><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table></body>");

        // Assert
        result.Text.Should().Contain("| A | B |").And.Contain("| --- | --- |").And.Contain("| 1 | 2 |");
    }

    [Fact]
    public void Convert_ScriptDrivenPage_AddsNotice()
    {
        // Act
        var result = Convert("<body><div id=\"root\"></div><script src=a></script><script src=b></script><script src=c></script></body>");

        // Assert
        result.Notice.Should().Contain("JavaScript");
    }

    [Fact]
    public void Convert_HiddenElement_Dropped()
    {
        // Act
        var result = Convert("<body><p>shown</p><p style=\"display:none\">secret</p><p hidden>also</p></body>");

        // Assert
        result.Text.Should().Be("shown");
    }

    private static ExtractedDocument Convert(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return HtmlToMarkdown.Convert(document, new Uri("https://example.com/dir/page"));
    }
}
