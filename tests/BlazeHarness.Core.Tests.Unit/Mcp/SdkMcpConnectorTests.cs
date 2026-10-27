using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class SdkMcpConnectorTests
{
    [Fact]
    public void Redact_ServerEchoesASetting_HidesTheValue()
    {
        // Arrange
        var config = new McpServerConfig(
            "s",
            McpTransport.Stdio,
            "x",
            [],
            new Dictionary<string, string> { ["API_KEY"] = "topsecret", ["MODE"] = "a" },
            null,
            null,
            new Dictionary<string, string>(),
            false);

        // Act
        var text = SdkMcpConnector.Redact("bad key topsecret, mode a", config);

        // Assert
        text.Should().Be("bad key ***, mode a");
    }

    [Fact]
    public void Redact_HeaderValue_IsHiddenToo()
    {
        // Arrange
        var config = new McpServerConfig(
            "s",
            McpTransport.Http,
            null,
            [],
            new Dictionary<string, string>(),
            null,
            "https://x.test/mcp",
            new Dictionary<string, string> { ["Authorization"] = "Bearer abc123" },
            false);

        // Act
        var text = SdkMcpConnector.Redact("401 for Bearer abc123", config);

        // Assert
        text.Should().Be("401 for ***");
    }
}
