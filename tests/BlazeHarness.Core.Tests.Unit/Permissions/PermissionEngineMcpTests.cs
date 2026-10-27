using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class PermissionEngineMcpTests
{
    private readonly PermissionEngine _sut = new();
    private readonly ToolContext _context = new(Path.GetTempPath());

    [Fact]
    public void Evaluate_NoHints_AsksAndKeysByServerAndTool()
    {
        // Act
        var decision = Evaluate(Tool("files", "read"));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("mcp:files/read");
        decision.Reason.Should().Contain("outside the sandbox");
    }

    [Fact]
    public void Evaluate_AutoApprove_StillAsks()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_NeverAsk_Allows()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(Tool("files", "write"));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_ReadOnlyHintOnAnUntrustedServer_Asks()
    {
        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ReadOnlyHintOnATrustedServer_Allows()
    {
        // Arrange
        _sut.TrustedMcpServer = name => name == "files";

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(false, null)]
    [InlineData(true, true)]
    public void Evaluate_TrustedServerWithoutAClearReadOnlyHint_Asks(bool? readOnly, bool? destructive)
    {
        // Arrange
        _sut.TrustedMcpServer = _ => true;

        // Act
        var decision = Evaluate(Tool("files", "write", readOnly, destructive));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_TrustOfAnotherServer_DoesNotCarryOver()
    {
        // Arrange
        _sut.TrustedMcpServer = name => name == "other";

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ReadOnlySession_AsksEvenForATrustedReadOnlyTool()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AskFirst);
        _sut.TrustedMcpServer = _ => true;

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ReadOnlySessionThatNeverAsks_Denies()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
        decision.Reason.Should().Contain("Read only");
    }

    [Fact]
    public void Evaluate_RememberedApproval_AllowsThatToolOnly()
    {
        // Arrange
        _sut.Remember("mcp:files/read");

        // Assert
        Evaluate(Tool("files", "read")).Verdict.Should().Be(PermissionVerdict.Allow);
        Evaluate(Tool("files", "write")).Verdict.Should().Be(PermissionVerdict.Ask);
        Evaluate(Tool("other", "read")).Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_DenyRule_RefusesEvenWhenNeverAsk()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);
        _sut.Rules = () => [new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Deny, "files/delete*")];

        // Assert
        Evaluate(Tool("files", "delete_all")).Verdict.Should().Be(PermissionVerdict.Deny);
        Evaluate(Tool("files", "read")).Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_AllowRule_SkipsTheQuestion()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Allow, "files/*")];

        // Assert
        Evaluate(Tool("files", "write")).Verdict.Should().Be(PermissionVerdict.Allow);
        Evaluate(Tool("other", "write")).Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_AskRule_AsksEvenForATrustedReadOnlyToolAndCannotBeRemembered()
    {
        // Arrange
        _sut.TrustedMcpServer = _ => true;
        _sut.Remember("mcp:files/read");
        _sut.Rules = () => [new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Ask, "files/read")];

        // Act
        var decision = Evaluate(Tool("files", "read", readOnly: true));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
    }

    [Fact]
    public void Evaluate_AllowRuleInAReadOnlySession_StillAsks()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AskFirst);
        _sut.Rules = () => [new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Allow, "files/*")];

        // Act
        var decision = Evaluate(Tool("files", "read"));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ShellAndFileRules_DoNotApplyToMcpTools()
    {
        // Arrange
        _sut.Rules = () =>
        [
            new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Deny, "*"),
            new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**"),
        ];

        // Act
        var decision = Evaluate(Tool("files", "read"));

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    private static McpTool Tool(string server, string name, bool? readOnly = null, bool? destructive = null)
    {
        var info = new McpToolInfo(name, "A tool.", """{"type":"object","properties":{}}""", readOnly, destructive);
        return new McpTool(McpToolNames.Build(server, name, new HashSet<string>()), server, info, Substitute.For<IMcpConnection>());
    }

    private PermissionDecision Evaluate(ITool tool) => _sut.Evaluate(tool, "{}", _context);
}
