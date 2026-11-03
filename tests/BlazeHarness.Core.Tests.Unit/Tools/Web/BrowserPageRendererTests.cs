using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserPageRendererTests
{
    [Fact]
    public void Render_ClippedEmptyPage_SaysSo()
    {
        // Arrange
        var empty = new BrowserPageView(new Uri("http://localhost/"), string.Empty, null, string.Empty, false, []);
        var clipped = new BrowserPageView(new Uri("http://localhost/"), string.Empty, null, "- text", true, []);

        // Act
        var emptyText = BrowserPageRenderer.Render(empty);
        var clippedText = BrowserPageRenderer.Render(clipped);

        // Assert
        emptyText.Should().Contain("(the page shows nothing)");
        clippedText.Should().Contain("the rest was cut");
    }

    [Fact]
    public void Render_Page_MarksContentAsUntrusted()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost/"), "T", 404, "- text", false, []);

        // Act
        var text = BrowserPageRenderer.Render(view);

        // Assert
        text.Should().StartWith("[Untrusted page content").And.Contain("(status 404)").And.NotContain("Problems");
    }
}
