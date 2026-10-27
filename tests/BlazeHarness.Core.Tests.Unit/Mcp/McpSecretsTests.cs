using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpSecretsTests
{
    [Theory]
    [InlineData("GITHUB_TOKEN", "ghp_abcdef", true)]
    [InlineData("API_KEY", "abc123", true)]
    [InlineData("DB_PASSWORD", "hunter2", true)]
    [InlineData("SOMETHING", "sk-abc123", true)]
    [InlineData("GITHUB_TOKEN", "${GITHUB_TOKEN}", false)]
    [InlineData("GITHUB_TOKEN", "", false)]
    [InlineData("LOG_LEVEL", "debug", false)]
    [InlineData("HOME_DIR", "/home/me", false)]
    public void LiteralsIn_EnvValue_IsFlaggedOnlyWhenItLooksLikeASecret(string name, string value, bool flagged)
    {
        // Arrange
        var server = Stdio(new Dictionary<string, string> { [name] = value });

        // Act
        var found = McpSecrets.LiteralsIn(server);

        // Assert
        found.Should().HaveCount(flagged ? 1 : 0);
        found.Should().OnlyContain(label => label == "env " + name);
    }

    [Fact]
    public void LiteralsIn_Headers_AreChecked()
    {
        // Arrange
        var server = Remote("https://x.test/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer abc", ["Accept"] = "text/event-stream", ["X-Api-Key"] = "${KEY}" });

        // Act
        var found = McpSecrets.LiteralsIn(server);

        // Assert
        found.Should().Equal("header Authorization");
    }

    [Theory]
    [InlineData("https://x.test/mcp?token=abc", true)]
    [InlineData("https://x.test/mcp?a=1&api_key=abc", true)]
    [InlineData("https://x.test/mcp?token=${TOKEN}", false)]
    [InlineData("https://x.test/mcp?page=2", false)]
    public void LiteralsIn_Url_IsFlaggedWhenItCarriesASecretInTheQuery(string url, bool flagged)
    {
        // Act
        var found = McpSecrets.LiteralsIn(Remote(url, new Dictionary<string, string>()));

        // Assert
        found.Should().HaveCount(flagged ? 1 : 0);
    }

    [Theory]
    [InlineData("http://example.test/mcp", "header Authorization (sent over plain http)")]
    [InlineData("https://example.test/mcp", "header Authorization")]
    [InlineData("http://localhost:3000/mcp", "header Authorization")]
    public void LiteralsIn_SecretHeader_IsNotedWhenItGoesOverPlainHttpToAnotherMachine(string url, string expected)
    {
        // Arrange
        var server = Remote(url, new Dictionary<string, string> { ["Authorization"] = "Bearer abc" });

        // Act
        var found = McpSecrets.LiteralsIn(server);

        // Assert
        found.Should().Equal(expected);
    }

    [Fact]
    public void LiteralsIn_NeverIncludesTheValue()
    {
        // Act
        var found = McpSecrets.LiteralsIn(Stdio(new Dictionary<string, string> { ["API_KEY"] = "topsecret" }));

        // Assert
        found.Should().NotContain(label => label.Contains("topsecret", StringComparison.Ordinal));
    }

    private static McpServerConfig Stdio(Dictionary<string, string> env)
    {
        return new McpServerConfig("s", McpTransport.Stdio, "x", [], env, null, null, new Dictionary<string, string>(), false);
    }

    private static McpServerConfig Remote(string url, Dictionary<string, string> headers)
    {
        return new McpServerConfig("s", McpTransport.Http, null, [], new Dictionary<string, string>(), null, url, headers, false);
    }
}
