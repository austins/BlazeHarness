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
    public void Upsert_NewServer_IsAddedAndTheRestIsKept()
    {
        // Arrange
        var server = new McpServerConfig("c", McpTransport.Stdio, "npx", ["-y", "pkg"], new Dictionary<string, string> { ["K"] = "v" }, "/work", null, new Dictionary<string, string>(), false);

        // Act
        var result = McpConfigEditor.Upsert(Original, server);

        // Assert
        result.Error.Should().BeNull();
        result.Added.Should().Equal("c");
        result.Replaced.Should().BeEmpty();
        var root = JsonNode.Parse(result.Text!)!;
        root["note"]!.GetValue<string>().Should().Be("keep");
        root["mcpServers"]!["a"]!["extra"]!["deep"]!.ToJsonString().Should().Be("[1,2]");
        var servers = McpConfigParser.Parse(result.Text!).Servers;
        servers.Select(s => s.Name).Should().Equal("a", "b", "c");
        var c = servers[2];
        c.Command.Should().Be("npx");
        c.Cwd.Should().Be("/work");
        c.Args.Should().Equal("-y", "pkg");
        c.Env["K"].Should().Be("v");
    }

    [Fact]
    public void Upsert_ExistingServer_KeepsUnknownFieldsAndDisabled()
    {
        // Arrange
        var server = new McpServerConfig("a", McpTransport.Stdio, "other", [], new Dictionary<string, string>(), null, null, new Dictionary<string, string>(), false);

        // Act
        var result = McpConfigEditor.Upsert(Original, server);

        // Assert
        result.Replaced.Should().Equal("a");
        result.Added.Should().BeEmpty();
        var a = JsonNode.Parse(result.Text!)!["mcpServers"]!["a"]!.AsObject();
        a["command"]!.GetValue<string>().Should().Be("other");
        a["extra"]!["deep"]!.ToJsonString().Should().Be("[1,2]");
        a["disabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Upsert_SwitchingToARemoteServer_DropsTheLocalFields()
    {
        // Arrange
        var server = new McpServerConfig("a", McpTransport.Sse, null, [], new Dictionary<string, string>(), null, "https://x.test/sse", new Dictionary<string, string> { ["Authorization"] = "Bearer x" }, false);

        // Act
        var result = McpConfigEditor.Upsert(Original, server);

        // Assert
        var a = JsonNode.Parse(result.Text!)!["mcpServers"]!["a"]!.AsObject();
        a.ContainsKey("command").Should().BeFalse();
        a["type"]!.GetValue<string>().Should().Be("sse");
        var parsed = McpConfigParser.Parse(result.Text!).Servers.Single(s => s.Name == "a");
        parsed.Transport.Should().Be(McpTransport.Sse);
        parsed.Headers["Authorization"].Should().Be("Bearer x");
    }

    [Fact]
    public void Upsert_FileWithoutAServerMap_CreatesOne()
    {
        // Arrange
        var server = new McpServerConfig("c", McpTransport.Http, null, [], new Dictionary<string, string>(), null, "https://x.test", new Dictionary<string, string>(), false);

        // Act
        var result = McpConfigEditor.Upsert("""{"note": 1}""", server);

        // Assert
        JsonNode.Parse(result.Text!)!["mcpServers"]!["c"].Should().NotBeNull();
        JsonNode.Parse(result.Text!)!["note"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Upsert_FileThatIsNotJson_ReportsAnErrorInsteadOfOverwritingIt()
    {
        // Arrange
        var server = new McpServerConfig("c", McpTransport.Stdio, "y", [], new Dictionary<string, string>(), null, null, new Dictionary<string, string>(), false);

        // Act
        var result = McpConfigEditor.Upsert("{ broken", server);

        // Assert
        result.Text.Should().BeNull();
        result.Error.Should().Contain("Fix the file first");
    }

    [Fact]
    public void Remove_ExistingServer_TakesItOutAndKeepsTheRest()
    {
        // Act
        var text = McpConfigEditor.Remove(Original, "a");

        // Assert
        McpConfigParser.Parse(text!).Servers.Select(s => s.Name).Should().Equal("b");
        JsonNode.Parse(text!)!["note"]!.GetValue<string>().Should().Be("keep");
    }

    [Theory]
    [InlineData("{ broken", "a")]
    [InlineData("""{"mcpServers": {"a": {"command": "x"}}}""", "missing")]
    public void Remove_NoSuchServerOrBadFile_ReturnsNull(string json, string server)
    {
        // Act
        var text = McpConfigEditor.Remove(json, server);

        // Assert
        text.Should().BeNull();
    }
}
