using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpConfigParserTests
{
    [Fact]
    public void Parse_StdioServer_ReadsEveryField()
    {
        // Arrange
        const string json = """
            {"mcpServers": {"files": {"command": "npx", "args": ["-y", "server"], "env": {"A": "1"}, "cwd": "/work", "disabled": true}}}
            """;

        // Act
        var result = McpConfigParser.Parse(json);

        // Assert
        result.Errors.Should().BeEmpty();
        var server = result.Servers.Should().ContainSingle().Subject;
        server.Name.Should().Be("files");
        server.Transport.Should().Be(McpTransport.Stdio);
        server.Command.Should().Be("npx");
        server.Args.Should().Equal("-y", "server");
        server.Env.Should().Equal(new Dictionary<string, string> { ["A"] = "1" });
        server.Cwd.Should().Be("/work");
        server.Disabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"mcpServers": {"s": {"url": "https://x.test/mcp"}}}""", McpTransport.Http)]
    [InlineData("""{"mcpServers": {"s": {"url": "https://x.test/mcp", "type": "http"}}}""", McpTransport.Http)]
    [InlineData("""{"mcpServers": {"s": {"url": "https://x.test/mcp", "type": "streamable-http"}}}""", McpTransport.Http)]
    [InlineData("""{"mcpServers": {"s": {"url": "https://x.test/sse", "type": "SSE"}}}""", McpTransport.Sse)]
    [InlineData("""{"mcpServers": {"s": {"command": "run", "type": "stdio"}}}""", McpTransport.Stdio)]
    public void Parse_Transport_IsTakenFromTheTypeOrInferred(string json, McpTransport expected)
    {
        // Act
        var result = McpConfigParser.Parse(json);

        // Assert
        result.Errors.Should().BeEmpty();
        result.Servers.Single().Transport.Should().Be(expected);
    }

    [Fact]
    public void Parse_RemoteServerHeaders_AreCaseInsensitive()
    {
        // Act
        var result = McpConfigParser.Parse("""{"mcpServers": {"s": {"url": "https://x.test", "headers": {"Authorization": "Bearer ${TOKEN}"}}}}""");

        // Assert
        result.Servers.Single().Headers["authorization"].Should().Be("Bearer ${TOKEN}");
    }

    [Fact]
    public void Parse_TheServersKey_IsAcceptedToo()
    {
        // Act
        var result = McpConfigParser.Parse("""{"servers": {"s": {"command": "run"}}}""");

        // Assert
        result.Servers.Should().ContainSingle().Which.Name.Should().Be("s");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("{}")]
    [InlineData("""{"mcpServers": {}}""")]
    public void Parse_NothingConfigured_IsEmptyWithoutErrors(string json)
    {
        // Act
        var result = McpConfigParser.Parse(json);

        // Assert
        result.Servers.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{", "not valid JSON")]
    [InlineData("[]", "must be a JSON object")]
    [InlineData("""{"other": 1}""", "no \"mcpServers\"")]
    [InlineData("""{"mcpServers": []}""", "must be an object")]
    [InlineData("""{"mcpServers": {"s": {}}}""", "needs a \"command\"")]
    [InlineData("""{"mcpServers": {"s": {"type": "http"}}}""", "needs a \"url\"")]
    [InlineData("""{"mcpServers": {"s": {"url": "ftp://x"}}}""", "http or https")]
    [InlineData("""{"mcpServers": {"s": {"url": "not a url"}}}""", "http or https")]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "type": "grpc"}}}""", "not supported")]
    [InlineData("""{"mcpServers": {"s": {"command": 1}}}""", "\"command\" must be a string")]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "args": "a"}}}""", "\"args\" must be a list")]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "args": [1]}}}""", "\"args\" must be a list")]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "env": {"A": 1}}}}""", "\"A\" is not a string")]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "disabled": "yes"}}}""", "true or false")]
    [InlineData("""{"mcpServers": {"s": 5}}""", "must be an object")]
    [InlineData("""{"mcpServers": {"bad name": {"command": "x"}}}""", "letters, digits")]
    [InlineData("""{"mcpServers": {"a/b": {"command": "x"}}}""", "letters, digits")]
    [InlineData("""{"mcpServers": {"a__b": {"command": "x"}}}""", "letters, digits")]
    [InlineData("""{"mcpServers": {"a_": {"command": "x"}}}""", "letters, digits")]
    public void Parse_BadInput_ReportsOneErrorAndNoServers(string json, string message)
    {
        // Act
        var result = McpConfigParser.Parse(json);

        // Assert
        result.Servers.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain(message);
    }

    [Fact]
    public void Parse_InvalidJson_NamesTheLine()
    {
        // Act
        var result = McpConfigParser.Parse("{\n\"mcpServers\": {\n\"a\": nope\n}}");

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Contain("line 3");
    }

    [Fact]
    public void Parse_OneBadServer_DoesNotHideTheOthers()
    {
        // Act
        var result = McpConfigParser.Parse("""{"mcpServers": {"good": {"command": "x"}, "bad": {}, "also": {"url": "https://x.test"}}}""");

        // Assert
        result.Servers.Select(s => s.Name).Should().Equal("good", "also");
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Server \"bad\"");
    }

    [Fact]
    public void Parse_UrlWithAVariable_IsAcceptedBeforeExpansion()
    {
        // Act
        var result = McpConfigParser.Parse("""{"mcpServers": {"s": {"url": "${BASE:-https://x.test}/mcp"}}}""");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Servers.Single().Url.Should().Be("${BASE:-https://x.test}/mcp");
    }
}
