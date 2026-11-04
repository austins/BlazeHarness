using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class PermissionEngineSendToChatTests
{
    private const string Arguments = """{"chat":"chat:abc","message":"hello"}""";

    private readonly PermissionEngine _sut = new();
    private readonly ToolContext _context = new(Path.GetTempPath());
    private SendToChatResolution _resolution = new(new SendToChatPlan("abc", "Story 2", "/work", SameScope: true, "hello"));

    [Fact]
    public void Evaluate_SameFolderAskFirst_AsksAndKeysByChat()
    {
        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("chat:abc");
        decision.Reason.Should().Contain("Story 2").And.NotContain("hello");
    }

    [Fact]
    public void Evaluate_NewChatAskFirst_AsksWithoutAGrantKey()
    {
        // Arrange
        _resolution = new SendToChatResolution(new SendToChatPlan(null, null, "/work", SameScope: true, "hello"));

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
    }

    [Fact]
    public void Evaluate_RememberedApproval_Allows()
    {
        // Arrange
        _sut.Remember("chat:abc");

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Theory]
    [InlineData(ApprovalMode.AutoApprove)]
    [InlineData(ApprovalMode.NeverAsk)]
    public void Evaluate_SameFolderWithoutAsking_Allows(ApprovalMode approvals)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, approvals);

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Theory]
    [InlineData(AccessLevel.WriteInFolder, ApprovalMode.AskFirst)]
    [InlineData(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove)]
    [InlineData(AccessLevel.Unrestricted, ApprovalMode.AutoApprove)]
    public void Evaluate_OtherFolder_AsksEveryTimeEvenWithARememberedApproval(AccessLevel access, ApprovalMode approvals)
    {
        // Arrange
        _resolution = new SendToChatResolution(new SendToChatPlan("abc", "Story 2", "/other", SameScope: false, "hello"));
        _sut.Remember("chat:abc");
        _sut.Settings = new PermissionSettings(access, approvals);

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.Sticky.Should().BeTrue();
        decision.GrantKey.Should().BeNull();
    }

    [Theory]
    [InlineData(AccessLevel.WriteInFolder)]
    [InlineData(AccessLevel.Unrestricted)]
    public void Evaluate_OtherFolderThatNeverAsks_Denies(AccessLevel access)
    {
        // Arrange
        _resolution = new SendToChatResolution(new SendToChatPlan("abc", "Story 2", "/other", SameScope: false, "hello"));
        _sut.Settings = new PermissionSettings(access, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_ReadOnlyChat_AsksEveryTime()
    {
        // Arrange
        _sut.Remember("chat:abc");
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove);

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.Sticky.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ReadOnlyChatThatNeverAsks_Denies()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_NoChatToSendTo_DeniesWithTheError()
    {
        // Arrange
        _resolution = new SendToChatResolution(null, "No chat has that title.");

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
        decision.Reason.Should().Be("No chat has that title.");
    }

    [Fact]
    public void Evaluate_AllowRulesOfEveryKind_DoNotSkipTheQuestion()
    {
        // Arrange
        _sut.Rules = () =>
        [
            new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Allow, "*"),
            new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Allow, "*"),
            new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Allow, "*"),
            new PermissionRule(RuleTarget.WebHost, PermissionVerdict.Allow, "*"),
            new PermissionRule(RuleTarget.McpTool, PermissionVerdict.Allow, "send_to_chat"),
        ];
        _resolution = new SendToChatResolution(new SendToChatPlan("abc", "Story 2", "/other", SameScope: false, "hello"));

        // Act
        var decision = Evaluate();

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.Sticky.Should().BeTrue();
    }

    private PermissionDecision Evaluate()
    {
        return _sut.Evaluate(new SendToChatTool((_, _, _) => _resolution, (_, _) => Task.FromResult(ToolResult.Ok("sent"))), Arguments, _context);
    }
}
