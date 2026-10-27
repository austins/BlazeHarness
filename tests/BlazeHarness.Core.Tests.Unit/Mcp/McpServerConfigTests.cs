using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpServerConfigTests
{
    [Fact]
    public void TryExpand_ReplacesEveryFieldThatCanHoldAReference()
    {
        // Arrange
        var parsed = McpConfigParser.Parse("""
            {"mcpServers": {
              "s": {"command": "${BIN}", "args": ["--root", "${ROOT}"], "env": {"KEY": "${SECRET}"}, "cwd": "${ROOT}/w"},
              "r": {"url": "${HOST}/mcp", "headers": {"Authorization": "Bearer ${SECRET}"}}
            }}
            """);
        var values = new Dictionary<string, string> { ["BIN"] = "tool", ["ROOT"] = "/r", ["SECRET"] = "s3", ["HOST"] = "https://h.test" };

        // Act
        var stdio = parsed.Servers[0].TryExpand(values.GetValueOrDefault, out var local, out var localError);
        var remote = parsed.Servers[1].TryExpand(values.GetValueOrDefault, out var web, out var webError);

        // Assert
        stdio.Should().BeTrue(localError);
        local.Command.Should().Be("tool");
        local.Args.Should().Equal("--root", "/r");
        local.Env["KEY"].Should().Be("s3");
        local.Cwd.Should().Be("/r/w");
        remote.Should().BeTrue(webError);
        web.Url.Should().Be("https://h.test/mcp");
        web.Headers["Authorization"].Should().Be("Bearer s3");
    }

    [Fact]
    public void TryExpand_AVariableIsMissing_FailsAndNamesIt()
    {
        // Arrange
        var server = McpConfigParser.Parse("""{"mcpServers": {"s": {"command": "x", "env": {"K": "${NOPE}"}}}}""").Servers.Single();

        // Act
        var ok = server.TryExpand(_ => null, out var expanded, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("NOPE");
        expanded.Should().BeSameAs(server);
    }

    [Fact]
    public void TryExpand_DoesNotChangeTheOriginal()
    {
        // Arrange
        var server = McpConfigParser.Parse("""{"mcpServers": {"s": {"command": "${BIN}"}}}""").Servers.Single();

        // Act
        server.TryExpand(_ => "x", out var expanded, out _);

        // Assert
        server.Command.Should().Be("${BIN}");
        expanded.Command.Should().Be("x");
    }
}
