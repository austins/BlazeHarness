using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpSecretVariablesTests
{
    [Theory]
    [InlineData("github", "token", "MCP_GITHUB_TOKEN")]
    [InlineData("my-server", "api.key", "MCP_MY_SERVER_API_KEY")]
    public void NameFor_ServerAndKey_GivesAnUppercaseVariable(string server, string key, string expected)
    {
        // Act
        var name = McpSecretVariables.NameFor(server, key);

        // Assert
        name.Should().Be(expected);
    }

    [Theory]
    [InlineData("${MCP_A_B}", true, "MCP_A_B")]
    [InlineData("${OTHER}", false, "")]
    [InlineData("Bearer ${MCP_A_B}", false, "")]
    [InlineData("${MCP_A_B:-x}", false, "")]
    [InlineData(null, false, "")]
    public void TryParseReference_Value_AcceptsOnlyAWholeSecretReference(string? value, bool expected, string variable)
    {
        // Act
        var parsed = McpSecretVariables.TryParseReference(value, out var found);

        // Assert
        parsed.Should().Be(expected);
        found.Should().Be(variable);
    }

    [Fact]
    public void Referenced_Server_ReturnsOnlyItsOwnVariables()
    {
        // Arrange
        var server = new McpServerConfig(
            "a",
            McpTransport.Stdio,
            "x",
            [],
            new Dictionary<string, string> { ["TOKEN"] = "${MCP_A_TOKEN}", ["STEAL"] = "${MCP_B_TOKEN}", ["PLAIN"] = "v" },
            null,
            null,
            new Dictionary<string, string> { ["Key"] = "${MCP_A_KEY}" },
            false);

        // Act
        var variables = McpSecretVariables.Referenced(server);

        // Assert
        variables.Should().BeEquivalentTo("MCP_A_TOKEN", "MCP_A_KEY");
    }
}
