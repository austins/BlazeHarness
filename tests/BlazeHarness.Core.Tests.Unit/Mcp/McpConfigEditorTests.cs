using BlazeHarness.Core.Mcp;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpConfigEditorTests
{
    private const string Original = """
        {"note": "keep", "mcpServers": {"a": {"command": "x", "extra": {"deep": [1, 2]}, "disabled": true}, "b": {"url": "https://x.test", "x-other": 3}}}
        """;

    [Fact]
    public void SetDisabled_False_RemovesTheFieldAndKeepsEverythingElse()
    {
        // Act
        var text = McpConfigEditor.SetDisabled(Original, "a", disabled: false);

        // Assert
        var root = JsonNode.Parse(text!)!;
        root["note"]!.GetValue<string>().Should().Be("keep");
        root["mcpServers"]!["a"]!.AsObject().ContainsKey("disabled").Should().BeFalse();
        root["mcpServers"]!["a"]!["extra"]!["deep"]!.ToJsonString().Should().Be("[1,2]");
        root["mcpServers"]!["b"]!["x-other"]!.GetValue<int>().Should().Be(3);
        McpConfigParser.Parse(text!).Servers.Select(s => s.Disabled).Should().Equal(false, false);
    }

    [Fact]
    public void SetDisabled_True_AddsTheField()
    {
        // Act
        var text = McpConfigEditor.SetDisabled(Original, "b", disabled: true);

        // Assert
        McpConfigParser.Parse(text!).Servers.Select(s => s.Disabled).Should().Equal(true, true);
    }

    [Fact]
    public void SetDisabled_KeepsTheOrderOfServersAndFields()
    {
        // Act
        var text = McpConfigEditor.SetDisabled(Original, "b", disabled: true)!;

        // Assert
        text.IndexOf("\"note\"", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("\"mcpServers\"", StringComparison.Ordinal));
        text.IndexOf("\"a\"", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("\"b\"", StringComparison.Ordinal));
    }

    [Fact]
    public void SetDisabled_TheServersKey_IsEditedInPlace()
    {
        // Act
        var text = McpConfigEditor.SetDisabled("""{"servers": {"a": {"command": "x"}}}""", "a", disabled: true);

        // Assert
        text.Should().Contain("\"servers\"").And.NotContain("mcpServers");
    }

    [Theory]
    [InlineData("{", "a")]
    [InlineData("[]", "a")]
    [InlineData("""{"mcpServers": {"a": {}}}""", "missing")]
    [InlineData("""{"mcpServers": {"a": 1}}""", "a")]
    [InlineData("{}", "a")]
    public void SetDisabled_NothingToChange_ReturnsNull(string json, string server)
    {
        // Act
        var text = McpConfigEditor.SetDisabled(json, server, disabled: true);

        // Assert
        text.Should().BeNull();
    }
}
