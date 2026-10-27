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

    [Fact]
    public void Merge_WholeFile_AddsNewServersAndKeepsTheRest()
    {
        // Act
        var result = McpConfigEditor.Merge(Original, """{"mcpServers": {"c": {"command": "y", "args": ["--flag"]}}}""");

        // Assert
        result.Error.Should().BeNull();
        result.Added.Should().Equal("c");
        result.Replaced.Should().BeEmpty();
        var root = JsonNode.Parse(result.Text!)!;
        root["note"]!.GetValue<string>().Should().Be("keep");
        root["mcpServers"]!["a"]!["extra"]!["deep"]!.ToJsonString().Should().Be("[1,2]");
        McpConfigParser.Parse(result.Text!).Servers.Select(s => s.Name).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Merge_SameName_ReplacesTheServer()
    {
        // Act
        var result = McpConfigEditor.Merge(Original, """{"servers": {"b": {"command": "other"}}}""");

        // Assert
        result.Replaced.Should().Equal("b");
        result.Added.Should().BeEmpty();
        var b = McpConfigParser.Parse(result.Text!).Servers.Single(s => s.Name == "b");
        b.Command.Should().Be("other");
    }

    [Theory]
    [InlineData("""{"c": {"command": "y"}}""")]
    [InlineData("\"c\": {\"command\": \"y\"}")]
    [InlineData("\"c\": {\"command\": \"y\"},")]
    public void Merge_BareMapOfServers_IsAccepted(string snippet)
    {
        // Act
        var result = McpConfigEditor.Merge(string.Empty, snippet);

        // Assert
        result.Added.Should().Equal("c");
        McpConfigParser.Parse(result.Text!).Servers.Should().ContainSingle().Which.Command.Should().Be("y");
    }

    [Fact]
    public void Merge_FileWithoutAServerMap_CreatesOne()
    {
        // Act
        var result = McpConfigEditor.Merge("""{"note": 1}""", """{"c": {"command": "y"}}""");

        // Assert
        JsonNode.Parse(result.Text!)!["mcpServers"]!["c"].Should().NotBeNull();
        JsonNode.Parse(result.Text!)!["note"]!.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"c": 5}""")]
    [InlineData("""{"command": "y"}""")]
    [InlineData("[1]")]
    public void Merge_SnippetWithoutServers_ReportsAnError(string snippet)
    {
        // Act
        var result = McpConfigEditor.Merge(Original, snippet);

        // Assert
        result.Text.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Merge_FileThatIsNotJson_ReportsAnErrorInsteadOfOverwritingIt()
    {
        // Act
        var result = McpConfigEditor.Merge("{ broken", """{"c": {"command": "y"}}""");

        // Assert
        result.Text.Should().BeNull();
        result.Error.Should().Contain("Fix the file first");
    }
}
