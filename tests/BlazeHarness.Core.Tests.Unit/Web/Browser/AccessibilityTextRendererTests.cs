using BlazeHarness.Core.Web.Browser;
using PuppeteerSharp.PageAccessibility;

namespace BlazeHarness.Core.Tests.Unit.Web.Browser;

public sealed class AccessibilityTextRendererTests
{
    [Fact]
    public void Render_NoTree_ReturnsEmpty()
    {
        // Act
        var result = AccessibilityTextRenderer.Render(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_NestedNodes_IndentsChildrenAndSkipsRoot()
    {
        // Arrange
        var root = Node("RootWebArea", "Page", Node("heading", "Title", level: 1), Node("button", "Save"));

        // Act
        var result = AccessibilityTextRenderer.Render(root);

        // Assert
        result.Should().Be("- heading \"Title\" [level=1]\n- button \"Save\"".ReplaceLineEndings());
    }

    [Fact]
    public void Render_TextNode_ShowsQuotedText()
    {
        // Arrange
        var root = Node("RootWebArea", string.Empty, Node("paragraph", string.Empty, Node("StaticText", "Hello \"world\"")));

        // Act
        var result = AccessibilityTextRenderer.Render(root);

        // Assert
        result.Should().Be("- paragraph\n  - text: \"Hello \\\"world\\\"\"".ReplaceLineEndings());
    }

    [Fact]
    public void Render_StatesAndValue_AreShown()
    {
        // Arrange
        var field = Node("textbox", "Name");
        field.Value = "Ada";
        field.Required = true;
        field.Focused = true;
        var box = Node("checkbox", "Agree");
        box.Checked = CheckedState.True;
        var root = Node("RootWebArea", string.Empty, field, box);

        // Act
        var result = AccessibilityTextRenderer.Render(root);

        // Assert
        result.Should().Contain("- textbox \"Name\"").And.Contain("[required]").And.Contain("[focused]").And.Contain(": \"Ada\"");
        result.Should().Contain("- checkbox \"Agree\" [checked]");
    }

    [Fact]
    public void Render_VeryDeepTree_StopsAtDepthLimit()
    {
        // Arrange
        var node = Node("group", "leaf");
        for (var i = 0; i < 100; i++)
        {
            node = Node("group", "level" + i, node);
        }

        // Act
        var result = AccessibilityTextRenderer.Render(Node("RootWebArea", string.Empty, node));

        // Assert
        result.Split(Environment.NewLine).Should().HaveCount(40);
    }

    private static SerializedAXNode Node(string role, string name, params SerializedAXNode[] children) =>
        new() { Role = role, Name = name, Children = children, Level = 0 };

    private static SerializedAXNode Node(string role, string name, int level) =>
        new() { Role = role, Name = name, Children = [], Level = level };
}
