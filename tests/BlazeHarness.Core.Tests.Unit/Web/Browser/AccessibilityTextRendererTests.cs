using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Web.Browser;

public sealed class AccessibilityTextRendererTests
{
    [Fact]
    public void Render_NoTree_ReturnsEmpty()
    {
        // Act
        var result = AccessibilityTextRenderer.Render(default);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_NestedNodes_IndentsChildrenAndSkipsRoot()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "name": { "value": "Page" }, "childIds": ["2", "3"] }""",
            """{ "nodeId": "2", "role": { "value": "heading" }, "name": { "value": "Title" }, "properties": [{ "name": "level", "value": { "value": 1 } }], "backendDOMNodeId": 7 }""",
            """{ "nodeId": "3", "role": { "value": "button" }, "name": { "value": "Save" }, "backendDOMNodeId": 8 }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Be("- heading \"Title\" [level=1]\n- button \"Save\" [ref=e8]".ReplaceLineEndings());
    }

    [Fact]
    public void Render_TextNode_ShowsQuotedText()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2"] }""",
            """{ "nodeId": "2", "role": { "value": "paragraph" }, "childIds": ["3"] }""",
            """{ "nodeId": "3", "role": { "value": "StaticText" }, "name": { "value": "Hello \"world\"" } }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Be("- paragraph\n  - text: \"Hello \\\"world\\\"\"".ReplaceLineEndings());
    }

    [Fact]
    public void Render_StatesAndValue_AreShown()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2", "3"] }""",
            """
            { "nodeId": "2", "role": { "value": "textbox" }, "name": { "value": "Name" }, "value": { "value": "Ada" }, "backendDOMNodeId": 5,
              "properties": [{ "name": "required", "value": { "value": true } }, { "name": "focused", "value": { "value": true } }, { "name": "invalid", "value": { "value": false } }] }
            """,
            """{ "nodeId": "3", "role": { "value": "checkbox" }, "name": { "value": "Agree" }, "backendDOMNodeId": 6, "properties": [{ "name": "checked", "value": { "value": "true" } }] }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Contain("- textbox \"Name\"").And.Contain("[required]").And.Contain("[focused]").And.Contain("[ref=e5]").And.Contain(": \"Ada\"").And.NotContain("invalid");
        result.Should().Contain("- checkbox \"Agree\" [checked] [ref=e6]");
    }

    [Fact]
    public void Render_LinkWithUrl_ShowsUrlAndRef()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2"] }""",
            """{ "nodeId": "2", "role": { "value": "link" }, "name": { "value": "Docs" }, "backendDOMNodeId": 9, "properties": [{ "name": "url", "value": { "value": "http://localhost/docs" } }] }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Be("- link \"Docs\" [url=http://localhost/docs] [ref=e9]");
    }

    [Fact]
    public void Render_IgnoredAndGenericNodes_PassTheirChildrenThrough()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2"] }""",
            """{ "nodeId": "2", "role": { "value": "generic" }, "childIds": ["3"] }""",
            """{ "nodeId": "3", "ignored": true, "role": { "value": "none" }, "childIds": ["4"] }""",
            """{ "nodeId": "4", "role": { "value": "button" }, "name": { "value": "Go" }, "backendDOMNodeId": 3 }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Be("- button \"Go\" [ref=e3]");
    }

    [Fact]
    public void Render_FocusableGenericNode_GetsRefAndKeepsRole()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2"] }""",
            """{ "nodeId": "2", "role": { "value": "generic" }, "name": { "value": "Card" }, "backendDOMNodeId": 4, "properties": [{ "name": "focusable", "value": { "value": true } }] }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().Be("- generic \"Card\" [ref=e4]");
    }

    [Fact]
    public void Render_EmptyNonInteractiveNode_IsDropped()
    {
        // Arrange
        var nodes = Tree(
            """{ "nodeId": "1", "role": { "value": "RootWebArea" }, "childIds": ["2"] }""",
            """{ "nodeId": "2", "role": { "value": "paragraph" } }""");

        // Act
        var result = AccessibilityTextRenderer.Render(nodes);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_VeryDeepTree_StopsAtDepthLimit()
    {
        // Arrange
        var lines = new List<string> { """{ "nodeId": "0", "role": { "value": "RootWebArea" }, "childIds": ["1"] }""" };
        for (var i = 1; i <= 100; i++)
        {
            lines.Add($$"""{ "nodeId": "{{i}}", "role": { "value": "group" }, "name": { "value": "level{{i}}" }, "childIds": ["{{i + 1}}"] }""");
        }

        // Act
        var result = AccessibilityTextRenderer.Render(Tree([.. lines]));

        // Assert
        result.Split(Environment.NewLine).Should().HaveCount(40);
    }

    private static JsonElement Tree(params string[] nodes)
    {
        using var document = JsonDocument.Parse("[" + string.Join(",", nodes) + "]");
        return document.RootElement.Clone();
    }
}
