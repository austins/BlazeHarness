using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class WebFetchToolRenderTests
{
    [Fact]
    public void Render_EmptyDocumentWithStart_DoesNotThrow()
    {
        // Arrange
        var page = new CachedPage(new Uri("https://example.com"), new ExtractedDocument(string.Empty, string.Empty, []));

        // Act
        var text = WebFetchTool.Render(page, 5, 1000);

        // Assert
        text.Should().Contain("past the end");
    }

    [Fact]
    public void Render_LongDocument_GivesNextStartChar()
    {
        // Arrange
        var page = new CachedPage(new Uri("https://example.com"), new ExtractedDocument("T", new string('a', 5000), []));

        // Act
        var text = WebFetchTool.Render(page, 0, 1000);

        // Assert
        text.Should().Contain("next_start_char: 1000").And.Contain("Characters: 0-1000 of 5000");
    }
}
